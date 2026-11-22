using SilentScan.Core.Reporting.Sarif;

namespace SilentScan.Core.Reporting.RuleDocs.Lineage;

internal static class ExpressionDerivedColumn
{
    public static string RuleId => SarifRuleCatalog.ExpressionDerivedRuleId;

    public static RuleDocContent Content { get; } = new(
        WhyItMatters: """
            A predicate can compare something that looks like a column but isn't one anymore by the
            time the query actually sees it - somewhere between the predicate and the real base
            table, a `CAST`/`CONVERT` or other expression replaced it with a computed value, either
            right there in the current statement's own derived table, or further upstream, baked
            into a view or inline TVF's own `SELECT` list. A comparison against a computed
            expression generally cannot use an index seek - the index exists on the real
            underlying column's stored values, not on a value the engine has to compute fresh for
            every row. Oracle-confirmed exceptions are not reported: a `CAST` that stays inside the
            numeric family (`INT` to `BIGINT`, `DECIMAL`) or inside the date/time family
            (`DATETIME` to `DATE`) still seeks the base column's index, because the engine converts
            the constant instead. So does a `VARCHAR` column with a Windows collation cast to an
            `NVARCHAR` at least as wide. A cast to a narrower string, a cast of a SQL-collation
            column, a function such as `UPPER`, and arithmetic on the column all lose the seek,
            and those are what this finding covers.

            This is reported separately from this tool's ordinary sargability verdicts (the
            `verdict/*` family), which are about type-precedence mismatches between two otherwise-
            real operands - a genuinely different problem. Here, the seek is lost regardless of
            types on either side, because one side simply isn't a real column anymore. This tool's
            own lineage pass traces the full transformation chain (every layer, outermost first,
            that introduced the CAST/CONVERT) all the way down to whichever real base table columns
            are reachable underneath it, and reports whether each of those underlying columns is
            itself indexed - the fact that determines whether fixing the expression would actually
            restore a seek.
            """,
        Examples:
        [
            new RuleDocExample(
                Title: "A CAST baked into an upstream view, round-tripped through a second view",
                NoncompliantSql: """
                    CREATE TABLE dbo.Orders (OrderId INT NOT NULL PRIMARY KEY, CustomerId INT NOT NULL);
                    GO
                    CREATE VIEW dbo.vw_OrdersStr AS
                        SELECT OrderId, CAST(CustomerId AS VARCHAR(20)) AS CustomerIdStr
                        FROM dbo.Orders;
                    GO
                    CREATE VIEW dbo.vw_OrdersRoundTrip AS
                        SELECT OrderId, CAST(CustomerIdStr AS INT) AS CustomerIdAgain
                        FROM dbo.vw_OrdersStr;
                    GO
                    SELECT OrderId FROM dbo.vw_OrdersRoundTrip WHERE CustomerIdAgain = 5;
                    """,
                NoncompliantExplanation: "CustomerIdAgain looks like an ordinary INT column to this query, but it's actually the result of two CASTs baked into two upstream views - oracle-confirmed directly: the equivalent query against the real base column (WHERE CustomerId = 5 on dbo.Orders) uses an index seek, while this one does not, purely because of the view-layer CAST chain."),
        ]);
}
