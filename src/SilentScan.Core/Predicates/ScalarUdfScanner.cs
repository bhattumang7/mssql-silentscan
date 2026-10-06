using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Catalog;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;
using SilentScan.Core.Rules;
using SilentScan.Core.Common;

namespace SilentScan.Core.Predicates;

public static class ScalarUdfScanner
{
    public static IReadOnlyList<ScalarUdfFinding> Scan(
        SqlParseResult parseResult, DatabaseCatalog catalog, IReadOnlyDictionary<string, ScalarUdfOrigin> scalarUdfMap)
    {
        var rule = CreateRule(parseResult.SourcePath, catalog, scalarUdfMap);
        var walker = new ModuleWalker(parseResult.SourcePath, catalog, EmptyResolvedViews, rules: [rule]);
        parseResult.Fragment.Accept(walker);
        return Harvest(rule);
    }

    internal static Rule CreateRule(string sourcePath, DatabaseCatalog catalog, IReadOnlyDictionary<string, ScalarUdfOrigin> scalarUdfMap) =>
        new(sourcePath, catalog, scalarUdfMap);

    internal static IReadOnlyList<ScalarUdfFinding> Harvest(Rule rule) => rule.Findings;

    private static readonly IReadOnlyDictionary<string, ResolvedRelation> EmptyResolvedViews = new Dictionary<string, ResolvedRelation>();

    internal sealed class Rule(string sourcePath, DatabaseCatalog catalog, IReadOnlyDictionary<string, ScalarUdfOrigin> scalarUdfMap) : IModuleRule
    {

        private readonly List<(int Start, int End, ScalarUdfContext Context)> _regions = [];

        private readonly List<(int Start, int End)> _rowSourceRegions = [];

        private readonly List<(int Start, int End)> _uncorrelatedFunctionArgumentRegions = [];

        private readonly HashSet<FunctionCall> _claimed = [];

        public List<ScalarUdfFinding> Findings { get; } = [];

        private readonly List<(int Start, int End)> _columnFreeConjunctRegions = [];

        private readonly List<(int Start, int End)> _uniqueKeySeekBoundRegions = [];

        private bool _scanForcingHintSeen;

        public void OnEnterNamedTableReference(NamedTableReference node, ModuleWalker walker)
        {
            if (node.TableHints.Any(hint => hint.HintKind is TableHintKind.ForceScan || hint is IndexTableHint))
            {
                _scanForcingHintSeen = true;
            }
        }

        public void OnEnterWhereClause(WhereClause node, ModuleWalker walker)
        {
            RecordRegion(node.SearchCondition, ScalarUdfContext.Where);
            RecordColumnFreeConjuncts(node.SearchCondition);
            RecordUniqueKeySeekBounds(node.SearchCondition, walker);
        }

        private void RecordUniqueKeySeekBounds(BooleanExpression? condition, ModuleWalker walker)
        {
            if (_scanForcingHintSeen)
            {
                return;
            }

            var scopeChain = walker.CurrentScopeChain();
            foreach (var conjunct in PredicateTreeWalker.FlattenAnd(condition))
            {
                if (conjunct is not BooleanComparisonExpression { ComparisonType: BooleanComparisonType.Equals } comparison)
                {
                    continue;
                }

                var bound = UniqueKeySeekBound(comparison.FirstExpression, comparison.SecondExpression, scopeChain, walker)
                    ?? UniqueKeySeekBound(comparison.SecondExpression, comparison.FirstExpression, scopeChain, walker);
                if (bound is not null)
                {
                    _uniqueKeySeekBoundRegions.Add((bound.StartOffset, bound.StartOffset + bound.FragmentLength));
                }
            }
        }

        private FunctionCall? UniqueKeySeekBound(ScalarExpression keySide, ScalarExpression boundSide, ScopeChain scopeChain, ModuleWalker walker)
        {
            if (keySide is not ColumnReferenceExpression columnRef || boundSide is not FunctionCall call)
            {
                return null;
            }

            var columnCollector = new ColumnAliasHelpers.RawColumnReferenceCollector();
            call.Accept(columnCollector);
            if (columnCollector.References.Count > 0 || walker.ResolveCatalogColumn(columnRef, scopeChain) is not { } resolved)
            {
                return null;
            }

            var table = catalog.Find(resolved.TableQualifiedName, walker.CurrentProcScope);
            var hasSingleColumnUniqueKey = table is { Kind: CatalogTableKind.Table }
                && table.Indexes.Any(ix =>
                    ix.IsUnique && !ix.IsFiltered && !ix.IsDisabled && !ix.IsColumnstore
                    && ix.KeyColumns.Count == 1
                    && catalog.IdentifierComparer.Equals(ix.KeyColumns[0], resolved.Column.Name));
            if (!hasSingleColumnUniqueKey)
            {
                return null;
            }

            var typeContext = new ScalarExpressionResolver.ScalarTypeContext(Ledger: null, catalog.TypeAliases, catalog);
            var boundType = ScalarExpressionResolver.ResolveScalarType(call, scopeChain, sourcePath, typeContext);
            return boundType is not null && boundType.Category == resolved.Column.Type?.Category ? call : null;
        }

        public void OnEnterHavingClause(HavingClause node, ModuleWalker walker) => RecordRegion(node.SearchCondition, ScalarUdfContext.Having);

        public void OnEnterJoinSearchCondition(QualifiedJoin node, ModuleWalker walker)
        {
            RecordRegion(node.SearchCondition, ScalarUdfContext.JoinOn);
            RecordColumnFreeConjuncts(node.SearchCondition);
        }

        public void OnEnterMergeSearchCondition(MergeSpecification node, ModuleWalker walker) => RecordRegion(node.SearchCondition, ScalarUdfContext.MergeOn);

        public void OnEnterSelectScalarExpression(SelectScalarExpression node, ModuleWalker walker) => RecordRegion(node.Expression, ScalarUdfContext.SelectList);

        public void OnEnterOrderByClause(OrderByClause node, ModuleWalker walker) => RecordRegion(node, ScalarUdfContext.OrderBy);

        public void OnEnterGroupByClause(GroupByClause node, ModuleWalker walker) => RecordRegion(node, ScalarUdfContext.GroupBy);

        public void OnEnterAssignmentSetClause(AssignmentSetClause node, ModuleWalker walker) =>
            RecordRegion(node.NewValue, node.Variable is not null ? ScalarUdfContext.VariableAssignment : ScalarUdfContext.SetAssignment);

        public void OnEnterSelectSetVariable(SelectSetVariable node, ModuleWalker walker) => RecordRegion(node.Expression, ScalarUdfContext.VariableAssignment);

        public void OnEnterSetVariableStatement(SetVariableStatement node, ModuleWalker walker) => RecordRegion(node.Expression, ScalarUdfContext.VariableAssignment);

        public void OnEnterQuerySpecificationScope(QuerySpecification node, ScopeChain scopeChain, ModuleWalker walker)
        {
            if (node.FromClause is not null)
            {
                RecordRowSource(node);
            }
        }

        public void OnEnterUpdateStatementScope(UpdateStatement node, ScopeChain scopeChain, ModuleWalker walker) =>
            RecordRowSource(node.UpdateSpecification);

        public void OnEnterDeleteStatementScope(DeleteStatement node, ScopeChain scopeChain, ModuleWalker walker) =>
            RecordRowSource(node.DeleteSpecification);

        public void OnEnterMergeStatementScope(MergeStatement node, ScopeChain scopeChain, ModuleWalker walker) =>
            RecordRowSource(node.MergeSpecification);

        public void OnEnterFromClause(FromClause node, ModuleWalker walker)
        {
            foreach (var tableReference in node.TableReferences)
            {
                Flatten(tableReference, isApplied: false);
            }
        }

        public void OnEnterFunctionCall(FunctionCall node, ModuleWalker walker)
        {
            if (_claimed.Contains(node))
            {
                return;
            }

            if (node.CallTarget is MultiPartIdentifierCallTarget)
            {
                var qualifiedName = catalog.ResolveSynonymName(SchemaObjectNameHelper.QualifyFunctionCall(node));
                if (catalog.TryGetScalarUdfInfo(qualifiedName, out var info) && info is not null)
                {
                    Emit(node, qualifiedName, info);
                    ClaimNestedFunctionCalls(node);
                }
            }
        }

        private void RecordRegion(TSqlFragment? region, ScalarUdfContext context)
        {
            if (region is not null)
            {
                _regions.Add((region.StartOffset, region.StartOffset + region.FragmentLength, context));
            }
        }

        private void RecordRowSource(TSqlFragment fragment) =>
            _rowSourceRegions.Add((fragment.StartOffset, fragment.StartOffset + fragment.FragmentLength));

        private void RecordColumnFreeConjuncts(BooleanExpression? condition)
        {
            foreach (var conjunct in PredicateTreeWalker.FlattenAnd(condition))
            {
                var collector = new ColumnAliasHelpers.RawColumnReferenceCollector();
                conjunct.Accept(collector);
                if (collector.References.Count == 0)
                {
                    _columnFreeConjunctRegions.Add((conjunct.StartOffset, conjunct.StartOffset + conjunct.FragmentLength));
                }
            }
        }

        private bool IsEvaluatedPerRow(FunctionCall node) =>
            Contains(_rowSourceRegions, node)
            && !Contains(_uncorrelatedFunctionArgumentRegions, node)
            && !Contains(_columnFreeConjunctRegions, node)
            && !Contains(_uniqueKeySeekBoundRegions, node);

        private static bool Contains(List<(int Start, int End)> regions, FunctionCall node) =>
            regions.Exists(region => node.StartOffset >= region.Start && node.StartOffset < region.End);

        private void Flatten(TableReference tableReference, bool isApplied)
        {
            switch (tableReference)
            {
                case UnqualifiedJoin { UnqualifiedJoinType: UnqualifiedJoinType.CrossApply or UnqualifiedJoinType.OuterApply } apply:
                    Flatten(apply.FirstTableReference, isApplied);
                    Flatten(apply.SecondTableReference, isApplied: true);
                    break;

                case JoinTableReference join:
                    Flatten(join.FirstTableReference, isApplied);
                    Flatten(join.SecondTableReference, isApplied);
                    break;

                case JoinParenthesisTableReference parenthesis:
                    Flatten(parenthesis.Join, isApplied);
                    break;

                case SchemaObjectFunctionTableReference function:
                    if (!isApplied)
                    {
                        foreach (var parameter in function.Parameters)
                        {
                            _uncorrelatedFunctionArgumentRegions.Add((parameter.StartOffset, parameter.StartOffset + parameter.FragmentLength));
                        }
                    }

                    var functionQualifiedName = catalog.ResolveSynonymName(SchemaObjectNameHelper.Qualify(function.SchemaObject));
                    TryEmitNested(functionQualifiedName, function.StartLine, function.StartColumn, FragmentTextRenderer.Render(function));
                    break;

                case NamedTableReference named:
                    var namedQualifiedName = catalog.ResolveSynonymName(SchemaObjectNameHelper.Qualify(named.SchemaObject));
                    TryEmitNested(namedQualifiedName, named.StartLine, named.StartColumn, FragmentTextRenderer.Render(named));
                    break;
            }

        }

        private void TryEmitNested(string qualifiedName, int line, int column, string fragmentText)
        {
            if (!scalarUdfMap.TryGetValue(qualifiedName, out var origin))
            {
                return;
            }

            Findings.Add(new ScalarUdfFinding(
                ScalarUdfFindingKind.NestedUnderViewOrTvf,
                FunctionQualifiedName: origin.FunctionQualifiedName,
                ReferencedObjectQualifiedName: qualifiedName,
                UdfKind: origin.UdfKind,
                Inlineability: ScalarUdfInlineability.Unknown,
                InlineabilityBlocker: null,
                IsSchemaBound: null,
                ConstantArgumentsNotFolded: false,
                ClrDataAccess: null,
                Context: origin.OriginContext,
                SchemaDependencyKind: null,
                SourcePath: sourcePath,
                Line: line,
                Column: column,
                Depth: origin.Depth,
                OriginSourcePath: origin.OriginSourcePath,
                OriginLine: origin.OriginLine,
                ReferenceFragmentText: fragmentText));
        }

        private void Emit(FunctionCall node, string qualifiedName, ScalarUdfInfo info)
        {
            var context = ResolveContext(node);
            var kind = ScalarUdfClassifier.ClassifyInvocationKind(context);
            if (!IsEvaluatedPerRow(node))
            {
                return;
            }

            var (inlineability, blocker) = ScalarUdfInlineabilityClassifier.Classify(info, catalog.CompatibilityLevel);
            var constantArgumentsNotFolded = info.IsSchemaBound == false && node.Parameters.Count > 0 && node.Parameters.All(p => p is Literal);

            Findings.Add(new ScalarUdfFinding(
                kind,
                FunctionQualifiedName: qualifiedName,
                ReferencedObjectQualifiedName: qualifiedName,
                UdfKind: info.Kind,
                Inlineability: inlineability,
                InlineabilityBlocker: blocker,
                IsSchemaBound: info.IsSchemaBound,
                ConstantArgumentsNotFolded: constantArgumentsNotFolded,
                ClrDataAccess: info.ClrDataAccess,
                Context: context,
                SchemaDependencyKind: null,
                SourcePath: sourcePath,
                Line: node.StartLine,
                Column: node.StartColumn,
                ReferenceFragmentText: FragmentTextRenderer.Render(node)));
        }

        private ScalarUdfContext ResolveContext(FunctionCall node) =>
            ScalarUdfContextRegions.Resolve(_regions, node);

        private void ClaimNestedFunctionCalls(FunctionCall node)
        {
            var visitor = new NestedFunctionCallCollector();
            foreach (var parameter in node.Parameters)
            {
                parameter.Accept(visitor);
            }

            foreach (var nested in visitor.Found)
            {
                _claimed.Add(nested);
            }
        }

        private sealed class NestedFunctionCallCollector : TSqlFragmentVisitor
        {
            public List<FunctionCall> Found { get; } = [];

            public override void ExplicitVisit(FunctionCall node)
            {
                Found.Add(node);
                base.ExplicitVisit(node);
            }
        }
    }
}
