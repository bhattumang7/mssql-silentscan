using SilentScan.Core.Reporting.Sarif;

namespace SilentScan.Core.Reporting.RuleDocs.StringBuiltin;

internal static class StringAggMissingOrder
{
    public static string RuleId => SarifRuleCatalog.StringAggMissingOrderRuleId;

    public static RuleDocContent Content { get; } = new(
        WhyItMatters: """
            STRING_AGG concatenates the rows in a group, but without a WITHIN GROUP (ORDER BY ...)
            clause, the order those rows are concatenated in is not part of the query's contract -
            it's whatever order the chosen plan happens to feed the aggregate. Oracle-confirmed
            directly: STRING_AGG always compiles to a Stream Aggregate, which requires its input
            sorted by the GROUP BY key; when no index already provides that order the optimizer
            adds an explicit Sort, and rows sharing the same key come out of that Sort in whatever
            order the sort algorithm happens to leave equal keys in - not necessarily the order
            they were inserted. Adding an index on the grouped/concatenated columns changes the
            access path so the Sort is no longer needed, and the concatenation order silently
            changes as a direct, measured result - same rows, same query text, different index,
            different string, no error and no warning.

            This is easy to miss because a given STRING_AGG query, run repeatedly against a stable
            database, usually looks consistent - the same plan tends to get chosen, so the same
            "accidental" order keeps showing up. That stability breaks exactly when something about
            the environment changes rather than the query: a new index, a statistics update that
            flips the access path, or restoring the same query against a differently-indexed copy
            of the database. Code that depends on STRING_AGG's output being in some particular
            order - insertion order, primary key order, whatever it happened to look like in
            testing - is trusting an accident of plan choice, not a guarantee.
            """,
        HowToFixIt: """
            Add an explicit WITHIN GROUP (ORDER BY ...) clause naming the column(s) that should
            determine the concatenation order. This makes the order part of the query's own
            contract instead of an accident of whichever access path the optimizer picks.
            """,
        Examples:
        [
            new RuleDocExample(
                Title: "STRING_AGG with no WITHIN GROUP",
                NoncompliantSql: """
                    SELECT GroupId, STRING_AGG(Name, ',') AS Names
                    FROM dbo.Member
                    GROUP BY GroupId;
                    """,
                NoncompliantExplanation: "With no WITHIN GROUP, the order Name values are concatenated in is not guaranteed - it follows whatever order the plan happens to deliver rows in, and can change silently if an index is added, dropped, or the optimizer otherwise picks a different plan.",
                CompliantSql: """
                    SELECT GroupId, STRING_AGG(Name, ',') WITHIN GROUP (ORDER BY Name) AS Names
                    FROM dbo.Member
                    GROUP BY GroupId;
                    """,
                CompliantExplanation: "WITHIN GROUP (ORDER BY Name) makes the concatenation order an explicit, guaranteed contract of the query instead of an accident of plan choice."),
        ]);
}
