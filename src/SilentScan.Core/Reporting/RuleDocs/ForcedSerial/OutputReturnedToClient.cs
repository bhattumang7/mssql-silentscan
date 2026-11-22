using SilentScan.Core.Reporting.Sarif;

namespace SilentScan.Core.Reporting.RuleDocs.ForcedSerial;

internal static class OutputReturnedToClient
{
    public static string RuleId => SarifRuleCatalog.ForcedSerialOutputReturnedToClientRuleId;

    public static RuleDocContent Content { get; } = new(
        WhyItMatters: """
            An INSERT, UPDATE, DELETE or MERGE with an OUTPUT clause that has no INTO sends the
            affected rows back to the client as a result set. The engine compiles such a statement
            with a serial plan: a real executed plan carries NonParallelPlanReason =
            "DMLQueryReturnsOutputToClient" on its root, however large the read side of the
            statement is and however many workers would otherwise be available.

            The restriction follows from returning the rows, not from the OUTPUT clause as such.
            The same statement with OUTPUT ... INTO a temporary table does not carry this reason
            and can get a parallel plan. For a statement that touches a handful of rows this is
            invisible; it matters when the statement finds its rows through a large scan, join or
            aggregation that parallelism would have sped up.
            """,
        HowToFixIt: """
            If the statement's own cost is a measured problem, send the OUTPUT into a #temp table
            and select from that table afterwards. The statement itself can then be planned in
            parallel, and the client still receives the same rows from a cheap second read. Where
            the statement affects few rows, or runs rarely, the serial plan is only a cost to be
            aware of.
            """,
        Examples:
        [
            new RuleDocExample(
                Title: "A DELETE that returns its rows runs serial",
                NoncompliantSql: """
                    CREATE TABLE dbo.Orders
                    (
                        OrderId    INT          NOT NULL PRIMARY KEY,
                        CustomerId INT          NOT NULL,
                        Status     VARCHAR(20)  NOT NULL
                    );

                    DELETE o
                    OUTPUT deleted.OrderId
                    FROM dbo.Orders AS o
                    JOIN (SELECT CustomerId FROM dbo.Orders GROUP BY CustomerId HAVING COUNT(*) > 100) AS c
                        ON c.CustomerId = o.CustomerId;
                    """,
                NoncompliantExplanation: "The OUTPUT clause has no INTO, so the deleted rows go to the client and the whole statement, including the scan and aggregation that find its rows, is planned serial.",
                CompliantSql: """
                    CREATE TABLE #Deleted (OrderId INT NOT NULL);

                    DELETE o
                    OUTPUT deleted.OrderId INTO #Deleted
                    FROM dbo.Orders AS o
                    JOIN (SELECT CustomerId FROM dbo.Orders GROUP BY CustomerId HAVING COUNT(*) > 100) AS c
                        ON c.CustomerId = o.CustomerId;

                    SELECT OrderId FROM #Deleted;
                    """,
                CompliantExplanation: "OUTPUT ... INTO a temporary table does not carry the restriction, so the statement can use a parallel plan and the client reads the rows from the temp table afterwards."),
        ]);
}
