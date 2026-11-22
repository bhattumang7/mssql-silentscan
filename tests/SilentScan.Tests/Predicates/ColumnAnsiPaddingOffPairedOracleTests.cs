using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.ColumnAnsiPaddingOffRuleId)]
public sealed class ColumnAnsiPaddingOffPairedOracleTests : RuleOracle
{
    private const string Unpadded = "SET ANSI_PADDING OFF;\nGO\nCREATE TABLE dbo.T (Code VARCHAR(10) NULL);\nGO\n";

    private const string Padded = "SET ANSI_PADDING ON;\nGO\nCREATE TABLE dbo.T (Code VARCHAR(10) NULL);\nGO\n";

    private const string Trimming = "column-created-with-ansi-padding-off";

    private const string Statement = "INSERT INTO dbo.T (Code) VALUES ('ab   ');";

    protected override IReadOnlyList<Func<IFinding, IFinding>> ClaimMutations { get; } =
    [
        finding => ((AnsiPaddingOffColumnFinding)finding) with { TableQualifiedName = "dbo.Other" },
        finding => ((AnsiPaddingOffColumnFinding)finding) with { ColumnName = "Other" },
    ];

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(Trimming, Unpadded, Statement),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("column-created-with-ansi-padding-on", Padded, Statement), Trimming, OwnSetup: true),
    ];
}
