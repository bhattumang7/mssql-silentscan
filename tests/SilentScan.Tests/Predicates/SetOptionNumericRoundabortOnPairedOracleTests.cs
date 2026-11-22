using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.SetOptionNumericRoundabortOnBlocksIndexedFeatureRuleId)]
public sealed class SetOptionNumericRoundabortOnPairedOracleTests : RuleOracle
{
    private const string Tables = """
        CREATE TABLE dbo.Orders (OrderId INT NOT NULL, CustomerId INT NOT NULL, IsActive BIT NOT NULL);
        GO
        CREATE INDEX IX_Orders_ActiveCustomer ON dbo.Orders(CustomerId) WHERE IsActive = 1;
        GO
        INSERT INTO dbo.Orders (OrderId, CustomerId, IsActive)
        SELECT TOP (20000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)), ROW_NUMBER() OVER (ORDER BY (SELECT NULL)), 1
        FROM sys.all_objects a CROSS JOIN sys.all_objects b;
        GO
        """;

    private const string Query = "SELECT CustomerId FROM dbo.Orders WHERE IsActive = 1 AND CustomerId = 5;";

    private const string Compiled = "module-sets-the-blocking-option";

    private const string Statement = "EXEC dbo.P;";

    protected override IReadOnlyList<Func<IFinding, IFinding>> ClaimMutations { get; } =
    [
        finding => ((SetOptionFinding)finding) with { ModuleQualifiedName = "dbo.Other" },
    ];

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(Compiled, Tables + "\nCREATE PROCEDURE dbo.P AS \nSET NUMERIC_ROUNDABORT ON; \n" + Query + "\nGO\n", Statement),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("module-sets-the-safe-value", Tables + "\nCREATE PROCEDURE dbo.P AS \nSET NUMERIC_ROUNDABORT OFF; \n" + Query + "\nGO\n", Statement), Compiled, OwnSetup: true),
    ];
}
