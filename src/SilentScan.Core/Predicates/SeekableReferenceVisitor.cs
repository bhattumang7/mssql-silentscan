using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SilentScan.Core.Predicates;

internal sealed class SeekableReferenceVisitor(TSqlFragmentVisitor references) : TSqlFragmentVisitor
{
    public override void ExplicitVisit(ColumnReferenceExpression node) => node.Accept(references);

    public override void ExplicitVisit(FunctionCall node)
    {
        if (string.Equals(node.FunctionName.Value, "ISNULL", StringComparison.OrdinalIgnoreCase))
        {
            base.ExplicitVisit(node);
        }
    }

    public override void ExplicitVisit(CoalesceExpression node)
    {
        _ = node;
    }

    public override void ExplicitVisit(BinaryExpression node)
    {
        _ = node;
    }

    public override void ExplicitVisit(LikePredicate node)
    {
        if (node.SecondExpression is not StringLiteral { Value: ['%', ..] })
        {
            base.ExplicitVisit(node);
        }
    }
}
