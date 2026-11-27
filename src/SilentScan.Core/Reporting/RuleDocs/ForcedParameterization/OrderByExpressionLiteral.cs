using SilentScan.Core.Predicates;
using SilentScan.Core.Reporting.Sarif;

namespace SilentScan.Core.Reporting.RuleDocs.ForcedParameterization;

internal static class OrderByExpressionLiteral
{
    public static string RuleId => SarifRuleCatalog.ForcedParameterizationRuleId(ForcedParameterizationFindingKind.OrderByExpressionLiteral);

    public static RuleDocContent Content { get; } = new(
        WhyItMatters: """
            Reported only when the target database has `PARAMETERIZATION FORCED` on. That setting
            replaces literals in ad hoc and dynamic SQL with parameters so that statements
            differing only by value share one plan, but it skips an ORDER BY expression.

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
                Title: "A changing value spliced into an ORDER BY expression",
                NoncompliantSql: """
                    CREATE PROCEDURE dbo.RunDynamic @Value NVARCHAR(100) AS
                    BEGIN
                        DECLARE @sql NVARCHAR(MAX) = N'SELECT CustomerId FROM dbo.Customers ORDER BY CHARINDEX(''' + @Value + N''', Name)';
                        EXEC (@sql);
                    END;
                    """,
                NoncompliantExplanation: "Each distinct @Value yields a different statement text that forced parameterization leaves as it is, so each one compiles its own plan.",
                CompliantSql: """
                    CREATE PROCEDURE dbo.RunParameterized @Value NVARCHAR(100) AS
                    EXEC sys.sp_executesql N'SELECT CustomerId FROM dbo.Customers ORDER BY CHARINDEX(@p, Name)', N'@p NVARCHAR(100)', @p = @Value;
                    """,
                CompliantExplanation: "The text is identical on every call; only the parameter value changes, so one plan is reused."),
        ]);
}
