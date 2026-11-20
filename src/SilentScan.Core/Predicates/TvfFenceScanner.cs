using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Catalog;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;
using SilentScan.Core.Rules;
using SilentScan.Core.Common;

namespace SilentScan.Core.Predicates;

public static class TvfFenceScanner
{
    public static IReadOnlyList<TvfFenceFinding> Scan(
        SqlParseResult parseResult, DatabaseCatalog catalog, IReadOnlyDictionary<string, TvfFenceOrigin> fenceMap)
    {
        var rule = CreateRule(parseResult.SourcePath, catalog, fenceMap);
        var walker = new ModuleWalker(parseResult.SourcePath, catalog, EmptyResolvedViews, rules: [rule]);
        parseResult.Fragment.Accept(walker);
        return Harvest(rule);
    }

    internal static Rule CreateRule(string sourcePath, DatabaseCatalog catalog, IReadOnlyDictionary<string, TvfFenceOrigin> fenceMap) =>
        new(sourcePath, catalog, fenceMap);

    internal static IReadOnlyList<TvfFenceFinding> Harvest(Rule rule) => rule.Findings;

    private static readonly IReadOnlyDictionary<string, ResolvedRelation> EmptyResolvedViews = new Dictionary<string, ResolvedRelation>();

    internal sealed class Rule(string sourcePath, DatabaseCatalog catalog, IReadOnlyDictionary<string, TvfFenceOrigin> fenceMap) : IModuleRule
    {
        public List<TvfFenceFinding> Findings { get; } = [];

        private readonly HashSet<QuerySpecification> _unionOperands = [];

        private FromClause? _soleSourceFromClause;

        private int _queryDepth;

        private int _blockedDepth;

        public void OnEnterBinaryQueryExpression(BinaryQueryExpression node, ModuleWalker walker) => CollectUnionOperands(node);

        public void OnEnterInsertStatementScope(InsertStatement node, ModuleWalker walker) => _blockedDepth++;

        public void OnLeaveInsertStatementScope(InsertStatement node, ModuleWalker walker) => _blockedDepth--;

        public void OnEnterUpdateStatementScope(UpdateStatement node, ScopeChain scopeChain, ModuleWalker walker) => _blockedDepth++;

        public void OnLeaveUpdateStatementScope(UpdateStatement node, ScopeChain scopeChain, ModuleWalker walker) => _blockedDepth--;

        public void OnEnterDeleteStatementScope(DeleteStatement node, ScopeChain scopeChain, ModuleWalker walker) => _blockedDepth++;

        public void OnLeaveDeleteStatementScope(DeleteStatement node, ScopeChain scopeChain, ModuleWalker walker) => _blockedDepth--;

        public void OnEnterMergeStatementScope(MergeStatement node, ScopeChain scopeChain, ModuleWalker walker) => _blockedDepth++;

        public void OnLeaveMergeStatementScope(MergeStatement node, ScopeChain scopeChain, ModuleWalker walker) => _blockedDepth--;

        public void OnEnterSelectStatementScope(SelectStatement node, ModuleWalker walker)
        {
            if (node.WithCtesAndXmlNamespaces is not null || node.Into is not null)
            {
                _blockedDepth++;
            }
        }

        public void OnLeaveSelectStatementScope(SelectStatement node, ModuleWalker walker)
        {
            if (node.WithCtesAndXmlNamespaces is not null || node.Into is not null)
            {
                _blockedDepth--;
            }
        }

        public void OnEnterQuerySpecificationScope(QuerySpecification node, ScopeChain scopeChain, ModuleWalker walker)
        {
            if (_queryDepth == 0 && _blockedDepth == 0 && !_unionOperands.Contains(node) && SingleSourceQuery.IsPlain(node))
            {
                _soleSourceFromClause = node.FromClause;
            }

            _queryDepth++;
        }

        public void OnLeaveQuerySpecificationScope(QuerySpecification node, ScopeChain scopeChain, ModuleWalker walker) => _queryDepth--;

        private void CollectUnionOperands(QueryExpression expression)
        {
            switch (expression)
            {
                case BinaryQueryExpression binary:
                    CollectUnionOperands(binary.FirstQueryExpression);
                    CollectUnionOperands(binary.SecondQueryExpression);
                    break;
                case QueryParenthesisExpression parenthesis:
                    CollectUnionOperands(parenthesis.QueryExpression);
                    break;
                case QuerySpecification spec:
                    _unionOperands.Add(spec);
                    break;
            }
        }

        private bool IsSoleSource(TableReference tableReference) =>
            _soleSourceFromClause is { TableReferences: [var only] } && ReferenceEquals(only, tableReference);

        public void OnEnterFromClause(FromClause node, ModuleWalker walker)
        {
            foreach (var tableReference in node.TableReferences)
            {
                Flatten(tableReference, isApplySecondSide: false);
            }
        }

        private void Flatten(TableReference tableReference, bool isApplySecondSide)
        {
            switch (tableReference)
            {
                case JoinTableReference join:
                    var isApply = join is UnqualifiedJoin { UnqualifiedJoinType: UnqualifiedJoinType.CrossApply or UnqualifiedJoinType.OuterApply };
                    Flatten(join.FirstTableReference, isApplySecondSide: false);
                    Flatten(join.SecondTableReference, isApplySecondSide: isApply);
                    break;

                case JoinParenthesisTableReference parenthesis:
                    Flatten(parenthesis.Join, isApplySecondSide);
                    break;

                case SchemaObjectFunctionTableReference function:
                    VisitFunctionReference(function, isApplySecondSide);
                    break;

                case NamedTableReference named:
                    VisitNamedReference(named);
                    break;
            }
        }

        private void VisitFunctionReference(SchemaObjectFunctionTableReference function, bool isApplySecondSide)
        {
            var qualifiedName = catalog.ResolveSynonymName(SchemaObjectNameHelper.Qualify(function.SchemaObject));
            if (!catalog.TryGetTableValuedFunctionKind(qualifiedName, out var kind))
            {
                return;
            }

            if (kind is TableValuedFunctionKind.Inline)
            {
                TryEmitNestedFinding(qualifiedName, function, FragmentTextRenderer.Render(function));
                return;
            }

            if (!isApplySecondSide)
            {
                return;
            }

            var argumentColumns = function.Parameters.SelectMany(CollectColumnReferences)
                .Select(c => c.MultiPartIdentifier.Identifiers[^1].Value)
                .Distinct(catalog.IdentifierComparer)
                .ToList();

            if (argumentColumns.Count == 0)
            {
                return;
            }

            Findings.Add(new TvfFenceFinding(
                TvfFenceFindingKind.CorrelatedApply,
                FunctionQualifiedName: qualifiedName,
                ReferencedObjectQualifiedName: qualifiedName,
                FunctionKind: kind,
                SourcePath: sourcePath,
                Line: function.StartLine,
                Column: function.StartColumn,
                CorrelatedOuterColumns: argumentColumns,
                ReferenceFragmentText: FragmentTextRenderer.Render(function)));
        }

        private void VisitNamedReference(NamedTableReference named)
        {
            var qualifiedName = catalog.ResolveSynonymName(SchemaObjectNameHelper.Qualify(named.SchemaObject));
            TryEmitNestedFinding(qualifiedName, named, FragmentTextRenderer.Render(named));
        }

        private void TryEmitNestedFinding(string qualifiedName, TableReference reference, string fragmentText)
        {
            if (!fenceMap.TryGetValue(qualifiedName, out var origin))
            {
                return;
            }

            if (origin.SoleRowSource && IsSoleSource(reference))
            {
                return;
            }

            var (line, column) = (reference.StartLine, reference.StartColumn);

            Findings.Add(new TvfFenceFinding(
                TvfFenceFindingKind.NestedUnderViewOrTvf,
                FunctionQualifiedName: origin.FunctionQualifiedName,
                ReferencedObjectQualifiedName: qualifiedName,
                FunctionKind: origin.FunctionKind,
                SourcePath: sourcePath,
                Line: line,
                Column: column,
                Depth: origin.Depth,
                OriginSourcePath: origin.OriginSourcePath,
                OriginLine: origin.OriginLine,
                ReferenceFragmentText: fragmentText));
        }

        private static List<ColumnReferenceExpression> CollectColumnReferences(ScalarExpression argument)
        {
            var finder = new ColumnReferenceCollector();
            argument.Accept(finder);
            return finder.Found;
        }

        private sealed class ColumnReferenceCollector : TSqlFragmentVisitor
        {
            public List<ColumnReferenceExpression> Found { get; } = [];

            public override void ExplicitVisit(ColumnReferenceExpression node) => Found.Add(node);
        }
    }
}
