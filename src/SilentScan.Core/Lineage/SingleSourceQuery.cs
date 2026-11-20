using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SilentScan.Core.Lineage;

internal static class SingleSourceQuery
{
    public static bool IsPlain(SelectStatement statement) =>
        statement.WithCtesAndXmlNamespaces is null
        && statement.Into is null
        && statement.ComputeClauses.Count == 0
        && Unwrap(statement.QueryExpression) is QuerySpecification spec
        && IsPlain(spec);

    public static bool IsPlain(QuerySpecification spec)
    {
        if (spec is not
            {
                FromClause.TableReferences: [NamedTableReference or SchemaObjectFunctionTableReference],
                GroupByClause: null,
                HavingClause: null,
                TopRowFilter: null,
                OrderByClause: null,
                WindowClause: null,
                ForClause: null,
                UniqueRowFilter: UniqueRowFilter.NotSpecified,
            })
        {
            return false;
        }

        var inspector = new PlainShapeInspector();
        spec.Accept(inspector);
        return inspector.HasPlainShape;
    }

    private static QueryExpression Unwrap(QueryExpression expression) =>
        expression is QueryParenthesisExpression parenthesis ? Unwrap(parenthesis.QueryExpression) : expression;

    private sealed class PlainShapeInspector : TSqlFragmentVisitor
    {
        private int _querySpecifications;

        private bool _hasCall;

        public bool HasPlainShape => _querySpecifications == 1 && !_hasCall;

        public override void ExplicitVisit(QuerySpecification node)
        {
            _querySpecifications++;
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(FunctionCall node) => _hasCall = true;
    }
}
