using SilentScan.Core.Reporting.Sarif;

namespace SilentScan.Core.Reporting.RuleDocs.Predicates;

internal static class IsNullReplacementValueTruncation
{
    public static string RuleId => SarifRuleCatalog.IsNullReplacementValueTruncationRuleId;

    public static RuleDocContent Content { get; } = new(
        WhyItMatters: """
            `ISNULL(check_expression, replacement_value)` always returns `check_expression`'s exact
            declared type - length, precision, scale, and collation included. This is different from
            `COALESCE`, which behaves like a `CASE` expression and merges the widest type across every
            argument it's given. When `replacement_value`'s own type is wider than `check_expression`'s -
            a longer string, a `DECIMAL` with more digits after the point, a wider `VARBINARY` - the
            engine still accepts the call and silently coerces `replacement_value` down to
            `check_expression`'s type at the moment it's substituted in: a numeric value is rounded, a
            string or binary value has its trailing content cut off, all without raising an error.

            This holds regardless of which branch actually gets evaluated at runtime - the type
            derivation, and therefore the narrowing, is a property of the call itself, not of whether
            `check_expression` turns out to be `NULL` for a given row. A caller who only ever sees rows
            where `check_expression` is `NULL` (so `replacement_value` is the value that ends up in the
            result) can be silently getting a rounded or truncated value indefinitely with nothing in
            the query text or a runtime message hinting at it.
            """,
        Examples:
        [
            new RuleDocExample(
                Title: "A wider replacement string is silently truncated to the check expression's length",
                NoncompliantSql: """
                    DECLARE @ShortCode VARCHAR(5) = NULL;
                    DECLARE @FullDescription VARCHAR(50) = 'A much longer fallback description';

                    SELECT ISNULL(@ShortCode, @FullDescription) AS Result;
                    """,
                NoncompliantExplanation: "ISNULL's result type is @ShortCode's VARCHAR(5), not the wider VARCHAR(50) that COALESCE would have picked - @FullDescription is silently cut down to 5 characters ('A muc') with no error, even though @ShortCode is NULL and @FullDescription is the value actually being returned.",
                CompliantSql: """
                    DECLARE @ShortCode VARCHAR(5) = NULL;
                    DECLARE @FullDescription VARCHAR(50) = 'A much longer fallback description';

                    SELECT COALESCE(@ShortCode, @FullDescription) AS Result;
                    """,
                CompliantExplanation: "COALESCE merges the widest type across all of its arguments, the same way a CASE expression does, so the result is typed VARCHAR(50) and @FullDescription comes through in full."),
            new RuleDocExample(
                Title: "A more precise replacement decimal is silently rounded to the check expression's scale",
                NoncompliantSql: """
                    DECLARE @CachedTotal DECIMAL(9, 2) = NULL;
                    DECLARE @ComputedTotal DECIMAL(18, 6) = 123.456789;

                    SELECT ISNULL(@CachedTotal, @ComputedTotal) AS Result;
                    """,
                NoncompliantExplanation: "ISNULL's result type is @CachedTotal's DECIMAL(9, 2), so @ComputedTotal is silently rounded to 123.46 on the way in, losing four digits of precision, with no error.",
                CompliantSql: """
                    DECLARE @CachedTotal DECIMAL(18, 6) = NULL;
                    DECLARE @ComputedTotal DECIMAL(18, 6) = 123.456789;

                    SELECT ISNULL(@CachedTotal, @ComputedTotal) AS Result;
                    """,
                CompliantExplanation: "With @CachedTotal declared at the same precision/scale as @ComputedTotal, ISNULL's result type carries no less precision than @ComputedTotal already has, so nothing is rounded away."),
        ]);
}
