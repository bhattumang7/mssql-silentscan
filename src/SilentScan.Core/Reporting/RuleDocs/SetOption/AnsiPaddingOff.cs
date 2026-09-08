using SilentScan.Core.Predicates;
using SilentScan.Core.Reporting.Sarif;

namespace SilentScan.Core.Reporting.RuleDocs.SetOption;

internal static class AnsiPaddingOff
{
    public static string RuleId => SarifRuleCatalog.SetOptionRuleId(SetOptionFindingKind.AnsiPaddingOffBlocksIndexedFeature);

    public static RuleDocContent Content { get; } = new(
        WhyItMatters: """
            A filtered index or an indexed view also requires ANSI_PADDING to be ON at the point a
            statement touches them. Like the other three inline-SET kinds, this one is triggered by
            an explicit `SET ANSI_PADDING OFF` inside the module body. When it's OFF, the optimizer
            silently falls back to a base-table or clustered-index scan instead of using either
            feature - no error is raised.

            This is distinct from the AnsiPaddingMismatch finding, which is a data-semantics claim
            about which rows a LIKE pattern matches under trailing-space padding, not a plan-shape
            one - the two can both apply to the same module for entirely different reasons.
            """,
        HowToFixIt: """
            Remove the `SET ANSI_PADDING OFF` statement, or explicitly set it ON, before the module
            touches the filtered index or indexed view.
            """,
        Examples:
        [
            new RuleDocExample(
                Title: "A procedure with SET ANSI_PADDING OFF that touches a filtered index",
                NoncompliantSql: """
                    CREATE TABLE dbo.Orders (Id INT NOT NULL, Amount DECIMAL(10,2) NULL);
                    CREATE INDEX IX_Orders_Amount_Filtered ON dbo.Orders(Amount) WHERE Amount IS NOT NULL;
                    GO

                    CREATE PROCEDURE dbo.usp_GetOrders AS
                    BEGIN
                        SET ANSI_PADDING OFF;
                        SELECT Id FROM dbo.Orders WHERE Amount IS NOT NULL;
                    END;
                    """,
                NoncompliantExplanation: "SET ANSI_PADDING OFF blocks the optimizer from using IX_Orders_Amount_Filtered for the rest of this module - it silently falls back to scanning the base table instead.",
                CompliantSql: """
                    CREATE TABLE dbo.Orders (Id INT NOT NULL, Amount DECIMAL(10,2) NULL);
                    CREATE INDEX IX_Orders_Amount_Filtered ON dbo.Orders(Amount) WHERE Amount IS NOT NULL;
                    GO

                    CREATE PROCEDURE dbo.usp_GetOrders AS
                    BEGIN
                        SELECT Id FROM dbo.Orders WHERE Amount IS NOT NULL;
                    END;
                    """,
                CompliantExplanation: "Without SET ANSI_PADDING OFF, the optimizer is free to use IX_Orders_Amount_Filtered for this predicate."),
        ]);
}
