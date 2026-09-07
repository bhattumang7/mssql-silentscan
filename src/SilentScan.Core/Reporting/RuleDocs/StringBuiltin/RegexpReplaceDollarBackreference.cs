using SilentScan.Core.Reporting.Sarif;

namespace SilentScan.Core.Reporting.RuleDocs.StringBuiltin;

internal static class RegexpReplaceDollarBackreference
{
    public static string RuleId => SarifRuleCatalog.RegexpReplaceDollarBackreferenceRuleId;

    public static RuleDocContent Content { get; } = new(
        WhyItMatters: """
            REGEXP_REPLACE substitutes a captured group's matched text into the replacement string
            using a backslash-digit token - \1 for the first capturing group, \2 for the second,
            and so on. Oracle-confirmed (SQL Server 2025): the $ character has no special meaning
            anywhere in a REGEXP_REPLACE replacement string. REGEXP_REPLACE('abc123def',
            '([a-z]+)([0-9]+)', '$2-$1') returns '$2-$1def' - the $2 and $1 tokens are accepted
            with no error and pass straight through into the output unchanged, while the
            backslash-digit form (\2-\1) correctly produces '123-abcdef'.

            $1/$2-style backreferences are the convention in .NET Regex.Replace, JavaScript,
            and most other regex-flavored replacement syntax, so reaching for $N here is an easy,
            silent mistake: the call succeeds, returns a string, and nothing about the result looks
            obviously broken unless the literal $N text is spotted in the output.

            Only fires when both the pattern and the replacement are string literals, and only when
            the referenced group number is within the pattern's own actual capturing-group count
            (non-capturing (?:...) groups and parentheses inside a character class don't count) -
            a $N with no plausible matching group in the pattern is far more likely a coincidental
            literal dollar amount than a mistaken backreference.
            """,
        HowToFixIt: """
            Reference a captured group with a backslash-digit token (\1, \2, ...), not a
            dollar-digit token ($1, $2, ...).
            """,
        Examples:
        [
            new RuleDocExample(
                Title: "REGEXP_REPLACE with a $N backreference",
                NoncompliantSql: """
                    SELECT REGEXP_REPLACE('abc123def', '([a-z]+)([0-9]+)', '$2-$1') AS Swapped;
                    """,
                NoncompliantExplanation: "$2 and $1 have no special meaning in REGEXP_REPLACE, so they pass through into the output unchanged: '$2-$1def' instead of the intended group swap.",
                CompliantSql: """
                    SELECT REGEXP_REPLACE('abc123def', '([a-z]+)([0-9]+)', '\2-\1') AS Swapped;
                    """,
                CompliantExplanation: "\\2 and \\1 are the engine's real backreference syntax, correctly producing '123-abcdef'."),
        ]);
}
