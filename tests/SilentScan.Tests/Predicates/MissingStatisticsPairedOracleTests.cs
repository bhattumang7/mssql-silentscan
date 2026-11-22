using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.MissingStatisticsRuleId)]
public sealed class MissingStatisticsPairedOracleTests : RuleOracle
{
    private const string Base = """
        ALTER DATABASE CURRENT SET AUTO_CREATE_STATISTICS OFF;
        GO
        CREATE TABLE dbo.T (Id INT NOT NULL, B INT NOT NULL);
        GO
        INSERT INTO dbo.T (Id, B)
        SELECT TOP (30000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)), CASE WHEN ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) % 100 = 0 THEN ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) ELSE 1 END
        FROM sys.all_objects a CROSS JOIN sys.all_objects b;
        GO
        """;

    private const string Covered = Base + "\nCREATE STATISTICS ST_T_B ON dbo.T (B);\nGO\n";

    private const string NoStatistic = "predicate-column-without-statistic";

    private const string Statement = "SELECT Id FROM dbo.T WHERE B = 5000;";

    protected override IReadOnlyList<Func<IFinding, IFinding>> ClaimMutations { get; } =
    [
        finding => ((MissingStatisticsFinding)finding) with { TableQualifiedName = "dbo.Other" },
        finding => ((MissingStatisticsFinding)finding) with { ColumnName = "Id" },
    ];

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(NoStatistic, Base, Statement),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("predicate-column-with-statistic", Covered, Statement), NoStatistic, OwnSetup: true),
    ];
}
