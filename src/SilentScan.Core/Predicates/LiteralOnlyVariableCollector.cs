using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SilentScan.Core.Predicates;

internal sealed class LiteralOnlyVariableCollector : TSqlFragmentVisitor
{
    private readonly Dictionary<string, int> _longestLiteral = new(StringComparer.OrdinalIgnoreCase);

    private readonly HashSet<string> _otherwiseWritten = new(StringComparer.OrdinalIgnoreCase);

    public static LiteralOnlyVariableCollector Collect(TSqlFragment root)
    {
        var collector = new LiteralOnlyVariableCollector();
        root.Accept(collector);
        return collector;
    }

    public bool TryGetLongestLiteral(string variableName, out int length)
    {
        length = 0;
        if (_otherwiseWritten.Contains(variableName))
        {
            return false;
        }

        _longestLiteral.TryGetValue(variableName, out length);
        return true;
    }

    public override void ExplicitVisit(DeclareVariableElement node)
    {
        if (node.Value is not null)
        {
            RecordWrite(node.VariableName.Value, node.Value);
        }

        base.ExplicitVisit(node);
    }

    public override void ExplicitVisit(SetVariableStatement node)
    {
        if (node.Variable is not null)
        {
            RecordWrite(node.Variable.Name, node.AssignmentKind == AssignmentKind.Equals ? node.Expression : null);
        }

        base.ExplicitVisit(node);
    }

    public override void ExplicitVisit(SelectSetVariable node)
    {
        RecordWrite(node.Variable.Name, node.AssignmentKind == AssignmentKind.Equals ? node.Expression : null);
        base.ExplicitVisit(node);
    }

    public override void ExplicitVisit(AssignmentSetClause node)
    {
        if (node.Variable is not null)
        {
            RecordWrite(node.Variable.Name, null);
        }

        base.ExplicitVisit(node);
    }

    public override void ExplicitVisit(FetchCursorStatement node)
    {
        if (node.IntoVariables is not null)
        {
            foreach (var variable in node.IntoVariables)
            {
                RecordWrite(variable.Name, null);
            }
        }

        base.ExplicitVisit(node);
    }

    public override void ExplicitVisit(ExecuteParameter node)
    {
        if (node.IsOutput && node.ParameterValue is VariableReference variable)
        {
            RecordWrite(variable.Name, null);
        }

        base.ExplicitVisit(node);
    }

    public override void ExplicitVisit(ExecuteSpecification node)
    {
        if (node.Variable is not null)
        {
            RecordWrite(node.Variable.Name, null);
        }

        base.ExplicitVisit(node);
    }

    private void RecordWrite(string variableName, ScalarExpression? source)
    {
        switch (source)
        {
            case StringLiteral literal:
                _longestLiteral[variableName] = Math.Max(_longestLiteral.GetValueOrDefault(variableName), literal.Value.Length);
                break;

            case NullLiteral:
                break;

            default:
                _otherwiseWritten.Add(variableName);
                break;
        }
    }
}
