using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SilentScan.Core.Predicates;

internal sealed class VariableSourceCollector : TSqlFragmentVisitor
{
    private readonly Dictionary<string, int> _longestLiteral = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, List<ColumnReferenceExpression>> _columnSources = new(StringComparer.OrdinalIgnoreCase);

    private readonly HashSet<string> _otherwiseWritten = new(StringComparer.OrdinalIgnoreCase);

    public static VariableSourceCollector Collect(TSqlFragment root)
    {
        var collector = new VariableSourceCollector();
        root.Accept(collector);
        return collector;
    }

    public bool TryGetSources(string variableName, out int longestLiteral, out IReadOnlyList<ColumnReferenceExpression> columns)
    {
        _longestLiteral.TryGetValue(variableName, out longestLiteral);
        columns = _columnSources.TryGetValue(variableName, out var sources) ? sources : [];
        return !_otherwiseWritten.Contains(variableName);
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

            case ColumnReferenceExpression column:
                if (!_columnSources.TryGetValue(variableName, out var sources))
                {
                    _columnSources[variableName] = sources = [];
                }

                sources.Add(column);
                break;

            default:
                _otherwiseWritten.Add(variableName);
                break;
        }
    }
}
