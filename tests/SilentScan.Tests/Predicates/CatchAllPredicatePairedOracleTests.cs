using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;
using SilentScan.Verify;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.CatchAllPredicateRuleId)]
public sealed class CatchAllPredicatePairedOracleTests : RuleOracle
{
    private const string Base = """
        CREATE TABLE dbo.Wide (Id INT NOT NULL, A INT NOT NULL, Pad CHAR(100) NOT NULL DEFAULT 'x');
        GO
        INSERT INTO dbo.Wide (Id, A)
        SELECT TOP (60000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)), ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) % 4000
        FROM sys.all_objects a CROSS JOIN sys.all_objects b;
        CREATE INDEX IX_Wide_A ON dbo.Wide (A);
        GO
        """;

    private const string Cached = Base + "\nCREATE PROCEDURE dbo.P @p INT = NULL AS SELECT Id FROM dbo.Wide WHERE (A = @p OR @p IS NULL);\nGO\n";

    private const string Recompiled = Base + "\nCREATE PROCEDURE dbo.P @p INT = NULL AS SELECT Id FROM dbo.Wide WHERE (A = @p OR @p IS NULL) OPTION (RECOMPILE);\nGO\n";

    private const string CatchAll = "catch-all-optional-filter";

    private const string Statement = "EXEC dbo.P @p = 5;";

    private static readonly IReadOnlyList<EngineTarget> BelowOptionalParameterOptimization =
        [.. new[] { 110, 120, 130, 140, 150, 160 }.Select(level => new EngineTarget(SqlServerOptions.LocalDocker, level))];

    protected override IReadOnlyList<EngineTarget> Targets => BelowOptionalParameterOptimization;

    protected override IReadOnlyList<Func<IFinding, IFinding>> ClaimMutations { get; } =
    [
        finding => ((CatchAllPredicateFinding)finding) with { SourcePath = "dbo.Other" },
        finding => ((CatchAllPredicateFinding)finding) with { ParameterName = "@missing" },
    ];

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(CatchAll, Cached, Statement),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("statement-with-option-recompile", Recompiled, Statement), CatchAll, OwnSetup: true),
    ];
}
