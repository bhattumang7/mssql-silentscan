using SilentScan.Core.Predicates;
using SilentScan.Core.Reporting.Sarif;

namespace SilentScan.Core.Reporting.RuleDocs.ForcedParameterization;

internal static class TopOrPagingLiteral
{
    public static string RuleId => SarifRuleCatalog.ForcedParameterizationRuleId(ForcedParameterizationFindingKind.TopOrPagingLiteral);

    public static RuleDocContent Content { get; } = new(
        WhyItMatters: """
            Reported only when the target database has `PARAMETERIZATION FORCED` on. That setting
            replaces literals in ad hoc and dynamic SQL with parameters so that statements
            differing only by value share one plan, but it skips a TOP or OFFSET/FETCH row count.

            This dynamic SQL string splices a value that changes between executions into that
            position, so every distinct value produces a different statement and compiles its own
            plan. Literals written directly inside a procedure, view, function or trigger are not
            affected: the engine never applies forced parameterization there, and their text does
            not change.
            """,
        HowToFixIt: """
            Pass the value through sp_executesql's own parameter list, so the statement text stays the same for every value and one plan is reused.
            """,
        Examples:
        [
            new RuleDocExample(
                Title: "A changing value spliced into a TOP or OFFSET/FETCH row count",
                NoncompliantSql: """
                    CREATE PROCEDURE dbo.RunDynamic @Value INT AS
                    BEGIN
                        DECLARE @sql NVARCHAR(MAX) = N'SELECT TOP (' + CAST(@Value AS NVARCHAR(10)) + N') CustomerId FROM dbo.Customers';
                        EXEC (@sql);
                    END;
                    """,
                NoncompliantExplanation: "Each distinct @Value yields a different statement text that forced parameterization leaves as it is, so each one compiles its own plan.",
                CompliantSql: """
                    CREATE PROCEDURE dbo.RunParameterized @Value INT AS
                    EXEC sys.sp_executesql N'SELECT TOP (@p) CustomerId FROM dbo.Customers', N'@p INT', @p = @Value;
                    """,
                CompliantExplanation: "The text is identical on every call; only the parameter value changes, so one plan is reused."),
        ]);
}
