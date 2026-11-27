using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SilentScan.Core.Predicates;

internal static class ForcedParameterizationPosition
{
    public static TSqlStatement? EnclosingStatement(TSqlFragment root, int offset)
    {
        var finder = new EnclosingStatementFinder(offset);
        root.Accept(finder);
        return finder.Best;
    }

    public static bool IsRecompiled(TSqlStatement statement) =>
        statement is StatementWithCtesAndXmlNamespaces { OptimizerHints: { } hints }
        && hints.Any(h => h.HintKind == OptimizerHintKind.Recompile);

    public static bool UsesVariables(TSqlStatement statement)
    {
        var visitor = new VariableUseVisitor();
        statement.Accept(visitor);
        return visitor.Found;
    }

    private sealed class EnclosingStatementFinder(int offset) : TSqlFragmentVisitor
    {
        public TSqlStatement? Best { get; private set; }

        public override void Visit(TSqlStatement node)
        {
            if (node.StartOffset <= offset && offset < node.StartOffset + node.FragmentLength
                && (Best is null || node.FragmentLength < Best.FragmentLength))
            {
                Best = node;
            }
        }
    }

    private sealed class VariableUseVisitor : TSqlFragmentVisitor
    {
        public bool Found { get; private set; }

        public override void Visit(VariableReference node) => Found = true;

        public override void Visit(VariableTableReference node) => Found = true;
    }
}
