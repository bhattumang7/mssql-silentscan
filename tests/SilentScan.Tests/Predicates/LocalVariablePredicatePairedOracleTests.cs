using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.LocalVariablePredicateRuleId)]
public sealed class LocalVariablePredicatePairedOracleTests : RuleOracle
{
    private const string Base = """
        CREATE TABLE dbo.T (Id INT NOT NULL, B INT NOT NULL);
        GO
        INSERT INTO dbo.T (Id, B)
        SELECT TOP (30000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)), CASE WHEN ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) % 100 = 0 THEN ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) ELSE 1 END
        FROM sys.all_objects a CROSS JOIN sys.all_objects b;
        GO
        """;

    private const string Plain = Base + "\nCREATE PROCEDURE dbo.P AS BEGIN DECLARE @v INT = 5000; SELECT Id FROM dbo.T WHERE B = @v; END\nGO\n";

    private const string Recompiled = Base + "\nCREATE PROCEDURE dbo.P AS BEGIN DECLARE @v INT = 5000; SELECT Id FROM dbo.T WHERE B = @v OPTION (RECOMPILE); END\nGO\n";

    private const string LocalVariable = "predicate-on-a-local-variable";

    private const string Statement = "EXEC dbo.P;";

    protected override IReadOnlyList<Func<IFinding, IFinding>> ClaimMutations { get; } =
    [
        finding => ((LocalVariablePredicateFinding)finding) with { SourcePath = "dbo.Other" },
        finding => ((LocalVariablePredicateFinding)finding) with { VariableName = "@missing" },
    ];

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(LocalVariable, Plain, Statement),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("statement-with-option-recompile", Recompiled, Statement), LocalVariable, OwnSetup: true),
    ];
}
