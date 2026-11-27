using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SilentScan.Core.Predicates;

internal sealed class PlanPerBranchExclusions(HashSet<QuerySpecification> excluded) : TSqlFragmentVisitor
{
    public override void Visit(SetVariableStatement node) => Exclude(node);

    public override void Visit(DeclareVariableStatement node) => Exclude(node);

    public override void Visit(DeclareCursorStatement node) => Exclude(node);

    public override void Visit(IfStatement node) => Exclude(node.Predicate);

    public override void Visit(InsertStatement node)
    {
        if (node.InsertSpecification.Target is VariableTableReference)
        {
            Exclude(node);
        }
    }

    public override void Visit(SelectStatement node)
    {
        if (node.QueryExpression is QuerySpecification specification
            && specification.SelectElements.Any(element => element is SelectSetVariable))
        {
            Exclude(node);
        }
    }

    private void Exclude(TSqlFragment fragment) => fragment.Accept(new QueryCollector(excluded));

    private sealed class QueryCollector(HashSet<QuerySpecification> excluded) : TSqlFragmentVisitor
    {
        public override void Visit(QuerySpecification node) => excluded.Add(node);
    }
}
