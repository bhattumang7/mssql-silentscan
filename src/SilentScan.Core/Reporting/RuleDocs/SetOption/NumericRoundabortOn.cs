using SilentScan.Core.Predicates;
using SilentScan.Core.Reporting.Sarif;

namespace SilentScan.Core.Reporting.RuleDocs.SetOption;

internal static class NumericRoundabortOn
{
    public static string RuleId => SarifRuleCatalog.SetOptionRuleId(SetOptionFindingKind.NumericRoundabortOnBlocksIndexedFeature);

    public static RuleDocContent Content { get; } = new(
        WhyItMatters: """
            A filtered index or an indexed view also requires NUMERIC_ROUNDABORT to be OFF at the
            point a statement touches them - unlike QUOTED_IDENTIFIER/ANSI_NULLS, this one is
            triggered by an explicit `SET NUMERIC_ROUNDABORT ON` inside the module body itself,
            rather than by a compiled catalog flag. When it's ON, the optimizer silently falls back
            to a base-table or clustered-index scan instead of using either feature - no error is
            raised, and the query still returns correct results, just by a slower path.
            """,
        HowToFixIt: """
            Remove the `SET NUMERIC_ROUNDABORT ON` statement, or explicitly set it OFF, before the
            module touches the filtered index or indexed view.
            """,
        Examples:
        [
            new RuleDocExample(
                Title: "A procedure with SET NUMERIC_ROUNDABORT ON that touches a filtered index",
                NoncompliantSql: """
                    CREATE TABLE dbo.Orders (Id INT NOT NULL, Amount DECIMAL(10,2) NULL);
                    CREATE INDEX IX_Orders_Amount_Filtered ON dbo.Orders(Amount) WHERE Amount IS NOT NULL;
                    GO

                    CREATE PROCEDURE dbo.usp_GetOrders AS
                    BEGIN
                        SET NUMERIC_ROUNDABORT ON;
                        SELECT Id FROM dbo.Orders WHERE Amount IS NOT NULL;
                    END;
                    """,
                NoncompliantExplanation: "SET NUMERIC_ROUNDABORT ON blocks the optimizer from using IX_Orders_Amount_Filtered for the rest of this module - it silently falls back to scanning the base table instead.",
                CompliantSql: """
                    CREATE TABLE dbo.Orders (Id INT NOT NULL, Amount DECIMAL(10,2) NULL);
                    CREATE INDEX IX_Orders_Amount_Filtered ON dbo.Orders(Amount) WHERE Amount IS NOT NULL;
                    GO

                    CREATE PROCEDURE dbo.usp_GetOrders AS
                    BEGIN
                        SELECT Id FROM dbo.Orders WHERE Amount IS NOT NULL;
                    END;
                    """,
                CompliantExplanation: "Without SET NUMERIC_ROUNDABORT ON, the optimizer is free to use IX_Orders_Amount_Filtered for this predicate."),
        ]);
}
