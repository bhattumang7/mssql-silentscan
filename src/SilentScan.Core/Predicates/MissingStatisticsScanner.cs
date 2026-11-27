using SilentScan.Core.Catalog;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;

namespace SilentScan.Core.Predicates;

public static class MissingStatisticsScanner
{
    private static readonly IReadOnlyDictionary<string, ResolvedRelation> EmptyResolvedViews = new Dictionary<string, ResolvedRelation>();

    public static IReadOnlyList<MissingStatisticsFinding> Scan(SqlParseResult parseResult, DatabaseCatalog catalog)
    {
        if (catalog.IsAutoCreateStatsOn != false)
        {
            return [];
        }

        var visitor = CreateRule(parseResult.SourcePath, catalog);
        var walker = new ModuleWalker(parseResult.SourcePath, catalog, EmptyResolvedViews, rules: [visitor]);
        parseResult.Fragment.Accept(walker);
    return Harvest(visitor);
    }
    internal static Visitor CreateRule(string sourcePath, DatabaseCatalog catalog) => new(sourcePath, catalog);

    internal static IReadOnlyList<MissingStatisticsFinding> Harvest(Visitor visitor) =>
            [
            .. visitor.Findings
                .OrderBy(f => f.SourcePath, StringComparer.Ordinal)
                .ThenBy(f => f.Line)
                .ThenBy(f => f.Column)
                .ThenBy(f => f.TableQualifiedName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(f => f.ColumnName, StringComparer.OrdinalIgnoreCase),
        ];


    internal sealed class Visitor(string sourcePath, DatabaseCatalog catalog)
        : ConstrainedColumnStatementVisitor(sourcePath, catalog)
    {
        public List<MissingStatisticsFinding> Findings { get; } = [];

        protected override void InspectStatement(ConstrainedStatement statement)
        {
            var referenced = new HashSet<(string Table, string Column)>(TableColumnKeyComparer.For(Catalog));
            statement.Node.Accept(new BaseColumnResolver.ColumnReferenceCollector(SourcePath, statement.ScopeChain, referenced, Catalog));

            foreach (var table in statement.BaseTables)
            {
                var constrainedColumns = statement.AndConstrainedColumns
                    .Where(c => Catalog.IdentifierComparer.Equals(c.TableQualifiedName, table.QualifiedName))
                    .Select(c => c.ColumnName)
                    .Distinct(Catalog.IdentifierComparer);

                foreach (var columnName in constrainedColumns)
                {
                    if (HasApplicableStatistic(table, columnName, referenced) || CannotHaveAStatistic(table, columnName))
                    {
                        continue;
                    }

                    Findings.Add(new MissingStatisticsFinding(
                        table.QualifiedName, columnName, SourcePath, statement.Node.StartLine, statement.Node.StartColumn));
                }
            }
        }

        private bool CannotHaveAStatistic(CatalogTable table, string columnName) =>
            table.FindColumn(columnName, Catalog.IdentifierComparer) is { IsComputed: true, IsDeterministic: false };

        private bool HasApplicableStatistic(
            CatalogTable table, string columnName, HashSet<(string Table, string Column)> referenced) =>
            table.EffectiveStatistics.Any(s =>
                s.KeyColumns.Count > 0 && Catalog.IdentifierComparer.Equals(s.KeyColumns[0], columnName)
                && FilterColumns(s.FilterDefinition).All(column => referenced.Contains((table.QualifiedName, column))));

        private IEnumerable<string> FilterColumns(string? filterDefinition)
        {
            if (filterDefinition is null)
            {
                return [];
            }

            var parsed = SqlScriptParser.ParseText(
                "filter-definition.sql", $"SELECT 1 WHERE {filterDefinition};", initialQuotedIdentifiers: true, Catalog.CompatibilityLevel);
            var collector = new ColumnAliasHelpers.RawColumnReferenceCollector();
            parsed.Fragment.Accept(collector);
            return collector.References.Select(reference => reference.MultiPartIdentifier.Identifiers[^1].Value);
        }
    }
}
