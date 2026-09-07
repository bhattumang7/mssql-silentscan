using SilentScan.Core.Reporting.Sarif;

namespace SilentScan.Core.Reporting.RuleDocs.StringBuiltin;

internal static class UnistrUnpairedSurrogate
{
    public static string RuleId => SarifRuleCatalog.UnistrUnpairedSurrogateRuleId;

    public static RuleDocContent Content { get; } = new(
        WhyItMatters: """
            UNISTR turns \XXXX and \+XXXXXX escape sequences in a string literal into the Unicode
            code points they name. A code point in the UTF-16 surrogate range (U+D800-U+DFFF) is
            never a valid character on its own - it only means something as one half of a
            high/low surrogate pair used to represent a character outside the Basic Multilingual
            Plane. Oracle-confirmed (SQL Server 2025): UNISTR(N'\D800') is accepted with no error
            and returns a two-byte NVARCHAR value holding that single, unpaired surrogate code
            unit - an ill-formed value under the Unicode standard that SQL Server itself never
            rejects, at creation, at conversion to a UTF-8 collation, or when serialized through
            FOR JSON. A high surrogate not immediately followed by its matching low surrogate, a
            low surrogate on its own, or a pair written in the wrong order, all produce the same
            silent result: one or more unpaired surrogate code units baked into the string.

            This is not a case where SQL Server can't validate surrogate pairs - its own JSON text
            parser does exactly that, rejecting an unpaired escape outright. UNISTR simply skips
            the check the engine already knows how to perform, so a typo in a hex digit (\D800
            instead of the intended \D841\DE41, say) turns a working escape into a lookalike that
            passes silently and can surface later as corrupted or rejected text wherever the value
            eventually crosses something that does enforce well-formed Unicode - a UTF-8 boundary,
            a downstream JSON consumer, or another system entirely.

            Only a literal string argument is checked - a UNISTR call built from a variable,
            parameter, or expression could still hide the same defect, but its content isn't known
            until runtime.
            """,
        HowToFixIt: """
            Only use a lone \XXXX or \+XXXXXX escape for a code point outside U+D800-U+DFFF, and
            always write a high surrogate immediately followed by its matching low surrogate as a
            pair when representing a character outside the Basic Multilingual Plane.
            """,
        Examples:
        [
            new RuleDocExample(
                Title: "UNISTR escape with an unpaired surrogate",
                NoncompliantSql: """
                    SELECT UNISTR(N'\D800') AS BrokenChar;
                    """,
                NoncompliantExplanation: "\\D800 is a lone high surrogate with no matching low surrogate immediately after it. The engine accepts this with no error and returns a value holding one unpaired, ill-formed code unit.",
                CompliantSql: """
                    SELECT UNISTR(N'\D800\DC00') AS FixedChar;
                    """,
                CompliantExplanation: "The high surrogate \\D800 is immediately followed by a valid low surrogate \\DC00, forming a well-formed pair that represents a single character outside the Basic Multilingual Plane."),
        ]);
}
