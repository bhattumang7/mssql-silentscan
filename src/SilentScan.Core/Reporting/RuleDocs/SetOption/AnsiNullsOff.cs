using SilentScan.Core.Predicates;
using SilentScan.Core.Reporting.Sarif;

namespace SilentScan.Core.Reporting.RuleDocs.SetOption;

internal static class AnsiNullsOff
{
    public static string RuleId => SarifRuleCatalog.SetOptionRuleId(SetOptionFindingKind.AnsiNullsOffBlocksIndexedFeature);

    public static RuleDocContent Content { get; } = new(
        WhyItMatters: """
            Like QUOTED_IDENTIFIER, ANSI_NULLS is one of the session-level settings a filtered index
            or an indexed view requires to be ON at the point a statement touches them. A module
            compiled under ANSI_NULLS OFF - recorded permanently as
            `sys.sql_modules.uses_ansi_nulls` - can never use either feature: the optimizer silently
            falls back to a base-table or clustered-index scan instead of raising any error, no
            matter how the query is written.

            This is a purely catalog-derived fact once a module is deployed - the compiled setting is
            fixed for the module's lifetime - so it's decidable without executing anything.
            """,
        HowToFixIt: """
            Ensure ANSI_NULLS is ON at the time the module is created or altered - most commonly by
            removing an explicit `SET ANSI_NULLS OFF` ahead of the `CREATE`/`ALTER` statement - then
            redeploy so the compiled setting on the object changes.
            """,
        Examples:
        [
            new RuleDocExample(
                Title: "An ad-hoc script compiled under ANSI_NULLS OFF that touches a filtered index",
                NoncompliantSql: """
                    CREATE TABLE dbo.Orders (Id INT NOT NULL, Amount DECIMAL(10,2) NULL);
                    CREATE INDEX IX_Orders_Amount_Filtered ON dbo.Orders(Amount) WHERE Amount IS NOT NULL;
                    GO

                    SET ANSI_NULLS OFF;
                    SELECT Id FROM dbo.Orders WHERE Amount IS NOT NULL;
                    """,
                NoncompliantExplanation: "With ANSI_NULLS OFF in effect, the optimizer cannot use IX_Orders_Amount_Filtered even though the predicate matches the index's own filter exactly - it silently falls back to scanning the base table instead.",
                CompliantSql: """
                    CREATE TABLE dbo.Orders (Id INT NOT NULL, Amount DECIMAL(10,2) NULL);
                    CREATE INDEX IX_Orders_Amount_Filtered ON dbo.Orders(Amount) WHERE Amount IS NOT NULL;

                    SELECT Id FROM dbo.Orders WHERE Amount IS NOT NULL;
                    """,
                CompliantExplanation: "With ANSI_NULLS left ON (the default), the optimizer is free to use IX_Orders_Amount_Filtered for this predicate."),
        ]);
}
