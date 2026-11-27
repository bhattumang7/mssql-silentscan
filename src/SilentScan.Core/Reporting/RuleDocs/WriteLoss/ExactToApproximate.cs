using SilentScan.Core.Reporting.Sarif;

namespace SilentScan.Core.Reporting.RuleDocs.WriteLoss;

internal static class ExactToApproximate
{
    public static string RuleId => SarifRuleCatalog.WriteLossExactToApproximateRuleId;

    public static RuleDocContent Content { get; } = new(
        WhyItMatters: """
            FLOAT and REAL are approximate types. FLOAT keeps about 15 significant digits and REAL
            about 7. Integers, DECIMAL/NUMERIC and MONEY are exact and can hold more: BIGINT and
            MONEY up to 19 digits, DECIMAL up to 38, INT up to 10.

            Storing such a value into an approximate target is an implicit conversion that raises
            nothing. Digits beyond what the target keeps are rounded away, so large identifiers
            collide and monetary amounts change in their last places.
            """,
        HowToFixIt: """
            Declare the target as an exact type wide enough for the source, such as BIGINT or
            DECIMAL with matching precision and scale. Where an approximate value is intended, make
            the conversion explicit with CAST at the assignment.
            """,
        Examples:
        [
            new RuleDocExample(
                Title: "A BIGINT stored into a FLOAT variable",
                NoncompliantSql: """
                    CREATE PROCEDURE dbo.ReadBalance AS
                    BEGIN
                        DECLARE @AccountNumber BIGINT = 9007199254740993;
                        DECLARE @Copy FLOAT;
                        SET @Copy = @AccountNumber;
                    END;
                    """,
                NoncompliantExplanation: "FLOAT cannot hold this value, so @Copy receives a neighbouring number with no error.",
                CompliantSql: """
                    CREATE PROCEDURE dbo.ReadBalance AS
                    BEGIN
                        DECLARE @AccountNumber BIGINT = 9007199254740993;
                        DECLARE @Copy BIGINT;
                        SET @Copy = @AccountNumber;
                    END;
                    """,
                CompliantExplanation: "The target is exact and wide enough, so every digit is kept."),
        ]);
}
