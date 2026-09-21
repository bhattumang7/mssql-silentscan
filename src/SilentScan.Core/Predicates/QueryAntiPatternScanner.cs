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

    private static readonly HashSet<string> AggregateFunctionNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "SUM", "COUNT", "COUNT_BIG", "AVG", "MIN", "MAX", "APPROX_COUNT_DISTINCT",
        "CHECKSUM_AGG", "GROUPING", "GROUPING_ID", "STDEV", "STDEVP", "STRING_AGG", "VAR", "VARP",
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
        return new Rule(parseResult.SourcePath, catalog, cteNameCollector.Names);
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

    internal sealed class Rule(string sourcePath, DatabaseCatalog catalog, HashSet<string> cteNames) : IModuleRule
    {
        public List<QueryAntiPatternFinding> Findings { get; } = [];

        private readonly HashSet<string> _tableVariableNames = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _tableValuedParameterNames = new(StringComparer.OrdinalIgnoreCase);

        private static string? AliasOf(TableReference reference) =>
            reference is NamedTableReference named ? named.Alias?.Value ?? named.SchemaObject.BaseIdentifier.Value : null;

        public void OnEnterProcedureOrFunctionBody(ProcedureStatementBodyBase node, ModuleWalker walker) =>
            InspectTableValuedParameters(node.Parameters);

        public void OnEnterDeclareTableVariableStatement(DeclareTableVariableStatement node, ModuleWalker walker) =>
            _tableVariableNames.Add(node.Body.VariableName.Value);

        public void OnEnterFromClause(FromClause node, ModuleWalker walker)
        {
            foreach (var tableReference in node.TableReferences)
            {
                foreach (var variableRef in CollectVariableTableReferences(tableReference))
                {
                    if (_tableVariableNames.Contains(variableRef.Variable.Name)
                        && catalog.CompatibilityLevel is { } level && level < 150)
                    {
                        Findings.Add(new QueryAntiPatternFinding(
                            QueryAntiPatternFindingKind.TableVariableLowCompatEstimate, sourcePath,
                            variableRef.StartLine, variableRef.StartColumn,
                            $"{variableRef.Variable.Name} (connected compatibility level {level}, below 150)",
                            FindingConfidence.High));
                    }

                    if (_tableValuedParameterNames.Contains(variableRef.Variable.Name)
                        && catalog.CompatibilityLevel is >= 170)
                    {
                        Findings.Add(new QueryAntiPatternFinding(
                            QueryAntiPatternFindingKind.TableVariablePspSkip, sourcePath,
                            variableRef.StartLine, variableRef.StartColumn,
                            variableRef.Variable.Name,
                            FindingConfidence.High));
                    }
                }

                foreach (var named in CollectNamedTableReferences(tableReference))
                {
                    InspectUnqualifiedReference(named);
                }
            }
        }

        public void OnEnterInsertStatementScope(InsertStatement node, ModuleWalker walker)
        {
            InspectSiteIfNamedTable(node.InsertSpecification.Target);
        }

        public void OnEnterUpdateStatementScope(UpdateStatement node, ScopeChain scopeChain, ModuleWalker walker)
        {
            InspectSiteIfNamedTable(node.UpdateSpecification.Target);
            InspectUnboundedWrite(node.UpdateSpecification.WhereClause, node.UpdateSpecification.TopRowFilter, node);
        }

        public void OnEnterDeleteStatementScope(DeleteStatement node, ScopeChain scopeChain, ModuleWalker walker)
        {
            InspectSiteIfNamedTable(node.DeleteSpecification.Target);
            InspectUnboundedWrite(node.DeleteSpecification.WhereClause, node.DeleteSpecification.TopRowFilter, node);
        }

        public void OnEnterMergeStatementScope(MergeStatement node, ScopeChain scopeChain, ModuleWalker walker)
        {
            InspectSiteIfNamedTable(node.MergeSpecification.Target);
            InspectSiteIfNamedTable(node.MergeSpecification.TableReference);
            InspectMergeHazards(node.MergeSpecification);
        }

        public void OnEnterWhileStatement(WhileStatement node, ModuleWalker walker)
        {
            if (catalog.CompatibilityLevel is not { } knownLevel || knownLevel >= 150)
            {
                InspectStaleTableVariableInLoop(node);
            }

            InspectRbarSingleRowLoopDml(node);
        }

        public void OnEnterDeclareCursorStatement(DeclareCursorStatement node, ModuleWalker walker) =>
            InspectCursorGlobalness(node.CursorDefinition, node.Name.Value, node.StartLine, node.StartColumn);

        public void OnEnterSetVariableStatement(SetVariableStatement node, ModuleWalker walker)
        {
            if (node.CursorDefinition is not null)
            {
                InspectCursorGlobalness(node.CursorDefinition, node.Variable.Name, node.StartLine, node.StartColumn);
            }
        }

        public void OnEnterStatementList(StatementList node, ModuleWalker walker)
        {
            InspectCountStarExistenceSequence(node.Statements);
            InspectMultiRowInsertIgnoreDupKeySequence(node.Statements);
        }

        public void OnEnterTSqlBatch(TSqlBatch node, ModuleWalker walker)
        {
            InspectCountStarExistenceSequence(node.Statements);
            InspectMultiRowInsertIgnoreDupKeySequence(node.Statements);
            _tableVariableNames.Clear();
            _tableValuedParameterNames.Clear();
        }

        public void OnEnterQuerySpecificationScope(QuerySpecification node, ScopeChain scopeChain, ModuleWalker walker)
        {
            var byAlias = scopeChain[0].ByAlias;

            InspectHaving(node, scopeChain, walker);
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
            if (named.SchemaObject.SchemaIdentifier is not null
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

                var hazardIndex = resolved.Indexes.FirstOrDefault(ix => ix.IsUnique && ix.IgnoreDupKey);
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
                    FindingConfidence.High));
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

                case JoinParenthesisTableReference parenthesis:
                    foreach (var t in CollectNamedTableReferences(parenthesis.Join))
                    {
                        yield return t;
                    }

                    break;
            }
        }

        private void InspectUnboundedWrite(WhereClause? where, TopRowFilter? top, TSqlStatement node)
        {
            if (where is not null || top is not null)
            {
                return;
            }

            var verb = node is UpdateStatement ? "UPDATE" : "DELETE";
            Findings.Add(new QueryAntiPatternFinding(
                QueryAntiPatternFindingKind.UnboundedTableWrite, sourcePath,
                node.StartLine, node.StartColumn,
                $"{verb} with no WHERE clause and no TOP - a whole-table write with no row-limiting mechanism at all. A deliberate full-table maintenance statement is a legitimate reason this fires; verify intent before treating this as a bug.",
                FindingConfidence.Medium));
        }

        private void InspectMergeHazards(MergeSpecification spec)
        {
            InspectMergeMissingHoldlock(spec);
            InspectMergeUnconditionalDelete(spec);
        }

        private void InspectMergeMissingHoldlock(MergeSpecification spec)
        {
            if (spec.Target is not NamedTableReference targetRef)
            {
                return;
            }

            var hintKinds = targetRef.TableHints.Select(h => h.HintKind).ToHashSet();
            if (hintKinds.Contains(TableHintKind.HoldLock) || hintKinds.Contains(TableHintKind.Serializable))
            {
                return;
            }

            Findings.Add(new QueryAntiPatternFinding(
                QueryAntiPatternFindingKind.MergeMissingHoldlock, sourcePath,
                spec.StartLine, spec.StartColumn,
                "MERGE target carries no WITH (HOLDLOCK)/SERIALIZABLE hint - two concurrent sessions can both take the WHEN NOT MATCHED branch under READ COMMITTED and race a primary-key violation.",
                FindingConfidence.Medium));
        }

        private void InspectMergeUnconditionalDelete(MergeSpecification spec)
        {
            foreach (var clause in spec.ActionClauses)
            {
                if (clause.Action is not DeleteMergeAction || clause.SearchCondition is not null)
                {
                    continue;
                }

                var matchKindText = clause.Condition switch
                {
                    MergeCondition.NotMatchedBySource => "WHEN NOT MATCHED BY SOURCE THEN DELETE",
                    MergeCondition.Matched => "WHEN MATCHED THEN DELETE",
                    _ => "THEN DELETE",
                };

                Findings.Add(new QueryAntiPatternFinding(
                    QueryAntiPatternFindingKind.MergeUnconditionalDelete, sourcePath,
                    clause.StartLine, clause.StartColumn,
                    $"{matchKindText} with no additional AND condition of its own - {(clause.Condition == MergeCondition.NotMatchedBySource ? "deletes every target row absent from the USING source's result set" : "deletes every row the join matched")}.",
                    FindingConfidence.Medium));
            }
        }

        private void InspectStaleTableVariableInLoop(WhileStatement node)
        {
            var writtenVariables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var readSites = new List<VariableTableReference>();

            var collector = new LoopWriteAndReadCollector(readSites, writtenVariables, _tableVariableNames);
            node.Statement.Accept(collector);

            foreach (var read in readSites.Where(read => writtenVariables.Contains(read.Variable.Name)))
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

                case JoinParenthesisTableReference parenthesis:
                    foreach (var v in CollectVariableTableReferences(parenthesis.Join))
                    {
                        yield return v;
                    }

                    break;
            }
        }

        private sealed class LoopWriteAndReadCollector(
            List<VariableTableReference> readSites,
            HashSet<string> writtenVariables,
            HashSet<string> knownTableVariables) : TSqlFragmentVisitor
        {
            public override void ExplicitVisit(WhileStatement node)
            {
                _ = node;
            }

            public override void ExplicitVisit(FromClause node)
            {
                foreach (var tableReference in node.TableReferences)
                {
                    foreach (var variableRef in CollectVariableTableReferences(tableReference)
                        .Where(v => knownTableVariables.Contains(v.Variable.Name)))
                    {
                        readSites.Add(variableRef);
                    }
                }

                base.ExplicitVisit(node);
            }

            public override void ExplicitVisit(InsertStatement node)
            {
                RecordWrite(node.InsertSpecification.Target, null);
                base.ExplicitVisit(node);
            }

            public override void ExplicitVisit(UpdateStatement node)
            {
                RecordWrite(node.UpdateSpecification.Target, node.UpdateSpecification.OutputIntoClause);
                base.ExplicitVisit(node);
            }

            public override void ExplicitVisit(DeleteStatement node)
            {
                RecordWrite(node.DeleteSpecification.Target, node.DeleteSpecification.OutputIntoClause);
                base.ExplicitVisit(node);
            }

            public override void ExplicitVisit(MergeStatement node)
            {
                RecordWrite(node.MergeSpecification.Target, node.MergeSpecification.OutputIntoClause);
                base.ExplicitVisit(node);
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
            var dmlSites = new List<(WhereClause? Where, int Line, int Column)>();
            var collector = new LoopDmlAndAssignmentCollector(dmlSites, assignedVariables);
            node.Statement.Accept(collector);

            foreach (var (where, line, column) in dmlSites)
            {
                if (SingleVariableEqualityColumn(where, assignedVariables) is { } detail)
                {
                    Findings.Add(new QueryAntiPatternFinding(
                        QueryAntiPatternFindingKind.RbarSingleRowLoopDml, sourcePath, line, column,
                        detail, FindingConfidence.Medium));
                }
            }
        }

        private static string? SingleVariableEqualityColumn(WhereClause? where, HashSet<string> loopVariables)
        {
            if (where?.SearchCondition is not BooleanComparisonExpression { ComparisonType: BooleanComparisonType.Equals } cmp)
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
            List<(WhereClause? Where, int Line, int Column)> dmlSites,
            HashSet<string> assignedVariables) : TSqlFragmentVisitor
        {
            public override void ExplicitVisit(WhileStatement node)
            {
                _ = node;
            }

            public override void ExplicitVisit(UpdateStatement node)
            {
                dmlSites.Add((node.UpdateSpecification.WhereClause, node.StartLine, node.StartColumn));
                base.ExplicitVisit(node);
            }

            public override void ExplicitVisit(DeleteStatement node)
            {
                dmlSites.Add((node.DeleteSpecification.WhereClause, node.StartLine, node.StartColumn));
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

        private void InspectCursorGlobalness(CursorDefinition definition, string cursorName, int line, int column)
        {
            var kinds = definition.Options.Select(o => o.OptionKind).ToHashSet();
            if (kinds.Contains(CursorOptionKind.Local))
            {
                return;
            }

            Findings.Add(new QueryAntiPatternFinding(
                QueryAntiPatternFindingKind.GlobalCursorDeclaration, sourcePath, line, column,
                $"{cursorName} ({(kinds.Contains(CursorOptionKind.Global) ? "explicit GLOBAL" : "no LOCAL/GLOBAL keyword, defaults to GLOBAL")})",
                FindingConfidence.Low));
        }

        private void InspectCountStarExistenceSequence(IList<TSqlStatement> statements)
        {
            for (var i = 0; i + 1 < statements.Count; i++)
            {
                if (CountStarAssignedVariable(statements[i]) is not { } variableName)
                {
                    continue;
                }

                if (IsZeroExistenceComparison(NextStatementPredicate(statements[i + 1]), variableName))
                {
                    Findings.Add(new QueryAntiPatternFinding(
                        QueryAntiPatternFindingKind.CountStarVariableExistenceCheck, sourcePath,
                        statements[i].StartLine, statements[i].StartColumn,
                        $"{variableName} = COUNT(*) then compared only to zero in the very next statement",
                        FindingConfidence.High));
                }
            }
        }

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

        private void InspectHaving(
            QuerySpecification node, IReadOnlyList<(IReadOnlyDictionary<string, ScopeEntry> ByAlias, IReadOnlyList<ScopeEntry> Ordered)> scopeChain, ModuleWalker walker)
        {
            if (node.HavingClause?.SearchCondition is not { } having || node.GroupByClause is not { } groupBy)
            {
                return;
            }

            var groupByColumns = groupBy.GroupingSpecifications
                .OfType<ExpressionGroupingSpecification>()
                .Select(g => g.Expression)
                .OfType<ColumnReferenceExpression>()
                .Select(c => c.MultiPartIdentifier.Identifiers[^1].Value)
                .ToHashSet(catalog.IdentifierComparer);

            var dead = PredicateSurvivalAnalyzer.FindDeadComparisons(having, columnRef => walker.ResolveColumnFacts(columnRef, scopeChain));

            foreach (var condition in PredicateTreeWalker.FlattenAnd(having))
            {
                if (ContainsAggregate(condition) || dead.Contains(condition))
                {
                    continue;
                }

                var collector = new ColumnAliasHelpers.RawColumnReferenceCollector();
                condition.Accept(collector);
                if (collector.References.Count == 0)
                {
                    continue;
                }

                var allGroupByOrLiteral = collector.References.All(c =>
                    groupByColumns.Contains(c.MultiPartIdentifier.Identifiers[^1].Value));
                if (!allGroupByOrLiteral)
                {
                    continue;
                }

                Findings.Add(new QueryAntiPatternFinding(
                    QueryAntiPatternFindingKind.NonAggregateHavingPredicate, sourcePath,
                    condition.StartLine, condition.StartColumn,
                    "HAVING condition references only GROUP BY key columns/literals - equivalent WHERE condition would filter before aggregation",
                    FindingConfidence.High));
            }
        }

        private static bool ContainsAggregate(TSqlFragment fragment)
        {
            var collector = new AggregateCallCollector();
            fragment.Accept(collector);
            return collector.Found;
        }

        private sealed class AggregateCallCollector : TSqlFragmentVisitor
        {
            public bool Found { get; private set; }

            public override void ExplicitVisit(FunctionCall node)
            {
                if (AggregateFunctionNames.Contains(node.FunctionName.Value))
                {
                    Found = true;
                }

                base.ExplicitVisit(node);
            }
        }

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

                var joinColumns = PredicateTreeWalker.FlattenAnd(join.SearchCondition)
                    .OfType<BooleanComparisonExpression>()
                    .Where(c => c.ComparisonType == BooleanComparisonType.Equals)
                    .SelectMany(c => new[] { c.FirstExpression, c.SecondExpression })
                    .Select(e => ColumnAliasHelpers.ColumnNameIfQualifiedByAlias(e, joinedAlias, catalog.IdentifierComparer))
                    .Where(c => c is not null)
                    .Select(c => c!)
                    .Distinct(catalog.IdentifierComparer)
                    .ToList();

                if (joinColumns.Count == 0)
                {
                    continue;
                }

                var isProvablyUnique = joinedTable.Indexes.Any(ix =>
                    ix.IsUnique && !ix.IsFiltered && !ix.IsDisabled
                    && ix.KeyColumns.Count > 0
                    && ix.KeyColumns.All(kc => joinColumns.Contains(kc, catalog.IdentifierComparer)));
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
