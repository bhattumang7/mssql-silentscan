using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Catalog;
using SilentScan.Core.Common;
using SilentScan.Core.Lineage;

namespace SilentScan.Core.Predicates;

internal sealed class ExtremumCopyTracker(string sourcePath, DatabaseCatalog catalog)
{
    private readonly TableColumnKeyComparer _comparer = TableColumnKeyComparer.For(catalog);
    private readonly Dictionary<(string Table, string Column), List<ColumnProvenance.BaseColumn>> _sources = new(TableColumnKeyComparer.For(catalog));
    private readonly HashSet<(string Table, string Column)> _poisonedColumns = new(TableColumnKeyComparer.For(catalog));
    private readonly HashSet<string> _poisonedTables = new(catalog.IdentifierComparer);
    private readonly HashSet<string> _assignedColumnNames = new(catalog.IdentifierComparer);
    private PendingInsert? _pending;

    private readonly record struct PendingInsert(string Table, IReadOnlyList<string> Columns);

    public void OnInsert(InsertStatement node, ModuleWalker walker)
    {
        _pending = null;
        var spec = node.InsertSpecification;
        var table = TargetTableName(spec.Target);
        if (table is null)
        {
            return;
        }

        if (node.WithCtesAndXmlNamespaces is not null || spec.InsertSource is not SelectInsertSource { Select: QuerySpecification })
        {
            _poisonedTables.Add(table);
            return;
        }

        var columns = TargetColumnNames(spec, table, walker);
        if (columns is null)
        {
            _poisonedTables.Add(table);
            return;
        }

        _pending = new PendingInsert(table, columns);
    }

    public void OnQuerySpecification(QuerySpecification node, ScopeChain scopeChain)
    {
        if (_pending is not { } pending)
        {
            return;
        }

        _pending = null;
        if (node.SelectElements.Count != pending.Columns.Count || node.SelectElements.Any(e => e is not SelectScalarExpression))
        {
            _poisonedTables.Add(pending.Table);
            return;
        }

        for (var i = 0; i < pending.Columns.Count; i++)
        {
            var key = (pending.Table, pending.Columns[i]);
            var source = ExtremumSource(((SelectScalarExpression)node.SelectElements[i]).Expression, scopeChain);
            if (source is null)
            {
                _poisonedColumns.Add(key);
                continue;
            }

            if (!_sources.TryGetValue(key, out var sources))
            {
                sources = [];
                _sources[key] = sources;
            }

            sources.Add(source);
        }
    }

    public void OnAssignment(AssignmentSetClause node)
    {
        if (node.Column?.MultiPartIdentifier?.Identifiers is { Count: > 0 } identifiers)
        {
            _assignedColumnNames.Add(identifiers[^1].Value);
        }
    }

    public void OnMerge(MergeStatement node)
    {
        if (TargetTableName(node.MergeSpecification.Target) is { } table)
        {
            _poisonedTables.Add(table);
        }
    }

    public bool AreExactCopies(ColumnProvenance.BaseColumn first, ColumnProvenance.BaseColumn? second) =>
        second is not null && (IsCopyOf(first, second) || IsCopyOf(second, first));

    private bool IsCopyOf(ColumnProvenance.BaseColumn target, ColumnProvenance.BaseColumn source)
    {
        var key = (target.TableQualifiedName, target.ColumnName);
        return !_poisonedTables.Contains(target.TableQualifiedName)
            && !_poisonedColumns.Contains(key)
            && !_assignedColumnNames.Contains(target.ColumnName)
            && _sources.TryGetValue(key, out var sources)
            && sources.All(s => _comparer.Equals(s, source));
    }

    private ColumnProvenance.BaseColumn? ExtremumSource(ScalarExpression expression, ScopeChain scopeChain) =>
        expression is FunctionCall { Parameters.Count: 1 } call
            && call.Parameters[0] is ColumnReferenceExpression column
            && (call.FunctionName.Value.Equals("MIN", StringComparison.OrdinalIgnoreCase)
                || call.FunctionName.Value.Equals("MAX", StringComparison.OrdinalIgnoreCase))
                ? BaseColumnResolver.ResolveBaseColumn(column, sourcePath, scopeChain, catalog)
                : null;

    private string? TargetTableName(TableReference target) => target switch
    {
        VariableTableReference variable => variable.Variable.Name,
        NamedTableReference named => catalog.ResolveSynonymName(SchemaObjectNameHelper.Qualify(named.SchemaObject)),
        _ => null,
    };

    private List<string>? TargetColumnNames(InsertSpecification spec, string table, ModuleWalker walker)
    {
        if (spec.Columns.Count > 0)
        {
            return [.. spec.Columns.Select(c => c.MultiPartIdentifier.Identifiers[^1].Value)];
        }

        return catalog.Find(table, walker.CurrentProcScope) is { } known
            ? [.. known.Columns.Where(c => c is { IsIdentity: false, IsComputed: false }).Select(c => c.Name)]
            : null;
    }
}
