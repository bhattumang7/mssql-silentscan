using SilentScan.Core.Reporting.Sarif;

namespace SilentScan.Core.Reporting.RuleDocs.JsonBuiltin;

internal static class JsonObjectDuplicateKey
{
    public static string RuleId => SarifRuleCatalog.JsonObjectDuplicateKeyRuleId;

    public static RuleDocContent Content { get; } = new(
        WhyItMatters: """
            JSON_OBJECT builds a JSON object from a list of key/value pairs, but the engine
            never checks whether two of those keys are the same string. Oracle-confirmed (SQL
            Server 2025): JSON_OBJECT('a':1, 'a':2) is accepted with no error and produces
            {"a":1,"a":2} - both keys survive in the JSON text unchanged, which itself already
            looks wrong to anyone who assumes JSON objects have unique keys.

            The real trap is what happens next: JSON_VALUE and every other single-value JSON
            reader always resolve a repeated key to its first occurrence. Oracle-confirmed that
            JSON_VALUE(..., '$.a') against {"a":1,"a":2} returns 1, silently discarding the 2 -
            so whichever value was written under the later, "overriding" pair is the one that
            actually gets lost, with nothing in the write or the read raising an error or a
            warning to say so. This is easy to introduce by accident: copy-pasting a key/value
            pair and only updating the value, or two branches of logic each independently
            contributing a pair with the same key name.

            Only a literal, case-sensitive key match is flagged - a key built from a variable,
            parameter, or expression could coincide with another key at runtime, but that isn't
            something this tool (or SQL Server's own catalog) can decide ahead of time.
            """,
        HowToFixIt: """
            Give each key/value pair in the JSON_OBJECT call a distinct key. If the intent was to
            overwrite an earlier value, replace the earlier pair instead of adding a new one with
            the same key.
            """,
        Examples:
        [
            new RuleDocExample(
                Title: "JSON_OBJECT with a repeated literal key",
                NoncompliantSql: """
                    SELECT JSON_OBJECT('status': 'pending', 'status': @newStatus) AS Doc;
                    """,
                NoncompliantExplanation: "Both 'status' pairs are accepted with no error and both appear in the JSON text, but JSON_VALUE(Doc, '$.status') always returns 'pending' - the value of @newStatus is silently discarded.",
                CompliantSql: """
                    SELECT JSON_OBJECT('status': @newStatus) AS Doc;
                    """,
                CompliantExplanation: "A single 'status' key means there is nothing for a JSON reader to silently choose between."),
        ]);
}
