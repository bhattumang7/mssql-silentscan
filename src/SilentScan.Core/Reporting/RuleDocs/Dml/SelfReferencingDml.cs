using SilentScan.Core.Reporting.Sarif;

namespace SilentScan.Core.Reporting.RuleDocs.Dml;

internal static class SelfReferencingDml
{
    public static string RuleId => SarifRuleCatalog.SelfReferencingDmlRuleId;

    public static RuleDocContent Content { get; } = new(
        WhyItMatters: """
            When a single statement both reads and writes the same table - a DELETE whose WHERE
            clause subqueries the table it's deleting from, an INSERT ... SELECT that reads from
            the very table it inserts into, an UPDATE ... FROM that joins the target table back to
            itself, or any of these reached indirectly through a view built over the same base
            table - SQL Server can no longer assume the read side sees a stable snapshot of rows
            that the write side hasn't touched yet. As the statement proceeds, rows it has already
            modified could, in principle, be revisited by its own read side, producing a row that's
            read once, written, then read again in its new state and written again - the
            Halloween-problem family of anomalies, so named because it was first characterized on a
            statement that kept giving already-raised employees another raise as it repeatedly
            re-encountered their updated rows.

            The engine's defense against this is architectural, not optional, and it can cost real
            plan work: the plan must fully consume the read side before the write side can see
            any of its own in-flight changes. It does that either with an extra Eager Spool or
            Sort inserted just for the purpose, or, when the chosen plan already has a blocking
            operator over the target's rows (for instance a hash join that reads the target as its
            build input, or a hash aggregate), by relying on that operator and adding nothing. Which
            of the two happens is a cost-based plan decision, so the rule cannot promise an extra
            operator for every flagged statement; it flags the statements for which the engine has
            to make this choice at all. An otherwise identical statement whose read side names a
            different table needs no such protection, so its plan never carries an operator added
            for it. This is oracle-confirmed by comparing plans directly: same row counts, same
            indexes, same statement shape, differing only in whether the read side names the write
            target - the self-referencing versions in the common shapes (a NOT EXISTS hole-filling
            INSERT, a self-join UPDATE, a DELETE with an EXISTS subquery, a MERGE) carry the extra
            spool or sort, while a heap INSERT whose plan reads the target through a hash build
            carries neither.

            The performance cost is easy to miss because nothing about it shows up in the source
            text - the statement reads like ordinary DML, and the extra plan work only becomes
            visible in an actual execution plan, not in the query itself. It also scales with the
            table, not with how much data logically needs re-checking: a self-join DELETE against a
            large table pays for materializing its full read side even when, semantically, no row
            was ever going to overlap between what's read and what's deleted.

            One real, oracle-confirmed exception: a statement whose own TOP row limiter is the
            literal integer 1 (not PERCENT, not a variable) guarantees at most one row can ever be
            touched, and across all four statement kinds the extra spool or sort disappears from the
            plan entirely - this rule does not fire on that shape. The same holds for an INSERT ...
            SELECT whose source can only ever produce one row - a SELECT with no FROM clause (the
            usual INSERT ... SELECT ... WHERE NOT EXISTS guard) or an aggregate-only SELECT with no
            GROUP BY, such as SELECT MAX(Id) + 1 FROM the same table - which the engine plans
            without any spool or sort, on heaps and clustered tables alike; this rule does not fire
            on that shape either.
            """,
        Examples:
        [
            new RuleDocExample(
                Title: "A DELETE whose subquery reads the table it deletes from",
                NoncompliantSql: """
                    CREATE TABLE dbo.Orders
                    (
                        OrderId    INT NOT NULL PRIMARY KEY,
                        CustomerId INT NOT NULL,
                        CreatedAt  DATETIME2(0) NOT NULL
                    );

                    DELETE o
                    FROM dbo.Orders AS o
                    WHERE o.CreatedAt < DATEADD(YEAR, -1, SYSDATETIME())
                      AND o.OrderId NOT IN (
                          SELECT TOP (1) o2.OrderId
                          FROM dbo.Orders AS o2
                          WHERE o2.CustomerId = o.CustomerId
                          ORDER BY o2.CreatedAt DESC
                      );
                    """,
                NoncompliantExplanation: "The correlated subquery reads dbo.Orders - the exact table the DELETE writes to - to find each customer's most recent order and keep it. SQL Server must materialize the read side (an Eager Spool) before deleting any row, so the subquery can never see rows this same statement has already removed."),
        ]);
}
