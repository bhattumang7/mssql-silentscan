using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;

namespace SilentScan.Core.Reporting.RuleHarness;

public interface IRule
{
    string Id { get; }

    DynamicSqlApplicability DynamicSql { get; }
}

public interface IPerFileRule : IRule
{
    object? Prepare(RuleContext context) => null;

    IReadOnlyList<IFinding> Scan(SqlParseResult parseResult, RuleContext context, object? state);

    IReadOnlyList<IFinding> ScanCatalogOnce(RuleContext context) => [];

    IComparer<IFinding>? Comparer => null;

    IModuleRule? CreateModuleRule(SqlParseResult parseResult, RuleContext context, object? state) => null;

    IReadOnlyList<IFinding> HarvestFindings(SqlParseResult parseResult, RuleContext context, object? state, IModuleRule moduleRule) => [];
}

public interface ICatalogRule : IRule
{
    IReadOnlyList<IFinding> Scan(RuleContext context);
}
