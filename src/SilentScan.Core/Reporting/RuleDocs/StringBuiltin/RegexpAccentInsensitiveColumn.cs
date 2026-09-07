using SilentScan.Core.Reporting.Sarif;

namespace SilentScan.Core.Reporting.RuleDocs.StringBuiltin;

internal static class RegexpAccentInsensitiveColumn
{
    public static string RuleId => SarifRuleCatalog.RegexpAccentInsensitiveColumnRuleId;

    public static RuleDocContent Content { get; } = new(
        WhyItMatters: """
            An _AI (accent-insensitive) collation makes = and LIKE fold accented characters
            together - 'cafe' matches 'café' the same way a case-insensitive collation folds
            'ABC' and 'abc'. REGEXP_LIKE, REGEXP_REPLACE, REGEXP_COUNT, REGEXP_SUBSTR,
            REGEXP_MATCHES, and REGEXP_SPLIT_TO_TABLE never do this, and unlike the equivalent
            case-sensitivity gap, there is no flag that fixes it: oracle-confirmed (SQL Server
            2025) the match_type argument only accepts c, i, s, and m (case, case-insensitive,
            dot-matches-newline, multiline anchors) - none of them restores accent folding.
            Against a column using SQL_Latin1_General_CP1_CI_AI, WHERE Name = 'cafe' matches a
            row storing 'café', but WHERE REGEXP_LIKE(Name, 'cafe') never will, no matter what
            match_type is passed, with no error to say so.

            This makes REGEXP_* a strictly narrower replacement for =/LIKE on an accent-insensitive
            column: reaching for it as a drop-in for a LIKE-based search permanently drops accent
            folding for that comparison, and there is no REGEXP_* argument that gets it back.

            Only fires when the subject is a column whose resolved collation is accent-insensitive
            and the pattern is a literal containing at least one letter (a pattern with none can
            never be affected by accent folding).
            """,
        HowToFixIt: """
            REGEXP_* has no accent-insensitive match mode. If the subject's accent-insensitive
            collation is load-bearing, use =/LIKE instead of REGEXP_*, or accept that the regex
            call will only match the exact accented form written in the pattern.
            """,
        Examples:
        [
            new RuleDocExample(
                Title: "REGEXP_LIKE against an accent-insensitive column",
                NoncompliantSql: """
                    SELECT * FROM dbo.Customer WHERE REGEXP_LIKE(Name, 'cafe');
                    """,
                NoncompliantExplanation: "Name uses an accent-insensitive collation, but REGEXP_LIKE never folds accents - a row storing 'café' is silently never matched, and no match_type flag can fix it.",
                CompliantSql: """
                    SELECT * FROM dbo.Customer WHERE Name = 'cafe';
                    """,
                CompliantExplanation: "= follows the column's own accent-insensitive collation, correctly matching 'café'."),
        ]);
}
