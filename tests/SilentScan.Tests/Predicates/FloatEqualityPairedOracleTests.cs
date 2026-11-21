using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.FloatEqualityRuleId)]
public sealed class FloatEqualityPairedOracleTests : RuleOracle
{
    private const string Setup = """
        CREATE TABLE dbo.T (Id INT NOT NULL, F FLOAT NOT NULL, R REAL NOT NULL, D DECIMAL(10,2) NOT NULL, I INT NOT NULL);
        GO
        INSERT INTO dbo.T (Id, F, R, D, I) VALUES (1, 0.3, CAST(0.3 AS REAL), 0.30, 3);
        GO
        """;

    private const string FloatEquals = "float-equality";

    private const string FloatNotEquals = "float-inequality";

    private const string RealEquals = "real-equality";

    private const string UpdatePredicate = "update-predicate";

    private const string DeletePredicate = "delete-predicate";

    private static FloatEqualityFinding Claim(string column, string type) => new("dbo.T", column, type, "claimed.sql", 1, 1);

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(FloatEquals, Setup, "SELECT Id FROM dbo.T WHERE F = 0.3;"),
        new(FloatNotEquals, Setup, "SELECT Id FROM dbo.T WHERE F <> 0.3;"),
        new(RealEquals, Setup, "SELECT Id FROM dbo.T WHERE R = CAST(0.3 AS REAL);"),
        new(UpdatePredicate, Setup, "UPDATE dbo.T SET I = 9 WHERE F = 0.3;"),
        new(DeletePredicate, Setup, "DELETE FROM dbo.T WHERE F = 0.3;"),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("decimal-column", Setup, "SELECT Id FROM dbo.T WHERE D = 0.30;"), Claim("D", "decimal(10,2)"), FloatEquals),
        new(new("integer-column", Setup, "SELECT Id FROM dbo.T WHERE I = 3;"), Claim("I", "int"), FloatEquals),
        new(new("range-comparison", Setup, "SELECT Id FROM dbo.T WHERE F > 0.1;"), Claim("F", "float"), FloatEquals),
    ];
}
