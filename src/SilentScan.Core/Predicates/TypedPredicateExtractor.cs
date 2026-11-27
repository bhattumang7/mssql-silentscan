using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Catalog;
using SilentScan.Core.Diagnostics;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;
using SilentScan.Core.Rules;
using SilentScan.Core.TypeInference;
using SilentScan.Core.Common;

namespace SilentScan.Core.Predicates;

public static class TypedPredicateExtractor
{
    public static PredicateExtractionResult Extract(
        SqlParseResult parseResult, DatabaseCatalog catalog, LineageCatalog lineage, IReadOnlyDictionary<string, SqlType?>? externalVariables = null,
        DynamicSqlScope? enclosingScope = null, IReadOnlyDictionary<string, IReadOnlyList<string>>? callerScopeByCalleeScope = null)
    {
        var resolvedViews = lineage.AllRelations;
        var ledger = new SkipLedger();
        var settings = new StatementSettings(
            SetOptionByStatementResolver.Resolve(parseResult.Fragment, SetOptions.NumericRoundAbort, setting: true),
            SetOptionByStatementResolver.Resolve(parseResult.Fragment, SetOptions.AnsiWarnings, setting: false));
        var rule = new Rule(parseResult.SourcePath, catalog, resolvedViews, externalVariables, ledger, callerScopeByCalleeScope, settings);
        var walker = new ModuleWalker(
            parseResult.SourcePath, catalog, resolvedViews, rules: [rule],
            callerContext: new ModuleWalkerCallerContext(ledger, enclosingScope?.ProcScope, callerScopeByCalleeScope));

        if (enclosingScope?.TriggerTarget is { } target)
        {
            walker.PushCteRelations(walker.BuildTriggerPseudoTableRelations(target, parseResult.Fragment));
        }

        parseResult.Fragment.Accept(walker);
        return new PredicateExtractionResult(
            rule.Findings, rule.ExpressionDerivedFindings, rule.WriteLossFindings, ledger.Entries,
            rule.UnderLengthParametersThatCanTruncate(), rule.AnsiPaddingMismatchFindings,
            rule.LocalVariablePredicateFindings, rule.FilteredIndexParameterMismatchFindings);
    }

    private sealed record StatementSettings(
        IReadOnlyDictionary<TSqlStatement, bool> NumericRoundAbortOn,
        IReadOnlyDictionary<TSqlStatement, bool> AnsiWarningsOff);

    private sealed class Rule(
        string sourcePath,
        DatabaseCatalog catalog,
        IReadOnlyDictionary<string, ResolvedRelation> resolvedViews,
        IReadOnlyDictionary<string, SqlType?>? externalVariables,
        SkipLedger ledger,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? callerScopeByCalleeScope,
        StatementSettings settings) : IModuleRule
    {
        private const string PredicateOperandConstructKind = "predicate operand";

        private const string NonSeekableOperatorConstructKind = "non-seekable operator";

        private const string WriteTargetConstructKind = "write target";

        private const string WriteSourceConstructKind = "write source";

        private const string OperandPositionConstructKind = "comparison inside scalar expression";

        private const string OperandPositionLedgerReason = "not a seek position - nested inside a CASE/IIF/COALESCE/NULLIF branch (or similar operand position) within an enclosing filter clause";

        private const string NoColumnOperandConstructKind = "no column operand";

        private const string UnresolvedColumnComparisonConstructKind = "unresolved column comparison";

        private const string FoldableLiteralComparisonConstructKind = "foldable literal comparison";

        private enum PredicatePosition
        {
            NotSeekable,

            Seekable,

            SuppressedOperand,
        }

        private PredicatePosition _position;

        private readonly Stack<PredicatePosition> _positionStack = new();

        private bool _negated;

        private readonly Stack<bool> _negatedStack = new();

        private TSqlFragment? _currentPredicateFragment;

        private ColumnReferenceExpression? _directPredicateOperand;

        private readonly Dictionary<string, SqlType?> _variables = externalVariables is null
            ? new Dictionary<string, SqlType?>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, SqlType?>(externalVariables, StringComparer.OrdinalIgnoreCase);

        private readonly HashSet<string> _formalParameterNames = externalVariables is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(externalVariables.Keys, StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, IReadOnlyList<(SqlType? Type, TSqlFragment Source)>> _cursorSelectSourcesByName =
            new(StringComparer.OrdinalIgnoreCase);

        private string? _pendingCursorDeclarationName;

        private readonly HashSet<QuerySpecification> _pendingCursorBranches = new(ReferenceEqualityComparer.Instance);

        private readonly List<QuerySpecification> _pendingCursorBranchOrder = [];

        private readonly Dictionary<QuerySpecification, List<InsertSourceColumn>?> _collectedCursorBranches = new(ReferenceEqualityComparer.Instance);

        public List<TypedPredicateFinding> Findings { get; } = [];

        public List<ExpressionDerivedFinding> ExpressionDerivedFindings { get; } = [];


        public List<WriteLossFinding> WriteLossFindings { get; } = [];


        private readonly List<(UnderLengthParameterFinding Finding, VariableSourceCollector? Sources, SqlType DeclaredType)> _underLengthCandidates = [];

        private readonly Dictionary<ColumnReferenceExpression, SqlType?> _assignedColumnTypes = [];

        public List<UnderLengthParameterFinding> UnderLengthParametersThatCanTruncate() =>
            [.. _underLengthCandidates.Where(c => !EverySourceFits(c.Finding, c.Sources, c.DeclaredType)).Select(c => c.Finding)];

        public List<AnsiPaddingMismatchFinding> AnsiPaddingMismatchFindings { get; } = [];

        public List<LocalVariablePredicateFinding> LocalVariablePredicateFindings { get; } = [];

        public List<FilteredIndexParameterMismatchFinding> FilteredIndexParameterMismatchFindings { get; } = [];

        private Dictionary<(string Table, string Column), List<(string? IndexName, string LiteralText)>>? _literalEqualityFilteredIndexesByColumn;

        private Dictionary<(string Table, string Column), List<(string? IndexName, string LiteralText)>> LiteralEqualityFilteredIndexesByColumn
        {
            get
            {
                if (_literalEqualityFilteredIndexesByColumn is not null)
                {
                    return _literalEqualityFilteredIndexesByColumn;
                }

                var map = new Dictionary<(string, string), List<(string?, string)>>(TableColumnKeyComparer.For(catalog));
                foreach (var table in catalog.Tables)
                {
                    foreach (var index in table.Indexes)
                    {
                        if (!index.IsFiltered || index.FilterDefinition is not { } filterDefinition
                            || IndexDesignScanner.TryExtractSimpleLiteralEqualityFilter(filterDefinition, catalog.CompatibilityLevel) is not { } extracted)
                        {
                            continue;
                        }

                        var key = (table.QualifiedName, extracted.ColumnName);
                        if (!map.TryGetValue(key, out var entries))
                        {
                            entries = [];
                            map[key] = entries;
                        }

                        entries.Add((index.Name, extracted.LiteralText));
                    }
                }

                _literalEqualityFilteredIndexesByColumn = map;
                return map;
            }
        }

        private bool _procedureHasWithRecompile;

        private SqlType? _scalarReturnType;

        private bool _statementHasOptionRecompile;

        private readonly Stack<(bool Recompile, HashSet<string> OptimizeFor)> _statementRecompileStack = new();

        private HashSet<string> _statementOptimizeForVariables = new(StringComparer.OrdinalIgnoreCase);

        private bool HasActiveRecompileGuard => _procedureHasWithRecompile || _statementHasOptionRecompile;

        private IReadOnlyList<CatalogColumn?>? _pendingInsertTargetColumns;

        private string? _pendingInsertTargetTable;

        private readonly HashSet<QuerySpecification> _pendingInsertBranches = new(ReferenceEqualityComparer.Instance);

        private readonly List<QuerySpecification> _pendingInsertBranchOrder = [];

        private readonly Dictionary<QuerySpecification, List<InsertSourceColumn>?> _collectedInsertBranches = new(ReferenceEqualityComparer.Instance);

        private readonly record struct InsertSourceColumn(SqlType? Type, TSqlFragment Source, ScalarExpression? Expression, ScopeChain Scope, ModuleWalker Walker);

        private bool _previousProcedureHasWithRecompile;

        private TSqlStatement? _currentWriteStatement;

        private bool IsNumericRoundAbortOnForCurrentStatement =>
            _currentWriteStatement is { } statement
            && settings.NumericRoundAbortOn.TryGetValue(statement, out var isOn)
            && isOn;

        private bool IsAnsiWarningsOffForCurrentStatement =>
            _currentWriteStatement is { } statement
            && settings.AnsiWarningsOff.TryGetValue(statement, out var isOff)
            && isOff;

        private VariableSourceCollector _variableSources = new();

        public void OnEnterProcedureOrFunctionBody(ProcedureStatementBodyBase node, ModuleWalker walker)
        {
            _variableSources = VariableSourceCollector.Collect(node);
            _variables.Clear();
            _formalParameterNames.Clear();
            _cursorSelectSourcesByName.Clear();
            RecordParameters(node.Parameters);
            EmitParameterDefaultFindings(node.Parameters, walker);

            _scalarReturnType = node is FunctionStatementBody { ReturnType: ScalarFunctionReturnType scalarReturn }
                ? SqlTypeReferenceResolver.Resolve(scalarReturn.DataType, columnCollation: null, catalog.TypeAliases)
                : null;

            _previousProcedureHasWithRecompile = _procedureHasWithRecompile;
            _procedureHasWithRecompile = node is ProcedureStatementBody { Options: { } options }
                && options.Any(o => o.OptionKind == ProcedureOptionKind.Recompile);
        }

        public void OnLeaveProcedureOrFunctionBody(ProcedureStatementBodyBase node, ModuleWalker walker)
        {
            _procedureHasWithRecompile = _previousProcedureHasWithRecompile;
            _scalarReturnType = null;
        }

        public void OnEnterSelectSetVariable(SelectSetVariable node, ModuleWalker walker)
        {
            if (node.Expression is { } sourceExpression)
            {
                AssignToVariable(node.Variable.Name, sourceExpression, walker);
            }
        }

        private void EmitParameterDefaultFindings(IList<ProcedureParameter> parameters, ModuleWalker walker)
        {
            foreach (var parameter in parameters)
            {
                if (parameter.Value is { } defaultValue)
                {
                    AssignToVariable(parameter.VariableName.Value, defaultValue, walker, WriteLossTarget.ParameterDefault);
                }
            }
        }

        public void OnEnterReturnStatement(ReturnStatement node, ModuleWalker walker)
        {
            if (_scalarReturnType is { } returnType && node.Expression is { } returned)
            {
                EmitResolvedAssignment(WriteLossTarget.FunctionReturn, tableQualifiedName: null, "RETURN", returnType, returned, walker);
            }
        }

        public void OnEnterTriggerBody(TriggerStatementBody node, ModuleWalker walker)
        {
            _variableSources = VariableSourceCollector.Collect(node);
            _variables.Clear();
            _formalParameterNames.Clear();
            _cursorSelectSourcesByName.Clear();
        }

        public void OnEnterSelectStatementScope(SelectStatement node, ModuleWalker walker) =>
            _statementRecompileStack.Push(BeginStatementOptimizerHints(node.OptimizerHints));

        public void OnLeaveSelectStatementScope(SelectStatement node, ModuleWalker walker) =>
            EndStatementOptimizerHints();

        public void OnEnterQuerySpecificationScope(QuerySpecification node, ScopeChain scopeChain, ModuleWalker walker)
        {
            if (_pendingInsertTargetColumns is { } pendingColumns && _pendingInsertBranches.Remove(node))
            {
                _collectedInsertBranches[node] = CollectSelectListSources(node.SelectElements, scopeChain, walker);
                if (_pendingInsertBranches.Count == 0)
                {
                    AnalyzeInsertBranches(pendingColumns, _pendingInsertTargetTable!);
                    _pendingInsertBranchOrder.Clear();
                    _collectedInsertBranches.Clear();
                    _pendingInsertTargetColumns = null;
                    _pendingInsertTargetTable = null;
                }
            }

            if (_pendingCursorDeclarationName is { } pendingCursorName && _pendingCursorBranches.Remove(node))
            {
                _collectedCursorBranches[node] = CollectSelectListSources(node.SelectElements, scopeChain, walker);
                if (_pendingCursorBranches.Count == 0)
                {
                    if (CombineCursorBranchSources() is { } cursorSources)
                    {
                        _cursorSelectSourcesByName[pendingCursorName] = cursorSources;
                    }

                    _pendingCursorDeclarationName = null;
                    _pendingCursorBranchOrder.Clear();
                    _collectedCursorBranches.Clear();
                }
            }

            _positionStack.Push(_position);
            _position = PredicatePosition.NotSeekable;

            _negatedStack.Push(_negated);
            _negated = false;
        }

        public void OnLeaveQuerySpecificationScope(QuerySpecification node, ScopeChain scopeChain, ModuleWalker walker)
        {
            _negated = _negatedStack.Pop();
            _position = _positionStack.Pop();
        }

        private readonly Stack<WhereEqualityScope> _whereEqualityScopes = new();

        private sealed record WhereEqualityScope(
            HashSet<BooleanExpression> TopLevelConjuncts,
            List<(LocalVariablePredicateFinding Finding, string Table, string Column)> Equalities,
            List<(string Table, string Column)> PinnedByValue);

        public void OnEnterWhereClause(WhereClause node, ModuleWalker walker)
        {
            _positionStack.Push(_position);
            _position = PredicatePosition.Seekable;
            _whereEqualityScopes.Push(new WhereEqualityScope([.. PredicateTreeWalker.FlattenAnd(node.SearchCondition)], [], []));
        }

        public void OnLeaveWhereClause(WhereClause node, ModuleWalker walker)
        {
            _position = _positionStack.Pop();
            var scope = _whereEqualityScopes.Pop();
            foreach (var tableGroup in scope.Equalities.GroupBy(e => e.Table, catalog.IdentifierComparer))
            {
                var pinnedColumns = tableGroup.Select(e => e.Column).ToHashSet(catalog.IdentifierComparer);
                pinnedColumns.UnionWith(scope.PinnedByValue
                    .Where(p => catalog.IdentifierComparer.Equals(p.Table, tableGroup.Key))
                    .Select(p => p.Column));
                if (catalog.Find(tableGroup.Key) is { } table && table.Indexes.Any(index => IsFullyPinnedUniqueKey(index, pinnedColumns)))
                {
                    LocalVariablePredicateFindings.RemoveAll(f => tableGroup.Any(e => ReferenceEquals(e.Finding, f)));
                }
            }
        }

        private static bool IsFullyPinnedUniqueKey(CatalogIndex index, HashSet<string> pinnedColumns) =>
            index.IsUnique
            && !index.IsFiltered
            && !index.IsDisabled
            && index.KeyColumns.Count > 1
            && index.KeyColumns.All(pinnedColumns.Contains);

        public void OnEnterHavingClause(HavingClause node, ModuleWalker walker)
        {
            _positionStack.Push(_position);
            _position = PredicatePosition.Seekable;
        }

        public void OnLeaveHavingClause(HavingClause node, ModuleWalker walker) => _position = _positionStack.Pop();

        public void OnEnterJoinSearchCondition(QualifiedJoin node, ModuleWalker walker)
        {
            _positionStack.Push(_position);
            _position = PredicatePosition.Seekable;
        }

        public void OnLeaveJoinSearchCondition(QualifiedJoin node, ModuleWalker walker) => _position = _positionStack.Pop();

        public void OnEnterMergeSearchCondition(MergeSpecification node, ModuleWalker walker)
        {
            _positionStack.Push(_position);
            _position = PredicatePosition.Seekable;
        }

        public void OnLeaveMergeSearchCondition(MergeSpecification node, ModuleWalker walker) => _position = _positionStack.Pop();

        public void OnEnterMergeActionSearchCondition(MergeActionClause node, ModuleWalker walker)
        {
            _positionStack.Push(_position);
            _position = PredicatePosition.Seekable;
        }

        public void OnLeaveMergeActionSearchCondition(MergeActionClause node, ModuleWalker walker) => _position = _positionStack.Pop();

        public void OnEnterUpdateStatementScope(UpdateStatement node, ScopeChain scopeChain, ModuleWalker walker)
        {
            _currentWriteStatement = node;
            _statementRecompileStack.Push(BeginStatementOptimizerHints(node.OptimizerHints));
        }

        public void OnLeaveUpdateStatementScope(UpdateStatement node, ScopeChain scopeChain, ModuleWalker walker)
        {
            EndStatementOptimizerHints();
            _currentWriteStatement = null;
        }

        public void OnEnterInsertStatementScope(InsertStatement node, ModuleWalker walker)
        {
            _currentWriteStatement = node;
            _statementRecompileStack.Push(BeginStatementOptimizerHints(node.OptimizerHints));
            AnalyzeInsertWriteLoss(node.InsertSpecification, walker);
        }

        public void OnLeaveInsertStatementScope(InsertStatement node, ModuleWalker walker) => EndStatementOptimizerHints();

        public void OnEnterDeleteStatementScope(DeleteStatement node, ScopeChain scopeChain, ModuleWalker walker) =>
            _statementRecompileStack.Push(BeginStatementOptimizerHints(node.OptimizerHints));

        public void OnLeaveDeleteStatementScope(DeleteStatement node, ScopeChain scopeChain, ModuleWalker walker) =>
            EndStatementOptimizerHints();

        public void OnEnterMergeStatementScope(MergeStatement node, ScopeChain scopeChain, ModuleWalker walker)
        {
            _currentWriteStatement = node;
            _statementRecompileStack.Push(BeginStatementOptimizerHints(node.OptimizerHints));
        }

        public void OnLeaveMergeStatementScope(MergeStatement node, ScopeChain scopeChain, ModuleWalker walker)
        {
            EndStatementOptimizerHints();
            _currentWriteStatement = null;
        }

        public void OnEnterAssignmentSetClause(AssignmentSetClause node, ModuleWalker walker)
        {
            if (node.Column is not { } columnRef)
            {
                return;
            }

            var scopeChain = walker.CurrentModificationTargetScopeChain() ?? walker.CurrentScopeChain();
            if (ResolveOperand(columnRef, scopeChain, walker) is PredicateOperand.Column target && target.Type is { } targetType)
            {
                EmitResolvedAssignment(WriteLossTarget.Column, target.TableQualifiedName, target.ColumnName, targetType, node.NewValue, walker);
            }
        }

        public void OnEnterSetVariableStatement(SetVariableStatement node, ModuleWalker walker)
        {
            _currentWriteStatement = node;

            if (node.Expression is { } sourceExpression)
            {
                AssignToVariable(node.Variable.Name, sourceExpression, walker);
            }
        }

        public void OnEnterTSqlBatch(TSqlBatch node, ModuleWalker walker)
        {
            _variableSources = VariableSourceCollector.Collect(node);
            _variables.Clear();
            _formalParameterNames.Clear();
            _cursorSelectSourcesByName.Clear();
            _pendingCursorDeclarationName = null;
            if (externalVariables is not null)
            {
                foreach (var (name, type) in externalVariables)
                {
                    _variables[name] = type;
                    _formalParameterNames.Add(name);
                }
            }
        }

        public void OnEnterDeclareCursorStatement(DeclareCursorStatement node, ModuleWalker walker)
        {
            var name = node.Name.Value;
            _cursorSelectSourcesByName.Remove(name);

            if (FlattenInsertBranches(node.CursorDefinition.Select.QueryExpression) is { Count: > 0 } branches
                && branches.All(b => b.SelectElements.Count > 0 && b.SelectElements.All(e => e is SelectScalarExpression))
                && branches.Select(b => b.SelectElements.Count).Distinct().Count() == 1)
            {
                _pendingCursorDeclarationName = name;
                _pendingCursorBranches.Clear();
                _pendingCursorBranches.UnionWith(branches);
                _pendingCursorBranchOrder.Clear();
                _pendingCursorBranchOrder.AddRange(branches);
                _collectedCursorBranches.Clear();
                return;
            }

            ledger.Record(
                AnalysisPass.Predicates, sourcePath, node.StartLine, node.StartColumn,
                WriteSourceConstructKind, $"cursor '{name}' is defined by a query this pass does not resolve a flat scalar select list for (top level is not a SELECT, a parenthesized SELECT or a set operation over plain scalar select lists) - FETCH INTO write-loss analysis skipped for it");
        }

        public void OnEnterFetchCursorStatement(FetchCursorStatement node, ModuleWalker walker)
        {
            _currentWriteStatement = node;

            var name = node.Cursor?.Name?.Value;
            if (name is null || node.IntoVariables is not { Count: > 0 } intoVariables
                || !_cursorSelectSourcesByName.TryGetValue(name, out var sources))
            {
                return;
            }

            var count = Math.Min(intoVariables.Count, sources.Count);
            for (var i = 0; i < count; i++)
            {
                var variableName = intoVariables[i].Name;
                if (!_variables.TryGetValue(variableName, out var targetType) || targetType is not { } target)
                {
                    continue;
                }

                var (sourceType, source) = sources[i];
                EmitAssignment(new AssignmentSite(WriteLossTarget.Variable, null, variableName, target, sourceType, source));
            }
        }

        private List<(SqlType? Type, TSqlFragment Source)>? CombineCursorBranchSources()
        {
            var branches = _pendingCursorBranchOrder.Select(branch => _collectedCursorBranches.GetValueOrDefault(branch)).ToList();
            if (branches.Count == 0 || branches.Any(b => b is null))
            {
                return null;
            }

            var width = branches.Min(b => b!.Count);
            var combinedSources = new List<(SqlType?, TSqlFragment)>(width);
            for (var position = 0; position < width; position++)
            {
                var column = branches.Select(b => b![position]).ToList();
                if (column.Count == 1)
                {
                    combinedSources.Add((column[0].Type, column[0].Source));
                    continue;
                }

                var typed = column.Where(source => source.Expression is not NullLiteral).ToList();
                var combined = typed.Count == 0 ? null : ExpressionTypeInferencer.MergeBranchTypes(typed.Select(source => source.Type));
                combinedSources.Add((combined, typed.Count == 0 ? column[0].Source : typed[0].Source));
            }

            return combinedSources;
        }

        public void OnEnterDeclareVariableStatement(DeclareVariableStatement node, ModuleWalker walker)
        {
            _currentWriteStatement = node;

            foreach (var declaration in node.Declarations)
            {
                var targetType = SqlTypeReferenceResolver.Resolve(declaration.DataType, columnCollation: null, catalog.TypeAliases);
                _variables[declaration.VariableName.Value] = targetType;

                if (declaration.Value is { } sourceExpression && targetType is { } resolvedTargetType)
                {
                    EmitResolvedAssignment(WriteLossTarget.Variable, tableQualifiedName: null, declaration.VariableName.Value, resolvedTargetType, sourceExpression, walker);
                }
            }
        }

        public void OnEnterBooleanNotExpression(BooleanNotExpression node, ModuleWalker walker) => _negated = !_negated;

        public void OnLeaveBooleanNotExpression(BooleanNotExpression node, ModuleWalker walker) => _negated = !_negated;

        public void OnEnterOperandPosition(TSqlFragment node, ModuleWalker walker)
        {
            _positionStack.Push(_position);
            _position = _position == PredicatePosition.Seekable ? PredicatePosition.SuppressedOperand : PredicatePosition.NotSeekable;
        }

        public void OnLeaveOperandPosition(TSqlFragment node, ModuleWalker walker) => _position = _positionStack.Pop();

        private (bool, HashSet<string>) BeginStatementOptimizerHints(IList<OptimizerHint> hints)
        {
            var previous = (_statementHasOptionRecompile, _statementOptimizeForVariables);
            _statementHasOptionRecompile = hints.Any(h => h.HintKind == OptimizerHintKind.Recompile);
            _statementOptimizeForVariables = new HashSet<string>(
                hints.OfType<OptimizeForOptimizerHint>()
                    .SelectMany(h => h.Pairs)
                    .Where(pair => pair.Value is not null && !pair.IsForUnknown && pair.Variable is not null)
                    .Select(pair => pair.Variable.Name),
                StringComparer.OrdinalIgnoreCase);
            return previous;
        }

        private void EndStatementOptimizerHints() =>
            (_statementHasOptionRecompile, _statementOptimizeForVariables) = _statementRecompileStack.Pop();

        private bool SkipIfNotSeekable(TSqlFragment node)
        {
            if (_position == PredicatePosition.Seekable)
            {
                return false;
            }

            if (_position == PredicatePosition.SuppressedOperand)
            {
                ledger.Record(AnalysisPass.Predicates, sourcePath, node.StartLine, node.StartColumn, OperandPositionConstructKind, OperandPositionLedgerReason);
            }

            return true;
        }

        private static List<QuerySpecification>? FlattenInsertBranches(QueryExpression expression)
        {
            switch (expression)
            {
                case QuerySpecification specification:
                    return [specification];

                case QueryParenthesisExpression parenthesis:
                    return FlattenInsertBranches(parenthesis.QueryExpression);

                case BinaryQueryExpression binary
                    when FlattenInsertBranches(binary.FirstQueryExpression) is { } first
                         && FlattenInsertBranches(binary.SecondQueryExpression) is { } second:
                    return [.. first, .. second];

                default:
                    return null;
            }
        }

        private void AnalyzeInsertWriteLoss(InsertSpecification spec, ModuleWalker walker)
        {
            var table = ResolveWriteTargetTable(spec.Target, walker);
            if (table is null)
            {
                ledger.Record(
                    AnalysisPass.Predicates, sourcePath, spec.Target.StartLine, spec.Target.StartColumn,
                    WriteTargetConstructKind, "INSERT target does not resolve to a known table - write-loss analysis skipped");
                return;
            }

            var targetColumns = ResolveInsertTargetColumns(spec, table);

            switch (spec.InsertSource)
            {
                case ValuesInsertSource values:
                    AnalyzeValuesInsertSource(values, targetColumns, table.QualifiedName, walker);
                    return;

                case SelectInsertSource { Select: { } selectSource }
                    when FlattenInsertBranches(selectSource) is { } branches
                         && branches.All(b => b.SelectElements.All(e => e is SelectScalarExpression or SelectStarExpression)):
                    _pendingInsertBranches.Clear();
                    _pendingInsertBranches.UnionWith(branches);
                    _pendingInsertBranchOrder.Clear();
                    _pendingInsertBranchOrder.AddRange(branches);
                    _collectedInsertBranches.Clear();
                    _pendingInsertTargetColumns = targetColumns;
                    _pendingInsertTargetTable = table.QualifiedName;
                    return;

                case ExecuteInsertSource { Execute.ExecutableEntity: ExecutableProcedureReference { ProcedureReference.ProcedureReference.Name: { } calleeName } } execSource
                    when !table.QualifiedName.StartsWith('#')
                         && catalog.TryGetProcedureResultColumns(catalog.ResolveSynonymName(SchemaObjectNameHelper.Qualify(calleeName)), out var resultTypes)
                         && resultTypes.Count == targetColumns.Count:
                    AnalyzeInsertExecWriteLoss(execSource, targetColumns, resultTypes, table.QualifiedName);
                    return;

                default:
                    ledger.Record(
                        AnalysisPass.Predicates, sourcePath, spec.StartLine, spec.StartColumn,
                        WriteSourceConstructKind, $"INSERT source of kind '{spec.InsertSource.GetType().Name}' is not analyzed for write-loss - only a plain VALUES list or a single non-UNION SELECT with an explicit scalar select list is");
                    return;
            }
        }

        private CatalogTable? ResolveWriteTargetTable(TableReference target, ModuleWalker walker)
        {
            if (target is VariableTableReference variableTable)
            {
                return catalog.Find(variableTable.Variable.Name, walker.CurrentProcScope);
            }

            if (target is not NamedTableReference named)
            {
                return null;
            }

            var qualifiedName = catalog.ResolveSynonymName(SchemaObjectNameHelper.Qualify(named.SchemaObject));
            var table = catalog.Find(qualifiedName, walker.CurrentProcScope);

            if (table is null && walker.CurrentProcScope is not null
                && callerScopeByCalleeScope is not null
                && callerScopeByCalleeScope.TryGetValue(walker.CurrentProcScope, out var callerScopes))
            {
                table = FromScopeResolver.TryResolveFromCallerScopes(catalog, qualifiedName, callerScopes);
            }

            return table;
        }

        private List<CatalogColumn?> ResolveInsertTargetColumns(InsertSpecification spec, CatalogTable table)
        {
            if (spec.Columns.Count == 0)
            {
                return [.. table.Columns];
            }

            var resolved = new List<CatalogColumn?>(spec.Columns.Count);
            foreach (var columnRef in spec.Columns)
            {
                var name = columnRef.MultiPartIdentifier.Identifiers[^1].Value;
                var column = table.FindColumn(name, catalog.IdentifierComparer);
                if (column is null)
                {
                    ledger.Record(
                        AnalysisPass.Predicates, sourcePath, columnRef.StartLine, columnRef.StartColumn,
                        WriteTargetConstructKind, $"INSERT target column '{name}' does not resolve on table '{table.QualifiedName}' - write-loss analysis skipped for this column");
                }

                resolved.Add(column);
            }

            return resolved;
        }

        private void AnalyzeValuesInsertSource(ValuesInsertSource values, List<CatalogColumn?> targetColumns, string targetTableQualifiedName, ModuleWalker walker)
        {
            var scopeChain = walker.CurrentScopeChain();
            foreach (var columnValues in values.RowValues.Select(row => row.ColumnValues))
            {
                var count = Math.Min(columnValues.Count, targetColumns.Count);
                for (var i = 0; i < count; i++)
                {

                    if (targetColumns[i]?.Type is not { } targetType || columnValues[i] is DefaultLiteral)
                    {
                        continue;
                    }

                    var sourceExpression = columnValues[i];
                    var sourceType = OperandType(ResolveOperand(sourceExpression, scopeChain, walker));
                    EmitAssignment(new AssignmentSite(WriteLossTarget.Column, targetTableQualifiedName, targetColumns[i]!.Name, targetType, sourceType, sourceExpression, scopeChain, walker));
                }
            }
        }

        private List<InsertSourceColumn>? CollectSelectListSources(IList<SelectElement> selectElements, ScopeChain scopeChain, ModuleWalker walker)
        {
            var columns = new List<InsertSourceColumn>();
            foreach (var element in selectElements)
            {
                switch (element)
                {
                    case SelectScalarExpression scalar:
                        columns.Add(new InsertSourceColumn(OperandType(ResolveOperand(scalar.Expression, scopeChain, walker)), scalar.Expression, scalar.Expression, scopeChain, walker));
                        break;

                    case SelectStarExpression star:
                        if (ExpandStarColumns(star, scopeChain) is not { } expanded)
                        {
                            return null;
                        }

                        columns.AddRange(expanded.Select(column => new InsertSourceColumn(
                            ColumnProvenanceAnalysis.TryGetScalarType(column.Provenance), star, null, scopeChain, walker)));
                        break;

                    default:
                        return null;
                }
            }

            return columns;
        }

        private void AnalyzeInsertBranches(IReadOnlyList<CatalogColumn?> targetColumns, string targetTableQualifiedName)
        {
            var branches = _pendingInsertBranchOrder
                .Select(branch => _collectedInsertBranches.GetValueOrDefault(branch))
                .ToList();
            if (branches.Count == 0 || branches.Any(b => b is null))
            {
                return;
            }

            var width = branches.Min(b => b!.Count);
            for (var position = 0; position < Math.Min(width, targetColumns.Count); position++)
            {
                if (targetColumns[position] is not { Type: { } targetType } targetColumn)
                {
                    continue;
                }

                var sources = branches.Select(b => b![position]).ToList();
                if (sources.Count == 1)
                {
                    EmitInsertAssignment(targetTableQualifiedName, targetColumn.Name, targetType, sources[0].Type, sources[0]);
                    continue;
                }

                var typed = sources.Where(source => source.Expression is not NullLiteral).ToList();
                if (typed.Count == 0)
                {
                    continue;
                }

                var combined = ExpressionTypeInferencer.MergeBranchTypes(typed.Select(source => source.Type));
                if (combined is null)
                {
                    continue;
                }

                if (EmitInsertAssignment(targetTableQualifiedName, targetColumn.Name, targetType, combined, typed[0]))
                {
                    continue;
                }

                foreach (var source in typed)
                {
                    EmitInsertAssignment(targetTableQualifiedName, targetColumn.Name, combined, source.Type, source);
                }
            }
        }

        private void AnalyzeInsertExecWriteLoss(
            ExecuteInsertSource source, List<CatalogColumn?> targetColumns, IReadOnlyList<SqlType?> resultTypes, string targetTableQualifiedName)
        {
            for (var position = 0; position < targetColumns.Count; position++)
            {
                if (targetColumns[position] is { Type: { } targetType } targetColumn)
                {
                    EmitAssignment(new AssignmentSite(
                        WriteLossTarget.Column, targetTableQualifiedName, targetColumn.Name, targetType, resultTypes[position], source));
                }
            }
        }

        private bool EmitInsertAssignment(string targetTableQualifiedName, string targetColumnName, SqlType targetType, SqlType? sourceType, InsertSourceColumn source) =>
            EmitAssignment(new AssignmentSite(
                WriteLossTarget.Column, targetTableQualifiedName, targetColumnName, targetType, sourceType, source.Source, source.Scope, source.Walker));

        private static IReadOnlyList<ResolvedColumn>? ExpandStarColumns(SelectStarExpression star, ScopeChain scopeChain)
        {
            if (scopeChain.Count == 0)
            {
                return null;
            }

            var (byAlias, ordered) = scopeChain[0];
            if (star.Qualifier is { Count: > 0 } qualifier)
            {
                return byAlias.TryGetValue(qualifier.Identifiers[^1].Value, out var entry) ? [.. entry.Relation.Columns] : null;
            }

            return [.. ordered.SelectMany(entry => entry.Relation.Columns)];
        }

        private void AssignToVariable(string variableName, ScalarExpression source, ModuleWalker walker, WriteLossTarget target = WriteLossTarget.Variable)
        {
            if (_variables.TryGetValue(variableName, out var variableType) && variableType is { } targetType)
            {
                if (source is ColumnReferenceExpression column)
                {
                    _assignedColumnTypes[column] = OperandType(ResolveOperand(column, walker.CurrentScopeChain(), walker));
                }

                EmitResolvedAssignment(target, tableQualifiedName: null, variableName, targetType, source, walker);
            }
        }

        private void EmitResolvedAssignment(
            WriteLossTarget target, string? tableQualifiedName, string name, SqlType targetType, ScalarExpression source, ModuleWalker walker)
        {
            var scopeChain = walker.CurrentScopeChain();
            var sourceType = OperandType(ResolveOperand(source, scopeChain, walker));
            EmitAssignment(new AssignmentSite(target, tableQualifiedName, name, targetType, sourceType, source, scopeChain, walker));
        }

        private bool EmitAssignment(AssignmentSite site)
        {
            Func<ScalarExpression, SqlType?>? operandType = site.Scope is null || site.Walker is null
                ? null
                : leaf => OperandType(ResolveOperand(leaf, site.Scope, site.Walker));
            var kind = Rules.WriteLossClassifier.Classify(
                site.TargetType, site.SourceType, site.Source as ScalarExpression,
                isVariableTarget: site.Target.IsSilentTarget() || IsAnsiWarningsOffForCurrentStatement, operandType);
            if (kind is null || !site.Target.IsOracleVerified(kind.Value))
            {
                return false;
            }

            if (kind.Value == WriteLossKind.NumericScaleNarrowing
                && site.TargetType.Category == SqlTypeCategory.Decimal && site.SourceType!.Category == SqlTypeCategory.Decimal
                && IsNumericRoundAbortOnForCurrentStatement)
            {
                return false;
            }

            WriteLossFindings.Add(new WriteLossFinding(
                site.TableQualifiedName, site.Name, kind.Value, site.TargetType, site.SourceType!,
                sourcePath, site.Source.StartLine, site.Source.StartColumn, Site: site.Target));
            return true;
        }

        public void OnBooleanComparisonExpression(BooleanComparisonExpression node, ModuleWalker walker)
        {
            if (walker.IsDeadPredicate(node))
            {
                ledger.Record(AnalysisPass.Predicates, sourcePath, node.StartLine, node.StartColumn, ModuleWalker.NormalizationEliminatedConstructKind, ModuleWalker.NormalizationEliminatedLedgerReason);
                return;
            }

            var operatorText = ToOperatorText(node.ComparisonType);
            if (operatorText is null)
            {
                ledger.Record(AnalysisPass.Predicates, sourcePath, node.StartLine, node.StartColumn, "comparison operator", $"unrecognized comparison operator '{node.ComparisonType}'");
                return;
            }

            TryAddFinding(node.FirstExpression, node.SecondExpression, _negated ? Negate(operatorText) : operatorText, node, walker);
        }

        public void OnBooleanTernaryExpression(BooleanTernaryExpression node, ModuleWalker walker)
        {
            if (node.TernaryExpressionType is not (BooleanTernaryExpressionType.Between or BooleanTernaryExpressionType.NotBetween))
            {
                return;
            }

            if (walker.IsDeadPredicate(node))
            {
                ledger.Record(AnalysisPass.Predicates, sourcePath, node.StartLine, node.StartColumn, ModuleWalker.NormalizationEliminatedConstructKind, ModuleWalker.NormalizationEliminatedLedgerReason);
                return;
            }

            TryAddFinding(node.FirstExpression, node.SecondExpression, ">=", node, walker);
            TryAddFinding(node.FirstExpression, node.ThirdExpression, "<=", node, walker);
        }

        public void OnLikePredicate(LikePredicate node, ModuleWalker walker)
        {
            if (walker.IsDeadPredicate(node))
            {
                ledger.Record(AnalysisPass.Predicates, sourcePath, node.StartLine, node.StartColumn, ModuleWalker.NormalizationEliminatedConstructKind, ModuleWalker.NormalizationEliminatedLedgerReason);
                return;
            }

            if (node.NotDefined || _negated)
            {

                if (walker.CurrentScopeChain().Count > 0 && _position == PredicatePosition.Seekable)
                {
                    ledger.Record(AnalysisPass.Predicates, sourcePath, node.StartLine, node.StartColumn, NonSeekableOperatorConstructKind, "NOT LIKE is not sargable regardless of type match - not attributed to a type-conversion verdict");
                }
                else if (_position == PredicatePosition.SuppressedOperand)
                {
                    ledger.Record(AnalysisPass.Predicates, sourcePath, node.StartLine, node.StartColumn, OperandPositionConstructKind, OperandPositionLedgerReason);
                }

                return;
            }

            TryAddFinding(node.FirstExpression, node.SecondExpression, "LIKE", node, walker);
        }

        public void OnInPredicate(InPredicate node, ModuleWalker walker)
        {
            if (walker.IsDeadPredicate(node))
            {
                ledger.Record(AnalysisPass.Predicates, sourcePath, node.StartLine, node.StartColumn, ModuleWalker.NormalizationEliminatedConstructKind, ModuleWalker.NormalizationEliminatedLedgerReason);
                return;
            }

            var scopeChain = walker.CurrentScopeChain();
            if (scopeChain.Count == 0)
            {
                RecordOutsideFromScope(node, [node.Expression, .. node.Values]);
                return;
            }

            if (SkipIfNotSeekable(node))
            {
                return;
            }

            var isNotIn = node.NotDefined ^ _negated;
            if (isNotIn && node.Subquery is not null)
            {
                AnalyzeNotInSubquery(node, scopeChain, walker);
                return;
            }

            _currentPredicateFragment = node;
            if (ResolvePredicateOperand(node.Expression, scopeChain, walker) is not PredicateOperand.Column column)
            {

                return;
            }

            var otherType = node.Subquery is not null
                ? ResolveInSubqueryType(node.Subquery, walker)
                : CombineListElementTypes(node.Values, scopeChain, walker);

            if (otherType is null)
            {
                ledger.Record(AnalysisPass.Predicates, sourcePath, node.StartLine, node.StartColumn, "IN predicate", "list contains a non-literal/unresolvable element, or the subquery's output column type could not be resolved");
                return;
            }

            if (!isNotIn && node.Values is [VariableReference only]
                && ResolvePredicateOperand(only, scopeChain, walker) is { } single)
            {
                TryAddFilteredIndexParameterMismatchFinding(column, single, "IN", node);
            }

            var inOperator = isNotIn ? "NOT IN" : "IN";
            var verdict = VerdictClassifier.Classify(column.Type, otherType, operatorText: inOperator);
            Findings.Add(new TypedPredicateFinding(verdict, column, new PredicateOperand.Value(otherType), inOperator, sourcePath, node.StartLine, node.StartColumn));
        }

        public void OnSubqueryComparisonPredicate(SubqueryComparisonPredicate node, ModuleWalker walker)
        {
            if (walker.IsDeadPredicate(node))
            {
                ledger.Record(AnalysisPass.Predicates, sourcePath, node.StartLine, node.StartColumn, ModuleWalker.NormalizationEliminatedConstructKind, ModuleWalker.NormalizationEliminatedLedgerReason);
                return;
            }

            var scopeChain = walker.CurrentScopeChain();
            if (scopeChain.Count == 0)
            {
                RecordOutsideFromScope(node, node.Expression);
                return;
            }

            if (SkipIfNotSeekable(node))
            {
                return;
            }

            var isAnyEquals = node.SubqueryComparisonPredicateType == SubqueryComparisonPredicateType.Any && node.ComparisonType == BooleanComparisonType.Equals;
            var isAllNotEquals = node.SubqueryComparisonPredicateType == SubqueryComparisonPredicateType.All
                && node.ComparisonType is BooleanComparisonType.NotEqualToBrackets or BooleanComparisonType.NotEqualToExclamation;

            if (_negated || (!isAnyEquals && !isAllNotEquals))
            {
                ledger.Record(AnalysisPass.Predicates, sourcePath, node.StartLine, node.StartColumn, "subquery comparison predicate", $"'{node.ComparisonType} {node.SubqueryComparisonPredicateType}' is not modeled - only '= ANY/SOME' and '<> ALL' are (the IN/NOT IN equivalents)");
                return;
            }

            if (isAllNotEquals)
            {

                ledger.Record(AnalysisPass.Predicates, sourcePath, node.StartLine, node.StartColumn, NonSeekableOperatorConstructKind, "<> ALL is not sargable regardless of type match - not attributed to a type-conversion verdict");
                return;
            }

            _currentPredicateFragment = node;
            if (ResolvePredicateOperand(node.Expression, scopeChain, walker) is not PredicateOperand.Column column)
            {
                return;
            }

            var otherType = ResolveInSubqueryType(node.Subquery, walker);
            if (otherType is null)
            {
                ledger.Record(AnalysisPass.Predicates, sourcePath, node.StartLine, node.StartColumn, "subquery comparison predicate", "the subquery's output column type could not be resolved");
                return;
            }

            var verdict = VerdictClassifier.Classify(column.Type, otherType, operatorText: "IN");
            Findings.Add(new TypedPredicateFinding(verdict, column, new PredicateOperand.Value(otherType), "IN", sourcePath, node.StartLine, node.StartColumn));
        }

        private static string? ToOperatorText(BooleanComparisonType comparisonType) => comparisonType switch
        {
            BooleanComparisonType.Equals => "=",
            BooleanComparisonType.GreaterThan => ">",
            BooleanComparisonType.NotGreaterThan => "!>",
            BooleanComparisonType.LessThan => "<",
            BooleanComparisonType.NotLessThan => "!<",
            BooleanComparisonType.GreaterThanOrEqualTo => ">=",
            BooleanComparisonType.LessThanOrEqualTo => "<=",
            BooleanComparisonType.NotEqualToBrackets => "<>",
            BooleanComparisonType.NotEqualToExclamation => "<>",
            _ => null,
        };

        private static string Negate(string operatorText) => operatorText switch
        {
            "=" => "<>",
            "<>" => "=",
            ">" => "<=",
            "<" => ">=",
            ">=" => "<",
            "<=" => ">",
            "!<" => "<",
            "!>" => ">",
            _ => operatorText,
        };

        private void RecordParameters(IList<ProcedureParameter> parameters)
        {
            foreach (var parameter in parameters)
            {
                _variables[parameter.VariableName.Value] = SqlTypeReferenceResolver.Resolve(parameter.DataType, columnCollation: null, catalog.TypeAliases);
                _formalParameterNames.Add(parameter.VariableName.Value);
            }
        }

        private static ScalarExpression UnwrapParentheses(ScalarExpression expression)
        {
            while (expression is ParenthesisExpression parenthesis)
            {
                expression = parenthesis.Expression;
            }

            return expression;
        }

        private void RecordOutsideFromScope(TSqlFragment node, params ScalarExpression[] operands)
        {
            if (!operands.Any(BaseColumnResolver.ContainsColumnReferenceOutsideSubqueries))
            {
                return;
            }

            ledger.Record(
                AnalysisPass.Predicates, sourcePath, node.StartLine, node.StartColumn, "comparison outside FROM scope",
                "a column reference appears where no FROM scope is in effect - its type cannot be resolved");
        }

        private void TryAddFinding(ScalarExpression first, ScalarExpression second, string operatorText, TSqlFragment node, ModuleWalker walker)
        {
            var scopeChain = walker.CurrentScopeChain();
            if (scopeChain.Count == 0)
            {

                RecordOutsideFromScope(node, first, second);
                return;
            }

            if (SkipIfNotSeekable(node))
            {
                return;
            }

            _currentPredicateFragment = node;
            var left = ResolvePredicateOperand(first, scopeChain, walker);
            var right = ResolvePredicateOperand(second, scopeChain, walker);

            if (left is PredicateOperand.Column leftColumn && right is PredicateOperand.Column rightColumn)
            {

                if (Rules.VerdictClassifier.HasGenuineCollationMismatch(leftColumn.Type, rightColumn.Type))
                {
                    return;
                }

                if (operatorText == "<>")
                {
                    ledger.Record(AnalysisPass.Predicates, sourcePath, node.StartLine, node.StartColumn, NonSeekableOperatorConstructKind, "<> between two columns plans as a scan with a residual filter even when the types match, so a type clash loses no seek - not attributed to a type-conversion verdict");
                    return;
                }

                AddFinding(leftColumn, rightColumn, operatorText, node, walker);
                AddFinding(rightColumn, leftColumn, operatorText, node, walker);
                return;
            }

            PredicateOperand.Column? column;
            PredicateOperand? other;
            if (left is PredicateOperand.Column singleLeftColumn)
            {
                (column, other) = (singleLeftColumn, right);
            }
            else if (right is PredicateOperand.Column singleRightColumn)
            {
                (column, other) = (singleRightColumn, left);
            }
            else
            {
                (column, other) = (null, null);
            }

            if (column is null || other is null)
            {
                RecordNoColumnOperand(first, second, node, scopeChain);
                return;
            }

            AddFinding(column, other, operatorText, node, walker);
        }

        private void RecordNoColumnOperand(ScalarExpression first, ScalarExpression second, TSqlFragment node, ScopeChain scopeChain)
        {
            var columnSides = new[] { first, second }.OfType<ColumnReferenceExpression>().ToList();
            if (columnSides.Count > 0)
            {
                var unresolved = columnSides
                    .Where(c => ScalarExpressionResolver.ResolveColumnReference(c, scopeChain, sourcePath, ledger: null, catalog) is ColumnProvenance.Unknown)
                    .Select(c => string.Join('.', c.MultiPartIdentifier.Identifiers.Select(i => i.Value)))
                    .ToList();
                if (unresolved.Count > 0)
                {
                    ledger.Record(
                        AnalysisPass.Predicates, sourcePath, node.StartLine, node.StartColumn, UnresolvedColumnComparisonConstructKind,
                        $"column reference ({string.Join(", ", unresolved)}) failed to resolve to a real column (most commonly an unresolved FROM-scope alias) - not the benign no-column-operand shape");
                }

                return;
            }

            if (node is BooleanComparisonExpression comparison
                && LiteralComparisonFolder.TryFoldComparison(first, second, comparison.ComparisonType) is { } truth)
            {
                ledger.Record(
                    AnalysisPass.Predicates, sourcePath, node.StartLine, node.StartColumn, FoldableLiteralComparisonConstructKind,
                    $"both sides are literals (optionally with one level of arithmetic) provably {(truth ? "always true" : "always false")} - not an arbitrary unresolvable comparison");
                return;
            }

            var operandsWithColumns = new[] { first, second }.Where(BaseColumnResolver.ContainsColumnReference).ToList();
            if (operandsWithColumns.All(NonSargablePredicateScanner.CoversComparisonSide))
            {
                return;
            }

            ledger.Record(
                AnalysisPass.Predicates, sourcePath, node.StartLine, node.StartColumn, NoColumnOperandConstructKind,
                "neither side of this comparison resolved to a real column - most commonly both sides are expressions (e.g. a column wrapped in COALESCE/CASE/NULLIF/IIF compared to a literal)");
        }

        private void AddFinding(PredicateOperand.Column column, PredicateOperand other, string operatorText, TSqlFragment node, ModuleWalker walker)
        {
            var otherIsLiteral = other is PredicateOperand.Value { IsLiteral: true };
            var otherType = other is PredicateOperand.Value value ? value.Type : ((PredicateOperand.Column)other).Type;
            var (verdict, unknownReason) = VerdictClassifier.ClassifyWithReason(column.Type, otherType, otherIsLiteral, operatorText, otherIsColumn: other is PredicateOperand.Column);

            Findings.Add(new TypedPredicateFinding(
                verdict, column, other, operatorText, sourcePath, node.StartLine, node.StartColumn,
                UnknownReason: unknownReason,
                PredicateFragmentText: _currentPredicateFragment is { } fragment ? Common.FragmentTextRenderer.Render(fragment) : null,
                Fingerprint: TypedPredicateFindingIdentity.ComputeFingerprint(column, other, operatorText)));

            RecordColumnPinnedByValue(column, other, operatorText, node);
            TryAddUnderLengthParameterFinding(column, other, otherIsLiteral, operatorText, node);
            TryAddAnsiPaddingMismatchFinding(column, other, operatorText, node, walker);
            TryAddLocalVariablePredicateFinding(column, other, operatorText, node);
            TryAddFilteredIndexParameterMismatchFinding(column, other, operatorText, node);
        }

        private void RecordColumnPinnedByValue(PredicateOperand.Column column, PredicateOperand other, string operatorText, TSqlFragment node)
        {
            if (operatorText == "="
                && other is PredicateOperand.Value
                && node is BooleanExpression comparison
                && _whereEqualityScopes.TryPeek(out var scope)
                && scope.TopLevelConjuncts.Contains(comparison))
            {
                scope.PinnedByValue.Add((column.TableQualifiedName, column.ColumnName));
            }
        }

        private void TryAddLocalVariablePredicateFinding(PredicateOperand.Column column, PredicateOperand other, string operatorText, TSqlFragment node)
        {
            if (HasActiveRecompileGuard
                || column.TableQualifiedName.StartsWith('@')
                || other is not PredicateOperand.Value { VariableName: { } variableName, IsFormalParameter: false }
                || _statementOptimizeForVariables.Contains(variableName)
                || IsUniqueSingleColumnEquality(column, operatorText))
            {
                return;
            }

            var finding = new LocalVariablePredicateFinding(
                column.TableQualifiedName, column.ColumnName, column.Indexed, column.Depth,
                variableName, operatorText, sourcePath, node.StartLine, node.StartColumn);
            LocalVariablePredicateFindings.Add(finding);

            if (operatorText == "="
                && node is BooleanExpression comparison
                && _whereEqualityScopes.TryPeek(out var scope)
                && scope.TopLevelConjuncts.Contains(comparison))
            {
                scope.Equalities.Add((finding, column.TableQualifiedName, column.ColumnName));
            }
        }

        private bool IsUniqueSingleColumnEquality(PredicateOperand.Column column, string operatorText) =>
            operatorText == "="
            && catalog.Find(column.TableQualifiedName) is { } table
            && table.Indexes.Any(index =>
                index.IsUnique
                && !index.IsFiltered
                && !index.IsDisabled
                && index.KeyColumns.Count == 1
                && catalog.IdentifierComparer.Equals(index.KeyColumns[0], column.ColumnName));

        private bool IsPinnedToFilterLiteral(TSqlFragment parameterPredicate, string literalText)
        {
            var columnPath = parameterPredicate switch
            {
                BooleanComparisonExpression comparison => ColumnPath(comparison.FirstExpression, comparison.SecondExpression),
                InPredicate inList => ColumnPath(inList.Expression, inList.Expression),
                _ => null,
            };

            return columnPath is not null
                && _whereEqualityScopes.TryPeek(out var scope)
                && scope.TopLevelConjuncts.OfType<BooleanComparisonExpression>().Any(conjunct =>
                    conjunct.ComparisonType == BooleanComparisonType.Equals
                    && string.Equals(ColumnPath(conjunct.FirstExpression, conjunct.SecondExpression), columnPath, StringComparison.OrdinalIgnoreCase)
                    && (conjunct.FirstExpression as Literal ?? conjunct.SecondExpression as Literal) is { } literal
                    && Rules.LiteralTextRenderer.Render(literal) == literalText);
        }

        private static string? ColumnPath(ScalarExpression first, ScalarExpression second) =>
            (first as ColumnReferenceExpression ?? second as ColumnReferenceExpression)?.MultiPartIdentifier is { } name
                ? string.Join('.', name.Identifiers.Select(identifier => identifier.Value))
                : null;

        private void TryAddFilteredIndexParameterMismatchFinding(PredicateOperand.Column column, PredicateOperand other, string operatorText, TSqlFragment node)
        {
            if (HasActiveRecompileGuard
                || other is not PredicateOperand.Value { VariableName: { } variableName, IsFormalParameter: var isFormalParameter }
                || !LiteralEqualityFilteredIndexesByColumn.TryGetValue((column.TableQualifiedName, column.ColumnName), out var candidates))
            {
                return;
            }

            foreach (var (indexName, literalText) in candidates.Where(c => !IsPinnedToFilterLiteral(node, c.LiteralText)))
            {
                FilteredIndexParameterMismatchFindings.Add(new FilteredIndexParameterMismatchFinding(
                    column.TableQualifiedName, column.ColumnName, indexName, literalText,
                    variableName, isFormalParameter, operatorText, sourcePath, node.StartLine, node.StartColumn));
            }
        }

        private void TryAddUnderLengthParameterFinding(
            PredicateOperand.Column column, PredicateOperand other, bool otherIsLiteral, string operatorText, TSqlFragment node)
        {
            if (otherIsLiteral || other is not PredicateOperand.Value { Type: { } otherType, IsDerivedType: false } value
                || Rules.ParameterLengthClassifier.ClassifyUnderLength(column.Type, otherType) is not { } result)
            {
                return;
            }

            var changesRangeOrPatternShape = Rules.ParameterLengthClassifier.ChangesRangeOrPatternShape(operatorText);

            var finding = new UnderLengthParameterFinding(
                column.TableQualifiedName, column.ColumnName, result.ColumnLength, result.OtherLength, result.IsImplicitDefault,
                operatorText, changesRangeOrPatternShape, sourcePath, node.StartLine, node.StartColumn,
                VariableName: value.VariableName);
            var sources = value.IsFormalParameter ? null : _variableSources;
            _underLengthCandidates.Add((finding, sources, otherType));
        }

        private bool EverySourceFits(UnderLengthParameterFinding finding, VariableSourceCollector? sources, SqlType declaredType)
        {
            if (sources is null
                || finding.VariableName is not { } name
                || !sources.TryGetSources(name, out var longestLiteral, out var columns))
            {
                return false;
            }

            var declaredLength = declaredType.Length ?? 1;
            return longestLiteral <= declaredLength
                && columns.All(c => _assignedColumnTypes.GetValueOrDefault(c) is { IsMax: false, Length: { } length } type
                    && type.Category == declaredType.Category
                    && length <= declaredLength);
        }

        private void TryAddAnsiPaddingMismatchFinding(PredicateOperand.Column column, PredicateOperand other, string operatorText, TSqlFragment node, ModuleWalker walker)
        {
            if (operatorText != "LIKE"
                || column.Type is not { Category: SqlTypeCategory.VarChar or SqlTypeCategory.Char }
                || other is not PredicateOperand.Value { IsLiteral: true, LiteralText: { } literalText }
                || !LiteralEndsWithSignificantWhitespace(literalText))
            {
                return;
            }

            var catalogColumn = catalog.Find(column.TableQualifiedName, walker.CurrentProcScope)?.FindColumn(column.ColumnName, catalog.IdentifierComparer);
            if (catalogColumn is not { IsAnsiPadded: false }
                || column.Type.Category == SqlTypeCategory.Char && !catalogColumn.IsNullable)
            {
                return;
            }

            AnsiPaddingMismatchFindings.Add(new AnsiPaddingMismatchFinding(
                column.TableQualifiedName, column.ColumnName, literalText, sourcePath, node.StartLine, node.StartColumn));
        }

        private static bool LiteralEndsWithSignificantWhitespace(string literalText)
        {
            var firstQuote = literalText.IndexOf('\'');
            var lastQuote = literalText.LastIndexOf('\'');
            if (firstQuote < 0 || lastQuote <= firstQuote)
            {
                return false;
            }

            var content = literalText[(firstQuote + 1)..lastQuote];
            return content.Length > 0 && content[^1] == ' ';
        }

        private PredicateOperand ResolvePredicateOperand(ScalarExpression expression, ScopeChain scopeChain, ModuleWalker walker)
        {
            expression = UnwrapParentheses(expression);

            var previous = _directPredicateOperand;
            _directPredicateOperand = expression as ColumnReferenceExpression;
            try
            {
                return ResolveOperand(expression, scopeChain, walker);
            }
            finally
            {
                _directPredicateOperand = previous;
            }
        }

        private PredicateOperand ResolveOperand(ScalarExpression expression, ScopeChain scopeChain, ModuleWalker walker)
        {
            switch (expression)
            {
                case ColumnReferenceExpression columnRef:
                    return ResolveColumnOperand(columnRef, scopeChain, walker);

                case VariableReference variableRef:
                    return new PredicateOperand.Value(
                        _variables.GetValueOrDefault(variableRef.Name), VariableName: variableRef.Name,
                        IsFormalParameter: _formalParameterNames.Contains(variableRef.Name));

                case Literal literal:
                    return new PredicateOperand.Value(TypeInference.LiteralTypeResolver.Resolve(literal), IsLiteral: true, Rules.LiteralTextRenderer.Render(literal));

                case GlobalVariableExpression globalVariable:
                    return ResolveGlobalVariableOperand(globalVariable) with { IsDerivedType = true };

                case ParameterlessCall parameterless
                    when TypeInference.BuiltinFunctionTypeResolver.ResolveParameterlessCall(parameterless.ParameterlessCallType) is { } parameterlessType:
                    return new PredicateOperand.Value(parameterlessType, IsDerivedType: true);

                case FunctionCall functionCall:
                    return ResolveFunctionCallOperand(functionCall, scopeChain, walker) with { IsDerivedType = true };

                case CastCall castCall:
                    return ResolveCastOrConvertOperand(castCall.DataType, castCall.Parameter, scopeChain, walker);

                case ConvertCall convertCall:
                    return ResolveCastOrConvertOperand(convertCall.DataType, convertCall.Parameter, scopeChain, walker);

                case TryCastCall tryCast:
                    return ResolveCastOrConvertOperand(tryCast.DataType, tryCast.Parameter, scopeChain, walker);

                case TryConvertCall tryConvert:
                    return ResolveCastOrConvertOperand(tryConvert.DataType, tryConvert.Parameter, scopeChain, walker);

                case ScalarSubquery scalarSubquery:
                    return new PredicateOperand.Value(ResolveInSubqueryType(scalarSubquery, walker), IsDerivedType: true);

                case ParenthesisExpression or UnaryExpression or BinaryExpression or LeftFunctionCall or RightFunctionCall
                    or CoalesceExpression or NullIfExpression or IIfCall
                    or SearchedCaseExpression or SimpleCaseExpression:
                    return new PredicateOperand.Value(
                        ExpressionTypeInferencer.Resolve(expression, e => OperandType(ResolveOperand(e, scopeChain, walker)), catalog.TypeAliases),
                        IsDerivedType: true);

                default:

                    ledger.Record(
                        AnalysisPass.Predicates, sourcePath, expression.StartLine, expression.StartColumn,
                        PredicateOperandConstructKind, $"operand of kind '{expression.GetType().Name}' has no type resolution - resolved Unknown");
                    return new PredicateOperand.Value(Type: null);
            }
        }

        private static SqlType? OperandType(PredicateOperand operand) => operand switch
        {
            PredicateOperand.Value v => v.Type,
            PredicateOperand.Column c => c.Type,
            _ => null,
        };

        private PredicateOperand.Value ResolveGlobalVariableOperand(GlobalVariableExpression globalVariable)
        {
            var type = TypeInference.BuiltinFunctionTypeResolver.ResolveGlobalVariable(globalVariable.Name);
            if (type is null)
            {
                ledger.Record(
                    AnalysisPass.Predicates, sourcePath, globalVariable.StartLine, globalVariable.StartColumn,
                    PredicateOperandConstructKind, $"global variable '{globalVariable.Name}' has no type resolution - resolved Unknown");
            }

            return new PredicateOperand.Value(type);
        }

        private PredicateOperand.Value ResolveFunctionCallOperand(FunctionCall functionCall, ScopeChain scopeChain, ModuleWalker walker)
        {
            var name = functionCall.FunctionName.Value;

            if (ScalarExpressionResolver.TryResolveMethodCall(
                    functionCall, scopeChain, sourcePath,
                    new ScalarExpressionResolver.ScalarTypeContext(Ledger: null, catalog.TypeAliases, catalog, _variables)) is { } methodCall)
            {
                if (methodCall.ResultType is null)
                {
                    ledger.Record(
                        AnalysisPass.Predicates, sourcePath, functionCall.StartLine, functionCall.StartColumn,
                        PredicateOperandConstructKind,
                        $"method '{name}' on a value of type '{methodCall.TargetType?.ToString() ?? "unresolved"}' has no return-type model - resolved Unknown");
                }

                return new PredicateOperand.Value(methodCall.ResultType);
            }

            if (TypeInference.BuiltinFunctionTypeResolver.TryGetArgumentTypeIndex(name) is { } argumentIndex && functionCall.Parameters.Count > argumentIndex)
            {
                var argumentType = OperandType(ResolveOperand(functionCall.Parameters[argumentIndex], scopeChain, walker));
                argumentType = TypeInference.BuiltinFunctionTypeResolver.AdjustArgumentTypeFunctionResult(name, argumentType);
                return new PredicateOperand.Value(argumentType);
            }

            if (TypeInference.BuiltinFunctionTypeResolver.TryGetMaxWidenedArgumentIndex(name) is { } maxWidenedIndex)
            {
                var decidingType = functionCall.Parameters.Count > maxWidenedIndex
                    ? OperandType(ResolveOperand(functionCall.Parameters[maxWidenedIndex], scopeChain, walker))
                    : null;
                return new PredicateOperand.Value(TypeInference.BuiltinFunctionTypeResolver.ResolveMaxWidenedReturnType(decidingType));
            }

            if (TypeInference.ArgumentShapedFunctionTypes.IsArgumentShaped(name) && functionCall.Parameters.Count > 0)
            {
                var shapingType = OperandType(ResolveOperand(functionCall.Parameters[0], scopeChain, walker));
                return new PredicateOperand.Value(TypeInference.ArgumentShapedFunctionTypes.Resolve(name, shapingType));
            }

            var fixedType = TypeInference.BuiltinFunctionTypeResolver.ResolveFixedReturnType(name);
            if (fixedType is not null)
            {
                return new PredicateOperand.Value(fixedType);
            }

            var qualifiedName = SchemaObjectNameHelper.QualifyFunctionCall(functionCall);
            if (catalog.TryGetScalarFunctionReturnType(qualifiedName, out var udfType))
            {
                if (udfType is null)
                {
                    ledger.Record(
                        AnalysisPass.Predicates, sourcePath, functionCall.StartLine, functionCall.StartColumn,
                        PredicateOperandConstructKind, $"function '{qualifiedName}' RETURNS type could not be resolved - resolved Unknown");
                }

                return new PredicateOperand.Value(udfType);
            }

            ledger.Record(
                AnalysisPass.Predicates, sourcePath, functionCall.StartLine, functionCall.StartColumn,
                PredicateOperandConstructKind, $"function '{name}' has no return-type resolution - resolved Unknown");

            return new PredicateOperand.Value(Type: null);
        }

        private PredicateOperand.Value ResolveCastOrConvertOperand(
            DataTypeReference dataType, ScalarExpression parameter, ScopeChain scopeChain, ModuleWalker walker)
        {

            var type = Parsing.SqlTypeReferenceResolver.Resolve(dataType, columnCollation: null, catalog.TypeAliases, unsizedStringOrBinaryDefaultLength: 30);
            if (type is null)
            {
                ledger.Record(
                    AnalysisPass.Predicates, sourcePath, parameter.StartLine, parameter.StartColumn,
                    PredicateOperandConstructKind, "CAST/CONVERT target type could not be resolved - resolved Unknown");
                return new PredicateOperand.Value(Type: null);
            }

            if (type.IsStringFamily)
            {
                var innerType = OperandType(ResolveOperand(parameter, scopeChain, walker));

                if (innerType is { IsStringFamily: true, Collation: { } innerCollation })
                {
                    type = type with { Collation = innerCollation };
                }
            }

            return new PredicateOperand.Value(type);
        }

        private SqlType? CombineListElementTypes(IList<ScalarExpression> values, ScopeChain scopeChain, ModuleWalker walker)
        {
            SqlType? best = null;
            foreach (var value in values)
            {
                var type = OperandType(ResolveOperand(value, scopeChain, walker));

                if (type is null)
                {
                    return null;
                }

                if (best is null || type.Category > best.Category)
                {
                    best = type;
                }
            }

            return best;
        }

        private void AnalyzeNotInSubquery(InPredicate node, ScopeChain scopeChain, ModuleWalker walker)
        {
            _currentPredicateFragment = node;
            var outerType = OperandType(ResolvePredicateOperand(node.Expression, scopeChain, walker));
            var columns = QueryExpressionResolver.Resolve(
                node.Subquery.QueryExpression, catalog, resolvedViews, sourcePath, ledger, new QueryResolutionScope(walker.CurrentCteRelations(), walker.CurrentProcScope));

            if (outerType is null || columns.Count != 1 || columns[0].Provenance is not ColumnProvenance.BaseColumn baseColumn)
            {
                ledger.Record(
                    AnalysisPass.Predicates, sourcePath, node.StartLine, node.StartColumn, NonSeekableOperatorConstructKind,
                    "NOT IN (subquery) is judged on the subquery's own base column, and either that column or the outer operand type could not be resolved");
                return;
            }

            var tableEntry = catalog.Find(baseColumn.TableQualifiedName, walker.CurrentProcScope);
            var matchedIndex = tableEntry?.FindIndexedColumn(baseColumn.ColumnName, catalog.IdentifierComparer);
            var innerColumn = new PredicateOperand.Column(
                baseColumn.TableQualifiedName, baseColumn.ColumnName, baseColumn.Type, tableEntry is null ? null : matchedIndex is not null,
                baseColumn.Depth, baseColumn, IndexName: matchedIndex?.Name);

            var verdict = VerdictClassifier.Classify(innerColumn.Type, outerType, operatorText: "NOT IN");
            Findings.Add(new TypedPredicateFinding(verdict, innerColumn, new PredicateOperand.Value(outerType), "NOT IN", sourcePath, node.StartLine, node.StartColumn));
        }

        private SqlType? ResolveInSubqueryType(ScalarSubquery subquery, ModuleWalker walker)
        {
            var innerCtes = walker.CurrentCteRelations();
            var columns = QueryExpressionResolver.Resolve(subquery.QueryExpression, catalog, resolvedViews, sourcePath, ledger, new QueryResolutionScope(innerCtes, walker.CurrentProcScope));
            if (columns.Count != 1)
            {

                return null;
            }

            return ColumnProvenanceAnalysis.TryGetScalarType(columns[0].Provenance);
        }

        private PredicateOperand ResolveColumnOperand(ColumnReferenceExpression columnRef, ScopeChain scopeChain, ModuleWalker walker)
        {

            var provenance = ScalarExpressionResolver.ResolveColumnReference(columnRef, scopeChain, sourcePath, ledger, catalog);
            var columnName = columnRef.MultiPartIdentifier.Identifiers[^1].Value;

            if (provenance is ColumnProvenance.BaseColumn baseColumn)
            {

                var tableEntry = catalog.Find(baseColumn.TableQualifiedName, walker.CurrentProcScope);
                var matchedIndex = tableEntry?.FindIndexedColumn(baseColumn.ColumnName, catalog.IdentifierComparer);

                bool? indexed = tableEntry is null ? null : matchedIndex is not null;
                var immediateRelation = ScalarExpressionResolver.TryResolveImmediateRelation(columnRef, scopeChain, catalog);
                return new PredicateOperand.Column(
                    baseColumn.TableQualifiedName, baseColumn.ColumnName, baseColumn.Type, indexed, baseColumn.Depth, baseColumn,
                    immediateRelation?.RelationQualifiedName, immediateRelation?.ExposedColumnName, matchedIndex?.Name);
            }

            if (provenance is ColumnProvenance.Declared declared)
            {

                return new PredicateOperand.Column(declared.TableQualifiedName ?? "?", columnName, declared.Type, Indexed: false, declared.Depth, declared);
            }

            if (ColumnProvenanceAnalysis.IsExpressionDerivedBeyondConstants(provenance))
            {
                if (ReferenceEquals(columnRef, _directPredicateOperand))
                {
                    RecordExpressionDerivedFinding(columnName, columnRef, provenance, scopeChain, walker);
                }
            }
            else if (provenance is ColumnProvenance.Union union)
            {

                var agreedType = ColumnProvenanceAnalysis.TryGetScalarType(union);
                if (agreedType is not null)
                {
                    return new PredicateOperand.Column("?", columnName, agreedType, Indexed: false, Depth: 0, union);
                }

                ledger.Record(
                    AnalysisPass.Predicates, sourcePath, columnRef.StartLine, columnRef.StartColumn,
                    PredicateOperandConstructKind, $"column '{columnName}' resolves through a UNION view whose branches disagree on type - not eligible for a single verdict, never guessed");
            }

            return new PredicateOperand.Value(Type: null);
        }

        private void RecordExpressionDerivedFinding(
            string columnName, ColumnReferenceExpression columnRef, ColumnProvenance provenance, ScopeChain scopeChain, ModuleWalker walker)
        {
            if (provenance is ColumnProvenance.Cast { ExplicitType: { } explicitType, Inner: ColumnProvenance.BaseColumn baseColumn }
                && ComputedColumnMatcher.HasIndexedMatchingCastComputedColumn(catalog, baseColumn.TableQualifiedName, baseColumn.ColumnName, explicitType))
            {
                return;
            }

            if (provenance is ColumnProvenance.Cast { ExplicitType: { } castType, Inner: ColumnProvenance.BaseColumn { Type: { } sourceType } source } cast
                && NonSargablePredicateScanner.CastKeepsSeek(
                    castType,
                    sourceType,
                    new NonSargablePredicateScanner.CastSite(
                        catalog.Find(source.TableQualifiedName, walker.CurrentProcScope)?.FindColumn(source.ColumnName, catalog.IdentifierComparer) is not { IsNullable: true },
                        cast.HasStyle,
                        cast.ExplicitCollation)))
            {
                return;
            }

            var underlyingBaseColumns = ColumnProvenanceAnalysis.FindUnderlyingBaseColumns(provenance)
                .Select(bc => new UnderlyingBaseColumn(bc.TableQualifiedName, bc.ColumnName, catalog.Find(bc.TableQualifiedName, walker.CurrentProcScope)?.IsIndexedColumn(bc.ColumnName, catalog.IdentifierComparer) ?? false))
                .ToList();

            if (underlyingBaseColumns.Count == 0)
            {

                ledger.Record(
                    AnalysisPass.Predicates, sourcePath, columnRef.StartLine, columnRef.StartColumn,
                    "expression-derived predicate", $"'{columnName}' is expression-derived but no underlying base column could be traced (e.g. ROW_NUMBER(), a derived-table alias over another expression, an XML shred) - nothing actionable to report");
                return;
            }

            var transformationChain = ColumnProvenanceAnalysis.DescribeTransformationChain(provenance);

            var immediateRelation = ScalarExpressionResolver.TryResolveImmediateRelation(columnRef, scopeChain, catalog);
            var identifiers = columnRef.MultiPartIdentifier.Identifiers;
            var alias = identifiers.Count >= 2 ? identifiers[^2].Value : null;

            ExpressionDerivedFindings.Add(new ExpressionDerivedFinding(
                columnName, sourcePath, columnRef.StartLine, columnRef.StartColumn, transformationChain, underlyingBaseColumns,
                PredicateFragmentText: _currentPredicateFragment is { } fragment ? Common.FragmentTextRenderer.Render(fragment) : null,
                ImmediateRelationQualifiedName: immediateRelation?.RelationQualifiedName,
                ImmediateRelationAlias: immediateRelation is not null ? alias : null,
                Confidence: underlyingBaseColumns.Any(c => c.Indexed) ? FindingConfidence.High : FindingConfidence.Medium));
        }
    }
}
