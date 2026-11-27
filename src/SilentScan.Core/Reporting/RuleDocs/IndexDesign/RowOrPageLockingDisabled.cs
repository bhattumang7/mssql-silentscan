using SilentScan.Core.Predicates;
using SilentScan.Core.Reporting.Sarif;

namespace SilentScan.Core.Reporting.RuleDocs.IndexDesign;

internal static class RowOrPageLockingDisabled
{
    public static string RuleId => SarifRuleCatalog.IndexDesignRuleId(IndexDesignFindingKind.RowOrPageLockingDisabled);

    public static RuleDocContent Content { get; } = new(
        WhyItMatters: """
            `ALLOW_ROW_LOCKS` and `ALLOW_PAGE_LOCKS` are per-index options
            (`sys.indexes.allow_row_locks` / `allow_page_locks`), set at `CREATE INDEX`/inline
            constraint time or later via `ALTER INDEX ... SET (...)`. Both default to `ON`. Turning
            either `OFF` changes the locking granularity the engine is willing to take for any DML
            that touches that index, permanently, until someone turns it back on. ALLOW_ROW_LOCKS = OFF forces page or table locks where row locks would be taken. ALLOW_PAGE_LOCKS = OFF removes the page level, so a scan that would lock pages takes one lock per row instead (more lock memory and lock calls) or a table lock. With both OFF every access locks the whole table. A plain
            `UPDATE`/`DELETE`/`INSERT` statement gives no hint of this at its own call site, because
            the option lives entirely in the index's own catalog metadata.

            This is the same "hint/index-option silently reverting locking granularity in a way
            invisible from the DML site" shape as `READCOMMITTEDLOCK` reverting RCSI row versioning
            - except here there's no keyword anywhere in the DML text at all. Two statements a
            developer assumes can run concurrently against unrelated rows can end up blocking or
            deadlocking on locks they did not expect. Shipped as a structural risk flag only:
            whether contention actually occurs depends on the concurrent access pattern, which is
            workload data out of reach for a static pass.
            """,
        HowToFixIt: """
            Re-enable ALLOW_ROW_LOCKS and ALLOW_PAGE_LOCKS on the index unless the changed locking granularity was a deliberate, documented choice.
            """,
        Examples:
        [
            new RuleDocExample(
                Title: "An index built with row-level locking disabled",
                NoncompliantSql: """
                    CREATE TABLE dbo.Orders (OrderId INT NOT NULL PRIMARY KEY, CustomerId INT NOT NULL);
                    GO
                    CREATE INDEX IX_Orders_CustomerId
                        ON dbo.Orders (CustomerId)
                        WITH (ALLOW_ROW_LOCKS = OFF);
                    """,
                NoncompliantExplanation: "Every UPDATE/DELETE that would take row locks on this index takes page or table locks instead - invisible from the DML statement itself, which never mentions this index's own locking configuration.",
                CompliantSql: """
                    CREATE TABLE dbo.Orders (OrderId INT NOT NULL PRIMARY KEY, CustomerId INT NOT NULL);
                    GO
                    CREATE INDEX IX_Orders_CustomerId
                        ON dbo.Orders (CustomerId);
                    """,
                CompliantExplanation: "With ALLOW_ROW_LOCKS left at its default ON, the engine can take ordinary row-level locks for DML against this index."),
        ]);
}
