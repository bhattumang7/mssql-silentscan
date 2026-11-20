using System.Xml;
using SilentScan.Core.Predicates;
using SilentScan.Verify.Catalog;

namespace SilentScan.Verify.Oracle;

public sealed class ModuleCorpusVerifier
{
    private readonly ModulePlanCapture _capture;

    public ModuleCorpusVerifier(SqlServerOptions options)
    {
        _capture = new ModulePlanCapture(options);
    }

    public async Task<IReadOnlyList<ModuleComparisonRow>> VerifyAsync(
        string database, LiveModule? module, IReadOnlyList<TypedPredicateFinding> findings, CancellationToken cancellationToken = default)
    {
        var kind = module is null ? null : KindOf(module.ObjectTypeCode);
        if (module is null || kind is null)
        {
            return NotDecided(findings, ModuleNotDecidedReason.ModuleKindNotPlannable);
        }

        var capture = await _capture.CaptureAsync(database, module.QualifiedName, kind.Value, cancellationToken);
        if (capture.PlanXml is null)
        {
            return NotDecided(findings, capture.Reason);
        }

        IReadOnlyList<PlannedStatement> statements;
        try
        {
            statements = PlanStatementLocator.Locate(module.Definition, capture.PlanXml);
        }
        catch (XmlException)
        {
            return NotDecided(findings, ModuleNotDecidedReason.PlanRequestFailed);
        }

        return ModulePlanComparer.Compare(findings, statements);
    }

    private static PlannableModuleKind? KindOf(string objectTypeCode) => objectTypeCode switch
    {
        "P" => PlannableModuleKind.Procedure,
        "V" => PlannableModuleKind.View,
        "IF" => PlannableModuleKind.InlineTableFunction,
        _ => null,
    };

    private static List<ModuleComparisonRow> NotDecided(IReadOnlyList<TypedPredicateFinding> findings, ModuleNotDecidedReason reason) =>
        [.. findings.Select(f => new ModuleComparisonRow(
            ModuleComparisonOutcome.NotDecided, reason, f.Column.TableQualifiedName, f.Column.ColumnName, null, f))];
}
