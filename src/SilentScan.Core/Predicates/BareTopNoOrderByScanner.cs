using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Catalog;
using SilentScan.Core.Common;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;

namespace SilentScan.Core.Predicates;

public static class BareTopNoOrderByScanner
{
    public static IReadOnlyList<BareTopNoOrderByFinding> Scan(SqlParseResult parseResult, DatabaseCatalog? catalog = null)
    {
        catalog ??= new DatabaseCatalog();
        var rule = CreateRule(parseResult.SourcePath, catalog);
        var walker = new ModuleWalker(parseResult.SourcePath, catalog, EmptyResolvedViews, rules: [rule]);
        parseResult.Fragment.Accept(walker);
    return Harvest(rule);
    }
    internal static Rule CreateRule(string sourcePath, DatabaseCatalog catalog) => new(sourcePath, catalog);

    internal static IReadOnlyList<BareTopNoOrderByFinding> Harvest(Rule rule) =>
            [
            .. rule.Findings
                .OrderBy(f => f.SourcePath, StringComparer.Ordinal)
                .ThenBy(f => f.Line)
                .ThenBy(f => f.Column),
        ];


    private static readonly IReadOnlyDictionary<string, ResolvedRelation> EmptyResolvedViews = new Dictionary<string, ResolvedRelation>();

    internal sealed class Rule(string sourcePath, DatabaseCatalog catalog) : IModuleRule
    {
        private readonly HashSet<QuerySpecification> existsSubqueries = [];

        private readonly HashSet<string> commonTableExpressionNames = new(catalog.IdentifierComparer);

        public List<BareTopNoOrderByFinding> Findings { get; } = [];

        public void OnEnterTSqlBatch(TSqlBatch node, ModuleWalker walker)
        {
            node.Accept(new ExistsSubqueryCollector(existsSubqueries));
            node.Accept(new CommonTableExpressionNameCollector(commonTableExpressionNames));
        }

        public void OnEnterQuerySpecificationScope(QuerySpecification node, ScopeChain scopeChain, ModuleWalker walker)
        {
            if (node.TopRowFilter is { } top && (node.OrderByClause is null || RowIndependentExpression.OrderKeysAreAllRowIndependent(node.OrderByClause.OrderByElements, node.SelectElements)) && !IsHundredPercent(top) && !CannotVaryByRowChoice(node, top))
            {
                Findings.Add(new BareTopNoOrderByFinding(sourcePath, top.StartLine, top.StartColumn));
            }
        }

        private bool CannotVaryByRowChoice(QuerySpecification node, TopRowFilter top) =>
            IsZero(top.Expression)
            || node.FromClause is null
            || ReadsAtMostOneRowSource(node.FromClause)
            || existsSubqueries.Contains(node)
            || node.SelectElements.All(IsLiteralSelectElement)
            || PinsOneRowByUniqueKey(node)
            || (node.GroupByClause is null && node.SelectElements.All(IsAggregateSelectElement));

        private bool ReadsAtMostOneRowSource(FromClause from) =>
            from.TableReferences is [var only]
            && (only is InlineDerivedTable { RowValues.Count: 1 }
                || only is QueryDerivedTable derived && ProducesAtMostOneRow(derived.QueryExpression));

        private bool ProducesAtMostOneRow(QueryExpression query) =>
            VariableWriteSites.ProducesAtMostOneRow(query)
            || query is QueryParenthesisExpression parenthesis && ProducesAtMostOneRow(parenthesis.QueryExpression)
            || query is QuerySpecification specification
                && (TopPercentLiteralHelper.ReturnsAtMostOneRow(specification.TopRowFilter)
                    || SingleRowKeyPinning.QueryReadsAtMostOneRow(specification, catalog, commonTableExpressionNames));

        private bool PinsOneRowByUniqueKey(QuerySpecification node) =>
            node.FromClause is { TableReferences: [var only] } && PinsAtMostOneRow(only, node.WhereClause);

        private bool PinsAtMostOneRow(TableReference reference, WhereClause? where)
        {
            var tables = new List<NamedTableReference>();
            var conjuncts = PinningConjunct.From(where?.SearchCondition);
            return SingleRowKeyPinning.CollectPinnableTables(reference, tables, conjuncts, catalog.IdentifierComparer)
                && SingleRowKeyPinning.PinsAtMostOneRow(tables, conjuncts, commonTableExpressionNames, catalog);
        }

        private static bool IsHundredPercent(TopRowFilter top) =>
            top.Percent && TopPercentLiteralHelper.IsHundredPercentLiteral(top.Expression);

        private static bool IsZero(ScalarExpression expression)
        {
            while (expression is ParenthesisExpression parenthesized)
            {
                expression = parenthesized.Expression;
            }

            return expression is IntegerLiteral { Value: "0" };
        }

        private sealed class CommonTableExpressionNameCollector(HashSet<string> names) : TSqlFragmentVisitor
        {
            public override void Visit(CommonTableExpression node) => names.Add(node.ExpressionName.Value);
        }

        private static bool IsLiteralSelectElement(SelectElement element) =>
            element is SelectScalarExpression { Expression: Literal }
                or SelectSetVariable { Expression: Literal, AssignmentKind: AssignmentKind.Equals };

        private static bool IsAggregateSelectElement(SelectElement element) =>
            element switch
            {
                SelectScalarExpression { Expression: { } expression } => RowIndependentExpression.IsAggregateOnly(expression),
                SelectSetVariable { Expression: { } expression } => RowIndependentExpression.IsAggregateOnly(expression),
                _ => false,
            };

        private sealed class ExistsSubqueryCollector(HashSet<QuerySpecification> target) : TSqlFragmentVisitor
        {
            public override void ExplicitVisit(ExistsPredicate node)
            {
                Collect(node.Subquery.QueryExpression);
                base.ExplicitVisit(node);
            }

            private void Collect(QueryExpression expression)
            {
                switch (expression)
                {
                    case QuerySpecification specification:
                        target.Add(specification);
                        break;
                    case QueryParenthesisExpression parenthesized:
                        Collect(parenthesized.QueryExpression);
                        break;
                    case BinaryQueryExpression binary:
                        Collect(binary.FirstQueryExpression);
                        Collect(binary.SecondQueryExpression);
                        break;
                }
            }
        }
    }
}
