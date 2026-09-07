using SilentScan.Core.Reporting.Sarif;

namespace SilentScan.Core.Reporting.RuleDocs.StringBuiltin;

internal static class RegexpDefaultCaseSensitiveOnCiColumn
{
    public static string RuleId => SarifRuleCatalog.RegexpDefaultCaseSensitiveOnCiColumnRuleId;

    public static RuleDocContent Content { get; } = new(
        WhyItMatters: """
            Every T-SQL string comparison - =, LIKE, ORDER BY, GROUP BY - follows the operand's
            own collation for case sensitivity. REGEXP_LIKE, REGEXP_REPLACE, REGEXP_COUNT, and
            REGEXP_SUBSTR do not: oracle-confirmed (SQL Server 2025) they always match
            case-sensitively unless an explicit match_type argument containing 'i' is passed, no
            matter what collation the operand carries. Against a table using the common
            SQL_Latin1_General_CP1_CI_AS default collation, WHERE Name = 'abc' and WHERE Name LIKE
            'abc' both match 'ABC', 'Abc', and 'abc' - but WHERE REGEXP_LIKE(Name, 'abc') with no
            match_type argument matches only the literal-case 'abc' row, silently dropping the
            others with no error.

            This makes REGEXP_LIKE and its siblings an easy trap when reached for as a drop-in
            replacement for LIKE or a same-collation equality check on a case-insensitive column:
            the call succeeds, returns fewer rows (or a different substring/count/replacement)
            than the equivalent LIKE/= would, and nothing about the result signals that case
            sensitivity is the reason. Passing 'i' as the match_type argument restores the
            collation-consistent, case-insensitive behavior - and if both 'c' and 'i' appear in the
            match_type string, the last one wins, so 'ic' is still case-sensitive.

            Only fires when the subject is a column whose resolved collation is case-insensitive,
            the pattern is a literal containing at least one letter (a pattern with no letters can
            never be affected by case), and the match_type argument is either absent or a literal
            whose last c/i flag isn't 'i' - a non-literal match_type could still resolve to 'i' at
            runtime and isn't flagged.
            """,
        HowToFixIt: """
            Pass an explicit match_type argument ending in 'i' whenever the regex call's subject
            has a case-insensitive collation and case shouldn't matter.
            """,
        Examples:
        [
            new RuleDocExample(
                Title: "REGEXP_LIKE against a case-insensitive column with no match_type",
                NoncompliantSql: """
                    SELECT * FROM dbo.Customer WHERE REGEXP_LIKE(Name, '[Jj]ohn');
                    """,
                NoncompliantExplanation: "Name uses the database's default case-insensitive collation, but REGEXP_LIKE still matches case-sensitively with no match_type argument, silently missing rows that = 'John' or LIKE '%John%' would find.",
                CompliantSql: """
                    SELECT * FROM dbo.Customer WHERE REGEXP_LIKE(Name, '[Jj]ohn', 'i');
                    """,
                CompliantExplanation: "The explicit 'i' match_type restores case-insensitive matching, consistent with the column's own collation."),
        ]);
}
