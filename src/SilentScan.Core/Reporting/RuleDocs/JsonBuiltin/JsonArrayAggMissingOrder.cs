using SilentScan.Core.Reporting.Sarif;

namespace SilentScan.Core.Reporting.RuleDocs.JsonBuiltin;

internal static class JsonArrayAggMissingOrder
{
    public static string RuleId => SarifRuleCatalog.JsonArrayAggMissingOrderRuleId;

    public static RuleDocContent Content { get; } = new(
        WhyItMatters: """
            JSON_ARRAYAGG aggregates the rows in a group into a JSON array, but without an
            ORDER BY inside the call's own argument list, the order those values appear in the
            array is not part of the query's contract - it's whatever order the chosen plan
            happens to feed the aggregate. This is the same missing-order gap STRING_AGG and
            FOR XML PATH concatenation already have: a new index, a statistics update that flips
            the access path, or restoring the same query against a differently-indexed copy of
            the database can all silently change the array's element order with no error and no
            warning.

            JSON_ARRAYAGG orders itself differently from STRING_AGG, and that difference is a
            trap of its own: STRING_AGG takes WITHIN GROUP (ORDER BY ...), but JSON_ARRAYAGG does
            not - its ORDER BY goes inside the call's own argument list instead, e.g.
            JSON_ARRAYAGG(col ORDER BY col). Oracle-confirmed (SQL Server 2025) that writing
            JSON_ARRAYAGG(col) WITHIN GROUP (ORDER BY col) - the STRING_AGG-shaped syntax someone
            reaching for "the way I always order an aggregate" would naturally write - is accepted
            by the engine with no error and has no effect at all on the array's element order.
            """,
        HowToFixIt: """
            Add an ORDER BY inside the JSON_ARRAYAGG call's own argument list, e.g.
            JSON_ARRAYAGG(col ORDER BY col), to make the array's element order an explicit,
            guaranteed contract of the query instead of an accident of plan choice. Do not use
            WITHIN GROUP (ORDER BY ...) here - the engine accepts it without error but it has no
            effect on JSON_ARRAYAGG's output order.
            """,
        Examples:
        [
            new RuleDocExample(
                Title: "JSON_ARRAYAGG with no ORDER BY",
                NoncompliantSql: """
                    SELECT GroupId, JSON_ARRAYAGG(Name) AS Names
                    FROM dbo.Member
                    GROUP BY GroupId;
                    """,
                NoncompliantExplanation: "With no ORDER BY inside the call, the order Name values appear in the array is not guaranteed - it follows whatever order the plan happens to deliver rows in, and can change silently if an index is added, dropped, or the optimizer otherwise picks a different plan.",
                CompliantSql: """
                    SELECT GroupId, JSON_ARRAYAGG(Name ORDER BY Name) AS Names
                    FROM dbo.Member
                    GROUP BY GroupId;
                    """,
                CompliantExplanation: "The ORDER BY inside the call makes the array's element order an explicit, guaranteed contract of the query instead of an accident of plan choice."),
        ]);
}
