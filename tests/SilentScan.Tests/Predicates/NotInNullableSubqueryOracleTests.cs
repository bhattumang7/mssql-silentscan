using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.NotInNullableSubqueryRuleId)]
public sealed class NotInNullableSubqueryOracleTests : RuleOracle
{
    private const string Setup = """
        CREATE TABLE dbo.Parent (Id INT NOT NULL);
        GO
        CREATE TABLE dbo.ChildNullable (RefId INT NULL);
        GO
        CREATE TABLE dbo.ChildNotNull (RefId INT NOT NULL);
        GO
        INSERT INTO dbo.Parent (Id) VALUES (1), (2), (3);
        INSERT INTO dbo.ChildNullable (RefId) VALUES (1), (NULL);
        INSERT INTO dbo.ChildNotNull (RefId) VALUES (1), (9);
        GO
        """;

    protected override OracleCase Fires { get; } = new(
        Setup,
        "SELECT Id FROM dbo.Parent WHERE Id NOT IN (SELECT RefId FROM dbo.ChildNullable) ORDER BY Id;");

    protected override OracleCase Silent { get; } = new(
        Setup,
        "SELECT Id FROM dbo.Parent WHERE Id NOT IN (SELECT RefId FROM dbo.ChildNotNull) ORDER BY Id;");

    protected override IFinding ClaimedOnSilent { get; } =
        new NotInNullableSubqueryFinding("Id", "dbo.ChildNotNull", "RefId", false, "claimed.sql", 1, 1);
}
