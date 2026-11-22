using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.ParameterReassignmentPredicateRuleId)]
public sealed class ParameterReassignmentPairedOracleTests : RuleOracle
{
    private const string Base = """
        CREATE TABLE dbo.T (Id INT NOT NULL, B INT NOT NULL);
        GO
        INSERT INTO dbo.T (Id, B)
        SELECT TOP (30000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)), CASE WHEN ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) % 100 = 0 THEN ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) ELSE 1 END
        FROM sys.all_objects a CROSS JOIN sys.all_objects b;
        CREATE INDEX IX_T_B ON dbo.T (B);
        GO
        """;

    private const string Plain = Base + "\nCREATE PROCEDURE dbo.P @p INT AS BEGIN SET @p = 5000; SELECT Id FROM dbo.T WHERE B = @p; END\nGO\n";

    private const string Recompiled = Base + "\nCREATE PROCEDURE dbo.P @p INT AS BEGIN SET @p = 5000; SELECT Id FROM dbo.T WHERE B = @p OPTION (RECOMPILE); END\nGO\n";

    private const string Reassigned = "predicate-on-a-reassigned-parameter";

    private const string Statement = "EXEC dbo.P @p = 1;";

    protected override IReadOnlyList<Func<IFinding, IFinding>> ClaimMutations { get; } =
    [
        finding => ((ParameterReassignmentPredicateFinding)finding) with { SourcePath = "dbo.Other" },
        finding => ((ParameterReassignmentPredicateFinding)finding) with { ParameterName = "@missing" },
    ];

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(Reassigned, Plain, Statement),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("statement-with-option-recompile", Recompiled, Statement), Reassigned, OwnSetup: true),
    ];
}
