using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;
using SilentScan.Verify;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.QueryAntiPatternTableVariableLowCompatEstimateRuleId)]
public sealed class TableVariableLowCompatEstimatePairedOracleTests : RuleOracle
{
    private const string Base = """
        CREATE TABLE dbo.Big (Id INT NOT NULL PRIMARY KEY, Pad CHAR(200) NOT NULL DEFAULT 'x');
        GO
        INSERT INTO dbo.Big (Id) SELECT TOP (60000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) FROM sys.all_objects a CROSS JOIN sys.all_objects b;
        GO
        """;

    private const string Fixed = Base + "\nCREATE PROCEDURE dbo.P AS BEGIN DECLARE @t TABLE (Id INT); INSERT INTO @t SELECT TOP (20000) Id FROM dbo.Big; SELECT b.Id FROM dbo.Big b JOIN @t t ON b.Id = t.Id; END\nGO\n";

    private const string Recompiled = Base + "\nCREATE PROCEDURE dbo.P AS BEGIN DECLARE @t TABLE (Id INT); INSERT INTO @t SELECT TOP (20000) Id FROM dbo.Big; SELECT b.Id FROM dbo.Big b JOIN @t t ON b.Id = t.Id OPTION (RECOMPILE); END\nGO\n";

    private const string FixedEstimate = "table-variable-joined-below-compat-150";

    private const string Statement = "EXEC dbo.P;";

    private static readonly IReadOnlyList<EngineTarget> BelowDeferredCompilation =
        [.. new[] { 110, 120, 130, 140 }.Select(level => new EngineTarget(SqlServerOptions.LocalDocker, level))];

    protected override IReadOnlyList<EngineTarget> Targets => BelowDeferredCompilation;

    protected override IReadOnlyList<Func<IFinding, IFinding>> ClaimMutations { get; } =
    [
        finding => ((QueryAntiPatternFinding)finding) with { SourcePath = "dbo.Other" },
        finding => ((QueryAntiPatternFinding)finding) with { DetailText = "@missing" },
    ];

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(FixedEstimate, Fixed, Statement),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("statement-with-option-recompile", Recompiled, Statement), FixedEstimate, OwnSetup: true),
    ];
}
