using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SilentScan.Core.Predicates;

internal sealed class ComparedExpressionCollector : TSqlFragmentVisitor
{
    public List<ScalarExpression> Expressions { get; } = [];

    public override void Visit(BooleanComparisonExpression node) =>
        Expressions.AddRange([node.FirstExpression, node.SecondExpression]);

    public override void Visit(BooleanTernaryExpression node) => Expressions.Add(node.FirstExpression);

    public override void Visit(InPredicate node) => Expressions.Add(node.Expression);
}
