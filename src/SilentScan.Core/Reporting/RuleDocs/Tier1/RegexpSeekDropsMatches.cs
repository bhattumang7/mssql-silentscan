using SilentScan.Core.Predicates;
using SilentScan.Core.Reporting.Sarif;

namespace SilentScan.Core.Reporting.RuleDocs.Tier1;

internal static class RegexpSeekDropsMatches
{
    public static string RuleId => SarifRuleCatalog.Tier1RuleId(SargabilityFindingKind.RegexpSeekDropsMatches);

    public static RuleDocContent Content { get; } = new(
        WhyItMatters: """
            For a REGEXP_LIKE pattern that starts with an anchored literal, the engine derives an
            index range from that literal and can seek it, evaluating the pattern only on the rows
            the range returns. The range does not take the flags into account.

            With the `m` flag the anchor also matches at the start of every later line, but the
            range only holds values that begin with the literal. With the `i` flag on a binary
            collation the range keeps the case written in the pattern, so values in another case
            fall outside it. In both situations the query returns fewer rows through the seek than
            the same predicate returns through a scan, without an error. Which of the two happens
            depends on the plan, so the result can change when the data or statistics change.
            """,
        HowToFixIt: """
            Remove the flag when the pattern does not need it. Otherwise make the predicate one the
            index cannot seek, for example by wrapping the column in an expression, or force a scan
            with a table hint, so that every row reaches the pattern.
            """,
        Examples:
        [
            new RuleDocExample(
                Title: "Ignoring case on a binary collation loses rows through the index",
                NoncompliantSql: """
                    ALTER DATABASE CURRENT SET COMPATIBILITY_LEVEL = 170;
                    GO
                    CREATE TABLE dbo.Products
                    (
                        ProductId INT          NOT NULL PRIMARY KEY,
                        Sku       VARCHAR(50)  COLLATE Latin1_General_100_BIN2 NOT NULL
                    );
                    CREATE INDEX IX_Products_Sku ON dbo.Products(Sku);

                    SELECT ProductId
                    FROM dbo.Products
                    WHERE REGEXP_LIKE(Sku, '^sku-1', 'i');
                    """,
                NoncompliantExplanation: "A seek reads only values that start with lower-case sku-1, so a row holding SKU-1 is never tested.",
                CompliantSql: """
                    ALTER DATABASE CURRENT SET COMPATIBILITY_LEVEL = 170;
                    GO
                    CREATE TABLE dbo.Products
                    (
                        ProductId INT          NOT NULL PRIMARY KEY,
                        Sku       VARCHAR(50)  COLLATE Latin1_General_100_BIN2 NOT NULL
                    );
                    CREATE INDEX IX_Products_Sku ON dbo.Products(Sku);

                    SELECT ProductId
                    FROM dbo.Products
                    WHERE REGEXP_LIKE(CONCAT(Sku, ''), '^sku-1', 'i');
                    """,
                CompliantExplanation: "The index cannot be sought through the expression, so every row is tested against the pattern with the flag applied.",
                RequiresLatestEngine: true),
        ]);
}
