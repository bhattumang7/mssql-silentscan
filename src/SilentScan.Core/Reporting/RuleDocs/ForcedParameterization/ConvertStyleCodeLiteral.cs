using SilentScan.Core.Predicates;
using SilentScan.Core.Reporting.Sarif;

namespace SilentScan.Core.Reporting.RuleDocs.ForcedParameterization;

internal static class ConvertStyleCodeLiteral
{
    public static string RuleId => SarifRuleCatalog.ForcedParameterizationRuleId(ForcedParameterizationFindingKind.ConvertStyleCodeLiteral);

    public static RuleDocContent Content { get; } = new(
        WhyItMatters: """
            Reported only when the target database has `PARAMETERIZATION FORCED` on. That setting
            replaces literals in ad hoc and dynamic SQL with parameters so that statements
            differing only by value share one plan, but it skips a CONVERT style code.

            This dynamic SQL string splices a value that changes between executions into that
            position, so every distinct value produces a different statement and compiles its own
            plan. Literals written directly inside a procedure, view, function or trigger are not
            affected: the engine never applies forced parameterization there, and their text does
            not change.
            """,
        HowToFixIt: """
            The engine accepts no parameter or does not parameterize this position, so keep the set of distinct values small and fixed - for example by branching to one literal statement per allowed value.
            """,
        Examples:
        [
            new RuleDocExample(
                Title: "A changing value spliced into a CONVERT style code",
                NoncompliantSql: """
                    CREATE PROCEDURE dbo.RunDynamic @Value INT AS
                    BEGIN
                        DECLARE @sql NVARCHAR(MAX) = N'SELECT CONVERT(VARCHAR(30), CreatedAt, ' + CAST(@Value AS NVARCHAR(10)) + N') FROM dbo.Customers';
                        EXEC (@sql);
                    END;
                    """,
                NoncompliantExplanation: "Each distinct @Value yields a different statement text that forced parameterization leaves as it is, so each one compiles its own plan.",
                CompliantSql: """
                    CREATE PROCEDURE dbo.RunFixed @Value INT AS
                    BEGIN
                        IF @Value = 101
                            EXEC (N'SELECT CONVERT(VARCHAR(30), CreatedAt, 101) FROM dbo.Customers');
                    END;
                    """,
                CompliantExplanation: "The dynamic string no longer changes with the caller's value, so no plan is added per value."),
        ]);
}
