using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.ForcedSerialOutputReturnedToClientRuleId)]
public sealed class OutputReturnedToClientPairedOracleTests : RuleOracle
{
    private const string Base = """
        CREATE TABLE dbo.BigTable (Id INT NOT NULL, Grp INT NOT NULL, Val VARCHAR(100) NOT NULL);
        GO
        INSERT INTO dbo.BigTable (Id, Grp, Val)
        SELECT TOP (200000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)), ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) % 100, REPLICATE('x', 50)
        FROM sys.all_objects a CROSS JOIN sys.all_objects b;
        GO
        """;

    private const string Client = Base + "\nCREATE PROCEDURE dbo.P AS BEGIN DELETE b OUTPUT deleted.Id FROM dbo.BigTable b JOIN (SELECT Grp, COUNT(*) AS c FROM dbo.BigTable GROUP BY Grp) g ON b.Grp = g.Grp WHERE g.c > 0 OPTION (MAXDOP 0); END\nGO\n";

    private const string IntoTemp = Base + "\nCREATE PROCEDURE dbo.P AS BEGIN CREATE TABLE #o (Id INT); DELETE b OUTPUT deleted.Id INTO #o FROM dbo.BigTable b JOIN (SELECT Grp, COUNT(*) AS c FROM dbo.BigTable GROUP BY Grp) g ON b.Grp = g.Grp WHERE g.c > 0 OPTION (MAXDOP 0); END\nGO\n";

    private const string Returned = "output-returned-to-the-client";

    private const string Statement = "EXEC dbo.P;";

    protected override IReadOnlyList<Func<IFinding, IFinding>> ClaimMutations { get; } =
    [
        finding => ((ForcedSerialFinding)finding) with { SourcePath = "dbo.Other" },
        finding => ((ForcedSerialFinding)finding) with { DetailText = "OUTPUT missing.Column" },
    ];

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(Returned, Client, Statement),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("output-into-a-temp-table", IntoTemp, Statement), Returned, OwnSetup: true),
    ];
}
