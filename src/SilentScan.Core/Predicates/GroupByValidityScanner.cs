using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Catalog;
using SilentScan.Core.Common;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;

namespace SilentScan.Core.Predicates;

public static class GroupByValidityScanner
{
    private static readonly HashSet<string> AggregateFunctionNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "SUM", "COUNT", "COUNT_BIG", "AVG", "MIN", "MAX",
        "STDEV", "STDEVP", "VAR", "VARP",
        "GROUPING", "GROUPING_ID", "STRING_AGG", "CHECKSUM_AGG", "APPROX_COUNT_DISTINCT",
    };

    public static IReadOnlyList<GroupByValidityFinding> Scan(SqlParseResult parseResult, DatabaseCatalog catalog)
    {
        var rule = CreateRule(parseResult.SourcePath, catalog);
        var walker = new ModuleWalker(parseResult.SourcePath, catalog, EmptyResolvedViews, rules: [rule]);
        parseResult.Fragment.Accept(walker);
        return Harvest(rule);
    }

    internal static Rule CreateRule(string sourcePath, DatabaseCatalog catalog) => new(sourcePath, catalog.IdentifierComparer);

    internal static IReadOnlyList<GroupByValidityFinding> Harvest(Rule rule) =>
        [
            .. rule.Findings
                .OrderBy(f => f.SourcePath, StringComparer.Ordinal)
                .ThenBy(f => f.Line)
                .ThenBy(f => f.Column),
        ];

    private static readonly IReadOnlyDictionary<string, ResolvedRelation> EmptyResolvedViews = new Dictionary<string, ResolvedRelation>();

    internal sealed class Rule(string sourcePath, StringComparer identifierComparer) : IModuleRule
    {
        public List<GroupByValidityFinding> Findings { get; } = [];

        public void OnEnterQuerySpecificationScope(QuerySpecification node, ScopeChain scopeChain, ModuleWalker walker)
        {
            if (node.GroupByClause is not { } groupBy)
            {
                return;
            }

            var groupedExpressions = groupBy.GroupingSpecifications.SelectMany(FlattenGroupingSpecification).ToList();

            var groupedTexts = new HashSet<string>(
                groupedExpressions.Select(FragmentTextRenderer.Render),
                StringComparer.Ordinal);

            var groupedColumnNames = new HashSet<string>(
                groupedExpressions
                    .OfType<ColumnReferenceExpression>()
                    .Where(c => c.MultiPartIdentifier.Identifiers.Count > 0)
                    .Select(c => c.MultiPartIdentifier.Identifiers[^1].Value),
                identifierComparer);

            foreach (var element in node.SelectElements.OfType<SelectScalarExpression>())
            {
                CheckScalar(element.Expression, groupedTexts, groupedColumnNames, GroupByValidityFindingKind.SelectList);
            }

            if (node.HavingClause?.SearchCondition is { } having)
            {
                CheckBoolean(having, groupedTexts, groupedColumnNames, GroupByValidityFindingKind.Having);
            }

            if (node.OrderByClause is { } orderBy)
            {
                foreach (var element in orderBy.OrderByElements)
                {
                    CheckScalar(element.Expression, groupedTexts, groupedColumnNames, GroupByValidityFindingKind.OrderBy);
                }
            }
        }

        private static IEnumerable<ScalarExpression> FlattenGroupingSpecification(GroupingSpecification spec) => spec switch
        {
            ExpressionGroupingSpecification expression => [expression.Expression],
            CompositeGroupingSpecification composite => composite.Items.SelectMany(FlattenGroupingSpecification),
            RollupGroupingSpecification rollup => rollup.Arguments.SelectMany(FlattenGroupingSpecification),
            CubeGroupingSpecification cube => cube.Arguments.SelectMany(FlattenGroupingSpecification),
            GroupingSetsGroupingSpecification groupingSets => groupingSets.Sets.SelectMany(FlattenGroupingSpecification),
            _ => [],
        };

        private void CheckBoolean(BooleanExpression node, HashSet<string> groupedTexts, HashSet<string> groupedColumnNames, GroupByValidityFindingKind kind)
        {
            switch (node)
            {
                case BooleanBinaryExpression binary:
                    CheckBoolean(binary.FirstExpression, groupedTexts, groupedColumnNames, kind);
                    CheckBoolean(binary.SecondExpression, groupedTexts, groupedColumnNames, kind);
                    break;

                case BooleanNotExpression not:
                    CheckBoolean(not.Expression, groupedTexts, groupedColumnNames, kind);
                    break;

                case BooleanParenthesisExpression paren:
                    CheckBoolean(paren.Expression, groupedTexts, groupedColumnNames, kind);
                    break;

                case BooleanComparisonExpression cmp:
                    CheckScalar(cmp.FirstExpression, groupedTexts, groupedColumnNames, kind);
                    CheckScalar(cmp.SecondExpression, groupedTexts, groupedColumnNames, kind);
                    break;

                case BooleanTernaryExpression ternary:
                    CheckScalar(ternary.FirstExpression, groupedTexts, groupedColumnNames, kind);
                    CheckScalar(ternary.SecondExpression, groupedTexts, groupedColumnNames, kind);
                    CheckScalar(ternary.ThirdExpression, groupedTexts, groupedColumnNames, kind);
                    break;

                case BooleanIsNullExpression isNull:
                    CheckScalar(isNull.Expression, groupedTexts, groupedColumnNames, kind);
                    break;

                case LikePredicate like:
                    CheckScalar(like.FirstExpression, groupedTexts, groupedColumnNames, kind);
                    CheckScalar(like.SecondExpression, groupedTexts, groupedColumnNames, kind);
                    break;

                case InPredicate { Subquery: null } inPredicate:
                    CheckScalar(inPredicate.Expression, groupedTexts, groupedColumnNames, kind);
                    foreach (var value in inPredicate.Values)
                    {
                        CheckScalar(value, groupedTexts, groupedColumnNames, kind);
                    }

                    break;

                case SubqueryComparisonPredicate subqueryComparison:
                    CheckScalar(subqueryComparison.Expression, groupedTexts, groupedColumnNames, kind);
                    break;
            }
        }

        private void CheckScalar(ScalarExpression node, HashSet<string> groupedTexts, HashSet<string> groupedColumnNames, GroupByValidityFindingKind kind)
        {
            if (groupedTexts.Contains(FragmentTextRenderer.Render(node)))
            {
                return;
            }

            switch (node)
            {
                case ColumnReferenceExpression { MultiPartIdentifier.Identifiers: [.., { } lastIdentifier] }
                    when groupedColumnNames.Contains(lastIdentifier.Value):
                    break;

                case ColumnReferenceExpression:
                    Findings.Add(new GroupByValidityFinding(kind, FragmentTextRenderer.Render(node), sourcePath, node.StartLine, node.StartColumn));
                    break;

                case BinaryExpression binary:
                    CheckScalar(binary.FirstExpression, groupedTexts, groupedColumnNames, kind);
                    CheckScalar(binary.SecondExpression, groupedTexts, groupedColumnNames, kind);
                    break;

                case UnaryExpression unary:
                    CheckScalar(unary.Expression, groupedTexts, groupedColumnNames, kind);
                    break;

                case ParenthesisExpression paren:
                    CheckScalar(paren.Expression, groupedTexts, groupedColumnNames, kind);
                    break;

                case CastCall castCall:
                    CheckScalar(castCall.Parameter, groupedTexts, groupedColumnNames, kind);
                    break;

                case ConvertCall convertCall:
                    CheckScalar(convertCall.Parameter, groupedTexts, groupedColumnNames, kind);
                    break;

                case TryCastCall tryCastCall:
                    CheckScalar(tryCastCall.Parameter, groupedTexts, groupedColumnNames, kind);
                    break;

                case TryConvertCall tryConvertCall:
                    CheckScalar(tryConvertCall.Parameter, groupedTexts, groupedColumnNames, kind);
                    break;

                case FunctionCall functionCall when functionCall.OverClause is null && AggregateFunctionNames.Contains(functionCall.FunctionName.Value):
                    break;

                case FunctionCall functionCall:
                    foreach (var parameter in functionCall.Parameters)
                    {
                        CheckScalar(parameter, groupedTexts, groupedColumnNames, kind);
                    }

                    break;

                case CoalesceExpression coalesce:
                    foreach (var expression in coalesce.Expressions)
                    {
                        CheckScalar(expression, groupedTexts, groupedColumnNames, kind);
                    }

                    break;

                case NullIfExpression nullIf:
                    CheckScalar(nullIf.FirstExpression, groupedTexts, groupedColumnNames, kind);
                    CheckScalar(nullIf.SecondExpression, groupedTexts, groupedColumnNames, kind);
                    break;

                case IIfCall iif:
                    CheckBoolean(iif.Predicate, groupedTexts, groupedColumnNames, kind);
                    CheckScalar(iif.ThenExpression, groupedTexts, groupedColumnNames, kind);
                    CheckScalar(iif.ElseExpression, groupedTexts, groupedColumnNames, kind);
                    break;

                case SearchedCaseExpression searchedCase:
                    foreach (var whenClause in searchedCase.WhenClauses)
                    {
                        CheckBoolean(whenClause.WhenExpression, groupedTexts, groupedColumnNames, kind);
                        CheckScalar(whenClause.ThenExpression, groupedTexts, groupedColumnNames, kind);
                    }

                    if (searchedCase.ElseExpression is { } searchedElse)
                    {
                        CheckScalar(searchedElse, groupedTexts, groupedColumnNames, kind);
                    }

                    break;

                case SimpleCaseExpression simpleCase:
                    CheckScalar(simpleCase.InputExpression, groupedTexts, groupedColumnNames, kind);
                    foreach (var whenClause in simpleCase.WhenClauses)
                    {
                        CheckScalar(whenClause.WhenExpression, groupedTexts, groupedColumnNames, kind);
                        CheckScalar(whenClause.ThenExpression, groupedTexts, groupedColumnNames, kind);
                    }

                    if (simpleCase.ElseExpression is { } simpleElse)
                    {
                        CheckScalar(simpleElse, groupedTexts, groupedColumnNames, kind);
                    }

                    break;
            }
        }
    }
}
