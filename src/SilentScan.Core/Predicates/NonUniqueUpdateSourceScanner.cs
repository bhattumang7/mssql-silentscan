using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Catalog;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;
using SilentScan.Core.TypeInference;
using SilentScan.Core.Predicates.Normalization;

namespace SilentScan.Core.Predicates;

public static class NonUniqueUpdateSourceScanner
{
    private static readonly IReadOnlyDictionary<string, ResolvedRelation> EmptyResolvedViews = new Dictionary<string, ResolvedRelation>();

    public static IReadOnlyList<NonUniqueUpdateSourceFinding> Scan(SqlParseResult parseResult, DatabaseCatalog catalog)
    {
        var rule = CreateRule(parseResult.SourcePath, catalog);
        var walker = new ModuleWalker(parseResult.SourcePath, catalog, EmptyResolvedViews, rules: [rule]);
        parseResult.Fragment.Accept(walker);
    return Harvest(rule);
    }
    internal static Rule CreateRule(string sourcePath, DatabaseCatalog catalog) => new(sourcePath, catalog);

    internal static IReadOnlyList<NonUniqueUpdateSourceFinding> Harvest(Rule rule) =>
            [
            .. rule.Findings
                .OrderBy(f => f.SourcePath, StringComparer.Ordinal)
                .ThenBy(f => f.Line)
                .ThenBy(f => f.Column),
        ];


    internal sealed class Rule(string sourcePath, DatabaseCatalog catalog) : IModuleRule
    {
        public List<NonUniqueUpdateSourceFinding> Findings { get; } = [];

        public void OnEnterUpdateStatementScope(UpdateStatement node, ScopeChain scopeChain, ModuleWalker walker)
        {
            var spec = node.UpdateSpecification;
            if (spec.FromClause is not null)
            {
                Inspect(spec, walker);
            }
        }

        private void Inspect(UpdateSpecification spec, ModuleWalker walker)
        {
            if (spec.Target is not NamedTableReference targetRef)
            {
                return;
            }

            var targetAlias = targetRef.Alias?.Value ?? targetRef.SchemaObject.BaseIdentifier.Value;

            var (byAlias, ordered) = FromScopeResolver.ResolveForDataModification(spec.Target, spec.FromClause, walker.CurrentResolutionContext());
            if (!byAlias.TryGetValue(targetAlias, out var targetEntry) || targetEntry.Relation.QualifiedName is not { } targetQualifiedName)
            {
                return;
            }

            var scopeChain = new List<(IReadOnlyDictionary<string, ScopeEntry> ByAlias, IReadOnlyList<ScopeEntry> Ordered)>
            {
                (byAlias, ordered),
            };

            if (PredicateSurvivalAnalyzer.IsUnsatisfiable(spec.WhereClause?.SearchCondition, columnRef => walker.ResolveColumnFacts(columnRef, scopeChain)))
            {
                return;
            }

            foreach (var join in spec.FromClause!.TableReferences.SelectMany(PredicateTreeWalker.FlattenJoinNodes))
            {
                InspectJoin(join, targetAlias, targetQualifiedName, spec, byAlias, scopeChain, walker);
            }
        }

        private static string? AliasOf(TableReference reference) =>
            reference is NamedTableReference named ? named.Alias?.Value ?? named.SchemaObject.BaseIdentifier.Value : null;

        private void InspectJoin(
            QualifiedJoin join, string targetAlias, string targetQualifiedName,
            UpdateSpecification spec, Dictionary<string, ScopeEntry> byAlias,
            List<(IReadOnlyDictionary<string, ScopeEntry> ByAlias, IReadOnlyList<ScopeEntry> Ordered)> scopeChain,
            ModuleWalker walker)
        {
            var firstAlias = AliasOf(join.FirstTableReference);
            var secondAlias = AliasOf(join.SecondTableReference);

            string sourceAlias, sourceQualifiedName;
            if (catalog.IdentifierComparer.Equals(firstAlias, targetAlias)
                && secondAlias is not null && byAlias.TryGetValue(secondAlias, out var secondEntry)
                && !secondEntry.IsViewLayer && secondEntry.Relation.QualifiedName is { } secondQualifiedName)
            {
                (sourceAlias, sourceQualifiedName) = (secondAlias, secondQualifiedName);
            }
            else if (catalog.IdentifierComparer.Equals(secondAlias, targetAlias)
                && firstAlias is not null && byAlias.TryGetValue(firstAlias, out var firstEntry)
                && !firstEntry.IsViewLayer && firstEntry.Relation.QualifiedName is { } firstQualifiedName)
            {
                (sourceAlias, sourceQualifiedName) = (firstAlias, firstQualifiedName);
            }
            else
            {
                InspectSecondHop(join, targetAlias, targetQualifiedName, spec, byAlias, scopeChain, walker);
                return;
            }

            var sourceTable = catalog.Find(sourceQualifiedName);
            if (sourceTable is null)
            {
                return;
            }

            var joinColumns = JoinKeyUniqueness.EqualityColumnsQualifiedBy(join.SearchCondition, sourceAlias, catalog.IdentifierComparer);
            if (joinColumns.Count == 0)
            {

                return;
            }

            var whereCondition = spec.WhereClause?.SearchCondition;
            var pinnedByWhere = whereCondition is null
                ? []
                : JoinKeyUniqueness.EqualityColumnsQualifiedBy(whereCondition, sourceAlias, catalog.IdentifierComparer);
            if (JoinKeyUniqueness.IsProvenUniqueOver(sourceTable, [.. joinColumns, .. pinnedByWhere], catalog.IdentifierComparer))
            {
                return;
            }

            if (PredicateSurvivalAnalyzer.IsUnsatisfiable(join.SearchCondition, columnRef => walker.ResolveColumnFacts(columnRef, scopeChain)))
            {
                return;
            }

            var conjuncts = PredicateTreeWalker.FlattenAnd(join.SearchCondition)
                .Concat(PredicateTreeWalker.FlattenAnd(whereCondition));
            var fixedColumns = FixedColumns(conjuncts, sourceAlias, sourceTable, targetAlias, catalog.Find(targetQualifiedName));
            var setColumnNames = SetColumnsReading(spec, sourceAlias, fixedColumns);

            if (setColumnNames.Count == 0)
            {

                return;
            }

            Findings.Add(new NonUniqueUpdateSourceFinding(
                targetQualifiedName, sourceQualifiedName, joinColumns, setColumnNames,
                sourcePath, join.StartLine, join.StartColumn));
        }

        private void InspectSecondHop(
            QualifiedJoin join, string targetAlias, string targetQualifiedName,
            UpdateSpecification spec, Dictionary<string, ScopeEntry> byAlias,
            List<(IReadOnlyDictionary<string, ScopeEntry> ByAlias, IReadOnlyList<ScopeEntry> Ordered)> scopeChain,
            ModuleWalker walker)
        {
            var tree = spec.FromClause!.TableReferences.FirstOrDefault(
                reference => PredicateTreeWalker.FlattenJoinNodes(reference).Contains(join));
            var nodes = tree is null ? [] : PredicateTreeWalker.FlattenJoinNodes(tree).ToList();
            if (nodes.Count < 2 || nodes.Exists(node => node.QualifiedJoinType != QualifiedJoinType.Inner))
            {
                return;
            }

            var conjuncts = nodes.SelectMany(node => PredicateTreeWalker.FlattenAnd(node.SearchCondition)).ToList();
            foreach (var sourceAlias in new[] { AliasOf(join.FirstTableReference), AliasOf(join.SecondTableReference) })
            {
                if (sourceAlias is null || catalog.IdentifierComparer.Equals(sourceAlias, targetAlias)
                    || !byAlias.TryGetValue(sourceAlias, out var entry) || entry.IsViewLayer
                    || entry.Relation.QualifiedName is not { } sourceQualifiedName
                    || catalog.Find(sourceQualifiedName) is not { } sourceTable
                    || !JoinsToAnAliasLinkedToTarget(join, sourceAlias, targetAlias, conjuncts))
                {
                    continue;
                }

                var conditions = nodes.Select(node => node.SearchCondition)
                    .Append(spec.WhereClause?.SearchCondition)
                    .OfType<BooleanExpression>();
                var equated = conditions
                    .SelectMany(condition => JoinKeyUniqueness.EqualityColumnsQualifiedBy(condition, sourceAlias, catalog.IdentifierComparer))
                    .Distinct(catalog.IdentifierComparer)
                    .ToList();
                if (JoinKeyUniqueness.IsProvenUniqueOver(sourceTable, equated, catalog.IdentifierComparer)
                    || PredicateSurvivalAnalyzer.IsUnsatisfiable(join.SearchCondition, columnRef => walker.ResolveColumnFacts(columnRef, scopeChain)))
                {
                    continue;
                }

                var setColumnNames = SetColumnsReading(spec, sourceAlias, equated);
                if (setColumnNames.Count > 0)
                {
                    Findings.Add(new NonUniqueUpdateSourceFinding(
                        targetQualifiedName, sourceQualifiedName, equated, setColumnNames,
                        sourcePath, join.StartLine, join.StartColumn));
                }
            }
        }

        private bool JoinsToAnAliasLinkedToTarget(
            QualifiedJoin join, string sourceAlias, string targetAlias, List<BooleanExpression> conjuncts)
        {
            var edges = conjuncts.Select(EquatedAliases).OfType<(string First, string Second)>().ToList();
            var comparer = catalog.IdentifierComparer;
            var linked = new HashSet<string>(comparer) { targetAlias };
            while (edges.Exists(edge =>
                !comparer.Equals(edge.First, sourceAlias) && !comparer.Equals(edge.Second, sourceAlias)
                && linked.Contains(edge.First) != linked.Contains(edge.Second)
                && linked.Add(edge.First) | linked.Add(edge.Second)))
            {
            }

            return PredicateTreeWalker.FlattenAnd(join.SearchCondition)
                .Select(EquatedAliases)
                .OfType<(string First, string Second)>()
                .Any(edge => comparer.Equals(edge.First, sourceAlias) && linked.Contains(edge.Second)
                    || comparer.Equals(edge.Second, sourceAlias) && linked.Contains(edge.First));
        }

        private static (string First, string Second)? EquatedAliases(BooleanExpression conjunct) =>
            conjunct is BooleanComparisonExpression
            {
                ComparisonType: BooleanComparisonType.Equals,
                FirstExpression: ColumnReferenceExpression { MultiPartIdentifier.Identifiers: [.., { Value: { } first }, _] },
                SecondExpression: ColumnReferenceExpression { MultiPartIdentifier.Identifiers: [.., { Value: { } second }, _] },
            }
                ? (first, second)
                : null;

        private List<string> SetColumnsReading(UpdateSpecification spec, string sourceAlias, List<string> equated) =>
            spec.SetClauses
                .OfType<AssignmentSetClause>()
                .Where(clause => ColumnsReadThrough(clause.NewValue, sourceAlias).Any(column => !equated.Contains(column, catalog.IdentifierComparer)))
                .Select(clause => clause.Column.MultiPartIdentifier.Identifiers[^1].Value)
                .Distinct(catalog.IdentifierComparer)
                .ToList();

        private IEnumerable<string> ColumnsReadThrough(ScalarExpression expression, string alias)
        {
            var collector = new ColumnAliasHelpers.RawColumnReferenceCollector();
            expression.Accept(collector);
            return collector.References
                .Select(columnRef => ColumnAliasHelpers.ColumnNameIfQualifiedByAlias(columnRef, alias, catalog.IdentifierComparer))
                .OfType<string>();
        }

        private static readonly HashSet<SqlTypeCategory> ValueIdenticalWhenEqual =
        [
            SqlTypeCategory.TinyInt, SqlTypeCategory.SmallInt, SqlTypeCategory.Int, SqlTypeCategory.BigInt,
            SqlTypeCategory.Bit, SqlTypeCategory.Decimal, SqlTypeCategory.Money, SqlTypeCategory.SmallMoney,
            SqlTypeCategory.Date, SqlTypeCategory.DateTime, SqlTypeCategory.SmallDateTime, SqlTypeCategory.DateTime2,
            SqlTypeCategory.Time, SqlTypeCategory.UniqueIdentifier,
        ];

        private List<string> FixedColumns(
            IEnumerable<BooleanExpression> conjuncts, string sourceAlias, CatalogTable sourceTable,
            string targetAlias, CatalogTable? targetTable)
        {
            var fixedColumns = new List<string>();
            foreach (var comparison in conjuncts.OfType<BooleanComparisonExpression>()
                .Where(c => c.ComparisonType == BooleanComparisonType.Equals))
            {
                foreach (var (columnSide, otherSide) in new[]
                {
                    (comparison.FirstExpression, comparison.SecondExpression),
                    (comparison.SecondExpression, comparison.FirstExpression),
                })
                {
                    if (ColumnAliasHelpers.ColumnNameIfQualifiedByAlias(columnSide, sourceAlias, catalog.IdentifierComparer) is not { } column
                        || sourceTable.FindColumn(column, catalog.IdentifierComparer)?.Type?.Category is not { } category
                        || !ValueIdenticalWhenEqual.Contains(category))
                    {
                        continue;
                    }

                    var fixedByOtherSide = otherSide switch
                    {
                        IntegerLiteral => category is SqlTypeCategory.TinyInt or SqlTypeCategory.SmallInt
                            or SqlTypeCategory.Int or SqlTypeCategory.BigInt,
                        _ => ColumnAliasHelpers.ColumnNameIfQualifiedByAlias(otherSide, targetAlias, catalog.IdentifierComparer) is { } target
                            && targetTable?.FindColumn(target, catalog.IdentifierComparer)?.Type?.Category == category,
                    };
                    if (fixedByOtherSide)
                    {
                        fixedColumns.Add(column);
                    }
                }
            }

            return fixedColumns;
        }
    }
}
