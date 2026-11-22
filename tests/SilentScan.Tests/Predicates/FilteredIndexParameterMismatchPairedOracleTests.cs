using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.FilteredIndexParameterMismatchRuleId)]
public sealed class FilteredIndexParameterMismatchPairedOracleTests : RuleOracle
{
    private const string Base = """
        CREATE TABLE dbo.Orders (Id INT NOT NULL PRIMARY KEY, Status INT NOT NULL, CustomerId INT NOT NULL, Pad CHAR(100) NOT NULL DEFAULT 'x');
        GO
        INSERT INTO dbo.Orders (Id, Status, CustomerId)
        SELECT TOP (60000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)), CASE WHEN ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) % 500 = 0 THEN 1 ELSE 0 END, ROW_NUMBER() OVER (ORDER BY (SELECT NULL))
        FROM sys.all_objects a CROSS JOIN sys.all_objects b;
        CREATE INDEX IX_Orders_Open ON dbo.Orders (CustomerId) WHERE Status = 1;
        GO
        """;

    private const string Parameterized = Base + "\nCREATE PROCEDURE dbo.P @s INT AS SELECT CustomerId FROM dbo.Orders WHERE Status = @s;\nGO\n";

    private const string Recompiled = Base + "\nCREATE PROCEDURE dbo.P @s INT AS SELECT CustomerId FROM dbo.Orders WHERE Status = @s OPTION (RECOMPILE);\nGO\n";

    private const string Mismatch = "parameter-against-a-filtered-index-literal";

    private const string Statement = "EXEC dbo.P @s = 1;";

    protected override IReadOnlyList<Func<IFinding, IFinding>> ClaimMutations { get; } =
    [
        finding => ((FilteredIndexParameterMismatchFinding)finding) with { SourcePath = "dbo.Other" },
        finding => ((FilteredIndexParameterMismatchFinding)finding) with { VariableName = "@missing" },
    ];

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(Mismatch, Parameterized, Statement),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("statement-with-option-recompile", Recompiled, Statement), Mismatch, OwnSetup: true),
    ];
}
