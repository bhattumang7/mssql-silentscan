using SilentScan.Core.Predicates;
using SilentScan.Core.Reporting.Sarif;

namespace SilentScan.Core.Reporting.RuleDocs.SetOption;

internal static class ConcatNullYieldsNullOff
{
    public static string RuleId => SarifRuleCatalog.SetOptionRuleId(SetOptionFindingKind.ConcatNullYieldsNullOffBlocksIndexedFeature);

    public static RuleDocContent Content { get; } = new(
        WhyItMatters: """
            A filtered index or an indexed view also requires CONCAT_NULL_YIELDS_NULL to be ON at
            the point a statement touches them. Like NUMERIC_ROUNDABORT and ANSI_WARNINGS, this one
            is triggered by an explicit `SET CONCAT_NULL_YIELDS_NULL OFF` inside the module body.
            When it's OFF, the optimizer silently falls back to a base-table or clustered-index scan
            instead of using either feature - no error is raised.
            """,
        HowToFixIt: """
            Remove the `SET CONCAT_NULL_YIELDS_NULL OFF` statement, or explicitly set it ON, before
            the module touches the filtered index or indexed view.
            """,
        Examples:
        [
            new RuleDocExample(
                Title: "A procedure with SET CONCAT_NULL_YIELDS_NULL OFF that touches a filtered index",
                NoncompliantSql: """
                    CREATE TABLE dbo.Orders (Id INT NOT NULL, Amount DECIMAL(10,2) NULL);
                    CREATE INDEX IX_Orders_Amount_Filtered ON dbo.Orders(Amount) WHERE Amount IS NOT NULL;
                    GO

                    CREATE PROCEDURE dbo.usp_GetOrders AS
                    BEGIN
                        SET CONCAT_NULL_YIELDS_NULL OFF;
                        SELECT Id FROM dbo.Orders WHERE Amount IS NOT NULL;
                    END;
                    """,
                NoncompliantExplanation: "SET CONCAT_NULL_YIELDS_NULL OFF blocks the optimizer from using IX_Orders_Amount_Filtered for the rest of this module - it silently falls back to scanning the base table instead.",
                CompliantSql: """
                    CREATE TABLE dbo.Orders (Id INT NOT NULL, Amount DECIMAL(10,2) NULL);
                    CREATE INDEX IX_Orders_Amount_Filtered ON dbo.Orders(Amount) WHERE Amount IS NOT NULL;
                    GO

                    CREATE PROCEDURE dbo.usp_GetOrders AS
                    BEGIN
                        SELECT Id FROM dbo.Orders WHERE Amount IS NOT NULL;
                    END;
                    """,
                CompliantExplanation: "Without SET CONCAT_NULL_YIELDS_NULL OFF, the optimizer is free to use IX_Orders_Amount_Filtered for this predicate."),
        ]);
}
