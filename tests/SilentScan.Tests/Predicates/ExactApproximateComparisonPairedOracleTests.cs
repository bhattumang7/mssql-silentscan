using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.ExactApproximateComparisonRuleId)]
public sealed class ExactApproximateComparisonPairedOracleTests : RuleOracle
{
    private const string BigintAgainstFloatSetup = """
        CREATE TABLE dbo.Accounts (Id BIGINT NOT NULL);
        CREATE TABLE dbo.Imports (Ref FLOAT NOT NULL, Other INT NOT NULL);
        GO
        INSERT INTO dbo.Accounts (Id) VALUES (9007199254740992), (9007199254740993);
        INSERT INTO dbo.Imports (Ref, Other) VALUES (9007199254740992, 1);
        GO
        """;

    private const string IntAgainstFloatSetup = """
        CREATE TABLE dbo.Accounts (Id INT NOT NULL);
        CREATE TABLE dbo.Imports (Ref FLOAT NOT NULL, Other INT NOT NULL);
        GO
        INSERT INTO dbo.Accounts (Id) VALUES (1), (2);
        INSERT INTO dbo.Imports (Ref, Other) VALUES (1, 1);
        GO
        """;

    private const string WideDecimalSetup = """
        CREATE TABLE dbo.Accounts (Id DECIMAL(18,0) NOT NULL);
        CREATE TABLE dbo.Imports (Ref FLOAT NOT NULL, Other INT NOT NULL);
        GO
        INSERT INTO dbo.Accounts (Id) VALUES (9007199254740992), (9007199254740993);
        INSERT INTO dbo.Imports (Ref, Other) VALUES (9007199254740992, 1);
        GO
        """;

    private const string IntAgainstRealSetup = """
        CREATE TABLE dbo.Accounts (Id INT NOT NULL);
        CREATE TABLE dbo.Imports (Ref REAL NOT NULL, Other INT NOT NULL);
        GO
        INSERT INTO dbo.Accounts (Id) VALUES (16777216), (16777217);
        INSERT INTO dbo.Imports (Ref, Other) VALUES (16777216, 1);
        GO
        """;

    private const string NarrowDecimalSetup = """
        CREATE TABLE dbo.Accounts (Id DECIMAL(10,2) NOT NULL);
        CREATE TABLE dbo.Imports (Ref FLOAT NOT NULL, Other INT NOT NULL);
        GO
        INSERT INTO dbo.Accounts (Id) VALUES (1.00), (2.00);
        INSERT INTO dbo.Imports (Ref, Other) VALUES (1, 1);
        GO
        """;

    private const string BigintAgainstFloat = "bigint-against-float";

    private const string Statement = "SELECT Id FROM dbo.Accounts JOIN dbo.Imports ON Ref = Id;";

    protected override IReadOnlyList<Func<IFinding, IFinding>> ClaimMutations { get; } =
    [
        finding => ((ExactApproximateComparisonFinding)finding) with { ApproximateColumn = "dbo.Other.Ref" },
        finding => ((ExactApproximateComparisonFinding)finding) with { ApproximateColumn = "dbo.Imports.Other" },
    ];

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(BigintAgainstFloat, BigintAgainstFloatSetup, Statement),
        new("wide-decimal-against-float", WideDecimalSetup, Statement),
        new("int-against-real", IntAgainstRealSetup, Statement),
        new("range-comparison", BigintAgainstFloatSetup, "SELECT Id FROM dbo.Accounts JOIN dbo.Imports ON Ref < Id;"),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("int-against-float", IntAgainstFloatSetup, Statement), BigintAgainstFloat, OwnSetup: true),
        new(new("narrow-decimal-against-float", NarrowDecimalSetup, Statement), BigintAgainstFloat, OwnSetup: true),
    ];
}
