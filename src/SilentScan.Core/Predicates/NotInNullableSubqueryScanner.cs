using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Catalog;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates.Normalization;

namespace SilentScan.Core.Predicates;

public static class NotInNullableSubqueryScanner
{
    private static readonly IReadOnlyDictionary<string, ResolvedRelation> EmptyResolvedViews = new Dictionary<string, ResolvedRelation>();

    public static IReadOnlyList<NotInNullableSubqueryFinding> Scan(SqlParseResult parseResult, DatabaseCatalog catalog)
    {
        var rule = CreateRule(parseResult.SourcePath, catalog);
        var walker = new ModuleWalker(parseResult.SourcePath, catalog, EmptyResolvedViews, rules: [rule]);
        parseResult.Fragment.Accept(walker);
    return Harvest(rule);
    }
    internal static Rule CreateRule(string sourcePath, DatabaseCatalog catalog) => new(sourcePath, catalog);

    internal static IReadOnlyList<NotInNullableSubqueryFinding> Harvest(Rule rule) =>
            [
            .. rule.Findings
                .OrderBy(f => f.SourcePath, StringComparer.Ordinal)
                .ThenBy(f => f.Line)
                .ThenBy(f => f.Column),
        ];


    internal sealed class Rule(string sourcePath, DatabaseCatalog catalog) : IModuleRule
    {
        public List<NotInNullableSubqueryFinding> Findings { get; } = [];

        public void OnEnterQuerySpecificationScope(QuerySpecification node, ScopeChain scopeChain, ModuleWalker walker) =>
            ModuleWalker.InspectAllPredicateLocations(node, scopeChain, (condition, _) => InspectSearchCondition(condition, walker));

        public void OnEnterUpdateStatementScope(UpdateStatement node, ScopeChain scopeChain, ModuleWalker walker) =>
            ModuleWalker.InspectAllPredicateLocations(node, scopeChain, (condition, _) => InspectSearchCondition(condition, walker));

        public void OnEnterDeleteStatementScope(DeleteStatement node, ScopeChain scopeChain, ModuleWalker walker) =>
            ModuleWalker.InspectAllPredicateLocations(node, scopeChain, (condition, _) => InspectSearchCondition(condition, walker));

        private void InspectSearchCondition(BooleanExpression? searchCondition, ModuleWalker walker)
        {
            if (searchCondition is null)
            {
                return;
            }

            var scopeChain = walker.CurrentScopeChain();
            if (PredicateSurvivalAnalyzer.IsUnsatisfiable(searchCondition, columnRef => walker.ResolveColumnFacts(columnRef, scopeChain)))
            {
                return;
            }

            foreach (var membership in NotInMemberships(searchCondition, negated: false, underDisjunction: false))
            {
                TryMatch(membership, scopeChain, walker);
            }
        }

        private readonly record struct Membership(
            ScalarExpression Outer, ScalarSubquery Subquery, BooleanExpression Predicate, bool UnderDisjunction);

        private static IEnumerable<Membership> NotInMemberships(BooleanExpression condition, bool negated, bool underDisjunction)
        {
            switch (condition)
            {
                case BooleanParenthesisExpression parenthesis:
                    return NotInMemberships(parenthesis.Expression, negated, underDisjunction);
                case BooleanNotExpression not:
                    return NotInMemberships(not.Expression, !negated, underDisjunction);
                case BooleanBinaryExpression binary:
                    var splits = underDisjunction || (binary.BinaryExpressionType == BooleanBinaryExpressionType.Or) != negated;
                    return NotInMemberships(binary.FirstExpression, negated, splits)
                        .Concat(NotInMemberships(binary.SecondExpression, negated, splits));
                case InPredicate { Subquery: { } subquery } membership when membership.NotDefined != negated:
                    return [new Membership(membership.Expression, subquery, membership, underDisjunction)];
                case SubqueryComparisonPredicate quantified when IsNotInShape(quantified, negated):
                    return [new Membership(quantified.Expression, quantified.Subquery, quantified, underDisjunction)];
                default:
                    return [];
            }
        }

        private static bool IsNotInShape(SubqueryComparisonPredicate predicate, bool negated) =>
            predicate.SubqueryComparisonPredicateType == SubqueryComparisonPredicateType.All
                ? !negated && predicate.ComparisonType is BooleanComparisonType.NotEqualToBrackets or BooleanComparisonType.NotEqualToExclamation
                : negated && predicate.ComparisonType == BooleanComparisonType.Equals;

        private void TryMatch(
            Membership membership,
            IReadOnlyList<(IReadOnlyDictionary<string, ScopeEntry> ByAlias, IReadOnlyList<ScopeEntry> Ordered)> enclosingScopeChain,
            ModuleWalker walker)
        {
            if (membership.Subquery.QueryExpression is not QuerySpecification subquerySpec)
            {
                return;
            }

            if (subquerySpec.SelectElements.Count != 1
                || subquerySpec.SelectElements[0] is not SelectScalarExpression { Expression: ColumnReferenceExpression innerColumnRef })
            {
                return;
            }

            var innerScope = FromScopeResolver.Resolve(subquerySpec.FromClause, walker.CurrentResolutionContext());
            var subqueryScopeChain = new List<(IReadOnlyDictionary<string, ScopeEntry> ByAlias, IReadOnlyList<ScopeEntry> Ordered)> { innerScope };
            subqueryScopeChain.AddRange(enclosingScopeChain);

            var innerProvenance = ScalarExpressionResolver.ResolveColumnReference(innerColumnRef, subqueryScopeChain, sourcePath, ledger: null, catalog);
            if (innerProvenance is not ColumnProvenance.BaseColumn { Depth: 0 } innerColumn)
            {
                return;
            }

            var catalogColumn = catalog.Find(innerColumn.TableQualifiedName)?.FindColumn(innerColumn.ColumnName, catalog.IdentifierComparer);
            if (catalogColumn is null || !(catalogColumn.IsNullable || innerColumn.IsNullableSide))
            {
                return;
            }

            if (HasDefensiveNotNullFilter([subquerySpec.WhereClause?.SearchCondition, subquerySpec.HavingClause?.SearchCondition, .. NullRejection.PreservedInnerJoinConditions(subquerySpec.FromClause)], innerColumn.TableQualifiedName, innerColumn.ColumnName, subqueryScopeChain))
            {
                return;
            }

            var outerColumnName = membership.Outer is ColumnReferenceExpression outerColumnRef
                ? outerColumnRef.MultiPartIdentifier.Identifiers[^1].Value
                : null;

            var indexed = catalog.Find(innerColumn.TableQualifiedName)?.IsIndexedColumn(innerColumn.ColumnName, catalog.IdentifierComparer) ?? false;

            Findings.Add(new NotInNullableSubqueryFinding(
                outerColumnName, innerColumn.TableQualifiedName, innerColumn.ColumnName, indexed,
                sourcePath, membership.Predicate.StartLine, membership.Predicate.StartColumn,
                UnderDisjunction: membership.UnderDisjunction));
        }

        private bool HasDefensiveNotNullFilter(
            IEnumerable<BooleanExpression?> filters, string tableQualifiedName, string columnName,
            IReadOnlyList<(IReadOnlyDictionary<string, ScopeEntry> ByAlias, IReadOnlyList<ScopeEntry> Ordered)> subqueryScopeChain)
        {
            foreach (var clause in filters.SelectMany(PredicateTreeWalker.FlattenAnd))
            {
                foreach (var filterColumnRef in NullRejection.Columns(clause))
                {
                    var provenance = ScalarExpressionResolver.ResolveColumnReference(filterColumnRef, subqueryScopeChain, sourcePath, ledger: null, catalog);
                    if (provenance is ColumnProvenance.BaseColumn { Depth: 0 } filterColumn
                        && catalog.IdentifierComparer.Equals(filterColumn.TableQualifiedName, tableQualifiedName)
                        && catalog.IdentifierComparer.Equals(filterColumn.ColumnName, columnName))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
