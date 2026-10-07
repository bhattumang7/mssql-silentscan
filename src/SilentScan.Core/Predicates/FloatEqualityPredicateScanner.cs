using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Catalog;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;
using SilentScan.Core.TypeInference;

namespace SilentScan.Core.Predicates;

public static class FloatEqualityPredicateScanner
{
    private static readonly IReadOnlyDictionary<string, ResolvedRelation> EmptyResolvedViews = new Dictionary<string, ResolvedRelation>();

    public static IReadOnlyList<FloatEqualityFinding> Scan(SqlParseResult parseResult, DatabaseCatalog catalog)
    {
        var rule = CreateRule(parseResult.SourcePath, catalog);
        var walker = new ModuleWalker(parseResult.SourcePath, catalog, EmptyResolvedViews, rules: [rule]);
        parseResult.Fragment.Accept(walker);
    return Harvest(rule);
    }
    internal static Rule CreateRule(string sourcePath, DatabaseCatalog catalog) => new(sourcePath, catalog);

    internal static IReadOnlyList<FloatEqualityFinding> Harvest(Rule rule) =>
            [
            .. rule.Findings
                .OrderBy(f => f.SourcePath, StringComparer.Ordinal)
                .ThenBy(f => f.Line)
                .ThenBy(f => f.Column),
        ];


    internal sealed class Rule(string sourcePath, DatabaseCatalog catalog) : IModuleRule
    {
        private readonly ExtremumCopyTracker _extremumCopies = new(sourcePath, catalog);
        private readonly List<(FloatEqualityFinding Finding, ColumnProvenance.BaseColumn Column, ColumnProvenance.BaseColumn? Other)> _candidates = [];

        public IReadOnlyList<FloatEqualityFinding> Findings =>
            [.. _candidates.Where(c => !_extremumCopies.AreExactCopies(c.Column, c.Other)).Select(c => c.Finding)];

        public void OnEnterQuerySpecificationScope(QuerySpecification node, ScopeChain scopeChain, ModuleWalker walker)
        {
            _extremumCopies.OnQuerySpecification(node, scopeChain);
            ModuleWalker.InspectAllPredicateLocations(node, scopeChain, Inspect);
        }

        public void OnEnterInsertStatementScope(InsertStatement node, ModuleWalker walker) =>
            _extremumCopies.OnInsert(node, walker);

        public void OnEnterAssignmentSetClause(AssignmentSetClause node, ModuleWalker walker) =>
            _extremumCopies.OnAssignment(node);

        public void OnEnterMergeStatementScope(MergeStatement node, ScopeChain scopeChain, ModuleWalker walker) =>
            _extremumCopies.OnMerge(node);

        public void OnEnterUpdateStatementScope(UpdateStatement node, ScopeChain scopeChain, ModuleWalker walker) =>
            ModuleWalker.InspectAllPredicateLocations(node, scopeChain, Inspect);

        public void OnEnterDeleteStatementScope(DeleteStatement node, ScopeChain scopeChain, ModuleWalker walker) =>
            ModuleWalker.InspectAllPredicateLocations(node, scopeChain, Inspect);

        private void Inspect(BooleanExpression searchCondition, ScopeChain scopeChain)
        {
            var collector = new EqualityCollector();
            searchCondition.Accept(collector);
            foreach (var comparison in collector.Comparisons)
            {
                InspectEquality(comparison, scopeChain);
            }
        }

        private void InspectEquality(BooleanComparisonExpression comparison, ScopeChain scopeChain)
        {
            foreach (var side in new[] { comparison.FirstExpression, comparison.SecondExpression })
            {
                if (side is not ColumnReferenceExpression columnRef
                    || BaseColumnResolver.ResolveBaseColumn(columnRef, sourcePath, scopeChain, catalog) is not { } resolved
                    || resolved.Type?.Category is not (SqlTypeCategory.Real or SqlTypeCategory.Float))
                {
                    continue;
                }

                var otherSide = ReferenceEquals(side, comparison.FirstExpression) ? comparison.SecondExpression : comparison.FirstExpression;
                var finding = new FloatEqualityFinding(
                    resolved.TableQualifiedName,
                    resolved.ColumnName,
                    resolved.Type!.ToString(),
                    sourcePath,
                    comparison.StartLine,
                    comparison.StartColumn);
                _candidates.Add((finding, resolved, BaseColumnResolver.ResolveBaseColumn(otherSide, sourcePath, scopeChain, catalog)));

                return;
            }
        }

        private sealed class EqualityCollector : TSqlFragmentVisitor
        {
            public List<BooleanComparisonExpression> Comparisons { get; } = [];

            public override void ExplicitVisit(BooleanComparisonExpression node)
            {
                if (node.ComparisonType is BooleanComparisonType.Equals
                    or BooleanComparisonType.NotEqualToBrackets
                    or BooleanComparisonType.NotEqualToExclamation)
                {
                    Comparisons.Add(node);
                }

                base.ExplicitVisit(node);
            }

            public override void ExplicitVisit(QuerySpecification node)
            {
                _ = node;
            }
        }
    }
}
