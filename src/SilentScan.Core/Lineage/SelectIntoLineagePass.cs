using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Catalog;
using SilentScan.Core.Diagnostics;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;
using SilentScan.Core.Common;

namespace SilentScan.Core.Lineage;

public static class SelectIntoLineagePass
{
    public static void Apply(DatabaseCatalog catalog, LineageCatalog lineage, IEnumerable<SqlParseResult> parseResults)
    {
        foreach (var result in parseResults)
        {
            var rule = new Rule(catalog, lineage.AllRelations, result.SourcePath);
            var walker = new ModuleWalker(
                result.SourcePath, catalog, lineage.AllRelations,
                rules: [rule], callerContext: new ModuleWalkerCallerContext(catalog.Skipped, null, null), triggerScopeAnalysisPass: AnalysisPass.Lineage);
            result.Fragment.Accept(walker);
        }
    }

    private sealed class Rule(DatabaseCatalog catalog, IReadOnlyDictionary<string, ResolvedRelation> resolvedViews, string sourcePath) : IModuleRule
    {
        public void OnEnterSelectStatementScope(SelectStatement node, ModuleWalker walker)
        {
            if (node.Into is not null)
            {
                ResolveSelectIntoTarget(node, walker);
            }
        }

        private void ResolveSelectIntoTarget(SelectStatement select, ModuleWalker walker)
        {
            var targetName = select.Into!;
            var qualifiedName = SchemaObjectNameHelper.Qualify(targetName);
            var isTemp = SchemaObjectNameHelper.IsLocalTempName(qualifiedName);

            var existing = catalog.Find(qualifiedName, isTemp ? walker.CurrentProcScope : null);
            if (existing is null)
            {

                catalog.Skipped.Record(
                    AnalysisPass.Lineage, sourcePath, select.StartLine, select.StartColumn, "SELECT INTO",
                    $"'{qualifiedName}' has no Pass-1 catalog entry to merge into - the SELECT INTO statement may not have reached CatalogBuilder");
                return;
            }

            var resolved = QueryExpressionResolver.Resolve(
                select.QueryExpression, catalog, resolvedViews, sourcePath, catalog.Skipped, walker.CurrentCteRelations(), walker.CurrentProcScope);

            var existingByName = new Dictionary<string, CatalogColumn>(catalog.IdentifierComparer);
            foreach (var column in existing.Columns)
            {
                existingByName.TryAdd(column.Name, column);
            }

            var mergedColumns = new List<CatalogColumn>();
            var resolvedNames = new HashSet<string>(catalog.IdentifierComparer);
            foreach (var r in resolved)
            {
                if (!resolvedNames.Add(r.Name))
                {
                    continue;
                }

                var resolvedType = ColumnProvenanceAnalysis.TryGetScalarType(r.Provenance);
                mergedColumns.Add(existingByName.TryGetValue(r.Name, out var known)
                    ? known with { Type = known.Type ?? resolvedType }
                    : new CatalogColumn(r.Name, resolvedType, IsNullable: true, IsIdentity: false, IsComputed: false, IsPersisted: false));
            }

            mergedColumns.AddRange(existing.Columns.Where(column => !resolvedNames.Contains(column.Name)));

            catalog.AddOrReplace(existing with { Columns = mergedColumns }, isTemp ? walker.CurrentProcScope : null);
        }
    }
}
