using SilentScan.Core.Predicates;
using SilentScan.Core.Reporting.Sarif;

namespace SilentScan.Core.Reporting.RuleDocs.Tier1;

internal static class RegexpPatternNoLiteralPrefix
{
    public static string RuleId => SarifRuleCatalog.Tier1RuleId(SargabilityFindingKind.RegexpPatternNoLiteralPrefix);

    public static RuleDocContent Content { get; } = new(
        WhyItMatters: """
            REGEXP_LIKE only produces a seek-shaped plan when the engine can derive a bounded
            range of values the pattern can match. For a literal pattern, that derivation reads the
            characters that follow a leading anchor. Plain characters, escaped punctuation, a
            bracket class and most quantifiers after them still leave a usable range. A missing
            anchor, a wildcard dot, a group, an alternation, a trailing anchor straight after the
            literal characters, a shorthand class such as \d, or an unescaped hyphen defeats it,
            and so does a quantifier that makes the only literal character optional. The engine
            then scans every row and evaluates the pattern per row.
            """,
        HowToFixIt: """
            Rewrite the pattern so it starts with an anchor followed by literal characters (e.g.
            '^John' instead of 'John' or '^(John|Jon)'), if the intent really is a prefix match.
            Escape literal punctuation such as a hyphen. If the pattern genuinely needs the richer
            regex construct, there is no seek-shaped alternative for that predicate - consider a
            computed/persisted column that captures just the prefix and indexing that instead.
            """,
        Examples:
        [
            new RuleDocExample(
                Title: "A literal pattern with no anchored literal prefix forces a scan",
                NoncompliantSql: """
                    ALTER DATABASE CURRENT SET COMPATIBILITY_LEVEL = 170;
                    GO
                    CREATE TABLE dbo.Products
                    (
                        ProductId INT           NOT NULL PRIMARY KEY,
                        Sku       NVARCHAR(50)  NOT NULL
                    );
                    CREATE INDEX IX_Products_Sku ON dbo.Products(Sku);

                    SELECT ProductId
                    FROM dbo.Products
                    WHERE REGEXP_LIKE(Sku, 'Sku1');
                    """,
                NoncompliantExplanation: "The pattern has no leading anchor, so the engine cannot derive any range of values it could match and scans every row.",
                CompliantSql: """
                    ALTER DATABASE CURRENT SET COMPATIBILITY_LEVEL = 170;
                    GO
                    CREATE TABLE dbo.Products
                    (
                        ProductId INT           NOT NULL PRIMARY KEY,
                        Sku       NVARCHAR(50)  NOT NULL
                    );
                    CREATE INDEX IX_Products_Sku ON dbo.Products(Sku);

                    SELECT ProductId
                    FROM dbo.Products
                    WHERE REGEXP_LIKE(Sku, '^Sku1');
                    """,
                CompliantExplanation: "The pattern starts with an anchor followed by literal characters, so the engine derives a seek range from it and produces an Index Seek.",
                RequiresLatestEngine: true),
        ]);
}
