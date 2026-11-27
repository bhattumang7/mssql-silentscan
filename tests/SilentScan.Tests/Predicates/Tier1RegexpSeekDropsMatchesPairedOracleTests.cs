using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.Tier1RegexpSeekDropsMatchesRuleId)]
public sealed class Tier1RegexpSeekDropsMatchesPairedOracleTests : RuleOracle
{
    private const string Multiline = "multiline-flag";

    private const string BinaryIgnoreCase = "ignore-case-flag-on-binary-collation";

    private const string Rows = """
        INSERT INTO dbo.T (Name) VALUES ('john1'), ('JOHN2'), ('John3'), (CONCAT('x', CHAR(10), 'john4')), ('1201'), ('1202');
        GO
        INSERT INTO dbo.T (Name) SELECT TOP (2000) CONCAT('k', ROW_NUMBER() OVER (ORDER BY (SELECT NULL))) FROM sys.all_columns;
        GO
        """;

    private const string Binary = """
        CREATE TABLE dbo.T (Name VARCHAR(40) COLLATE Latin1_General_100_BIN2 NOT NULL);
        GO
        CREATE INDEX IX_T_Name ON dbo.T (Name);
        GO

        """ + Rows;

    private const string Insensitive = """
        CREATE TABLE dbo.T (Name VARCHAR(40) COLLATE Latin1_General_100_CI_AS NOT NULL);
        GO
        CREATE INDEX IX_T_Name ON dbo.T (Name);
        GO

        """ + Rows;

    private static string Counting(string pattern, string flags) =>
        $"SELECT COUNT(*) AS Matches FROM dbo.T WHERE REGEXP_LIKE(Name, '{pattern}', '{flags}');";

    protected override IReadOnlyList<string> ScannerOnlyForms => [StatementForms.Procedure, StatementForms.View];

    protected override IReadOnlyList<Func<IFinding, IFinding>> ClaimMutations { get; } =
    [
        finding => ((SargabilityFinding)finding) with { TableQualifiedName = "dbo.Other" },
        finding => ((SargabilityFinding)finding) with { PredicateFragmentText = null },
    ];

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(Multiline, Insensitive, Counting("^john", "m")),
        new(BinaryIgnoreCase, Binary, Counting("^john", "i")),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("ignore-case-flag-on-insensitive-collation", Insensitive, Counting("^john", "i")), Multiline),
        new(new("case-sensitive-flag-on-binary-collation", Binary, Counting("^john", "c")), BinaryIgnoreCase),
        new(new("pattern-without-letters", Binary, Counting("^120", "i")), BinaryIgnoreCase),
        new(new("flags-without-multiline", Insensitive, Counting("^john", "s")), Multiline),
    ];
}
