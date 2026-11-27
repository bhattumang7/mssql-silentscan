using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SilentScan.Core.Common;

internal static class SetOptionByStatementResolver
{
    public static Dictionary<TSqlStatement, bool> Resolve(TSqlFragment fragment, SetOptions flag, bool setting)
    {
        var map = new Dictionary<TSqlStatement, bool>(ReferenceEqualityComparer.Instance);
        if (fragment is not TSqlScript script)
        {
            return map;
        }

        var policy = new Policy(map, flag, setting);
        var state = default(FlowState);
        foreach (var batch in script.Batches)
        {
            state = ProcedureBodyFlowWalker.Walk(batch.Statements, state with { Depth = 0 }, policy);
        }

        return map;
    }

    private readonly record struct FlowState(bool IsSet, bool RestoreIsSet, int Depth);

    private sealed class Policy(Dictionary<TSqlStatement, bool> map, SetOptions flag, bool setting) : IStatementFlowPolicy<FlowState>
    {
        private static StatementList? GetScopedStatementList(TSqlStatement statement) => statement switch
        {
            ProcedureStatementBodyBase p => p.StatementList,
            TriggerStatementBody t => t.StatementList,
            _ => null,
        };

        public bool IsDeclined(FlowState state) => false;

        public bool IsDone(FlowState state) => false;

        public FlowState PerStatement(TSqlStatement statement, FlowState state)
        {
            map[statement] = state.IsSet;

            if (GetScopedStatementList(statement) is { Statements: var nestedStatements })
            {
                ProcedureBodyFlowWalker.Walk(nestedStatements, default(FlowState), this);
            }

            if (statement is not PredicateSetStatement { Options: var options, IsOn: var isOn } || (options & flag) == 0)
            {
                return state;
            }

            return state with { IsSet = isOn == setting };
        }

        public FlowState OnReturn(FlowState state, TSqlStatement statement) => state;

        public FlowState OnThrow(FlowState state) => state;

        public FlowState OnGoTo(FlowState state) => state;

        public FlowState CloneForBranch(FlowState state) =>
            state with { RestoreIsSet = state.IsSet, Depth = state.Depth + 1 };

        public FlowState Merge(FlowState a, FlowState b)
        {
            var winner = a.Depth >= b.Depth ? a : b;
            return new FlowState(winner.RestoreIsSet, winner.RestoreIsSet, winner.Depth - 1);
        }
    }
}
