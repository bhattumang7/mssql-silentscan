using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.NotInNullableSubqueryRuleId)]
public sealed class NotInNullableSubqueryOracleTests : RuleOracle
{
    private const string Setup = """
        CREATE TABLE dbo.Parent (Id INT NOT NULL, Tag INT NULL);
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

    private const string SelectFiring = "select-over-nullable-column";

    private const string DeleteFiring = "delete-over-nullable-column";

    private const string UpdateFiring = "update-over-nullable-column";

    private static IFinding OverNotNullColumn(IFinding finding) =>
        ((NotInNullableSubqueryFinding)finding) with { SubqueryTableQualifiedName = "dbo.ChildNotNull" };

    protected override IReadOnlyList<Func<IFinding, IFinding>> ClaimMutations { get; } =
    [
        OverNotNullColumn,
        finding => ((NotInNullableSubqueryFinding)finding) with { SubqueryTableQualifiedName = "dbo.Parent", SubqueryColumnName = "Tag" },
    ];

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(SelectFiring, Setup, "SELECT Id FROM dbo.Parent WHERE Id NOT IN (SELECT RefId FROM dbo.ChildNullable);"),
        new(DeleteFiring, Setup, "DELETE FROM dbo.Parent WHERE Id NOT IN (SELECT RefId FROM dbo.ChildNullable);"),
        new(UpdateFiring, Setup, "UPDATE dbo.Parent SET Tag = 1 WHERE Id NOT IN (SELECT RefId FROM dbo.ChildNullable);"),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("column-is-not-null", Setup, "SELECT Id FROM dbo.Parent WHERE Id NOT IN (SELECT RefId FROM dbo.ChildNotNull);"), SelectFiring, OverNotNullColumn),
        new(new("is-not-null-filter", Setup, "SELECT Id FROM dbo.Parent WHERE Id NOT IN (SELECT RefId FROM dbo.ChildNullable WHERE RefId IS NOT NULL);"), SelectFiring),
        new(new("null-rejecting-comparison", Setup, "SELECT Id FROM dbo.Parent WHERE Id NOT IN (SELECT RefId FROM dbo.ChildNullable WHERE RefId > 0);"), SelectFiring),
        new(new("null-rejecting-between", Setup, "SELECT Id FROM dbo.Parent WHERE Id NOT IN (SELECT RefId FROM dbo.ChildNullable WHERE RefId BETWEEN 0 AND 100);"), SelectFiring),
        new(new("delete-behind-is-not-null-filter", Setup, "DELETE FROM dbo.Parent WHERE Id NOT IN (SELECT RefId FROM dbo.ChildNullable WHERE RefId IS NOT NULL);", "is-not-null-filter"), DeleteFiring),
        new(new("update-behind-is-not-null-filter", Setup, "UPDATE dbo.Parent SET Tag = 1 WHERE Id NOT IN (SELECT RefId FROM dbo.ChildNullable WHERE RefId IS NOT NULL);", "is-not-null-filter"), UpdateFiring),
    ];
}
