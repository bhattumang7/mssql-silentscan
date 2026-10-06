using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SilentScan.Core.Common;

internal interface IStatementFlowPolicy<TState>
{
    bool IsDeclined(TState state);

    bool IsDone(TState state);

    TState PerStatement(TSqlStatement statement, TState state);

    TState OnReturn(TState state, TSqlStatement statement);

    TState OnThrow(TState state);

    TState OnGoTo(TState state);

    TState CloneForBranch(TState state);

    TState Merge(TState a, TState b);

    int WhileFixpointCap => 1;

    bool StatesEqual(TState a, TState b) => true;

    TState MarkApproximateOnCapExceeded(TState state) => state;

    bool CatchEntersOnlyThroughExplicitRaise => false;
}

internal static class ProcedureBodyFlowWalker
{
    public static TState Walk<TState>(
        IList<TSqlStatement> statements, TState state, IStatementFlowPolicy<TState> policy, List<TState>? catchEntries = null)
    {
        foreach (var statement in statements)
        {
            if (policy.IsDeclined(state))
            {
                return state;
            }

            if (policy.IsDone(state))
            {
                continue;
            }

            state = policy.PerStatement(statement, state);

            if (catchEntries is not null && RaisesIntoCatch(statement))
            {
                catchEntries.Add(policy.CloneForBranch(state));
            }

            switch (statement)
            {
                case ReturnStatement:
                    return policy.OnReturn(state, statement);

                case ThrowStatement:
                    return policy.OnThrow(state);

                case GoToStatement:
                    return policy.OnGoTo(state);

                case BeginEndBlockStatement block:
                    state = Walk(block.StatementList.Statements, state, policy, catchEntries);
                    break;

                case IfStatement ifStatement:
                    state = WalkIf(ifStatement, state, policy, catchEntries);
                    break;

                case WhileStatement whileStatement:
                    state = WalkWhile(whileStatement, state, policy, catchEntries);
                    break;

                case TryCatchStatement tryCatch:
                    state = WalkTryCatch(tryCatch, state, policy, catchEntries);
                    break;

                default:
                    break;
            }
        }

        return state;
    }

    private static bool RaisesIntoCatch(TSqlStatement statement) =>
        statement is ThrowStatement
        || (statement is RaiseErrorStatement { SecondParameter: IntegerLiteral severity }
            && int.TryParse(severity.Value, out var level) && level >= 11);

    private static TState WalkIf<TState>(
        IfStatement ifStatement, TState enteringState, IStatementFlowPolicy<TState> policy, List<TState>? catchEntries)
    {
        if (policy.IsDeclined(enteringState))
        {
            return enteringState;
        }

        var thenResult = Walk(ToStatementList(ifStatement.ThenStatement), policy.CloneForBranch(enteringState), policy, catchEntries);
        var elseResult = ifStatement.ElseStatement is not null
            ? Walk(ToStatementList(ifStatement.ElseStatement), policy.CloneForBranch(enteringState), policy, catchEntries)
            : enteringState;

        return policy.Merge(thenResult, elseResult);
    }

    private static TState WalkWhile<TState>(
        WhileStatement whileStatement, TState enteringState, IStatementFlowPolicy<TState> policy, List<TState>? catchEntries)
    {
        if (policy.IsDeclined(enteringState))
        {
            return enteringState;
        }

        var current = enteringState;
        for (var iteration = 0; iteration < policy.WhileFixpointCap; iteration++)
        {
            var bodyResult = Walk(ToStatementList(whileStatement.Statement), policy.CloneForBranch(current), policy, catchEntries);
            var next = policy.Merge(enteringState, bodyResult);
            if (policy.StatesEqual(next, current))
            {
                return next;
            }

            current = next;
        }

        return policy.MarkApproximateOnCapExceeded(current);
    }

    private static TState WalkTryCatch<TState>(
        TryCatchStatement tryCatch, TState enteringState, IStatementFlowPolicy<TState> policy, List<TState>? outerCatchEntries)
    {
        if (policy.IsDeclined(enteringState))
        {
            return enteringState;
        }

        if (!policy.CatchEntersOnlyThroughExplicitRaise)
        {
            var tryOnly = Walk(tryCatch.TryStatements.Statements, policy.CloneForBranch(enteringState), policy, outerCatchEntries);
            var catchOnly = Walk(tryCatch.CatchStatements.Statements, policy.CloneForBranch(enteringState), policy, outerCatchEntries);
            return policy.Merge(tryOnly, catchOnly);
        }

        var raisedStates = new List<TState>();
        var tryResult = Walk(tryCatch.TryStatements.Statements, policy.CloneForBranch(enteringState), policy, raisedStates);
        if (raisedStates.Count == 0)
        {
            return tryResult;
        }

        var catchEntry = raisedStates.Aggregate(policy.Merge);
        var catchResult = Walk(tryCatch.CatchStatements.Statements, catchEntry, policy, outerCatchEntries);
        return policy.Merge(tryResult, catchResult);
    }

    public static IList<TSqlStatement> ToStatementList(TSqlStatement statement) =>
        statement is BeginEndBlockStatement block ? block.StatementList.Statements : [statement];
}
