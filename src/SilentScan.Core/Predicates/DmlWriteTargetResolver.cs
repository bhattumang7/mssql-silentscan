using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Catalog;
using SilentScan.Core.Common;

namespace SilentScan.Core.Predicates;

internal static class DmlWriteTargetResolver
{
    public static string? TryResolve(TableReference? target, WithCtesAndXmlNamespaces? withCtes, DatabaseCatalog catalog)
    {
        if (target is not NamedTableReference named)
        {
            return null;
        }

        if (CteNameHelper.IsCteReference(named.SchemaObject, withCtes, catalog.IdentifierComparer))
        {
            return null;
        }

        var qualifiedName = catalog.ResolveSynonymName(SchemaObjectNameHelper.Qualify(named.SchemaObject));
        return catalog.Find(qualifiedName) is { Kind: CatalogTableKind.Table } ? qualifiedName : null;
    }

    public static TableReference? ResolveFromClauseAlias(TableReference? target, FromClause? fromClause, StringComparer identifierComparer)
    {
        if (target is not NamedTableReference { SchemaObject: { SchemaIdentifier: null } targetName } || fromClause is null)
        {
            return target;
        }

        var references = new NamedTableReferenceCollector();
        fromClause.Accept(references);
        var aliased = references.References.FirstOrDefault(reference =>
            reference.Alias is { } alias
            && identifierComparer.Equals(alias.Value, targetName.BaseIdentifier.Value));

        return aliased ?? target;
    }

    private sealed class NamedTableReferenceCollector : TSqlFragmentVisitor
    {
        public List<NamedTableReference> References { get; } = [];

        public override void ExplicitVisit(NamedTableReference node) => References.Add(node);
    }
}
