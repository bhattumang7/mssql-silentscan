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
            || node.FromClause is null
            || existsSubqueries.Contains(node)
            || node.SelectElements.All(IsLiteralSelectElement)
            || PinsOneRowByUniqueKey(node)
            || (node.GroupByClause is null && node.SelectElements.All(IsAggregateSelectElement));

        private bool PinsOneRowByUniqueKey(QuerySpecification node)
        {
            if (node.FromClause is not { TableReferences: [NamedTableReference reference] }
                || node.WhereClause?.SearchCondition is not { } condition
                || catalog.Find(SchemaObjectNameHelper.Qualify(reference.SchemaObject)) is not { } table)
            {
                return false;
            }

            var names = new List<string> { reference.SchemaObject.BaseIdentifier.Value };
            if (reference.Alias is { } alias)
            {
                names.Add(alias.Value);
            }

            var pinned = new HashSet<string>(catalog.IdentifierComparer);
            CollectPinnedColumns(condition, names, pinned);
            return table.Indexes.Any(index =>
                index.IsUnique && !index.IsFiltered && !index.IsDisabled && index.KeyColumns.Count > 0
                && index.KeyColumns.All(pinned.Contains));
        }

        private void CollectPinnedColumns(BooleanExpression condition, List<string> tableNames, HashSet<string> pinned)
        {
            switch (condition)
            {
                case BooleanParenthesisExpression parenthesis:
                    CollectPinnedColumns(parenthesis.Expression, tableNames, pinned);
                    break;
                case BooleanBinaryExpression { BinaryExpressionType: BooleanBinaryExpressionType.And } both:
                    CollectPinnedColumns(both.FirstExpression, tableNames, pinned);
                    CollectPinnedColumns(both.SecondExpression, tableNames, pinned);
                    break;
                case BooleanComparisonExpression { ComparisonType: BooleanComparisonType.Equals } equality:
                    AddPinnedColumn(equality.FirstExpression, equality.SecondExpression, tableNames, pinned);
                    AddPinnedColumn(equality.SecondExpression, equality.FirstExpression, tableNames, pinned);
                    break;
            }
        }

        private void AddPinnedColumn(ScalarExpression column, ScalarExpression value, List<string> tableNames, HashSet<string> pinned)
        {
            if (column is not ColumnReferenceExpression { MultiPartIdentifier.Identifiers: { Count: > 0 } identifiers }
                || identifiers.Count > 2
                || (identifiers.Count == 2 && !tableNames.Contains(identifiers[0].Value, catalog.IdentifierComparer))
                || !IsRowIndependent(value))
            {
                return;
            }

            pinned.Add(identifiers[^1].Value);
        }

        private static bool IsRowIndependent(ScalarExpression value)
        {
            while (value is ParenthesisExpression parenthesis)
            {
                value = parenthesis.Expression;
            }

            return value is Literal or VariableReference or GlobalVariableExpression;
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

        private static bool IsLiteralSelectElement(SelectElement element) =>
            element is SelectScalarExpression { Expression: Literal }
                or SelectSetVariable { Expression: Literal, AssignmentKind: AssignmentKind.Equals };

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
