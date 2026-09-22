using ScopeChain = System.Collections.Generic.IReadOnlyList<(
    System.Collections.Generic.IReadOnlyDictionary<string, SilentScan.Core.Lineage.ScopeEntry> ByAlias,
    System.Collections.Generic.IReadOnlyList<SilentScan.Core.Lineage.ScopeEntry> Ordered)>;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Catalog;
using SilentScan.Core.Diagnostics;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;
using SilentScan.Core.Reporting.RuleHarness;

namespace SilentScan.Tests.Reporting.RuleHarness;

public sealed class RuleRunnerRunOnBatchTests
{
    private sealed record ProcScopeFinding(SourceSpan Location, FindingConfidence Confidence, string? ObservedProcScope) : IFinding;

    private sealed class ProcScopeModuleRule : IModuleRule
    {
        public List<string?> ObservedScopes { get; } = [];

        public void OnEnterQuerySpecificationScope(QuerySpecification node, ScopeChain scopeChain, ModuleWalker walker) =>
            ObservedScopes.Add(walker.CurrentProcScope);
    }

    private sealed class ProcScopeRule : IPerFileRule
    {
        public string Id => "ProcScopeTestRule";

        public DynamicSqlApplicability DynamicSql => DynamicSqlApplicability.Always;

        public IReadOnlyList<IFinding> Scan(SqlParseResult parseResult, RuleContext context, object? state) => [];

        public IModuleRule CreateModuleRule(SqlParseResult parseResult, RuleContext context, object? state) => new ProcScopeModuleRule();

        public IReadOnlyList<IFinding> HarvestFindings(SqlParseResult parseResult, RuleContext context, object? state, IModuleRule moduleRule) =>
            [.. ((ProcScopeModuleRule)moduleRule).ObservedScopes
                .Select(scope => new ProcScopeFinding(new SourceSpan(parseResult.SourcePath, 1, 1), FindingConfidence.High, scope))];
    }

    private static RuleContext BuildEmptyContext()
    {
        var catalog = CatalogBuilder.Build([]);
        var lineage = LineageResolver.Resolve(catalog, []);
        return new RuleContext(
            catalog, lineage, new SkipLedger(), new ProcCallGraph([]),
            new Dictionary<string, TvfFenceOrigin>(), new Dictionary<string, ScalarUdfOrigin>(),
            new Dictionary<string, ViewExpansionOrigin>(), [],
            new Dictionary<string, SelectStarViewCandidate>(), new Dictionary<string, IReadOnlyList<string>>());
    }

    [Fact]
    public void RunOnBatch_SeedsCurrentProcScopeFromCallerContext()
    {
        var innerParseResult = SqlScriptParser.ParseText("outer.sql::dynamic-sql@1", "SELECT 1;");
        var context = BuildEmptyContext();
        var callerContext = new ModuleWalkerCallerContext(context.Ledger, "dbo.OuterProc", null);
        var crashes = new List<SkippedConstruct>();

        var results = RuleRunner.RunOnBatch([new ProcScopeRule()], innerParseResult, context, callerContext, crashes);

        var finding = Assert.Single(results["ProcScopeTestRule"].Cast<ProcScopeFinding>());
        Assert.Equal("dbo.OuterProc", finding.ObservedProcScope);
        Assert.Empty(crashes);
    }

    [Fact]
    public void RunOnBatch_WithNoCallerScope_LeavesProcScopeNull()
    {
        var innerParseResult = SqlScriptParser.ParseText("outer.sql::dynamic-sql@1", "SELECT 1;");
        var context = BuildEmptyContext();
        var callerContext = new ModuleWalkerCallerContext(context.Ledger, null, null);
        var crashes = new List<SkippedConstruct>();

        var results = RuleRunner.RunOnBatch([new ProcScopeRule()], innerParseResult, context, callerContext, crashes);

        var finding = Assert.Single(results["ProcScopeTestRule"].Cast<ProcScopeFinding>());
        Assert.Null(finding.ObservedProcScope);
    }
}
