using SilentScan.Core.Predicates;
using SilentScan.Core.Reporting.Sarif;

namespace SilentScan.Core.Reporting.RuleDocs.SetOption;

internal static class QuotedIdentifierOff
{
    public static string RuleId => SarifRuleCatalog.SetOptionRuleId(SetOptionFindingKind.QuotedIdentifierOffBlocksIndexedFeature);

    public static RuleDocContent Content { get; } = new(
        WhyItMatters: """
            A filtered index and an indexed view both depend on a set of session-level SET options
            being ON at the time a statement touches them - QUOTED_IDENTIFIER is one of them. A
            module compiled under QUOTED_IDENTIFIER OFF - recorded permanently on the object as
            `sys.sql_modules.uses_quoted_identifier` - can never use either feature, no matter how
            the query against them is written: the optimizer silently falls back to a base-table or
            clustered-index scan instead of raising any error.

            This is a purely catalog-derived fact once a module is deployed: the compiled setting is
            fixed for the module's lifetime and doesn't depend on the caller's own session state, so
            it's decidable without executing anything - the same silent-scan-instead-of-error
            behavior the optimizer would otherwise hide from a query plan review.
            """,
        HowToFixIt: """
            Ensure QUOTED_IDENTIFIER is ON at the time the module is created or altered - most
            commonly by removing an explicit `SET QUOTED_IDENTIFIER OFF` ahead of the `CREATE`/
            `ALTER` statement - then redeploy so the compiled setting on the object changes.
            """,
        Examples:
        [
            new RuleDocExample(
                Title: "A procedure compiled under QUOTED_IDENTIFIER OFF that touches a filtered index",
                NoncompliantSql: """
                    CREATE TABLE dbo.Orders (Id INT NOT NULL, Amount DECIMAL(10,2) NULL);
                    CREATE INDEX IX_Orders_Amount_Filtered ON dbo.Orders(Amount) WHERE Amount IS NOT NULL;

                    SELECT Id FROM dbo.Orders WHERE Amount IS NOT NULL;

                    SET QUOTED_IDENTIFIER OFF;
                    GO
                    CREATE PROCEDURE dbo.usp_GetOrders AS
                    BEGIN
                        SELECT Id FROM dbo.Orders WHERE Amount IS NOT NULL;
                    END;
                    """,
                NoncompliantExplanation: "An ad-hoc query against dbo.Orders uses IX_Orders_Amount_Filtered fine, but usp_GetOrders is compiled under QUOTED_IDENTIFIER OFF, recorded permanently on the object - the optimizer can never use IX_Orders_Amount_Filtered for this procedure even though its query matches the index's own filter exactly, so it silently falls back to scanning the base table instead.",
                CompliantSql: """
                    CREATE TABLE dbo.Orders (Id INT NOT NULL, Amount DECIMAL(10,2) NULL);
                    CREATE INDEX IX_Orders_Amount_Filtered ON dbo.Orders(Amount) WHERE Amount IS NOT NULL;
                    GO
                    CREATE PROCEDURE dbo.usp_GetOrders AS
                    BEGIN
                        SELECT Id FROM dbo.Orders WHERE Amount IS NOT NULL;
                    END;
                    """,
                CompliantExplanation: "With QUOTED_IDENTIFIER left ON (the default) at the time usp_GetOrders is compiled, the optimizer is free to use IX_Orders_Amount_Filtered for this predicate."),
        ]);
}
