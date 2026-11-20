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
            return NotDecided(findings, ModuleNotDecidedReason.ModuleKindNotPlannable, module is null ? "module definition not found" : $"object type {module.ObjectTypeCode}");
        }

        var capture = await _capture.CaptureAsync(database, module.QualifiedName, kind.Value, cancellationToken);
        if (capture.PlanXml is null)
        {
            return NotDecided(findings, capture.Reason, capture.Detail);
        }

        IReadOnlyList<PlannedStatement> statements;
        try
        {
            statements = kind == PlannableModuleKind.Procedure
                ? PlanStatementLocator.Locate(module.Definition, capture.PlanXml)
                : [WholeModuleStatement(module.Definition, capture.PlanXml)];
        }
        catch (XmlException)
        {
            return NotDecided(findings, ModuleNotDecidedReason.PlanRequestFailed, "plan XML could not be parsed");
        }

        return ModulePlanComparer.Compare(findings, statements);
    }

    private static PlannedStatement WholeModuleStatement(string definition, string planXml) =>
        new(definition, 1, definition.Count(c => c == '\n') + 1, ConvertImplicitDetector.FindColumnConversions(planXml));

    private static PlannableModuleKind? KindOf(string objectTypeCode) => objectTypeCode switch
    {
        "P" => PlannableModuleKind.Procedure,
        "V" => PlannableModuleKind.View,
        "IF" => PlannableModuleKind.InlineTableFunction,
        _ => null,
    };

    private static List<ModuleComparisonRow> NotDecided(IReadOnlyList<TypedPredicateFinding> findings, ModuleNotDecidedReason reason, string? detail) =>
        [.. findings.Select(f => new ModuleComparisonRow(
            ModuleComparisonOutcome.NotDecided, reason, f.Column.TableQualifiedName, f.Column.ColumnName, null, f, detail))];
}
