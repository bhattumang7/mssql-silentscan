using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Catalog;
using SilentScan.Core.Common;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;

namespace SilentScan.Core.Predicates;

public static class BareTopNoOrderByScanner
{
    public static IReadOnlyList<BareTopNoOrderByFinding> Scan(SqlParseResult parseResult)
    {
        var rule = CreateRule(parseResult.SourcePath);
        var walker = new ModuleWalker(parseResult.SourcePath, new DatabaseCatalog(), EmptyResolvedViews, rules: [rule]);
        parseResult.Fragment.Accept(walker);
    return Harvest(rule);
    }
    internal static Rule CreateRule(string sourcePath) => new(sourcePath);

    internal static IReadOnlyList<BareTopNoOrderByFinding> Harvest(Rule rule) =>
            [
            .. rule.Findings
                .OrderBy(f => f.SourcePath, StringComparer.Ordinal)
                .ThenBy(f => f.Line)
                .ThenBy(f => f.Column),
        ];


    private static readonly IReadOnlyDictionary<string, ResolvedRelation> EmptyResolvedViews = new Dictionary<string, ResolvedRelation>();

    internal sealed class Rule(string sourcePath) : IModuleRule
    {
        private readonly HashSet<QuerySpecification> existsSubqueries = [];

        public List<BareTopNoOrderByFinding> Findings { get; } = [];

        public void OnEnterTSqlBatch(TSqlBatch node, ModuleWalker walker) =>
            node.Accept(new ExistsSubqueryCollector(existsSubqueries));

        public void OnEnterQuerySpecificationScope(QuerySpecification node, ScopeChain scopeChain, ModuleWalker walker)
        {
            if (node.TopRowFilter is { } top && node.OrderByClause is null && !IsHundredPercent(top) && !CannotVaryByRowChoice(node, top))
            {
                Findings.Add(new BareTopNoOrderByFinding(sourcePath, top.StartLine, top.StartColumn));
            }
        }

        private bool CannotVaryByRowChoice(QuerySpecification node, TopRowFilter top) =>
            IsZero(top.Expression)
            || existsSubqueries.Contains(node)
            || node.SelectElements.All(IsLiteralSelectElement)
            || (node.GroupByClause is null && node.SelectElements.All(IsAggregateSelectElement));

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

        private static bool IsLiteralSelectElement(SelectElement element) =>
            element is SelectScalarExpression { Expression: Literal };

        private static bool IsAggregateSelectElement(SelectElement element) =>
            element is SelectScalarExpression { Expression: FunctionCall { OverClause: null } call }
            && AggregateNames.Contains(call.FunctionName.Value);

        private static readonly HashSet<string> AggregateNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "COUNT", "COUNT_BIG", "SUM", "AVG", "MIN", "MAX", "STDEV", "STDEVP", "VAR", "VARP",
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
