using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.QueryAntiPatternMultiRowInsertIgnoreDupKeyDropRuleId)]
public sealed class IgnoreDupKeyDropPairedOracleTests : RuleOracle
{
    private const string Setup = """
        CREATE TABLE dbo.Keyed (K INT NOT NULL);
        GO
        CREATE UNIQUE INDEX IX_Keyed ON dbo.Keyed (K) WITH (IGNORE_DUP_KEY = ON);
        GO
        CREATE TABLE dbo.Strict (K INT NOT NULL);
        GO
        CREATE UNIQUE INDEX IX_Strict ON dbo.Strict (K);
        GO
        """;

    private const string MultiRow = "multi-row-insert-with-duplicate";

    private static IFinding OnStrictTable(IFinding finding) =>
        ((QueryAntiPatternFinding)finding) with { TableQualifiedName = "dbo.Strict", IndexName = "IX_Strict" };

    private static IFinding OnMissingIndex(IFinding finding) =>
        ((QueryAntiPatternFinding)finding) with { IndexName = "IX_Missing" };

    private static IFinding OnOtherTable(IFinding finding) =>
        ((QueryAntiPatternFinding)finding) with { TableQualifiedName = "dbo.Other" };

    protected override IReadOnlyList<Func<IFinding, IFinding>> ClaimMutations { get; } = [OnMissingIndex, OnOtherTable];

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(MultiRow, Setup, "INSERT INTO dbo.Keyed (K) VALUES (1), (1), (2);"),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("single-row-insert", Setup, "INSERT INTO dbo.Keyed (K) VALUES (1);"), MultiRow),
        new(new("index-without-ignore-dup-key", Setup, "INSERT INTO dbo.Strict (K) VALUES (1), (1), (2);"), MultiRow, OnStrictTable),
    ];
}
