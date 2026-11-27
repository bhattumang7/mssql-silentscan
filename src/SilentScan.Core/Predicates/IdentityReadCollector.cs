using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Catalog;
using SilentScan.Core.Common;

namespace SilentScan.Core.Predicates;

internal sealed class IdentityReadCollector(DatabaseCatalog catalog, HashSet<GlobalVariableExpression> safeReads)
    : TSqlFragmentVisitor
{
    public override void Visit(TSqlBatch node) => Walk(node.Statements);

    public override void Visit(StatementList node) => Walk(node.Statements);

    private void Walk(IList<TSqlStatement> statements)
    {
        var slotsAgree = false;
        foreach (var statement in statements)
        {
            if (slotsAgree && statement is not InsertStatement)
            {
                statement.Accept(new IdentityReads(safeReads, statement));
            }

            slotsAgree = IsPlainIdentityInsert(statement) || slotsAgree && LeavesTheSlotsAlone(statement);
        }
    }

    private static bool LeavesTheSlotsAlone(TSqlStatement statement)
    {
        if (statement is not (DeclareVariableStatement or SetVariableStatement))
        {
            return false;
        }

        var finder = new FunctionCallFinder();
        statement.Accept(finder);
        return !finder.Found;
    }

    private bool IsPlainIdentityInsert(TSqlStatement statement)
    {
        if (statement is not InsertStatement { InsertSpecification: { InsertSource: ValuesInsertSource, Target: NamedTableReference target } })
        {
            return false;
        }

        var name = SchemaObjectNameHelper.Qualify(target.SchemaObject);
        return catalog.Find(name) is { } table
            && table.Columns.Any(column => column.IsIdentity)
            && !table.Indexes.Any(index => index.IgnoreDupKey)
            && !catalog.TriggerEvents.Any(trigger =>
                !trigger.IsDisabled
                && catalog.IdentifierComparer.Equals(trigger.TableQualifiedName, name)
                && trigger.EventTypeDescription.Contains("INSERT", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class IdentityReads(HashSet<GlobalVariableExpression> safeReads, TSqlStatement statement) : TSqlFragmentVisitor
    {
        public override void Visit(GlobalVariableExpression node)
        {
            if (statement is not (BeginEndBlockStatement or IfStatement or WhileStatement or TryCatchStatement))
            {
                safeReads.Add(node);
            }
        }
    }

    private sealed class FunctionCallFinder : TSqlFragmentVisitor
    {
        public bool Found { get; private set; }

        public override void Visit(FunctionCall node) => Found = true;
    }
}
