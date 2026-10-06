using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SilentScan.Core.Predicates;

internal sealed class EstimateSensitiveFromClauseCollector : TSqlFragmentVisitor
{
    private int _outerSourceDepth;
    private int _semiJoinDepth;

    public HashSet<FromClause> FromClauses { get; } = [];

    public override void ExplicitVisit(FromClause node)
    {
        if (node.TableReferences.Count > 1 || node.TableReferences.Any(t => t is JoinTableReference))
        {
            FromClauses.Add(node);
        }

        base.ExplicitVisit(node);
    }

    public override void ExplicitVisit(QuerySpecification node)
    {
        if (node.FromClause is not null
            && (node.GroupByClause is not null || node.UniqueRowFilter == UniqueRowFilter.Distinct || _semiJoinDepth > 0))
        {
            FromClauses.Add(node.FromClause);
        }

        var hasSource = node.FromClause is not null;
        _outerSourceDepth += hasSource ? 1 : 0;
        base.ExplicitVisit(node);
        _outerSourceDepth -= hasSource ? 1 : 0;
    }

    public override void ExplicitVisit(UpdateSpecification node) => VisitWithOuterSource(() => base.ExplicitVisit(node));

    public override void ExplicitVisit(DeleteSpecification node) => VisitWithOuterSource(() => base.ExplicitVisit(node));

    public override void ExplicitVisit(MergeSpecification node) => VisitWithOuterSource(() => base.ExplicitVisit(node));

    public override void ExplicitVisit(InPredicate node)
    {
        node.Expression?.Accept(this);
        VisitAsSemiJoin(() => node.Subquery?.Accept(this));
        foreach (var value in node.Values)
        {
            value.Accept(this);
        }
    }

    public override void ExplicitVisit(ExistsPredicate node) => VisitAsSemiJoin(() => base.ExplicitVisit(node));

    private void VisitWithOuterSource(Action visit)
    {
        _outerSourceDepth++;
        visit();
        _outerSourceDepth--;
    }

    private void VisitAsSemiJoin(Action visit)
    {
        var applies = _outerSourceDepth > 0;
        _semiJoinDepth += applies ? 1 : 0;
        visit();
        _semiJoinDepth -= applies ? 1 : 0;
    }
}
