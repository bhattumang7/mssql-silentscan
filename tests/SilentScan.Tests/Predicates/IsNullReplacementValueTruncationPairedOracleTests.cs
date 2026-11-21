using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.IsNullReplacementValueTruncationRuleId)]
public sealed class IsNullReplacementValueTruncationPairedOracleTests : RuleOracle
{
    private const string Setup = """
        CREATE TABLE dbo.W (
            Short VARCHAR(3) NULL, Long VARCHAR(10) NOT NULL,
            Small DECIMAL(5,2) NULL, Big DECIMAL(10,4) NOT NULL,
            Ascii VARCHAR(10) NULL, Wide NVARCHAR(10) NOT NULL);
        GO
        INSERT INTO dbo.W (Short, Long, Small, Big, Ascii, Wide) VALUES (NULL, 'abcdef', NULL, 123.4567, NULL, NCHAR(26085) + NCHAR(26412));
        GO
        """;

    private const string Length = "length-truncation";

    private const string Scale = "numeric-scale-narrowing";

    private const string Unicode = "unicode-to-non-unicode";

    private static IsNullReplacementValueTruncationFinding Claim(WriteLossKind kind) =>
        new("check", "type", "replacement", "type", kind, "claimed.sql", 1, 1);

    protected override IReadOnlyList<string> ExcludedForms { get; } = ["procedure", "view"];

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(Length, Setup, "SELECT ISNULL(Short, Long) AS V FROM dbo.W;"),
        new(Scale, Setup, "SELECT ISNULL(Small, Big) AS V FROM dbo.W;"),
        new(Unicode, Setup, "SELECT ISNULL(Ascii, Wide) AS V FROM dbo.W;"),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("replacement-not-wider", Setup, "SELECT ISNULL(Long, Short) AS V FROM dbo.W;"), Claim(WriteLossKind.LengthTruncation), Length),
        new(new("scale-not-narrowed", Setup, "SELECT ISNULL(Big, Small) AS V FROM dbo.W;", "replacement-not-wider"), Claim(WriteLossKind.NumericScaleNarrowing), Scale),
        new(new("unicode-not-narrowed", Setup, "SELECT ISNULL(Wide, Ascii) AS V FROM dbo.W;", "replacement-not-wider"), Claim(WriteLossKind.UnicodeToNonUnicodeReplacement), Unicode),
        new(new("literal-fits", Setup, "SELECT ISNULL(Short, 'ab') AS V FROM dbo.W;"), Claim(WriteLossKind.LengthTruncation), Length),
    ];
}
