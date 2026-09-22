using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;

namespace SilentScan.Core.Reporting.RuleHarness.Adapters;

internal sealed class CrossModuleLockOrderRule : ICrossModuleRule
{
    public string Id => "CrossModuleLockOrderScanner";
    public IReadOnlyList<IFinding> Scan(IReadOnlyList<SqlParseResult> parseResults, RuleContext context) => CrossModuleLockOrderScanner.Scan(parseResults, context.Catalog);
    public IModuleRule CreateModuleRule(SqlParseResult parseResult, RuleContext context) => CrossModuleLockOrderScanner.CreateRule(parseResult.SourcePath, context.Catalog);
    public IReadOnlyList<IFinding> Aggregate(RuleContext context, IReadOnlyList<IModuleRule> moduleRules) =>
        CrossModuleLockOrderScanner.Harvest(context.Catalog, [.. moduleRules.Cast<CrossModuleLockOrderScanner.Rule>()]);
}
