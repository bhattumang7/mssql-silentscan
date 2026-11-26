using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Catalog;
using SilentScan.Core.Common;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;
using SilentScan.Core.TypeInference;

namespace SilentScan.Core.Lineage;

public static class ProcedureResultShapePass
{
    private static readonly HashSet<string> SystemProceduresWithoutResultSets = new(StringComparer.OrdinalIgnoreCase)
    {
        "sp_xml_preparedocument", "sp_xml_removedocument", "sp_getapplock", "sp_releaseapplock", "sp_rename",
    };

    public static void Apply(
        DatabaseCatalog catalog, LineageCatalog lineage, IEnumerable<SqlParseResult> parseResults,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? callerScopeByCalleeScope = null)
    {
        foreach (var result in parseResults)
        {
            var rule = new Rule(catalog, lineage.AllRelations, result.SourcePath);
            var walker = new ModuleWalker(
                result.SourcePath, catalog, lineage.AllRelations,
                rules: [rule], callerContext: new ModuleWalkerCallerContext(catalog.Skipped, null, callerScopeByCalleeScope));
            result.Fragment.Accept(walker);
        }
    }

    private sealed class Rule(DatabaseCatalog catalog, IReadOnlyDictionary<string, ResolvedRelation> resolvedViews, string sourcePath) : IModuleRule
    {
        private SelectStatement? _soleResultSelect;

        public void OnEnterProcedureOrFunctionBody(ProcedureStatementBodyBase node, ModuleWalker walker)
        {
            _soleResultSelect = null;
            if (node is not ProcedureStatementBody { StatementList: { } statements })
            {
                return;
            }

            var probe = new ResultProducerProbe();
            statements.Accept(probe);
            if (!probe.Disqualified && probe.ResultSelects.Count == 1)
            {
                _soleResultSelect = probe.ResultSelects[0];
            }
        }

        public void OnEnterSelectStatementScope(SelectStatement node, ModuleWalker walker)
        {
            if (!ReferenceEquals(node, _soleResultSelect) || walker.CurrentProcScope is not { } procedureName)
            {
                return;
            }

            var columns = QueryExpressionResolver.Resolve(
                node.QueryExpression, catalog, resolvedViews, sourcePath, catalog.Skipped, walker.CurrentCteRelations(), procedureName);
            catalog.SetProcedureResultColumns(
                procedureName, [.. columns.Select(c => ColumnProvenanceAnalysis.TryGetScalarType(c.Provenance))]);
        }
    }

    private sealed class ResultProducerProbe : TSqlFragmentVisitor
    {
        public List<SelectStatement> ResultSelects { get; } = [];

        public bool Disqualified { get; private set; }

        public override void ExplicitVisit(DeclareCursorStatement node)
        {
            _ = node;
        }

        public override void ExplicitVisit(SelectStatement node)
        {
            var isAssignmentOnly = node.QueryExpression is QuerySpecification { SelectElements: { Count: > 0 } elements }
                                   && elements.All(e => e is SelectSetVariable);
            if (node.Into is null && !isAssignmentOnly)
            {
                ResultSelects.Add(node);
            }

            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(ExecuteStatement node)
        {
            var isSilent = node.ExecuteSpecification.ExecutableEntity is ExecutableProcedureReference
                {
                    ProcedureReference.ProcedureReference.Name: { } name,
                }
                && SystemProceduresWithoutResultSets.Contains(name.BaseIdentifier.Value);
            if (!isSilent)
            {
                Disqualified = true;
            }
        }

        public override void ExplicitVisit(OutputClause node) => Disqualified = true;
    }
}
