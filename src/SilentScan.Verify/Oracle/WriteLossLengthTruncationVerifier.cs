using System.Text.RegularExpressions;
using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public sealed class WriteLossLengthTruncationVerifier : RuleVerifier<WriteLossFinding>
{
    public override string RuleId => FindingRuleIds.WriteLossLengthTruncationRuleId;

    protected override Perturbation Describe(WriteLossFinding finding)
    {
        if (finding.TableQualifiedName is not null || !finding.ColumnName.StartsWith('@'))
        {
            return Perturbation.NotApplicable("The finding does not name a variable target.");
        }

        var declaration = new Regex(
            @"(\bDECLARE\s+" + Regex.Escape(finding.ColumnName) + @"\s+(?:AS\s+)?)(n?var(?:char|binary)|n?char|binary)\s*\(\s*\d+\s*\)",
            RegexOptions.IgnoreCase);
        string? original = null;
        return new Perturbation(
            [],
            Difference.ResultChanged,
            (connection, transaction, cancellationToken) => ModuleRecompiler.RecompileAsync(
                connection,
                transaction,
                finding.SourcePath,
                string.Empty,
                definition =>
                {
                    original = definition;
                    return declaration.Replace(definition, match => match.Groups[1].Value + WideFamily(match.Groups[2].Value) + "(MAX)");
                },
                cancellationToken),
            (connection, transaction, cancellationToken) => ModuleRecompiler.RecompileAsync(
                connection, transaction, finding.SourcePath, string.Empty, definition => original ?? definition, cancellationToken),
            static _ => false);
    }

    private static string WideFamily(string type) =>
        type.ToUpperInvariant() switch
        {
            "NVARCHAR" or "NCHAR" => "NVARCHAR",
            "VARBINARY" or "BINARY" => "VARBINARY",
            _ => "VARCHAR",
        };
}
