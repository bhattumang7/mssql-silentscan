using System.Xml.Linq;
using Microsoft.Data.SqlClient;
using SilentScan.Core.Predicates;
using SilentScan.Core.Common;

namespace SilentScan.Verify.Oracle;

public sealed class TvfFenceVerifier
{
    private static readonly XNamespace ShowPlanNs = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";

    private readonly PlanXmlCapture _planXmlCapture;
    private readonly FunctionParameterReader _functionParameterReader;

    public TvfFenceVerifier(SqlServerOptions options)
    {
        _planXmlCapture = new PlanXmlCapture(options);
        _functionParameterReader = new FunctionParameterReader(options);
    }

    public async Task<TvfFenceResult> VerifyAsync(string database, TvfFenceFinding finding, CancellationToken cancellationToken = default)
    {
        if (finding.FunctionQualifiedName is not { } qualifiedName)
        {
            return new TvfFenceResult(finding, TvfFenceOutcome.NotProbeable, "No function qualified name on the finding.");
        }

        var parameterTypes = await _functionParameterReader.TryGetParameterTypesAsync(database, qualifiedName, cancellationToken);
        var probe = TvfFenceProbeBuilder.BuildFunctionProbe(finding, parameterTypes);
        if (probe is null)
        {
            return new TvfFenceResult(
                finding, TvfFenceOutcome.NotProbeable,
                $"Could not resolve/render '{qualifiedName}'s own parameter types into a dummy argument list.");
        }

        string planXml;
        try
        {
            planXml = await _planXmlCapture.CaptureAsync(database, probe, cancellationToken);
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException)
        {
            return new TvfFenceResult(finding, TvfFenceOutcome.ProbeFailed, ex.Message);
        }

        var hasFenceOperator = HasMatchingTableValuedFunction(planXml, qualifiedName);
        return hasFenceOperator
            ? new TvfFenceResult(finding, TvfFenceOutcome.Confirmed, null)
            : new TvfFenceResult(
                finding, TvfFenceOutcome.NotConfirmed,
                $"The plan for '{qualifiedName}' shows no Table-valued function operator naming '{qualifiedName}' itself - it dissolved into base operators like an inline TVF (or the plan's only TVF operator names a different function entirely), contradicting the finding's own claim.");
    }

    private static bool HasMatchingTableValuedFunction(string planXml, string qualifiedName)
    {
        var doc = XDocument.Parse(planXml);
        return doc.Descendants(ShowPlanNs + "RelOp")
            .Where(relOp => (string?)relOp.Attribute("PhysicalOp") == "Table-valued function")
            .SelectMany(relOp => relOp.Descendants(ShowPlanNs + "TableValuedFunction"))
            .SelectMany(tvf => tvf.Elements(ShowPlanNs + "Object"))
            .Any(obj => NamesSameFunction(obj, qualifiedName));
    }

    private static bool NamesSameFunction(XElement objectElement, string qualifiedName)
    {
        var schema = TrimBrackets((string?)objectElement.Attribute("Schema"));
        var table = TrimBrackets((string?)objectElement.Attribute("Table"));
        if (schema is null || table is null)
        {
            return false;
        }

        return string.Equals($"{schema}.{table}", qualifiedName, StringComparison.OrdinalIgnoreCase);
    }

    private static string? TrimBrackets(string? bracketedIdentifier) => bracketedIdentifier?.Trim('[', ']');
}
