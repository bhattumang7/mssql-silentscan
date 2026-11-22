using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.Tier1CaseFoldOnColumnRuleId)]
public sealed class Tier1CaseFoldPairedOracleTests : RuleOracle
{
    private const string Base = """
        CREATE TABLE dbo.T (Id INT NOT NULL, Name VARCHAR(50) NOT NULL);
        GO
        INSERT INTO dbo.T (Id, Name)
        SELECT TOP (30000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)), 'name ' + CAST(ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS VARCHAR(10))
        FROM sys.all_objects a CROSS JOIN sys.all_objects b;
        GO
        """;

    private const string Unmatched = Base;

    private const string Matched = Base + "\nALTER TABLE dbo.T ADD NameUpper AS (UPPER(Name));\nGO\nCREATE INDEX IX_T_NameUpper ON dbo.T (NameUpper);\nGO\n";

    private const string Wrapped = "upper-wrapped-column";

    private const string Statement = "SELECT Id FROM dbo.T WHERE UPPER(Name) = 'NAME 5';";

    protected override IReadOnlyList<Func<IFinding, IFinding>> ClaimMutations { get; } =
    [
        finding => ((SargabilityFinding)finding) with { TableQualifiedName = "dbo.Other" },
        finding => ((SargabilityFinding)finding) with { PredicateFragmentText = null },
    ];

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(Wrapped, Unmatched, Statement),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("indexed-computed-column-matches", Matched, Statement), Wrapped, OwnSetup: true),
    ];
}
