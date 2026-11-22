using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.QueryAntiPatternCountStarVariableExistenceCheckRuleId)]
public sealed class CountStarExistenceCheckPairedOracleTests : RuleOracle
{
    private const string Base = """
        CREATE TABLE dbo.Big (Id INT NOT NULL PRIMARY KEY, Col CHAR(1) NOT NULL, Pad CHAR(100) NOT NULL DEFAULT 'x');
        GO
        INSERT INTO dbo.Big (Id, Col)
        SELECT TOP (60000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)), 'x' FROM sys.all_objects a CROSS JOIN sys.all_objects b;
        GO
        """;

    private const string Assigned = Base + "\nCREATE PROCEDURE dbo.P AS BEGIN DECLARE @cnt INT; SELECT @cnt = COUNT(*) FROM dbo.Big WHERE Col = 'x'; IF @cnt > 0 SELECT 1 AS One; END\nGO\n";

    private const string Inline = Base + "\nCREATE PROCEDURE dbo.P AS BEGIN IF (SELECT COUNT(*) FROM dbo.Big WHERE Col = 'x') > 0 SELECT 1 AS One; END\nGO\n";

    private const string Aggregating = "count-assigned-then-compared-to-zero";

    private const string Statement = "EXEC dbo.P;";

    protected override IReadOnlyList<Func<IFinding, IFinding>> ClaimMutations { get; } =
    [
        finding => ((QueryAntiPatternFinding)finding) with { SourcePath = "dbo.Other" },
        finding => ((QueryAntiPatternFinding)finding) with { DetailText = "@missing" },
    ];

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(Aggregating, Assigned, Statement),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("inline-count-compared-to-zero", Inline, Statement), Aggregating, OwnSetup: true),
    ];
}
