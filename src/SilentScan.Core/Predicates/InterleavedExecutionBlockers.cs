using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SilentScan.Core.Predicates;

internal sealed class InterleavedExecutionBlockers(HashSet<FromClause> blocked) : TSqlFragmentVisitor
{
    private const string CompatibilityHintPrefix = "QUERY_OPTIMIZER_COMPATIBILITY_LEVEL_";

    private const int InterleavedExecutionCompatibilityLevel = 140;

    private const string DisableInterleavedExecutionTraceFlag = "11005";

    public override void Visit(InsertStatement node) => Block(node);

    public override void Visit(UpdateStatement node) => Block(node);

    public override void Visit(DeleteStatement node) => Block(node);

    public override void Visit(MergeStatement node) => Block(node);

    public override void Visit(DeclareCursorStatement node) => Block(node);

    public override void Visit(IfStatement node) => Block(node.Predicate);

    public override void Visit(WhileStatement node) => Block(node.Predicate);

    public override void Visit(SelectStatement node)
    {
        if (node.Into is not null || node.OptimizerHints.Any(DisablesInterleavedExecution))
        {
            Block(node);
        }
    }

    private static bool DisablesInterleavedExecution(OptimizerHint hint) => hint switch
    {
        UseHintList list => list.Hints.Any(value => DisablesInterleavedExecution(value.Value)),
        LiteralOptimizerHint { HintKind: OptimizerHintKind.QueryTraceOn, Value.Value: DisableInterleavedExecutionTraceFlag } => true,
        _ => false,
    };

    private static bool DisablesInterleavedExecution(string hint) =>
        string.Equals(hint, "DISABLE_INTERLEAVED_EXECUTION_TVF", StringComparison.OrdinalIgnoreCase)
        || (hint.StartsWith(CompatibilityHintPrefix, StringComparison.OrdinalIgnoreCase)
            && int.TryParse(hint.AsSpan(CompatibilityHintPrefix.Length), out var level)
            && level < InterleavedExecutionCompatibilityLevel);

    private void Block(TSqlFragment fragment) => fragment.Accept(new FromClauseCollector(blocked));

    private sealed class FromClauseCollector(HashSet<FromClause> blocked) : TSqlFragmentVisitor
    {
        public override void Visit(FromClause node) => blocked.Add(node);
    }
}
