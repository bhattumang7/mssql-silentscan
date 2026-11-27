using SilentScan.Core.Predicates;
using SilentScan.Core.Reporting.Sarif;

namespace SilentScan.Core.Reporting.RuleDocs.BoundedStringBuiltinTruncation;

internal static class ReplicateResultTruncated
{
    public static string RuleId => SarifRuleCatalog.BoundedStringBuiltinTruncationRuleId(BoundedStringBuiltinTruncationFindingKind.ReplicateResultTruncated);

    public static RuleDocContent Content { get; } = new(
        WhyItMatters: """
            REPLICATE's return type follows its first (source) argument: when that argument is
            VARCHAR(MAX)/NVARCHAR(MAX), the result can grow arbitrarily long, but when it is any
            other string type - including a plain string literal - the result is capped at
            VARCHAR(8000)/NVARCHAR(4000) no matter how large the requested repeat count is. This
            was probed directly against a real engine: REPLICATE('abcdefghij', 900), whose true
            repeated length is 9000 bytes, comes back exactly 8000 bytes long with no error and no
            warning - the extra 1000 bytes are simply gone.
            When the source length does not divide the cap evenly the engine keeps only whole
            copies, so the result can be a little shorter than the cap (REPLICATE('abc', 4000)
            returns 7998 bytes).

            When both the source literal and the repeat count are compile-time constants, the exact
            repeated length is knowable from the source text alone - no catalog access or runtime
            data is needed to prove the overflow.

            The 8000 limit counts bytes, so in a database with a UTF-8 collation a literal made of
            multi-byte characters reaches it with fewer characters.
            """,
        HowToFixIt: """
            Cast the source literal to VARCHAR(MAX)/NVARCHAR(MAX) before repeating it
            (REPLICATE(CAST('x' AS VARCHAR(MAX)), 9000)) if the full repeated length is genuinely
            needed - a MAX-typed source is never capped this way.
            """,
        Examples:
        [
            new RuleDocExample(
                Title: "REPLICATE past the 8000-byte cap",
                NoncompliantSql: "SELECT REPLICATE('abcdefghij', 900);",
                NoncompliantExplanation: "The true repeated length is 9000 bytes; the non-MAX-typed result is silently truncated to exactly 8000 bytes.",
                CompliantSql: "SELECT REPLICATE(CAST('abcdefghij' AS VARCHAR(MAX)), 900);",
                CompliantExplanation: "A VARCHAR(MAX) source is never capped - the full 9000-byte result comes back intact."),
        ]);
}
