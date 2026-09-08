using SilentScan.Core.Predicates;
using SilentScan.Core.Reporting.Sarif;

namespace SilentScan.Core.Reporting.RuleDocs.Catalog;

internal static class LegacyLargeObjectColumn
{
    public static string RuleId => SarifRuleCatalog.MaxTypedColumnRuleId(NonIndexableColumnFindingKind.LegacyLargeObject);

    public static RuleDocContent Content { get; } = new(
        WhyItMatters: """
            `TEXT`, `NTEXT`, and `IMAGE` are the deprecated predecessors to
            `VARCHAR(MAX)`/`NVARCHAR(MAX)`/`VARBINARY(MAX)`, and they carry a strictly stronger
            unindexability penalty than their MAX-typed replacements: a MAX-typed column can still be
            carried as a nonclustered index's `INCLUDE` column even though it can never be a key
            column, but a `TEXT`/`NTEXT`/`IMAGE` column cannot appear in any index at all, in any
            role, not even as an `INCLUDE` column. Any predicate or join on a column of one of these
            types can never seek, and no covering index can ever include it either - the engine goes
            by the declared type, so this is true regardless of how short the actual data happens to
            be.

            This is a purely catalog-derived structural fact (the column's declared type, read
            straight from `sys.columns`), true the moment the column is declared this way,
            independent of any query.
            """,
        HowToFixIt: """
            Migrate away from the deprecated TEXT/NTEXT/IMAGE types to VARCHAR(MAX)/NVARCHAR(MAX)/
            VARBINARY(MAX), which at least support being carried as a covering INCLUDE column.
            """,
        Examples:
        [
            new RuleDocExample(
                Title: "A legacy IMAGE column that can never be indexed in any way",
                NoncompliantSql: """
                    CREATE TABLE dbo.Attachments
                    (
                        AttachmentId  INT     NOT NULL PRIMARY KEY,
                        Thumbnail     IMAGE   NULL
                    );
                    -- Thumbnail can never be a key column, and unlike VARBINARY(MAX) it can't even
                    -- be carried as a covering INCLUDE column on any index.

                    SELECT AttachmentId FROM dbo.Attachments WHERE Thumbnail IS NOT NULL;
                    """,
                NoncompliantExplanation: "Thumbnail is declared IMAGE, a legacy large-object type - it can never be an index key column, and it can never be carried as an INCLUDE column either, so this predicate can never seek and no index can ever cover it.",
                CompliantSql: """
                    CREATE TABLE dbo.Attachments
                    (
                        AttachmentId  INT             NOT NULL PRIMARY KEY,
                        Thumbnail     VARBINARY(MAX)  NULL
                    );

                    SELECT AttachmentId FROM dbo.Attachments WHERE Thumbnail IS NOT NULL;
                    """,
                CompliantExplanation: "VARBINARY(MAX) still can't be an index key column, but unlike IMAGE it can be carried as a nonclustered index's INCLUDE column, giving the optimizer a covering-index option IMAGE never allowed."),
        ]);
}
