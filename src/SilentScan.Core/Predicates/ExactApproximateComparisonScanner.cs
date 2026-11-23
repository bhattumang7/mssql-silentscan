using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Catalog;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;
using SilentScan.Core.TypeInference;

namespace SilentScan.Core.Predicates;

public static class ExactApproximateComparisonScanner
{
    private static readonly IReadOnlyDictionary<string, ResolvedRelation> EmptyResolvedViews = new Dictionary<string, ResolvedRelation>();

    private const int DoubleExactDigits = 15;

    private const int SingleExactDigits = 7;

    public static IReadOnlyList<ExactApproximateComparisonFinding> Scan(SqlParseResult parseResult, DatabaseCatalog catalog)
    {
        var rule = CreateRule(parseResult.SourcePath, catalog);
        var walker = new ModuleWalker(parseResult.SourcePath, catalog, EmptyResolvedViews, rules: [rule]);
        parseResult.Fragment.Accept(walker);
        return Harvest(rule);
    }

    internal static Rule CreateRule(string sourcePath, DatabaseCatalog catalog) => new(sourcePath, catalog);

    internal static IReadOnlyList<ExactApproximateComparisonFinding> Harvest(Rule rule) =>
        [
            .. rule.Findings
                .OrderBy(f => f.SourcePath, StringComparer.Ordinal)
                .ThenBy(f => f.Line)
                .ThenBy(f => f.Column),
        ];

    internal static bool ExceedsApproximateType(SqlType exact, SqlType approximate)
    {
        var digits = approximate.Category == SqlTypeCategory.Real ? SingleExactDigits : DoubleExactDigits;
        var exactDigits = exact.Category switch
        {
            SqlTypeCategory.Int => 10,
            SqlTypeCategory.BigInt => 19,
            SqlTypeCategory.SmallMoney => 10,
            SqlTypeCategory.Money => 19,
            SqlTypeCategory.Decimal => exact.Precision,
            _ => null,
        };

        return exactDigits > digits;
    }

    internal sealed class Rule(string sourcePath, DatabaseCatalog catalog) : IModuleRule
    {
        public List<ExactApproximateComparisonFinding> Findings { get; } = [];

        public void OnEnterQuerySpecificationScope(QuerySpecification node, ScopeChain scopeChain, ModuleWalker walker) =>
            ModuleWalker.InspectAllPredicateLocations(node, scopeChain, Inspect);

        public void OnEnterUpdateStatementScope(UpdateStatement node, ScopeChain scopeChain, ModuleWalker walker) =>
            ModuleWalker.InspectAllPredicateLocations(node, scopeChain, Inspect);

        public void OnEnterDeleteStatementScope(DeleteStatement node, ScopeChain scopeChain, ModuleWalker walker) =>
            ModuleWalker.InspectAllPredicateLocations(node, scopeChain, Inspect);

        private void Inspect(BooleanExpression searchCondition, ScopeChain scopeChain)
        {
            var collector = new ComparisonCollector();
            searchCondition.Accept(collector);
            foreach (var comparison in collector.Comparisons)
            {
                InspectComparison(comparison, scopeChain);
            }
        }

        private void InspectComparison(BooleanComparisonExpression comparison, ScopeChain scopeChain)
        {
            if (comparison.FirstExpression is not ColumnReferenceExpression first
                || comparison.SecondExpression is not ColumnReferenceExpression second
                || BaseColumnResolver.ResolveBaseColumn(first, sourcePath, scopeChain, catalog) is not { Type: { } firstType } left
                || BaseColumnResolver.ResolveBaseColumn(second, sourcePath, scopeChain, catalog) is not { Type: { } secondType } right)
            {
                return;
            }

            if (IsApproximate(secondType) && ExceedsApproximateType(firstType, secondType))
            {
                Add(left, firstType, right, secondType, comparison);
            }
            else if (IsApproximate(firstType) && ExceedsApproximateType(secondType, firstType))
            {
                Add(right, secondType, left, firstType, comparison);
            }
        }

        private static bool IsApproximate(SqlType type) => type.Category is SqlTypeCategory.Real or SqlTypeCategory.Float;

        private void Add(ColumnProvenance.BaseColumn exact, SqlType exactType, ColumnProvenance.BaseColumn approximate, SqlType approximateType, BooleanComparisonExpression comparison) =>
            Findings.Add(new ExactApproximateComparisonFinding(
                $"{exact.TableQualifiedName}.{exact.ColumnName}",
                exactType.ToString(),
                $"{approximate.TableQualifiedName}.{approximate.ColumnName}",
                approximateType.ToString(),
                sourcePath,
                comparison.StartLine,
                comparison.StartColumn));

        private sealed class ComparisonCollector : TSqlFragmentVisitor
        {
            public List<BooleanComparisonExpression> Comparisons { get; } = [];

            public override void ExplicitVisit(BooleanComparisonExpression node)
            {
                Comparisons.Add(node);
                base.ExplicitVisit(node);
            }

            public override void ExplicitVisit(QuerySpecification node)
            {
                _ = node;
            }
        }
    }
}
