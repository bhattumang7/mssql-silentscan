using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.ForcedSerialTableVariableModificationRuleId)]
public sealed class TableVariableModificationPairedOracleTests : RuleOracle
{
    private const string Base = """
        CREATE TABLE dbo.BigTable (Id INT NOT NULL, Grp INT NOT NULL, Val VARCHAR(100) NOT NULL);
        GO
        INSERT INTO dbo.BigTable (Id, Grp, Val)
        SELECT TOP (200000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)), ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) % 100, REPLICATE('x', 50)
        FROM sys.all_objects a CROSS JOIN sys.all_objects b;
        GO
        """;

    private const string Variable = Base + "\nCREATE PROCEDURE dbo.P AS BEGIN DECLARE @t TABLE (Grp INT, Cnt INT); INSERT INTO @t (Grp, Cnt) SELECT Grp, COUNT(*) FROM dbo.BigTable GROUP BY Grp OPTION (MAXDOP 0); END\nGO\n";

    private const string TempTable = Base + "\nCREATE PROCEDURE dbo.P AS BEGIN CREATE TABLE #t (Grp INT, Cnt INT); INSERT INTO #t (Grp, Cnt) SELECT Grp, COUNT(*) FROM dbo.BigTable GROUP BY Grp OPTION (MAXDOP 0); END\nGO\n";

    private const string Modified = "insert-into-a-table-variable";

    private const string Statement = "EXEC dbo.P;";

    protected override IReadOnlyList<Func<IFinding, IFinding>> ClaimMutations { get; } =
    [
        finding => ((ForcedSerialFinding)finding) with { SourcePath = "dbo.Other" },
        finding => ((ForcedSerialFinding)finding) with { DetailText = "@missing" },
    ];

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(Modified, Variable, Statement),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("insert-into-a-temp-table", TempTable, Statement), Modified, OwnSetup: true),
    ];
}
