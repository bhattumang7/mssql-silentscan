using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Catalog;
using SilentScan.Core.Common;

namespace SilentScan.Core.Predicates;

internal static class SingleRowKeyPinning
{
    public static bool PinsAtMostOneRow(
        IReadOnlyList<NamedTableReference> tables,
        IEnumerable<BooleanExpression> conjuncts,
        HashSet<string> cteNames,
        DatabaseCatalog catalog)
    {
        var entries = ResolveTables(tables, cteNames, catalog);
        if (entries is null)
        {
            return false;
        }

        var comparer = catalog.IdentifierComparer;
        var equalities = new List<ColumnEquality>();
        foreach (var conjunct in conjuncts)
        {
            if (conjunct is BooleanComparisonExpression { ComparisonType: BooleanComparisonType.Equals } comparison)
            {
                AddEquality(equalities, comparison.FirstExpression, comparison.SecondExpression, entries, comparer);
                AddEquality(equalities, comparison.SecondExpression, comparison.FirstExpression, entries, comparer);
            }
        }

        var pinned = new HashSet<string>(comparer);
        var progressed = true;
        while (progressed && pinned.Count < entries.Count)
        {
            progressed = false;
            foreach (var (alias, table) in entries)
            {
                if (!pinned.Contains(alias) && IsPinned(alias, table, equalities, pinned, comparer))
                {
                    pinned.Add(alias);
                    progressed = true;
                }
            }
        }

        return pinned.Count == entries.Count;
    }

    private static Dictionary<string, CatalogTable>? ResolveTables(IReadOnlyList<NamedTableReference> tables, HashSet<string> cteNames, DatabaseCatalog catalog)
    {
        if (tables.Count == 0)
        {
            return null;
        }

        var entries = new Dictionary<string, CatalogTable>(catalog.IdentifierComparer);
        foreach (var reference in tables)
        {
            var isCte = reference.SchemaObject.SchemaIdentifier is null && cteNames.Contains(reference.SchemaObject.BaseIdentifier.Value);
            var table = isCte ? null : catalog.Find(catalog.ResolveSynonymName(SchemaObjectNameHelper.Qualify(reference.SchemaObject)));
            var alias = reference.Alias?.Value ?? reference.SchemaObject.BaseIdentifier.Value;
            if (table is not { Kind: CatalogTableKind.Table } || !entries.TryAdd(alias, table))
            {
                return null;
            }
        }

        return entries;
    }

    private static bool IsPinned(string alias, CatalogTable table, List<ColumnEquality> equalities, HashSet<string> pinned, StringComparer comparer) =>
        table.Indexes.Any(ix =>
            ix.IsUnique && !ix.IsFiltered && !ix.IsDisabled && !ix.IsColumnstore && ix.KeyColumns.Count > 0
            && ix.KeyColumns.All(keyColumn => equalities.Exists(e =>
                comparer.Equals(e.Alias, alias)
                && comparer.Equals(e.Column, keyColumn)
                && e.BoundAliases.All(pinned.Contains))));

    private static void AddEquality(
        List<ColumnEquality> equalities,
        ScalarExpression columnSide,
        ScalarExpression boundSide,
        Dictionary<string, CatalogTable> entries,
        StringComparer comparer)
    {
        if (columnSide is not ColumnReferenceExpression { MultiPartIdentifier: { } identifier }
            || ReferencedAlias(identifier, entries) is not { } alias)
        {
            return;
        }

        var collector = new OuterReferenceCollector();
        boundSide.Accept(collector);
        if (collector.Invalid)
        {
            return;
        }

        var boundAliases = new List<string>();
        foreach (var reference in collector.References)
        {
            if (reference.MultiPartIdentifier is not { } boundIdentifier || ReferencedAlias(boundIdentifier, entries) is not { } boundAlias)
            {
                return;
            }

            boundAliases.Add(boundAlias);
        }

        equalities.Add(new ColumnEquality(alias, identifier.Identifiers[^1].Value, boundAliases));
    }

    private static string? ReferencedAlias(MultiPartIdentifier identifier, Dictionary<string, CatalogTable> entries)
    {
        if (identifier.Identifiers.Count >= 2)
        {
            var qualifier = identifier.Identifiers[^2].Value;
            return entries.ContainsKey(qualifier) ? qualifier : null;
        }

        return entries.Count == 1 ? entries.Keys.First() : null;
    }

    private sealed record ColumnEquality(string Alias, string Column, IReadOnlyList<string> BoundAliases);

    private sealed class OuterReferenceCollector : TSqlFragmentVisitor
    {
        public List<ColumnReferenceExpression> References { get; } = [];

        public bool Invalid { get; private set; }

        public override void ExplicitVisit(ColumnReferenceExpression node)
        {
            if (node.MultiPartIdentifier is not null)
            {
                References.Add(node);
            }
        }

        public override void ExplicitVisit(ScalarSubquery node)
        {
            if (!IsSelfContained(node))
            {
                Invalid = true;
            }
        }

        private static bool IsSelfContained(ScalarSubquery subquery)
        {
            if (subquery.QueryExpression is not QuerySpecification { FromClause: { } from } spec)
            {
                return false;
            }

            var tables = new NamedTableReferenceCollector();
            from.Accept(tables);
            if (tables.Unsupported || tables.Aliases.Count == 0)
            {
                return false;
            }

            var inner = new InnerReferenceChecker(tables.Aliases);
            spec.Accept(inner);
            return inner.Valid;
        }
    }

    private sealed class NamedTableReferenceCollector : TSqlFragmentVisitor
    {
        public HashSet<string> Aliases { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool Unsupported { get; private set; }

        public override void ExplicitVisit(NamedTableReference node) =>
            Aliases.Add(node.Alias?.Value ?? node.SchemaObject.BaseIdentifier.Value);

        public override void ExplicitVisit(QueryDerivedTable node) => Unsupported = true;

        public override void ExplicitVisit(SchemaObjectFunctionTableReference node) => Unsupported = true;

        public override void ExplicitVisit(VariableTableReference node) => Unsupported = true;
    }

    private sealed class InnerReferenceChecker(HashSet<string> aliases) : TSqlFragmentVisitor
    {
        public bool Valid { get; private set; } = true;

        public override void ExplicitVisit(ColumnReferenceExpression node)
        {
            if (node.MultiPartIdentifier is { } identifier
                && (identifier.Identifiers.Count < 2 || !aliases.Contains(identifier.Identifiers[^2].Value)))
            {
                Valid = false;
            }
        }

        public override void ExplicitVisit(ScalarSubquery node) => Valid = false;

        public override void ExplicitVisit(QueryDerivedTable node) => Valid = false;

        public override void ExplicitVisit(ExistsPredicate node) => Valid = false;

        public override void ExplicitVisit(InPredicate node)
        {
            if (node.Subquery is not null)
            {
                Valid = false;
                return;
            }

            base.ExplicitVisit(node);
        }
    }
}
