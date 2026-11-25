using System.Runtime.CompilerServices;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Common;
using SilentScan.Core.Parsing;

namespace SilentScan.Core.Catalog;

public static class ModuleSchemaNameBinding
{
    private static readonly ConditionalWeakTable<SchemaObjectName, string> BoundSchemas = new();

    public static bool TryGetBoundSchema(SchemaObjectName name, out string schema)
    {
        if (BoundSchemas.TryGetValue(name, out var bound))
        {
            schema = bound;
            return true;
        }

        schema = SchemaObjectNameHelper.DefaultSchema;
        return false;
    }

    public static void Bind(IReadOnlyList<SqlParseResult> results, DatabaseCatalog catalog)
    {
        var moduleNames = ModuleNames(results, catalog.IdentifierComparer);
        var procedureNames = ProcedureNames(results, catalog.IdentifierComparer);
        foreach (var result in results)
        {
            Bind(result, name => ObjectExists(catalog, moduleNames, name), catalog.IdentifierComparer);
            BindProcedureCalls(result, procedureNames, catalog.IdentifierComparer);
        }
    }

    public const string CallerDependentSchema = "?caller-dependent";

    public static void BindDynamicSqlTables(SqlParseResult result, DatabaseCatalog catalog, IReadOnlyCollection<string> defaultSchemas) =>
        result.Fragment.Accept(new DynamicTableBinder(catalog, defaultSchemas));

    public static void BindProcedureCalls(
        SqlParseResult result, IReadOnlySet<string> procedureNames, StringComparer comparer, IReadOnlyCollection<string>? defaultSchemas = null) =>
        result.Fragment.Accept(new ProcedureCallBinder(procedureNames, comparer, defaultSchemas));

    public static IReadOnlySet<string> ProcedureNames(IEnumerable<SqlParseResult> results, StringComparer comparer)
    {
        var names = new HashSet<string>(comparer);
        foreach (var result in results)
        {
            var collector = new ProcedureNameCollector();
            result.Fragment.Accept(collector);
            names.UnionWith(collector.Names);
        }

        return names;
    }

    public static void Bind(SqlParseResult result, Func<string, bool> objectExists, StringComparer comparer) =>
        result.Fragment.Accept(new ModuleVisitor(objectExists, comparer));

    public static bool ObjectExists(DatabaseCatalog catalog, IReadOnlySet<string> moduleNames, string qualifiedName) =>
        moduleNames.Contains(qualifiedName)
        || catalog.Find(qualifiedName) is not null
        || !catalog.IdentifierComparer.Equals(catalog.ResolveSynonymName(qualifiedName), qualifiedName)
        || catalog.TryGetTableValuedFunctionKind(qualifiedName, out _);

    public static IReadOnlySet<string> ModuleNames(IEnumerable<SqlParseResult> results, StringComparer comparer)
    {
        var names = new HashSet<string>(comparer);
        foreach (var result in results)
        {
            var collector = new ModuleNameCollector();
            result.Fragment.Accept(collector);
            names.UnionWith(collector.Names);
        }

        return names;
    }

    private static SchemaObjectName? ModuleName(TSqlStatement statement) => statement switch
    {
        ProcedureStatementBody procedure => procedure.ProcedureReference?.Name,
        ViewStatementBody view => view.SchemaObjectName,
        FunctionStatementBody function => function.Name,
        TriggerStatementBody trigger => trigger.Name,
        _ => null,
    };

    private sealed class ModuleNameCollector : TSqlFragmentVisitor
    {
        public List<string> Names { get; } = [];

        public override void Visit(TSqlStatement node)
        {
            if (node is ProcedureStatementBody or ViewStatementBody or FunctionStatementBody && ModuleName(node) is { } name)
            {
                Names.Add(SchemaObjectNameHelper.Qualify(name));
            }
        }
    }

    private sealed class DynamicTableBinder(DatabaseCatalog catalog, IReadOnlyCollection<string> defaultSchemas) : TSqlFragmentVisitor
    {
        public override void Visit(NamedTableReference node)
        {
            if (node.SchemaObject is not { SchemaIdentifier: null, DatabaseIdentifier: null, ServerIdentifier: null, BaseIdentifier.Value: { Length: > 0 } baseName } name
                || baseName.StartsWith('#')
                || baseName.StartsWith('@'))
            {
                return;
            }

            var comparer = catalog.IdentifierComparer;
            var candidates = catalog.Tables
                .Where(t => t.Kind == CatalogTableKind.Table && t.SchemaName is not null && comparer.Equals(t.Name, baseName))
                .Select(t => t.SchemaName!)
                .Distinct(comparer)
                .ToList();
            if (candidates.Count == 0)
            {
                return;
            }

            var outcomes = new HashSet<string>(comparer);
            foreach (var defaultSchema in defaultSchemas.Count == 0 ? [SchemaObjectNameHelper.DefaultSchema] : defaultSchemas)
            {
                var reached = candidates.FirstOrDefault(c => comparer.Equals(c, defaultSchema))
                    ?? candidates.FirstOrDefault(c => comparer.Equals(c, SchemaObjectNameHelper.DefaultSchema));
                if (reached is not null)
                {
                    outcomes.Add(reached);
                }
            }

            if (outcomes.Count > 0)
            {
                BoundSchemas.AddOrUpdate(name, outcomes.Count == 1 ? outcomes.First() : CallerDependentSchema);
            }
        }
    }

    private sealed class ProcedureNameCollector : TSqlFragmentVisitor
    {
        public List<string> Names { get; } = [];

        public override void Visit(ProcedureStatementBody node)
        {
            if (node.ProcedureReference?.Name is { } name)
            {
                Names.Add(SchemaObjectNameHelper.Qualify(name));
            }
        }
    }

    private sealed class ProcedureCallBinder(IReadOnlySet<string> procedureNames, StringComparer comparer, IReadOnlyCollection<string>? defaultSchemas) : TSqlFragmentVisitor
    {
        public override void Visit(ExecutableProcedureReference node)
        {
            if (node.ProcedureReference?.ProcedureReference?.Name is not { SchemaIdentifier: null, DatabaseIdentifier: null, ServerIdentifier: null, BaseIdentifier.Value: { Length: > 0 } baseName } name
                || baseName.StartsWith('#'))
            {
                return;
            }

            var candidates = procedureNames
                .Where(n => n.IndexOf('.', StringComparison.Ordinal) is var dot and > 0 && comparer.Equals(n[(dot + 1)..], baseName) && n.IndexOf('.', dot + 1) < 0)
                .Select(n => n[..n.IndexOf('.', StringComparison.Ordinal)])
                .ToList();
            if (candidates.Count == 0)
            {
                return;
            }

            if (defaultSchemas is null)
            {
                if (candidates.Count == 1)
                {
                    BoundSchemas.AddOrUpdate(name, candidates[0]);
                }

                return;
            }

            var outcomes = new HashSet<string>(comparer);
            foreach (var defaultSchema in defaultSchemas.Count == 0 ? [SchemaObjectNameHelper.DefaultSchema] : defaultSchemas)
            {
                var reached = candidates.FirstOrDefault(c => comparer.Equals(c, defaultSchema))
                    ?? candidates.FirstOrDefault(c => comparer.Equals(c, SchemaObjectNameHelper.DefaultSchema));
                if (reached is not null)
                {
                    outcomes.Add(reached);
                }
            }

            BoundSchemas.AddOrUpdate(name, outcomes.Count == 1 ? outcomes.First() : CallerDependentSchema);
        }
    }

    private sealed class ModuleVisitor(Func<string, bool> objectExists, StringComparer comparer) : TSqlFragmentVisitor
    {
        public override void Visit(TSqlStatement node)
        {
            if (ModuleName(node) is not { SchemaIdentifier.Value: { } schema }
                || comparer.Equals(schema, SchemaObjectNameHelper.DefaultSchema))
            {
                return;
            }

            var cteNames = new CteNameCollector(comparer);
            node.Accept(cteNames);
            node.Accept(new ReferenceBinder(schema, cteNames.Names, objectExists));
        }
    }

    private sealed class CteNameCollector(StringComparer comparer) : TSqlFragmentVisitor
    {
        public HashSet<string> Names { get; } = new(comparer);

        public override void Visit(CommonTableExpression node) => Names.Add(node.ExpressionName.Value);
    }

    private sealed class ReferenceBinder(string schema, IReadOnlySet<string> cteNames, Func<string, bool> objectExists) : TSqlFragmentVisitor
    {
        public override void Visit(NamedTableReference node) => TryBind(node.SchemaObject);

        public override void Visit(SchemaObjectFunctionTableReference node) => TryBind(node.SchemaObject);

        private void TryBind(SchemaObjectName? name)
        {
            if (name is not { SchemaIdentifier: null, DatabaseIdentifier: null, ServerIdentifier: null, BaseIdentifier.Value: { Length: > 0 } baseName }
                || baseName.StartsWith('#')
                || baseName.StartsWith('@')
                || cteNames.Contains(baseName)
                || !objectExists($"{schema}.{baseName}"))
            {
                return;
            }

            BoundSchemas.AddOrUpdate(name, schema);
        }
    }
}
