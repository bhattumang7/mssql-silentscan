using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Catalog;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates.Normalization;
using SilentScan.Core.Common;

namespace SilentScan.Core.Predicates;

public static class QueryAntiPatternScanner
{
    private static readonly HashSet<string> CountStarFunctionNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "COUNT", "COUNT_BIG",
    };

    public static IReadOnlyList<QueryAntiPatternFinding> Scan(SqlParseResult parseResult, DatabaseCatalog catalog)
    {
        var rule = CreateRule(parseResult, catalog);
        var walker = new ModuleWalker(parseResult.SourcePath, catalog, EmptyResolvedViews, rules: [rule]);
        parseResult.Fragment.Accept(walker);
        return Harvest(rule);
    }

    internal static Rule CreateRule(SqlParseResult parseResult, DatabaseCatalog catalog)
    {
        var cteNameCollector = new Rule.CteNameCollector(catalog.IdentifierComparer);
        parseResult.Fragment.Accept(cteNameCollector);
        var estimateSensitiveFromClauses = new EstimateSensitiveFromClauseCollector();
        parseResult.Fragment.Accept(estimateSensitiveFromClauses);
        var moduleDetector = new Rule.ModuleBodyDetector();
        parseResult.Fragment.Accept(moduleDetector);
        return new Rule(parseResult.SourcePath, catalog, cteNameCollector.Names, estimateSensitiveFromClauses.FromClauses, moduleDetector.Found);
    }

    internal static IReadOnlyList<QueryAntiPatternFinding> Harvest(Rule rule) =>
            [
            .. rule.Findings
                .OrderBy(f => f.Kind)
                .ThenBy(f => f.SourcePath, StringComparer.Ordinal)
                .ThenBy(f => f.Line)
                .ThenBy(f => f.Column),
        ];


    private static readonly IReadOnlyDictionary<string, ResolvedRelation> EmptyResolvedViews = new Dictionary<string, ResolvedRelation>();

    internal sealed class Rule(
        string sourcePath, DatabaseCatalog catalog, HashSet<string> cteNames, HashSet<FromClause> estimateSensitiveFromClauses, bool insideModule) : IModuleRule
    {
        public List<QueryAntiPatternFinding> Findings { get; } = [];

        private TSqlBatch? _currentBatch;
        private readonly HashSet<string> _tableVariableNames = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _tableValuedParameterNames = new(StringComparer.OrdinalIgnoreCase);

        private static string? AliasOf(TableReference reference) =>
            reference is NamedTableReference named ? named.Alias?.Value ?? named.SchemaObject.BaseIdentifier.Value : null;

        public void OnEnterProcedureOrFunctionBody(ProcedureStatementBodyBase node, ModuleWalker walker) =>
            InspectTableValuedParameters(node.Parameters);

        public void OnEnterDeclareTableVariableStatement(DeclareTableVariableStatement node, ModuleWalker walker) =>
            _tableVariableNames.Add(node.Body.VariableName.Value);

        private readonly Stack<int?> _hintedCompatibilityLevels = new();

        private int? StatementCompatibilityLevel =>
            (_hintedCompatibilityLevels.TryPeek(out var hinted) ? hinted : null) ?? catalog.CompatibilityLevel;

        private bool TableVariableEstimateIsFixed(int? level) =>
            level is < 150 || level is not null && catalog.IsScopedConfigurationDisabled("DEFERRED_COMPILATION_TV");

        private static int? HintedCompatibilityLevel(StatementWithCtesAndXmlNamespaces statement)
        {
            const string Prefix = "QUERY_OPTIMIZER_COMPATIBILITY_LEVEL_";
            return statement.OptimizerHints.OfType<UseHintList>()
                .SelectMany(list => list.Hints)
                .Select(hint => hint.Value)
                .Where(value => value.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
                .Select(value => int.TryParse(value.AsSpan(Prefix.Length), out var level) ? level : (int?)null)
                .FirstOrDefault(level => level is not null);
        }

        public void OnEnterFromClause(FromClause node, ModuleWalker walker)
        {
            foreach (var tableReference in node.TableReferences)
            {
                foreach (var variableRef in CollectVariableTableReferences(tableReference))
                {
                    InspectLowCompatEstimate(node, variableRef);
                    InspectTableValuedParameterPspSkip(variableRef);
                }

                foreach (var named in CollectNamedTableReferences(tableReference))
                {
                    InspectUnqualifiedReference(named);
                }
            }
        }

        private void InspectLowCompatEstimate(FromClause node, VariableTableReference variableRef)
        {
            if (_recompileStatementDepth != 0
                || !estimateSensitiveFromClauses.Contains(node)
                || !_tableVariableNames.Contains(variableRef.Variable.Name)
                || StatementCompatibilityLevel is not { } level
                || !TableVariableEstimateIsFixed(level))
            {
                return;
            }

            var reason = level < 150
                ? $"connected compatibility level {level}, below 150"
                : "deferred compilation of table variables is switched off for the database";
            Findings.Add(new QueryAntiPatternFinding(
                QueryAntiPatternFindingKind.TableVariableLowCompatEstimate, sourcePath,
                variableRef.StartLine, variableRef.StartColumn,
                $"{variableRef.Variable.Name} ({reason})",
                FindingConfidence.High));
        }

        private void InspectTableValuedParameterPspSkip(VariableTableReference variableRef)
        {
            if (!_tableValuedParameterNames.Contains(variableRef.Variable.Name)
                || catalog.CompatibilityLevel is not >= 170
                || catalog.IsScopedConfigurationDisabled("PARAMETER_SENSITIVE_PLAN_OPTIMIZATION"))
            {
                return;
            }

            Findings.Add(new QueryAntiPatternFinding(
                QueryAntiPatternFindingKind.TableVariablePspSkip, sourcePath,
                variableRef.StartLine, variableRef.StartColumn,
                variableRef.Variable.Name,
                FindingConfidence.High));
        }

        public void OnEnterSelectStatementScope(SelectStatement node, ModuleWalker walker) =>
            EnterStatementHints(node);

        public void OnLeaveSelectStatementScope(SelectStatement node, ModuleWalker walker) =>
            LeaveStatementHints(node);

        public void OnLeaveInsertStatementScope(InsertStatement node, ModuleWalker walker) =>
            LeaveStatementHints(node);

        public void OnLeaveUpdateStatementScope(UpdateStatement node, ScopeChain scopeChain, ModuleWalker walker) =>
            LeaveStatementHints(node);

        public void OnLeaveDeleteStatementScope(DeleteStatement node, ScopeChain scopeChain, ModuleWalker walker) =>
            LeaveStatementHints(node);

        public void OnLeaveMergeStatementScope(MergeStatement node, ScopeChain scopeChain, ModuleWalker walker) =>
            LeaveStatementHints(node);

        private int _recompileStatementDepth;

        private static bool HasOptionRecompile(StatementWithCtesAndXmlNamespaces statement) =>
            statement.OptimizerHints.Any(h => h.HintKind == OptimizerHintKind.Recompile);

        private void EnterStatementHints(StatementWithCtesAndXmlNamespaces statement)
        {
            _hintedCompatibilityLevels.Push(HintedCompatibilityLevel(statement));
            if (HasOptionRecompile(statement))
            {
                _recompileStatementDepth++;
            }
        }

        private void LeaveStatementHints(StatementWithCtesAndXmlNamespaces statement)
        {
            _hintedCompatibilityLevels.Pop();
            if (HasOptionRecompile(statement))
            {
                _recompileStatementDepth--;
            }
        }

        public void OnEnterInsertStatementScope(InsertStatement node, ModuleWalker walker)
        {
            EnterStatementHints(node);
            InspectSiteIfNamedTable(node.InsertSpecification.Target);
        }

        public void OnEnterUpdateStatementScope(UpdateStatement node, ScopeChain scopeChain, ModuleWalker walker)
        {
            EnterStatementHints(node);
            InspectSiteIfNamedTable(node.UpdateSpecification.Target);
        }

        public void OnEnterDeleteStatementScope(DeleteStatement node, ScopeChain scopeChain, ModuleWalker walker)
        {
            EnterStatementHints(node);
            InspectSiteIfNamedTable(node.DeleteSpecification.Target);
        }

        public void OnEnterMergeStatementScope(MergeStatement node, ScopeChain scopeChain, ModuleWalker walker)
        {
            EnterStatementHints(node);
            InspectSiteIfNamedTable(node.MergeSpecification.Target);
            InspectSiteIfNamedTable(node.MergeSpecification.TableReference);
            InspectMergeHazards(node.MergeSpecification);
        }

        public void OnEnterWhileStatement(WhileStatement node, ModuleWalker walker)
        {
            InspectStaleTableVariableInLoop(node);

            InspectRbarSingleRowLoopDml(node);
        }

        public void OnEnterStatementList(StatementList node, ModuleWalker walker)
        {
            InspectCountStarExistenceSequence(node.Statements);
            InspectMultiRowInsertIgnoreDupKeySequence(node.Statements);
        }

        public void OnEnterTSqlBatch(TSqlBatch node, ModuleWalker walker)
        {
            _currentBatch = node;
            InspectCountStarExistenceSequence(node.Statements);
            InspectMultiRowInsertIgnoreDupKeySequence(node.Statements);
            _tableVariableNames.Clear();
            _tableValuedParameterNames.Clear();
        }

        public void OnEnterQuerySpecificationScope(QuerySpecification node, ScopeChain scopeChain, ModuleWalker walker)
        {
            var byAlias = scopeChain[0].ByAlias;

            InspectDistinctJoinFanout(node, byAlias, scopeChain, walker);
        }

        private void InspectTableValuedParameters(IList<ProcedureParameter> parameters)
        {
            foreach (var parameter in parameters)
            {
                if (parameter.DataType is not UserDataTypeReference userType
                    || catalog.Find(SchemaObjectNameHelper.Qualify(userType.Name)) is not { Kind: CatalogTableKind.TableType })
                {
                    continue;
                }

                _tableValuedParameterNames.Add(parameter.VariableName.Value);
            }
        }

        internal sealed class ModuleBodyDetector : TSqlFragmentVisitor
        {
            public bool Found { get; private set; }

            public override void Visit(ProcedureStatementBodyBase node) => Found = true;

            public override void Visit(ViewStatementBody node) => Found = true;

            public override void Visit(TriggerStatementBody node) => Found = true;
        }

        internal sealed class CteNameCollector(StringComparer identifierComparer) : TSqlFragmentVisitor
        {
            public HashSet<string> Names { get; } = new(identifierComparer);

            public override void ExplicitVisit(CommonTableExpression node)
            {
                Names.Add(node.ExpressionName.Value);
                base.ExplicitVisit(node);
            }
        }

        private void InspectSiteIfNamedTable(TableReference? tableReference)
        {
            if (tableReference is NamedTableReference named)
            {
                InspectUnqualifiedReference(named);
            }
        }

        private void InspectUnqualifiedReference(NamedTableReference named)
        {
            if (insideModule
                || named.SchemaObject.SchemaIdentifier is not null
                || named.SchemaObject.BaseIdentifier.Value.StartsWith('#')
                || cteNames.Contains(named.SchemaObject.BaseIdentifier.Value))
            {
                return;
            }

            var qualifiedName = catalog.ResolveSynonymName(SchemaObjectNameHelper.Qualify(named.SchemaObject));
            var resolved = catalog.Find(qualifiedName);
            if (resolved is not { Kind: CatalogTableKind.Table })
            {
                return;
            }

            Findings.Add(new QueryAntiPatternFinding(
                QueryAntiPatternFindingKind.UnqualifiedTableReference, sourcePath,
                named.StartLine, named.StartColumn,
                $"'{named.SchemaObject.BaseIdentifier.Value}' resolves to '{qualifiedName}' with no explicit schema qualifier at this reference.",
                FindingConfidence.Medium));
        }

        private void InspectMultiRowInsertIgnoreDupKeySequence(IList<TSqlStatement> statements)
        {
            for (var i = 0; i < statements.Count; i++)
            {
                if (statements[i] is not InsertStatement node
                    || node.InsertSpecification.InsertSource is not ValuesInsertSource { RowValues.Count: > 1 }
                    || node.InsertSpecification.Target is not NamedTableReference named)
                {
                    continue;
                }

                var qualifiedName = catalog.ResolveSynonymName(SchemaObjectNameHelper.Qualify(named.SchemaObject));
                var resolved = catalog.Find(qualifiedName);
                if (resolved is not { Kind: CatalogTableKind.Table })
                {
                    continue;
                }

                var hazardIndex = resolved.Indexes.FirstOrDefault(ix => ix.IsUnique && ix.IgnoreDupKey && !ix.IsDisabled);
                if (hazardIndex is null)
                {
                    continue;
                }

                if (i + 1 < statements.Count && NextStatementGuardsRowCount(statements[i + 1]))
                {
                    continue;
                }

                Findings.Add(new QueryAntiPatternFinding(
                    QueryAntiPatternFindingKind.MultiRowInsertIgnoreDupKeyDrop, sourcePath,
                    node.StartLine, node.StartColumn,
                    $"Multi-row INSERT into '{qualifiedName}' - unique index '{hazardIndex.Name}' has IGNORE_DUP_KEY=ON, so a row whose key duplicates an existing (or an earlier row in this same batch's) value is silently skipped instead of raising an error.",
                    FindingConfidence.High,
                    TableQualifiedName: qualifiedName,
                    IndexName: hazardIndex.Name));
            }
        }

        private static bool NextStatementGuardsRowCount(TSqlStatement statement)
        {
            var collector = new RowCountReferenceCollector();
            statement.Accept(collector);
            return collector.Found;
        }

        private sealed class RowCountReferenceCollector : TSqlFragmentVisitor
        {
            public bool Found { get; private set; }

            public override void ExplicitVisit(GlobalVariableExpression node)
            {
                if (string.Equals(node.Name, "@@ROWCOUNT", StringComparison.OrdinalIgnoreCase))
                {
                    Found = true;
                }

                base.ExplicitVisit(node);
            }
        }

        private static IEnumerable<NamedTableReference> CollectNamedTableReferences(TableReference tableReference)
        {
            switch (tableReference)
            {
                case NamedTableReference named:
                    yield return named;
                    break;

                case QualifiedJoin join:
                    foreach (var t in CollectNamedTableReferences(join.FirstTableReference))
                    {
                        yield return t;
                    }

                    foreach (var t in CollectNamedTableReferences(join.SecondTableReference))
                    {
                        yield return t;
                    }

                    break;

                case UnqualifiedJoin unqualified:
                    foreach (var t in CollectNamedTableReferences(unqualified.FirstTableReference))
                    {
                        yield return t;
                    }

                    foreach (var t in CollectNamedTableReferences(unqualified.SecondTableReference))
                    {
                        yield return t;
                    }

                    break;

                case JoinParenthesisTableReference parenthesis:
                    foreach (var t in CollectNamedTableReferences(parenthesis.Join))
                    {
                        yield return t;
                    }

                    break;
            }
        }

        private void InspectMergeHazards(MergeSpecification spec)
        {
            InspectMergeMissingHoldlock(spec);
        }

        private bool _serializableIsolationSet;

        public void OnEnterSetTransactionIsolationLevelStatement(SetTransactionIsolationLevelStatement node, ModuleWalker walker) =>
            _serializableIsolationSet = node.Level == IsolationLevel.Serializable;

        private void InspectMergeMissingHoldlock(MergeSpecification spec)
        {
            if (_serializableIsolationSet
                || spec.Target is not NamedTableReference targetRef
                || SchemaObjectNameHelper.IsLocalTempName(targetRef.SchemaObject.BaseIdentifier.Value)
                || !spec.ActionClauses.Any(clause => clause.Action is InsertMergeAction))
            {
                return;
            }

            var hintKinds = targetRef.TableHints.Select(h => h.HintKind).ToHashSet();
            if (hintKinds.Overlaps([TableHintKind.HoldLock, TableHintKind.Serializable, TableHintKind.TabLock, TableHintKind.TabLockX]))
            {
                return;
            }

            if (!MatchKeyIsNotEnforcedUnique(spec, targetRef))
            {
                return;
            }

            Findings.Add(new QueryAntiPatternFinding(
                QueryAntiPatternFindingKind.MergeMissingHoldlock, sourcePath,
                spec.StartLine, spec.StartColumn,
                "MERGE target carries no WITH (HOLDLOCK)/SERIALIZABLE hint - two concurrent sessions can both take the WHEN NOT MATCHED branch under READ COMMITTED and both insert the same key, because no unique index on the matched columns rejects the second row.",
                FindingConfidence.Medium));
        }

        private bool MatchKeyIsNotEnforcedUnique(MergeSpecification spec, NamedTableReference targetRef)
        {
            if (catalog.Find(SchemaObjectNameHelper.Qualify(targetRef.SchemaObject)) is not { } table)
            {
                return false;
            }

            var targetNames = new List<string> { targetRef.SchemaObject.BaseIdentifier.Value };
            if (spec.TableAlias is { } alias)
            {
                targetNames.Add(alias.Value);
            }

            var matchedColumns = new HashSet<string>(catalog.IdentifierComparer);
            var collector = new MatchColumnCollector(targetNames, catalog.IdentifierComparer, matchedColumns);
            spec.SearchCondition.Accept(collector);
            if (collector.HasUnqualifiedColumn || matchedColumns.Count == 0)
            {
                return false;
            }

            return !table.Indexes.Any(index =>
                index.IsUnique && !index.IsFiltered && !index.IsDisabled && index.KeyColumns.Count > 0
                && index.KeyColumns.All(matchedColumns.Contains));
        }

        private sealed class MatchColumnCollector(List<string> targetNames, StringComparer comparer, HashSet<string> matchedColumns) : TSqlFragmentVisitor
        {
            public bool HasUnqualifiedColumn { get; private set; }

            public override void ExplicitVisit(ColumnReferenceExpression node)
            {
                var identifiers = node.MultiPartIdentifier?.Identifiers;
                if (identifiers is null || identifiers.Count == 0)
                {
                    return;
                }

                if (identifiers.Count == 1)
                {
                    HasUnqualifiedColumn = true;
                    return;
                }

                if (targetNames.Contains(identifiers[^2].Value, comparer))
                {
                    matchedColumns.Add(identifiers[^1].Value);
                }
            }
        }

        private void InspectStaleTableVariableInLoop(WhileStatement node)
        {
            var writtenVariables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var readSites = new List<(VariableTableReference Reference, int? HintedLevel)>();

            var collector = new LoopWriteAndReadCollector(readSites, writtenVariables, _tableVariableNames);
            node.Statement.Accept(collector);

            foreach (var read in readSites
                .Where(site => writtenVariables.Contains(site.Reference.Variable.Name)
                    && !TableVariableEstimateIsFixed(site.HintedLevel ?? catalog.CompatibilityLevel))
                .Select(site => site.Reference))
            {
                Findings.Add(new QueryAntiPatternFinding(
                    QueryAntiPatternFindingKind.TableVariableStaleEstimateInLoop, sourcePath,
                    read.StartLine, read.StartColumn,
                    $"{read.Variable.Name} read inside a WHILE loop that also writes to it",
                    FindingConfidence.Medium));
            }
        }

        private static IEnumerable<VariableTableReference> CollectVariableTableReferences(TableReference tableReference)
        {
            switch (tableReference)
            {
                case VariableTableReference variableRef:
                    yield return variableRef;
                    break;

                case QualifiedJoin join:
                    foreach (var v in CollectVariableTableReferences(join.FirstTableReference))
                    {
                        yield return v;
                    }

                    foreach (var v in CollectVariableTableReferences(join.SecondTableReference))
                    {
                        yield return v;
                    }

                    break;

                case UnqualifiedJoin unqualified:
                    foreach (var v in CollectVariableTableReferences(unqualified.FirstTableReference))
                    {
                        yield return v;
                    }

                    foreach (var v in CollectVariableTableReferences(unqualified.SecondTableReference))
                    {
                        yield return v;
                    }

                    break;

                case JoinParenthesisTableReference parenthesis:
                    foreach (var v in CollectVariableTableReferences(parenthesis.Join))
                    {
                        yield return v;
                    }

                    break;
            }
        }

        private sealed class LoopWriteAndReadCollector(
            List<(VariableTableReference Reference, int? HintedLevel)> readSites,
            HashSet<string> writtenVariables,
            HashSet<string> knownTableVariables) : TSqlFragmentVisitor
        {
            private int _recompileDepth;
            private int? _hintedLevel;

            public override void ExplicitVisit(WhileStatement node)
            {
                _ = node;
            }

            public override void ExplicitVisit(FromClause node)
            {
                if (_recompileDepth == 0)
                {
                    foreach (var tableReference in node.TableReferences)
                    {
                        foreach (var variableRef in CollectVariableTableReferences(tableReference)
                            .Where(v => knownTableVariables.Contains(v.Variable.Name)))
                        {
                            readSites.Add((variableRef, _hintedLevel));
                        }
                    }
                }

                base.ExplicitVisit(node);
            }

            public override void ExplicitVisit(SelectStatement node) => VisitWithRecompileScope(node, () => base.ExplicitVisit(node));

            public override void ExplicitVisit(InsertStatement node) => VisitWithRecompileScope(node, () =>
            {
                RecordWrite(node.InsertSpecification.Target, null);
                base.ExplicitVisit(node);
            });

            public override void ExplicitVisit(UpdateStatement node) => VisitWithRecompileScope(node, () =>
            {
                RecordWrite(node.UpdateSpecification.Target, node.UpdateSpecification.OutputIntoClause);
                base.ExplicitVisit(node);
            });

            public override void ExplicitVisit(DeleteStatement node) => VisitWithRecompileScope(node, () =>
            {
                RecordWrite(node.DeleteSpecification.Target, node.DeleteSpecification.OutputIntoClause);
                base.ExplicitVisit(node);
            });

            public override void ExplicitVisit(MergeStatement node) => VisitWithRecompileScope(node, () =>
            {
                RecordWrite(node.MergeSpecification.Target, node.MergeSpecification.OutputIntoClause);
                base.ExplicitVisit(node);
            });

            private void VisitWithRecompileScope(StatementWithCtesAndXmlNamespaces statement, Action visit)
            {
                var recompiles = HasOptionRecompile(statement);
                if (recompiles)
                {
                    _recompileDepth++;
                }

                var enclosingLevel = _hintedLevel;
                _hintedLevel = HintedCompatibilityLevel(statement) ?? enclosingLevel;
                visit();
                _hintedLevel = enclosingLevel;
                if (recompiles)
                {
                    _recompileDepth--;
                }
            }

            private void RecordWrite(TableReference target, OutputIntoClause? outputInto)
            {
                if (target is VariableTableReference targetVariable)
                {
                    writtenVariables.Add(targetVariable.Variable.Name);
                }

                if (outputInto?.IntoTable is VariableTableReference outputVariable)
                {
                    writtenVariables.Add(outputVariable.Variable.Name);
                }
            }
        }

        private void InspectRbarSingleRowLoopDml(WhileStatement node)
        {
            var assignedVariables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var dmlSites = new List<(WhereClause? Where, int Line, int Column, TableReference? Target)>();
            var collector = new LoopDmlAndAssignmentCollector(dmlSites, assignedVariables);
            node.Statement.Accept(collector);

            foreach (var (where, line, column, target) in dmlSites)
            {
                if (SingleVariableEqualityColumn(where, assignedVariables) is { } detail
                    && ComparedColumnIdentifiesOneRow(target, where))
                {
                    Findings.Add(new QueryAntiPatternFinding(
                        QueryAntiPatternFindingKind.RbarSingleRowLoopDml, sourcePath, line, column,
                        detail, FindingConfidence.Medium));
                }
            }
        }

        private bool ComparedColumnIdentifiesOneRow(TableReference? target, WhereClause? where)
        {
            var condition = where?.SearchCondition;
            while (condition is BooleanParenthesisExpression paren)
            {
                condition = paren.Expression;
            }

            if (target is not NamedTableReference named
                || condition is not BooleanComparisonExpression cmp
                || (cmp.FirstExpression as ColumnReferenceExpression ?? cmp.SecondExpression as ColumnReferenceExpression)
                    is not { MultiPartIdentifier.Identifiers: [.., { } column] }
                || catalog.Find(catalog.ResolveSynonymName(SchemaObjectNameHelper.Qualify(named.SchemaObject))) is not { } table)
            {
                return false;
            }

            return table.Indexes.Any(index => index.IsUnique && !index.IsFiltered && !index.IsDisabled
                && index.KeyColumns.Count == 1 && catalog.IdentifierComparer.Equals(index.KeyColumns[0], column.Value));
        }

        private static string? SingleVariableEqualityColumn(WhereClause? where, HashSet<string> loopVariables)
        {
            var condition = where?.SearchCondition;
            while (condition is BooleanParenthesisExpression paren)
            {
                condition = paren.Expression;
            }

            if (condition is not BooleanComparisonExpression { ComparisonType: BooleanComparisonType.Equals } cmp)
            {
                return null;
            }

            var (columnExpr, variableExpr) = cmp.FirstExpression switch
            {
                ColumnReferenceExpression col when cmp.SecondExpression is VariableReference v => (col, v),
                VariableReference v when cmp.SecondExpression is ColumnReferenceExpression col => (col, v),
                _ => (null, null),
            };

            if (columnExpr is null || variableExpr is null || !loopVariables.Contains(variableExpr.Name))
            {
                return null;
            }

            var columnName = columnExpr.MultiPartIdentifier.Identifiers is { Count: > 0 } identifiers
                ? identifiers[^1].Value
                : "?";
            return $"{columnName} = {variableExpr.Name}";
        }

        private sealed class LoopDmlAndAssignmentCollector(
            List<(WhereClause? Where, int Line, int Column, TableReference? Target)> dmlSites,
            HashSet<string> assignedVariables) : TSqlFragmentVisitor
        {
            public override void ExplicitVisit(WhileStatement node)
            {
                _ = node;
            }

            public override void ExplicitVisit(UpdateStatement node)
            {
                dmlSites.Add((node.UpdateSpecification.WhereClause, node.StartLine, node.StartColumn, node.UpdateSpecification.Target));
                base.ExplicitVisit(node);
            }

            public override void ExplicitVisit(DeleteStatement node)
            {
                dmlSites.Add((node.DeleteSpecification.WhereClause, node.StartLine, node.StartColumn, node.DeleteSpecification.Target));
                base.ExplicitVisit(node);
            }

            public override void ExplicitVisit(SetVariableStatement node)
            {
                if (node.CursorDefinition is null)
                {
                    assignedVariables.Add(node.Variable.Name);
                }

                base.ExplicitVisit(node);
            }

            public override void ExplicitVisit(SelectSetVariable node)
            {
                assignedVariables.Add(node.Variable.Name);
                base.ExplicitVisit(node);
            }

            public override void ExplicitVisit(FetchCursorStatement node)
            {
                foreach (var v in node.IntoVariables)
                {
                    assignedVariables.Add(v.Name);
                }

                base.ExplicitVisit(node);
            }
        }

        private void InspectCountStarExistenceSequence(IList<TSqlStatement> statements)
        {
            for (var i = 0; i + 1 < statements.Count; i++)
            {
                if (CountStarAssignedVariable(statements[i]) is not { } variableName
                    || ReadsAtMostOneRow(statements[i]))
                {
                    continue;
                }

                if (IsZeroExistenceComparison(NextStatementPredicate(statements[i + 1]), variableName)
                    && OnlyEverComparedToZero(variableName))
                {
                    Findings.Add(new QueryAntiPatternFinding(
                        QueryAntiPatternFindingKind.CountStarVariableExistenceCheck, sourcePath,
                        statements[i].StartLine, statements[i].StartColumn,
                        $"{variableName} = COUNT(*) then compared only to zero in the very next statement",
                        FindingConfidence.High));
                }
            }
        }

        private bool OnlyEverComparedToZero(string variableName)
        {
            if (_currentBatch is null)
            {
                return true;
            }

            var counter = new VariableUseCounter(variableName);
            _currentBatch.Accept(counter);
            return counter.Uses == 0;
        }

        private sealed class VariableUseCounter(string variableName) : TSqlFragmentVisitor
        {
            public int Uses { get; private set; }

            public override void ExplicitVisit(VariableReference node)
            {
                if (string.Equals(node.Name, variableName, StringComparison.OrdinalIgnoreCase))
                {
                    Uses++;
                }
            }

            public override void ExplicitVisit(SelectSetVariable node) => node.Expression?.Accept(this);

            public override void ExplicitVisit(SetVariableStatement node) => node.Expression?.Accept(this);

            public override void ExplicitVisit(ProcedureParameter node)
            {
                if (node.Modifier == ParameterModifier.Output
                    && string.Equals(node.VariableName.Value, variableName, StringComparison.OrdinalIgnoreCase))
                {
                    Uses++;
                }
            }

            public override void ExplicitVisit(IfStatement node)
            {
                VisitPredicate(node.Predicate);
                node.ThenStatement?.Accept(this);
                node.ElseStatement?.Accept(this);
            }

            public override void ExplicitVisit(WhileStatement node)
            {
                VisitPredicate(node.Predicate);
                node.Statement?.Accept(this);
            }

            private void VisitPredicate(BooleanExpression predicate)
            {
                if (!IsZeroExistenceComparison(predicate, variableName))
                {
                    predicate.Accept(this);
                }
            }
        }

        private bool ReadsAtMostOneRow(TSqlStatement statement) =>
            statement is SelectStatement { QueryExpression: QuerySpecification { FromClause: not null } spec }
            && SingleRowKeyPinning.QueryReadsAtMostOneRow(spec, catalog);

        private static string? CountStarAssignedVariable(TSqlStatement statement)
        {
            if (statement is not SelectStatement { QueryExpression: QuerySpecification { SelectElements.Count: 1 } spec }
                || spec.SelectElements[0] is not SelectSetVariable { AssignmentKind: AssignmentKind.Equals } setVar
                || setVar.Expression is not FunctionCall call
                || !CountStarFunctionNames.Contains(call.FunctionName.Value)
                || call.Parameters.Count != 1)
            {
                return null;
            }

            var isStar = call.Parameters[0] is ColumnReferenceExpression { ColumnType: ColumnType.Wildcard };
            var isOne = call.Parameters[0] is IntegerLiteral { Value: "1" };
            return isStar || isOne ? setVar.Variable.Name : null;
        }

        private static BooleanExpression? NextStatementPredicate(TSqlStatement statement) => statement switch
        {
            IfStatement ifStatement => ifStatement.Predicate,
            WhileStatement whileStatement => whileStatement.Predicate,
            _ => null,
        };

        private static bool IsZeroExistenceComparison(BooleanExpression? expression, string variableName)
        {
            if (expression is not BooleanComparisonExpression cmp)
            {
                return false;
            }

            var (literal, comparisonType) = cmp.FirstExpression switch
            {
                VariableReference v when string.Equals(v.Name, variableName, StringComparison.OrdinalIgnoreCase)
                    && cmp.SecondExpression is IntegerLiteral secondLiteral
                    => (secondLiteral, cmp.ComparisonType),
                IntegerLiteral firstLiteral when cmp.SecondExpression is VariableReference v
                    && string.Equals(v.Name, variableName, StringComparison.OrdinalIgnoreCase)
                    => (firstLiteral, Flip(cmp.ComparisonType)),
                _ => (null, cmp.ComparisonType),
            };

            if (literal is null)
            {
                return false;
            }

            return (comparisonType, literal.Value) switch
            {
                (BooleanComparisonType.GreaterThan, "0") => true,
                (BooleanComparisonType.GreaterThanOrEqualTo, "1") => true,
                (BooleanComparisonType.Equals, "0") => true,
                (BooleanComparisonType.NotEqualToBrackets or BooleanComparisonType.NotEqualToExclamation, "0") => true,
                _ => false,
            };
        }

        private static BooleanComparisonType Flip(BooleanComparisonType type) => type switch
        {
            BooleanComparisonType.GreaterThan => BooleanComparisonType.LessThan,
            BooleanComparisonType.LessThan => BooleanComparisonType.GreaterThan,
            BooleanComparisonType.GreaterThanOrEqualTo => BooleanComparisonType.LessThanOrEqualTo,
            BooleanComparisonType.LessThanOrEqualTo => BooleanComparisonType.GreaterThanOrEqualTo,
            _ => type,
        };

        private List<string> JoinColumnsQualifiedBy(QualifiedJoin join, string joinedAlias) =>
            PredicateTreeWalker.FlattenAnd(join.SearchCondition)
                .OfType<BooleanComparisonExpression>()
                .Where(c => c.ComparisonType == BooleanComparisonType.Equals)
                .SelectMany(c => new[] { c.FirstExpression, c.SecondExpression })
                .Select(e => ColumnAliasHelpers.ColumnNameIfQualifiedByAlias(e, joinedAlias, catalog.IdentifierComparer))
                .OfType<string>()
                .Distinct(catalog.IdentifierComparer)
                .ToList();

        private List<string> PinnedColumnsQualifiedBy(QuerySpecification node, string joinedAlias) =>
            node.WhereClause is { SearchCondition: { } whereCondition }
                ? PredicateTreeWalker.FlattenAnd(whereCondition)
                    .OfType<BooleanComparisonExpression>()
                    .Where(c => c.ComparisonType == BooleanComparisonType.Equals)
                    .Select(c => (c.FirstExpression, c.SecondExpression) switch
                    {
                        (var column, Literal or VariableReference) => ColumnAliasHelpers.ColumnNameIfQualifiedByAlias(column, joinedAlias, catalog.IdentifierComparer),
                        (Literal or VariableReference, var column) => ColumnAliasHelpers.ColumnNameIfQualifiedByAlias(column, joinedAlias, catalog.IdentifierComparer),
                        _ => null,
                    })
                    .OfType<string>()
                    .ToList()
                : [];

        private void InspectDistinctJoinFanout(QuerySpecification node, IReadOnlyDictionary<string, ScopeEntry> byAlias, ScopeChain scopeChain, ModuleWalker walker)
        {
            if (node.UniqueRowFilter != UniqueRowFilter.Distinct || node.FromClause is null)
            {
                return;
            }

            if (PredicateSurvivalAnalyzer.IsUnsatisfiable(node.WhereClause?.SearchCondition, columnRef => walker.ResolveColumnFacts(columnRef, scopeChain)))
            {
                return;
            }

            foreach (var join in node.FromClause.TableReferences.SelectMany(PredicateTreeWalker.FlattenJoinNodes))
            {
                var joinedAlias = AliasOf(join.SecondTableReference);
                if (joinedAlias is null || !byAlias.TryGetValue(joinedAlias, out var joinedEntry)
                    || joinedEntry.IsViewLayer || joinedEntry.Relation.QualifiedName is not { } joinedQualifiedName)
                {
                    continue;
                }

                var joinedTable = catalog.Find(joinedQualifiedName);
                if (joinedTable is null)
                {
                    continue;
                }

                var joinColumns = JoinColumnsQualifiedBy(join, joinedAlias);
                if (joinColumns.Count == 0)
                {
                    continue;
                }

                var pinnedColumns = PinnedColumnsQualifiedBy(node, joinedAlias);
                var isProvablyUnique = joinedTable.Indexes.Any(ix =>
                    ix.IsUnique && !ix.IsFiltered && !ix.IsDisabled
                    && ix.KeyColumns.Count > 0
                    && ix.KeyColumns.All(kc => joinColumns.Contains(kc, catalog.IdentifierComparer) || pinnedColumns.Contains(kc, catalog.IdentifierComparer)));
                if (isProvablyUnique)
                {
                    continue;
                }

                Findings.Add(new QueryAntiPatternFinding(
                    QueryAntiPatternFindingKind.DistinctMaskingJoinFanout, sourcePath,
                    join.StartLine, join.StartColumn,
                    $"SELECT DISTINCT joins {joinedQualifiedName} on columns not backed by a unique index ({string.Join(", ", joinColumns)})",
                    FindingConfidence.Medium));
            }
        }
    }

}
