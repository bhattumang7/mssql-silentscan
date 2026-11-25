using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Catalog;
using SilentScan.Core.Common;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;

namespace SilentScan.Core.Predicates;

public static class ForcedSerialScanner
{
    private static readonly HashSet<string> NonParallelizableIntrinsicFunctionNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "OBJECT_ID", "IDENT_CURRENT", "OBJECT_NAME", "SCHEMA_NAME", "OBJECTPROPERTY", "COLUMNPROPERTY",
        "XACT_STATE", "SCHEMA_ID", "COL_LENGTH", "USER_ID", "IS_MEMBER", "OBJECTPROPERTYEX", "CURRENT_TRANSACTION_ID",
        "ORIGINAL_LOGIN", "TYPE_ID", "CURSOR_STATUS", "OBJECT_DEFINITION", "INDEXPROPERTY", "IDENT_SEED",
        "ERROR_NUMBER", "ERROR_MESSAGE", "ERROR_LINE", "ERROR_SEVERITY", "ERROR_STATE", "ERROR_PROCEDURE",
        "COL_NAME", "OBJECT_SCHEMA_NAME", "IDENT_INCR", "IDENTITYPROPERTY", "HAS_PERMS_BY_NAME", "IS_ROLEMEMBER", "IS_SRVROLEMEMBER",
        "PERMISSIONS", "TYPEPROPERTY", "DATABASE_PRINCIPAL_ID", "FILE_ID", "INDEX_COL",
        "APPLOCK_MODE", "APPLOCK_TEST", "LOGINPROPERTY", "SESSIONPROPERTY", "CERT_ID", "ASYMKEY_ID", "KEY_ID", "KEY_GUID",
        "CHANGE_TRACKING_CURRENT_VERSION", "HAS_DBACCESS", "CERTPROPERTY", "ASYMKEYPROPERTY", "USER_SID", "INDEXKEY_PROPERTY", "CERTENCODED",
        "COLUMNPROPERTYEX", "CERTPRIVATEKEY", "DEFAULT_DOMAIN", "ENCRYPTBYASYMKEY", "ENCRYPTBYCERT", "EVENTDATA",
        "GET_FILESTREAM_TRANSACTION_CONTEXT", "GET_TRANSMISSION_STATUS", "IS_OBJECTSIGNED", "TEXTVALID", "USER_OID", "COLUMNS_UPDATED",
    };

    public static IReadOnlyList<ForcedSerialFinding> Scan(SqlParseResult parseResult)
    {
        var rule = CreateRule(parseResult.SourcePath);
        var walker = new ModuleWalker(parseResult.SourcePath, new DatabaseCatalog(), EmptyResolvedViews, rules: [rule]);
        parseResult.Fragment.Accept(walker);
    return Harvest(rule);
    }
    internal static Rule CreateRule(string sourcePath) => new(sourcePath);

    internal static IReadOnlyList<ForcedSerialFinding> Harvest(Rule rule) =>
            [
            .. rule.Findings
                .OrderBy(f => f.Kind)
                .ThenBy(f => f.SourcePath, StringComparer.Ordinal)
                .ThenBy(f => f.Line)
                .ThenBy(f => f.Column),
        ];


    private static readonly IReadOnlyDictionary<string, ResolvedRelation> EmptyResolvedViews = new Dictionary<string, ResolvedRelation>();

    internal sealed class Rule(string sourcePath) : IModuleRule
    {
        public List<ForcedSerialFinding> Findings { get; } = [];

        private readonly HashSet<string> _tableVariableNames = new(StringComparer.OrdinalIgnoreCase);

        private int _queryWithFromDepth;

        private bool _outermostQueryReadsSystemCatalogView;

        private static bool ReadsSystemCatalogView(TSqlFragment query)
        {
            var references = new NamedTableReferenceCollector();
            query.Accept(references);
            return references.References.Any(IsSystemCatalogView);
        }

        private static bool IsSystemCatalogView(NamedTableReference reference)
        {
            var (schema, name) = SchemaObjectNameHelper.Resolve(reference.SchemaObject);
            return schema is not null
                && (string.Equals(schema, "sys", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(schema, "INFORMATION_SCHEMA", StringComparison.OrdinalIgnoreCase)
                    || SystemCatalogViewRegistry.TryResolve($"{schema}.{name}") is not null);
        }

        private sealed class NamedTableReferenceCollector : TSqlFragmentVisitor
        {
            public List<NamedTableReference> References { get; } = [];

            public override void ExplicitVisit(NamedTableReference node)
            {
                References.Add(node);
                base.ExplicitVisit(node);
            }
        }

        public void OnEnterTSqlBatch(TSqlBatch node, ModuleWalker walker) => _tableVariableNames.Clear();

        public void OnEnterDeclareTableVariableStatement(DeclareTableVariableStatement node, ModuleWalker walker) =>
            _tableVariableNames.Add(node.Body.VariableName.Value);

        public void OnEnterInsertStatementScope(InsertStatement node, ModuleWalker walker) =>
            InspectDataModification(node.InsertSpecification);

        public void OnEnterUpdateStatementScope(UpdateStatement node, ScopeChain scopeChain, ModuleWalker walker) =>
            InspectDataModification(node.UpdateSpecification);

        public void OnEnterDeleteStatementScope(DeleteStatement node, ScopeChain scopeChain, ModuleWalker walker) =>
            InspectDataModification(node.DeleteSpecification);

        public void OnEnterMergeStatementScope(MergeStatement node, ScopeChain scopeChain, ModuleWalker walker) =>
            InspectDataModification(node.MergeSpecification);

        public void OnEnterDeclareCursorStatement(DeclareCursorStatement node, ModuleWalker walker) =>
            InspectCursorDefinition(node.CursorDefinition, node.Name.Value);

        public void OnEnterSetVariableStatement(SetVariableStatement node, ModuleWalker walker)
        {
            if (node.CursorDefinition is not null)
            {
                InspectCursorDefinition(node.CursorDefinition, node.Variable.Name);
            }
        }

        public void OnEnterQuerySpecificationScope(QuerySpecification node, ScopeChain scopeChain, ModuleWalker walker)
        {
            if (node.FromClause is not null)
            {
                if (_queryWithFromDepth == 0)
                {
                    _outermostQueryReadsSystemCatalogView = ReadsSystemCatalogView(node);
                }

                _queryWithFromDepth++;
            }
        }

        public void OnLeaveQuerySpecificationScope(QuerySpecification node, ScopeChain scopeChain, ModuleWalker walker)
        {
            if (node.FromClause is not null)
            {
                _queryWithFromDepth--;
            }
        }

        public void OnEnterFunctionCall(FunctionCall node, ModuleWalker walker)
        {
            if (_queryWithFromDepth > 0 && !_outermostQueryReadsSystemCatalogView && NonParallelizableIntrinsicFunctionNames.Contains(node.FunctionName.Value))
            {
                Findings.Add(new ForcedSerialFinding(
                    ForcedSerialFindingKind.NonParallelizableIntrinsic, sourcePath, sourcePath,
                    node.StartLine, node.StartColumn, DetailText: node.FunctionName.Value));
            }
        }

        public void OnEnterGlobalVariableExpression(GlobalVariableExpression node, ModuleWalker walker)
        {
            if (_queryWithFromDepth > 0 && !_outermostQueryReadsSystemCatalogView && string.Equals(node.Name, "@@TRANCOUNT", StringComparison.OrdinalIgnoreCase))
            {
                Findings.Add(new ForcedSerialFinding(
                    ForcedSerialFindingKind.NonParallelizableIntrinsic, sourcePath, sourcePath,
                    node.StartLine, node.StartColumn, DetailText: "@@TRANCOUNT"));
            }
        }

        private void InspectDataModification(DataModificationSpecification spec)
        {
            if (spec.OutputClause is { } output)
            {
                Findings.Add(new ForcedSerialFinding(
                    ForcedSerialFindingKind.OutputReturnedToClient, sourcePath, sourcePath,
                    output.StartLine, output.StartColumn, DetailText: SourceText(output)));
            }

            var targetVariable = TableVariableName(spec.Target);
            var outputVariable = TableVariableName(spec.OutputIntoClause?.IntoTable);
            var variableName = targetVariable ?? outputVariable;
            if (variableName is null || (targetVariable is not null && InsertHasNoRowSource(spec)))
            {
                return;
            }

            Findings.Add(new ForcedSerialFinding(
                ForcedSerialFindingKind.TableVariableModification, sourcePath, sourcePath,
                spec.StartLine, spec.StartColumn, DetailText: variableName));
        }

        private static string SourceText(TSqlFragment fragment) =>
            string.Concat(Enumerable.Range(fragment.FirstTokenIndex, fragment.LastTokenIndex - fragment.FirstTokenIndex + 1).Select(i => fragment.ScriptTokenStream[i].Text));

        private static bool InsertHasNoRowSource(DataModificationSpecification spec)
        {
            if (spec is not InsertSpecification insert)
            {
                return false;
            }

            if (insert.InsertSource is SelectInsertSource select)
            {
                var references = new TableReferenceCounter();
                select.Select.Accept(references);
                return references.Count == 0;
            }

            return true;
        }

        private sealed class TableReferenceCounter : TSqlFragmentVisitor
        {
            public int Count { get; private set; }

            public override void Visit(TSqlFragment fragment)
            {
                if (fragment is TableReference)
                {
                    Count++;
                }
            }
        }

        private string? TableVariableName(TableReference? tableReference) =>
            tableReference is VariableTableReference variableRef && _tableVariableNames.Contains(variableRef.Variable.Name)
                ? variableRef.Variable.Name
                : null;

        private static bool IsBareTableVariableScan(QueryExpression query) =>
            query is QuerySpecification
            {
                FromClause.TableReferences: [VariableTableReference],
                WhereClause: null,
                GroupByClause: null,
                HavingClause: null,
                TopRowFilter: null,
                OrderByClause: null,
                WindowClause: null,
                UniqueRowFilter: UniqueRowFilter.NotSpecified,
            } spec
            && spec.SelectElements.All(e => e is SelectStarExpression || e is SelectScalarExpression { Expression: ColumnReferenceExpression });

        private void InspectCursorDefinition(CursorDefinition definition, string cursorName)
        {
            if (ReadsSystemCatalogView(definition.Select) || IsBareTableVariableScan(definition.Select.QueryExpression))
            {
                return;
            }

            var kinds = definition.Options.Select(o => o.OptionKind).ToHashSet();

            var hasExplicitType = kinds.Contains(CursorOptionKind.Static) || kinds.Contains(CursorOptionKind.Keyset) || kinds.Contains(CursorOptionKind.Dynamic);
            var fires = kinds.Contains(CursorOptionKind.FastForward)
                || (kinds.Contains(CursorOptionKind.ForwardOnly) && kinds.Contains(CursorOptionKind.ReadOnly) && !hasExplicitType);

            if (!fires)
            {
                return;
            }

            Findings.Add(new ForcedSerialFinding(
                ForcedSerialFindingKind.FastForwardCursor, sourcePath, sourcePath,
                definition.StartLine, definition.StartColumn, DetailText: cursorName));
        }
    }
}
