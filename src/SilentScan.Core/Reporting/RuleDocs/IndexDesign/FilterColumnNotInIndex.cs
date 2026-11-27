using SilentScan.Core.Predicates;
using SilentScan.Core.Reporting.Sarif;

namespace SilentScan.Core.Reporting.RuleDocs.IndexDesign;

internal static class FilterColumnNotInIndex
{
    public static string RuleId => SarifRuleCatalog.IndexDesignRuleId(IndexDesignFindingKind.FilterColumnNotInIndex);

    public static RuleDocContent Content { get; } = new(
        WhyItMatters: """
            A filtered index's own filter predicate can only be substituted by the optimizer for a
            query whose own WHERE clause restates (or logically implies) that same predicate. When
            the filter predicate references a column the index itself does not carry - neither as a
            key column nor as an INCLUDE column - any query that returns, sorts or otherwise needs
            that column's value must fetch it from the base table with a key lookup per row, even
            though the filter fixes what the value is. Oracle-confirmed on the engine: a query whose only use of the column is a predicate the index filter already
            guarantees (such as restating the filter) is covered; a stricter predicate on it
            needs the lookup, and a query that also selects it gets the lookup unless the column is in the INCLUDE
            list. The finding is therefore a structural fact about the index, and the lookup cost
            applies only to queries that need the column's value.

            This finding only fires when the filter's own definition text reparses cleanly - a
            filter this pass cannot parse is left unanalyzed rather than guessed at, the same
            "never guess" discipline the sibling `duplicate-index`/`subsumed-index` rules already
            apply to an unfiltered index's own key/INCLUDE comparison.
            """,
        HowToFixIt: """
            Add the filtered index's own filter column(s) to its key or INCLUDE list.
            """,
        Examples:
        [
            new RuleDocExample(
                Title: "A filtered index whose filter column isn't carried by the index itself",
                NoncompliantSql: """
                    CREATE TABLE dbo.Orders (CustomerId INT NOT NULL, IsActive BIT NOT NULL);
                    CREATE NONCLUSTERED INDEX IX_Orders_Active
                        ON dbo.Orders (CustomerId)
                        WHERE IsActive = 1;
                    """,
                NoncompliantExplanation: "The filter references IsActive, but the index only carries CustomerId - IsActive is neither a key column nor an INCLUDE column, so any query that returns or sorts by IsActive must fetch it from the base table with a key lookup per row.",
                CompliantSql: """
                    CREATE TABLE dbo.Orders (CustomerId INT NOT NULL, IsActive BIT NOT NULL);
                    CREATE NONCLUSTERED INDEX IX_Orders_Active
                        ON dbo.Orders (CustomerId)
                        INCLUDE (IsActive)
                        WHERE IsActive = 1;
                    """,
                CompliantExplanation: "IsActive is now carried by the index as an INCLUDE column, so a query that returns IsActive is answered from the index alone, with no key lookup."),
        ]);
}
