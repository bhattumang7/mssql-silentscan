using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.IndexDesignUnindexedForeignKeyRuleId)]
public sealed class UnindexedForeignKeyPairedOracleTests : RuleOracle
{
    private const string Base = """
        CREATE TABLE dbo.Parent (Id INT NOT NULL PRIMARY KEY);
        GO
        CREATE TABLE dbo.Child (Id INT NOT NULL PRIMARY KEY, ParentId INT NOT NULL, Pad CHAR(200) NOT NULL DEFAULT 'x');
        GO
        INSERT INTO dbo.Parent (Id) SELECT TOP (500) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) FROM sys.all_objects;
        GO
        INSERT INTO dbo.Child (Id, ParentId)
        SELECT TOP (60000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)), 1 + ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) % 500 FROM sys.all_objects a CROSS JOIN sys.all_objects b;
        GO
        """;

    private const string Enforced = Base + "\nALTER TABLE dbo.Child ADD CONSTRAINT FK_Child_Parent FOREIGN KEY (ParentId) REFERENCES dbo.Parent (Id);\nGO\n";

    private const string Disabled = Enforced + "ALTER TABLE dbo.Child NOCHECK CONSTRAINT FK_Child_Parent;\nGO\n";

    private const string Unindexed = "enforced-foreign-key-without-index";

    private const string Statement = "DELETE FROM dbo.Parent WHERE Id = 100000;";

    protected override IReadOnlyList<Func<IFinding, IFinding>> ClaimMutations { get; } =
    [
        finding => ((IndexDesignFinding)finding) with { TableQualifiedName = "dbo.Other" },
        finding => ((IndexDesignFinding)finding) with { ColumnNames = null },
    ];

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(Unindexed, Enforced, Statement),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("disabled-foreign-key", Disabled, Statement), Unindexed, OwnSetup: true),
    ];
}
