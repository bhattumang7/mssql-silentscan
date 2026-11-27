using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SilentScan.Core.Predicates;

internal static class DynamicSqlAutoParameterizationGuard
{
    public static bool IsProvablyNotAutoParameterized(TSqlFragment root, int offset)
    {
        if (ForcedParameterizationPosition.EnclosingStatement(root, offset) is not { } statement
            || ForcedParameterizationPosition.IsRecompiled(statement))
        {
            return false;
        }

        var evidence = new IneligibilityVisitor();
        statement.Accept(evidence);
        return evidence.Found;
    }

    private sealed class IneligibilityVisitor : TSqlFragmentVisitor
    {
        public bool Found { get; private set; }

        public override void Visit(JoinTableReference node) => Found = true;

        public override void Visit(ScalarSubquery node) => Found = true;

        public override void Visit(QueryDerivedTable node) => Found = true;

        public override void Visit(ExistsPredicate node) => Found = true;

        public override void Visit(InPredicate node)
        {
            if (node.Subquery is not null)
            {
                Found = true;
            }
        }

        public override void Visit(GroupByClause node) => Found = true;

        public override void Visit(HavingClause node) => Found = true;

        public override void Visit(TopRowFilter node) => Found = true;

        public override void Visit(BinaryQueryExpression node) => Found = true;

        public override void Visit(OptimizerHint node) => Found = true;

        public override void Visit(OverClause node) => Found = true;

        public override void Visit(WithCtesAndXmlNamespaces node)
        {
            if (node.CommonTableExpressions.Count > 0)
            {
                Found = true;
            }
        }

        public override void Visit(QuerySpecification node)
        {
            if (node.UniqueRowFilter == UniqueRowFilter.Distinct || node.FromClause?.TableReferences.Count > 1)
            {
                Found = true;
            }
        }
    }
}
