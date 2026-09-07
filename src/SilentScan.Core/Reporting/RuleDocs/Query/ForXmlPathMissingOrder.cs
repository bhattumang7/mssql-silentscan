using SilentScan.Core.Reporting.Sarif;

namespace SilentScan.Core.Reporting.RuleDocs.Query;

internal static class ForXmlPathMissingOrder
{
    public static string RuleId => SarifRuleCatalog.ForXmlPathMissingOrderRuleId;

    public static RuleDocContent Content { get; } = new(
        WhyItMatters: """
            SELECT ... FOR XML PATH('') is a long-standing idiom for concatenating a column across
            rows into a single string (often wrapped in STUFF to drop a leading separator), predating
            STRING_AGG. Without an ORDER BY, the order those rows are concatenated in is not part of
            the query's contract - it's whatever order the chosen plan happens to feed the query in.
            Oracle-confirmed directly, with the identical measurement STRING_AGG's own missing-order
            finding uses: concatenating the same rows via this idiom with no ORDER BY produces one
            string against a heap-scanned plan and a different string, same rows, once a supporting
            index removes the need for an explicit Sort - no error, no warning, just a different
            result.

            This is easy to miss for the same reason STRING_AGG's missing-order gap is: a given query,
            run repeatedly against a stable database, usually looks consistent because the same plan
            tends to get chosen. That stability breaks precisely when something about the environment
            changes - a new index, a statistics update, a restored copy of the database - not when the
            query text changes.
            """,
        HowToFixIt: """
            Add an explicit ORDER BY to the subquery that drives the FOR XML PATH concatenation,
            naming the column(s) that should determine the resulting order. Where possible, prefer
            STRING_AGG WITH WITHIN GROUP (ORDER BY ...) instead - it expresses the same intent without
            the FOR XML PATH/STUFF machinery.
            """,
        Examples:
        [
            new RuleDocExample(
                Title: "FOR XML PATH concatenation with no ORDER BY",
                NoncompliantSql: """
                    SELECT G.GroupId,
                           STUFF((
                               SELECT ',' + M.Name
                               FROM dbo.Member AS M
                               WHERE M.GroupId = G.GroupId
                               FOR XML PATH('')
                           ), 1, 1, '') AS Names
                    FROM dbo.Grp AS G;
                    """,
                NoncompliantExplanation: "With no ORDER BY inside the correlated subquery, the order Name values are concatenated in is not guaranteed - it follows whatever order the plan happens to deliver rows in, and can change silently if an index is added, dropped, or the optimizer otherwise picks a different plan.",
                CompliantSql: """
                    SELECT G.GroupId,
                           STUFF((
                               SELECT ',' + M.Name
                               FROM dbo.Member AS M
                               WHERE M.GroupId = G.GroupId
                               ORDER BY M.Name
                               FOR XML PATH('')
                           ), 1, 1, '') AS Names
                    FROM dbo.Grp AS G;
                    """,
                CompliantExplanation: "ORDER BY M.Name makes the concatenation order an explicit, guaranteed contract of the query instead of an accident of plan choice."),
        ]);
}
