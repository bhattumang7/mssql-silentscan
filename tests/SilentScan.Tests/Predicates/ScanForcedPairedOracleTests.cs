using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.VerdictScanForcedRuleId)]
public sealed class ScanForcedPairedOracleTests : RuleOracle
{
    private const string Setup = """
        CREATE TABLE dbo.T (Id INT NOT NULL PRIMARY KEY, Code VARCHAR(20) NOT NULL, Pad CHAR(100) NOT NULL DEFAULT 'x');
        GO
        INSERT INTO dbo.T (Id, Code)
        SELECT TOP (60000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)), CAST(ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS VARCHAR(20))
        FROM sys.all_objects a CROSS JOIN sys.all_objects b;
        CREATE INDEX IX_T_Code ON dbo.T (Code);
        GO
        """;

    private const string UnicodeColumn = """
        CREATE TABLE dbo.T (Id INT NOT NULL PRIMARY KEY, Code NVARCHAR(20) NOT NULL, Pad CHAR(100) NOT NULL DEFAULT 'x');
        GO
        INSERT INTO dbo.T (Id, Code)
        SELECT TOP (60000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)), CAST(ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS NVARCHAR(20))
        FROM sys.all_objects a CROSS JOIN sys.all_objects b;
        CREATE INDEX IX_T_Code ON dbo.T (Code);
        GO
        """;

    private const string Converted = "column-converted-to-the-literal-type";

    protected override IReadOnlyList<string> ScannerOnlyForms { get; } =
        [StatementForms.Procedure, StatementForms.View, StatementForms.DynamicSql, StatementForms.CommonTableExpression, StatementForms.DerivedTable];

    protected override IReadOnlyList<Func<IFinding, IFinding>> ClaimMutations { get; } =
    [
        finding => ((TypedPredicateFinding)finding) with { OtherOperand = new PredicateOperand.Value(null, true, "N'other'") },
    ];

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(Converted, Setup, "SELECT Id FROM dbo.T WHERE Code = N'1234';"),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("unicode-column-compared-to-a-unicode-literal", UnicodeColumn, "SELECT Id FROM dbo.T WHERE Code = N'1234';"), Converted, OwnSetup: true),
    ];
}
