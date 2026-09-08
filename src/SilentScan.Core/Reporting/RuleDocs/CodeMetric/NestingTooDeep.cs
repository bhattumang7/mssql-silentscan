using SilentScan.Core.Predicates;
using SilentScan.Core.Reporting.Sarif;

namespace SilentScan.Core.Reporting.RuleDocs.CodeMetric;

internal static class NestingTooDeep
{
    public static string RuleId => SarifRuleCatalog.CodeMetricRuleId(CodeMetricFindingKind.NestingTooDeep);

    public static RuleDocContent Content { get; } = new(
        WhyItMatters: """
            An IF/WHILE/TRY nests more than the configured maximum depth inside a routine. Purely a
            readability signal - no query result or execution plan is affected. Deep nesting forces
            a reader to hold several levels of condition in mind at once to know whether a given
            statement even executes, which is where nesting-related logic bugs tend to hide.
            """,
        HowToFixIt: """
            Flatten the nesting with early returns/guard clauses, or extract an inner block into its
            own procedure so each level of logic can be reasoned about independently.
            """,
        Examples:
        [
            new RuleDocExample(
                Title: "Deeply nested conditionals instead of guard clauses",
                NoncompliantSql: """
                    CREATE PROCEDURE dbo.ProcessOrder (@orderId INT) AS
                    BEGIN
                        IF EXISTS (SELECT 1 FROM dbo.Orders WHERE OrderId = @orderId)
                        BEGIN
                            IF NOT EXISTS (SELECT 1 FROM dbo.Orders WHERE OrderId = @orderId AND Status = 'Cancelled')
                            BEGIN
                                IF EXISTS (SELECT 1 FROM dbo.Inventory WHERE OrderId = @orderId AND Quantity > 0)
                                BEGIN
                                    IF NOT EXISTS (SELECT 1 FROM dbo.Orders WHERE OrderId = @orderId AND Status = 'OnHold')
                                    BEGIN
                                        IF EXISTS (SELECT 1 FROM dbo.Customers c JOIN dbo.Orders o ON o.CustomerId = c.CustomerId WHERE o.OrderId = @orderId AND c.IsActive = 1)
                                        BEGIN
                                            IF NOT EXISTS (SELECT 1 FROM dbo.CustomerHolds h JOIN dbo.Orders o ON o.CustomerId = h.CustomerId WHERE o.OrderId = @orderId)
                                            BEGIN
                                                IF EXISTS (SELECT 1 FROM dbo.PaymentAuthorizations WHERE OrderId = @orderId AND IsApproved = 1)
                                                BEGIN
                                                    IF NOT EXISTS (SELECT 1 FROM dbo.FraudFlags WHERE OrderId = @orderId)
                                                    BEGIN
                                                        IF EXISTS (SELECT 1 FROM dbo.ShippingAddresses WHERE OrderId = @orderId AND IsVerified = 1)
                                                        BEGIN
                                                            IF NOT EXISTS (SELECT 1 FROM dbo.Orders WHERE OrderId = @orderId AND IsPriceOverridden = 1)
                                                            BEGIN
                                                                IF EXISTS (SELECT 1 FROM dbo.Warehouses w JOIN dbo.Inventory i ON i.WarehouseId = w.WarehouseId WHERE i.OrderId = @orderId AND w.IsOperational = 1)
                                                                BEGIN
                                                                    UPDATE dbo.Orders SET Status = 'Processed' WHERE OrderId = @orderId;
                                                                END
                                                            END
                                                        END
                                                    END
                                                END
                                            END
                                        END
                                    END
                                END
                            END
                        END
                    END
                    """,
                NoncompliantExplanation: "Eleven levels of nested IFs force a reader to track all eleven conditions at once to know whether the UPDATE ever runs.",
                CompliantSql: """
                    CREATE PROCEDURE dbo.ProcessOrder (@orderId INT) AS
                    BEGIN
                        IF NOT EXISTS (SELECT 1 FROM dbo.Orders WHERE OrderId = @orderId) RETURN;
                        IF EXISTS (SELECT 1 FROM dbo.Orders WHERE OrderId = @orderId AND Status = 'Cancelled') RETURN;
                        IF NOT EXISTS (SELECT 1 FROM dbo.Inventory WHERE OrderId = @orderId AND Quantity > 0) RETURN;
                        IF EXISTS (SELECT 1 FROM dbo.Orders WHERE OrderId = @orderId AND Status = 'OnHold') RETURN;
                        IF NOT EXISTS (SELECT 1 FROM dbo.Customers c JOIN dbo.Orders o ON o.CustomerId = c.CustomerId WHERE o.OrderId = @orderId AND c.IsActive = 1) RETURN;
                        IF EXISTS (SELECT 1 FROM dbo.CustomerHolds h JOIN dbo.Orders o ON o.CustomerId = h.CustomerId WHERE o.OrderId = @orderId) RETURN;
                        IF NOT EXISTS (SELECT 1 FROM dbo.PaymentAuthorizations WHERE OrderId = @orderId AND IsApproved = 1) RETURN;
                        IF EXISTS (SELECT 1 FROM dbo.FraudFlags WHERE OrderId = @orderId) RETURN;
                        IF NOT EXISTS (SELECT 1 FROM dbo.ShippingAddresses WHERE OrderId = @orderId AND IsVerified = 1) RETURN;
                        IF EXISTS (SELECT 1 FROM dbo.Orders WHERE OrderId = @orderId AND IsPriceOverridden = 1) RETURN;
                        IF NOT EXISTS (SELECT 1 FROM dbo.Warehouses w JOIN dbo.Inventory i ON i.WarehouseId = w.WarehouseId WHERE i.OrderId = @orderId AND w.IsOperational = 1) RETURN;

                        UPDATE dbo.Orders SET Status = 'Processed' WHERE OrderId = @orderId;
                    END
                    """,
                CompliantExplanation: "Guard clauses that return early replace the nested structure - each condition is checked once, independently, at the top level."),
        ]);
}
