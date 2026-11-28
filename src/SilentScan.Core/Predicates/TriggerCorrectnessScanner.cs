using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Catalog;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;
using SilentScan.Core.Common;
using SilentScan.Core.Predicates.Normalization;
using SilentScan.Core.TypeInference;

namespace SilentScan.Core.Predicates;

public static class TriggerCorrectnessScanner
{
    private const string InsertedTableName = "inserted";

    private static readonly HashSet<string> PseudoTableNames = new(StringComparer.OrdinalIgnoreCase) { InsertedTableName, "deleted" };

    public static IReadOnlyList<TriggerCorrectnessFinding> Scan(SqlParseResult parseResult, DatabaseCatalog catalog)
    {
        var rule = CreateRule(parseResult.SourcePath, catalog);
        var walker = new ModuleWalker(parseResult.SourcePath, catalog, EmptyResolvedViews, rules: [rule]);
        parseResult.Fragment.Accept(walker);
    return Harvest(rule);
    }
    internal static Rule CreateRule(string sourcePath, DatabaseCatalog catalog) => new(sourcePath, catalog);

    internal static IReadOnlyList<TriggerCorrectnessFinding> Harvest(Rule rule) =>
            [
            .. rule.Findings
                .OrderBy(f => f.Kind)
                .ThenBy(f => f.SourcePath, StringComparer.Ordinal)
                .ThenBy(f => f.Line)
                .ThenBy(f => f.Column),
        ];


    private static readonly IReadOnlyDictionary<string, ResolvedRelation> EmptyResolvedViews = new Dictionary<string, ResolvedRelation>();

    internal sealed class Rule(string sourcePath, DatabaseCatalog catalog) : IModuleRule
    {
        public List<TriggerCorrectnessFinding> Findings { get; } = [];

        public void OnEnterTriggerStatementScope(TriggerStatementBody node, SchemaObjectName name, TriggerObject triggerObject, ModuleWalker walker) =>
            VisitTrigger(node, name, triggerObject, node.StatementList);

        private void VisitTrigger(TriggerStatementBody node, SchemaObjectName name, TriggerObject triggerObject, StatementList? statementList)
        {
            var qualifiedName = SchemaObjectNameHelper.Qualify(name);

            var topLevelStatements = statementList?.Statements is [BeginEndBlockStatement singleBlock]
                ? singleBlock.StatementList.Statements
                : statementList?.Statements;

            if (topLevelStatements is not null)
            {
                InspectMultiRowUnsafeAssignments(qualifiedName, topLevelStatements);
                InspectInsteadOfInsertFilteredReinsert(qualifiedName, node, triggerObject, topLevelStatements);
            }

            if (statementList is not null && node.TriggerActions.Any(action => action.TriggerActionType == TriggerActionType.Update))
            {
                InspectUpdateFunctionWithoutValueComparison(qualifiedName, statementList);
            }

        }

        private void InspectMultiRowUnsafeAssignments(string triggerQualifiedName, IList<TSqlStatement> statements)
        {
            for (var i = 0; i < statements.Count; i++)
            {
                var variableName = TryGetUnsafeAssignedVariable(statements[i]);
                if (variableName is null || HasMultiRowExitGuard(statements, i))
                {
                    continue;
                }

                var keyedDmlLine = FindStraightLineKeyedDmlUse(statements, i + 1, variableName);
                if (keyedDmlLine is { } line)
                {
                    Findings.Add(new TriggerCorrectnessFinding(
                        TriggerCorrectnessFindingKind.MultiRowUnsafeKeyedDml, triggerQualifiedName, sourcePath,
                        statements[i].StartLine, statements[i].StartColumn,
                        $"'{variableName}' is assigned from a single, unspecified row of inserted/deleted with no WHERE/TOP/aggregate, then used as the sole key of a write at line {line} in the same trigger body - a multi-row INSERT/UPDATE/MERGE silently drives that write off one arbitrary row's value.",
                        FindingConfidence.High));
                    continue;
                }

                Findings.Add(new TriggerCorrectnessFinding(
                    TriggerCorrectnessFindingKind.MultiRowUnsafeSingleRowAssignment, triggerQualifiedName, sourcePath,
                    statements[i].StartLine, statements[i].StartColumn,
                    $"'{variableName}' is assigned from a single, unspecified row of inserted/deleted (no WHERE/TOP/aggregate) - silently wrong for every multi-row INSERT/UPDATE/MERGE that fires this trigger.",
                    FindingConfidence.High));
            }
        }

        private static bool HasMultiRowExitGuard(IList<TSqlStatement> statements, int assignmentIndex)
        {
            for (var i = 0; i < assignmentIndex; i++)
            {
                if (statements[i] is IfStatement { Predicate: BooleanComparisonExpression comparison } ifStatement
                    && RejectsMoreThanOneRow(comparison)
                    && (!ReadsRowCountVariable(comparison) || RowCountIntactBefore(statements, i))
                    && ExitsTheTrigger(ifStatement.ThenStatement))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ReadsRowCountVariable(BooleanComparisonExpression comparison) =>
            comparison.FirstExpression is GlobalVariableExpression || comparison.SecondExpression is GlobalVariableExpression
            || comparison.FirstExpression is ParenthesisExpression { Expression: GlobalVariableExpression }
            || comparison.SecondExpression is ParenthesisExpression { Expression: GlobalVariableExpression };

        private static bool RowCountIntactBefore(IList<TSqlStatement> statements, int guardIndex) =>
            statements.Take(guardIndex).All(statement => statement is DeclareVariableStatement { Declarations: var declarations }
                && declarations.All(declaration => declaration.Value is null));

        private static bool RejectsMoreThanOneRow(BooleanComparisonExpression comparison)
        {
            var (countSide, literalSide, type) = IsRowCount(comparison.FirstExpression)
                ? (comparison.FirstExpression, comparison.SecondExpression, comparison.ComparisonType)
                : (comparison.SecondExpression, comparison.FirstExpression, Reverse(comparison.ComparisonType));

            if (!IsRowCount(countSide) || literalSide is not IntegerLiteral literal
                || !int.TryParse(literal.Value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var limit))
            {
                return false;
            }

            return type switch
            {
                BooleanComparisonType.GreaterThan => limit == 1,
                BooleanComparisonType.GreaterThanOrEqualTo => limit == 2,
                BooleanComparisonType.NotEqualToBrackets or BooleanComparisonType.NotEqualToExclamation => limit == 1,
                _ => false,
            };
        }

        private static BooleanComparisonType Reverse(BooleanComparisonType type) => type switch
        {
            BooleanComparisonType.GreaterThan => BooleanComparisonType.LessThan,
            BooleanComparisonType.LessThan => BooleanComparisonType.GreaterThan,
            BooleanComparisonType.GreaterThanOrEqualTo => BooleanComparisonType.LessThanOrEqualTo,
            BooleanComparisonType.LessThanOrEqualTo => BooleanComparisonType.GreaterThanOrEqualTo,
            _ => type,
        };

        private static bool IsRowCount(ScalarExpression expression)
        {
            while (expression is ParenthesisExpression parenthesized)
            {
                expression = parenthesized.Expression;
            }

            return expression switch
            {
                GlobalVariableExpression global => string.Equals(global.Name, "@@ROWCOUNT", StringComparison.OrdinalIgnoreCase),
                ScalarSubquery { QueryExpression: QuerySpecification { SelectElements: [SelectScalarExpression { Expression: FunctionCall call }] } spec } =>
                    string.Equals(call.FunctionName.Value, "COUNT", StringComparison.OrdinalIgnoreCase)
                    && spec.WhereClause is null
                    && spec.FromClause is { TableReferences: [NamedTableReference { SchemaObject.SchemaIdentifier: null } named] }
                    && PseudoTableNames.Contains(named.SchemaObject.BaseIdentifier.Value),
                _ => false,
            };
        }

        private static bool ExitsTheTrigger(TSqlStatement statement)
        {
            var finder = new ExitStatementFinder();
            statement.Accept(finder);
            return finder.Found;
        }

        private sealed class ExitStatementFinder : TSqlFragmentVisitor
        {
            public bool Found { get; private set; }

            public override void Visit(ReturnStatement node) => Found = true;

            public override void Visit(ThrowStatement node) => Found = true;

            public override void Visit(RollbackTransactionStatement node) => Found = true;
        }

        private static string? TryGetUnsafeAssignedVariable(TSqlStatement statement) =>
            statement is SelectStatement { QueryExpression: QuerySpecification { SelectElements: [SelectSetVariable { AssignmentKind: AssignmentKind.Equals } setVar] } spec }
                && IsUnsafeSourceQuery(spec) && !(spec.GroupByClause is null && IsAggregateExpression(setVar.Expression))
                && VariesByRow(setVar.Expression, (NamedTableReference)spec.FromClause.TableReferences[0])
                ? setVar.Variable.Name
                : null;

        private static bool VariesByRow(ScalarExpression expression, NamedTableReference source)
        {
            var finder = new RowColumnFinder(source.Alias?.Value ?? source.SchemaObject.BaseIdentifier.Value);
            expression.Accept(finder);
            return finder.Found;
        }

        private sealed class RowColumnFinder(string sourceName) : TSqlFragmentVisitor
        {
            private int _subqueryDepth;

            public bool Found { get; private set; }

            public override void ExplicitVisit(ScalarSubquery node)
            {
                _subqueryDepth++;
                base.ExplicitVisit(node);
                _subqueryDepth--;
            }

            public override void ExplicitVisit(ColumnReferenceExpression node)
            {
                var identifiers = node.MultiPartIdentifier?.Identifiers;
                if (identifiers is null)
                {
                    return;
                }

                Found |= _subqueryDepth == 0
                    || identifiers.Count > 1 && string.Equals(identifiers[^2].Value, sourceName, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static bool IsAggregateExpression(ScalarExpression expression)
        {
            var finder = new AggregateFinder();
            expression.Accept(finder);
            return finder.Found;
        }

        private sealed class AggregateFinder : TSqlFragmentVisitor
        {
            public bool Found { get; private set; }

            public override void ExplicitVisit(FunctionCall node)
            {
                if (node.OverClause is null && AggregateFunctions.Names.Contains(node.FunctionName.Value))
                {
                    Found = true;
                    return;
                }

                base.ExplicitVisit(node);
            }

            public override void ExplicitVisit(ScalarSubquery node)
            {
                _ = node;
            }
        }

        private static bool IsUnsafeSourceQuery(QuerySpecification spec) =>
            spec.WhereClause is null
            && spec.TopRowFilter is null
            && spec.FromClause is { TableReferences: [NamedTableReference { SchemaObject.SchemaIdentifier: null } named] }
            && PseudoTableNames.Contains(named.SchemaObject.BaseIdentifier.Value);

        private static int? FindStraightLineKeyedDmlUse(IList<TSqlStatement> statements, int startIndex, string variableName)
        {
            for (var i = startIndex; i < statements.Count; i++)
            {
                var whereClause = statements[i] switch
                {
                    UpdateStatement { UpdateSpecification.Target: NamedTableReference } upd => upd.UpdateSpecification.WhereClause,
                    DeleteStatement { DeleteSpecification.Target: NamedTableReference } del => del.DeleteSpecification.WhereClause,
                    _ => null,
                };

                if (whereClause is not null && IsSoleKeyEquality(whereClause.SearchCondition, variableName))
                {
                    return statements[i].StartLine;
                }
            }

            return null;
        }

        private static bool IsSoleKeyEquality(BooleanExpression condition, string variableName)
        {
            while (condition is BooleanParenthesisExpression paren)
            {
                condition = paren.Expression;
            }

            return condition is BooleanComparisonExpression { ComparisonType: BooleanComparisonType.Equals } cmp
            && ((cmp.FirstExpression is ColumnReferenceExpression && cmp.SecondExpression is VariableReference v1
                    && string.Equals(v1.Name, variableName, StringComparison.OrdinalIgnoreCase))
                || (cmp.SecondExpression is ColumnReferenceExpression && cmp.FirstExpression is VariableReference v2
                    && string.Equals(v2.Name, variableName, StringComparison.OrdinalIgnoreCase)));
        }

        private void InspectInsteadOfInsertFilteredReinsert(string triggerQualifiedName, TriggerStatementBody node, TriggerObject triggerObject, IList<TSqlStatement> topLevelStatements)
        {
            if (node.TriggerType != TriggerType.InsteadOf
                || node.TriggerActions is not { } actions
                || !actions.Any(a => a.TriggerActionType == TriggerActionType.Insert))
            {
                return;
            }

            var filteredInserts = topLevelStatements
                .OfType<InsertStatement>()
                .Where(ins => ins.InsertSpecification.Target is NamedTableReference
                    && ins.InsertSpecification.InsertSource is SelectInsertSource { Select: QuerySpecification spec }
                    && IsFilteredReinsertFromInserted(spec)
                    && !FilterKeepsEveryRow(spec, triggerObject))
                .ToList();

            if (filteredInserts.Count == 0)
            {
                return;
            }

            var hasCompanionInsert = HasCompanionInsert(topLevelStatements, filteredInserts);
            var hasRejectPath = ContainsRejectPath(topLevelStatements);
            var hasProvenExhaustiveCoverage = HasProvenExhaustiveCoverage(filteredInserts, triggerObject);

            if (hasCompanionInsert || hasRejectPath || hasProvenExhaustiveCoverage)
            {
                return;
            }

            foreach (var insert in filteredInserts)
            {
                Findings.Add(new TriggerCorrectnessFinding(
                    TriggerCorrectnessFindingKind.InsteadOfInsertFilteredNoRejectPath, triggerQualifiedName, sourcePath,
                    insert.StartLine, insert.StartColumn,
                    "INSTEAD OF INSERT trigger re-inserts a WHERE/join-filtered subset of inserted with no companion INSERT/RAISERROR/THROW anywhere in the body - the caller's own INSERT reports success while rows matching the negated filter are silently dropped, no error, no trace.",
                    FindingConfidence.High));
            }
        }

        private bool FilterKeepsEveryRow(QuerySpecification spec, TriggerObject triggerObject)
        {
            if (triggerObject.Name is not { } targetTableName
                || spec.FromClause.TableReferences is not [NamedTableReference]
                || spec.WhereClause?.SearchCondition is not { } condition)
            {
                return false;
            }

            var table = catalog.Find(catalog.ResolveSynonymName(SchemaObjectNameHelper.Qualify(targetTableName)));
            var conjuncts = new List<BooleanExpression>();
            Flatten(condition, conjuncts);
            return table is not null
                && conjuncts.All(conjunct => conjunct is BooleanIsNullExpression { IsNot: true, Expression: ColumnReferenceExpression { MultiPartIdentifier.Identifiers: [.., { } column] } }
                    && table.FindColumn(column.Value, catalog.IdentifierComparer) is { IsNullable: false });
        }

        private static void Flatten(BooleanExpression expression, List<BooleanExpression> conjuncts)
        {
            switch (expression)
            {
                case BooleanParenthesisExpression parenthesis:
                    Flatten(parenthesis.Expression, conjuncts);
                    break;
                case BooleanBinaryExpression { BinaryExpressionType: BooleanBinaryExpressionType.And } and:
                    Flatten(and.FirstExpression, conjuncts);
                    Flatten(and.SecondExpression, conjuncts);
                    break;
                default:
                    conjuncts.Add(expression);
                    break;
            }
        }

        private static bool IsFilteredReinsertFromInserted(QuerySpecification spec)
        {
            if (spec.FromClause is not { TableReferences: { Count: > 0 } tableReferences })
            {
                return false;
            }

            var referencesInserted = tableReferences.Any(ReferencesInsertedTable);
            var isFiltered = spec.WhereClause is not null || tableReferences.Any(ContainsQualifiedJoin)
                || spec.TopRowFilter is { } top && !KeepsAllRows(top);
            return referencesInserted && isFiltered;
        }

        private static bool KeepsAllRows(TopRowFilter top) =>
            top is { Percent: true, Expression: var expression }
            && (expression is ParenthesisExpression parenthesis ? parenthesis.Expression : expression) is IntegerLiteral { Value: "100" };

        private static bool HasCompanionInsert(IList<TSqlStatement> statements, List<InsertStatement> filteredInserts)
        {
            var collector = new InsertedDerivedCollector();
            foreach (var statement in statements)
            {
                statement.Accept(collector);
            }

            return statements.OfType<InsertStatement>()
                .Except(filteredInserts)
                .Any(insert => ReadsAnyOf(insert, collector.Names));
        }

        private static bool ReadsAnyOf(TSqlFragment fragment, HashSet<string> derivedFromInserted)
        {
            var reader = new SourceNameCollector();
            fragment.Accept(reader);
            return reader.Names.Contains(InsertedTableName) || reader.Names.Overlaps(derivedFromInserted);
        }

        private sealed class SourceNameCollector : TSqlFragmentVisitor
        {
            public HashSet<string> Names { get; } = new(StringComparer.OrdinalIgnoreCase);

            public override void ExplicitVisit(NamedTableReference node)
            {
                Names.Add(node.SchemaObject.BaseIdentifier.Value);
                base.ExplicitVisit(node);
            }

            public override void ExplicitVisit(VariableTableReference node) => Names.Add(node.Variable.Name);

            public override void ExplicitVisit(VariableReference node) => Names.Add(node.Name);
        }

        private sealed class InsertedDerivedCollector : TSqlFragmentVisitor
        {
            public HashSet<string> Names { get; } = new(StringComparer.OrdinalIgnoreCase);

            public override void ExplicitVisit(InsertStatement node)
            {
                var target = node.InsertSpecification.Target switch
                {
                    NamedTableReference named => named.SchemaObject.BaseIdentifier.Value,
                    VariableTableReference variable => variable.Variable.Name,
                    _ => null,
                };

                if (target is not null && ReadsAnyOf(node.InsertSpecification.InsertSource, Names))
                {
                    Names.Add(target);
                }
            }

            public override void ExplicitVisit(SelectStatement node)
            {
                if (node.Into is { } into && ReadsAnyOf(node.QueryExpression, Names))
                {
                    Names.Add(into.BaseIdentifier.Value);
                }

                if (node.QueryExpression is QuerySpecification specification && ReadsAnyOf(specification, Names))
                {
                    Names.UnionWith(specification.SelectElements.OfType<SelectSetVariable>().Select(set => set.Variable.Name));
                }
            }
        }

        private static bool ReferencesInsertedTable(TableReference reference) => reference switch
        {
            NamedTableReference { SchemaObject.SchemaIdentifier: null } named
                => string.Equals(named.SchemaObject.BaseIdentifier.Value, InsertedTableName, StringComparison.OrdinalIgnoreCase),
            QualifiedJoin join => ReferencesInsertedTable(join.FirstTableReference) || ReferencesInsertedTable(join.SecondTableReference),
            UnqualifiedJoin join => ReferencesInsertedTable(join.FirstTableReference) || ReferencesInsertedTable(join.SecondTableReference),
            JoinParenthesisTableReference parenthesis => ReferencesInsertedTable(parenthesis.Join),
            _ => false,
        };

        private static bool ContainsQualifiedJoin(TableReference reference) => reference switch
        {
            QualifiedJoin => true,
            UnqualifiedJoin join => ContainsQualifiedJoin(join.FirstTableReference) || ContainsQualifiedJoin(join.SecondTableReference),
            JoinParenthesisTableReference parenthesis => ContainsQualifiedJoin(parenthesis.Join),
            _ => false,
        };

        private bool HasProvenExhaustiveCoverage(List<InsertStatement> filteredInserts, TriggerObject triggerObject)
        {
            if (triggerObject.Name is not { } targetTableName)
            {
                return false;
            }

            var searchConditions = new List<BooleanExpression>();
            foreach (var insert in filteredInserts)
            {
                if (insert.InsertSpecification.InsertSource is not SelectInsertSource
                    {
                        Select: QuerySpecification
                        {
                            FromClause.TableReferences: [NamedTableReference { SchemaObject.SchemaIdentifier: null } named],
                            WhereClause: { SearchCondition: { } condition },
                        },
                    }
                    || !string.Equals(named.SchemaObject.BaseIdentifier.Value, InsertedTableName, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                searchConditions.Add(condition);
            }

            var targetQualifiedName = catalog.ResolveSynonymName(SchemaObjectNameHelper.Qualify(targetTableName));

            PredicateSurvivalAnalyzer.ColumnFacts ResolveInsertedColumnFacts(ColumnReferenceExpression columnRef)
            {
                var identifiers = columnRef.MultiPartIdentifier.Identifiers;
                if (identifiers is not [.., { } last])
                {
                    return default;
                }

                if (identifiers.Count > 1 && !PseudoTableNames.Contains(identifiers[^2].Value))
                {
                    return default;
                }

                var catalogColumn = catalog.Find(targetQualifiedName)?.FindColumn(last.Value, catalog.IdentifierComparer);
                return new PredicateSurvivalAnalyzer.ColumnFacts(
                    catalogColumn is null ? null : !catalogColumn.IsNullable,
                    null,
                    catalogColumn?.Type?.Category is SqlTypeCategory.Text or SqlTypeCategory.NText or SqlTypeCategory.Image);
            }

            return PredicateSurvivalAnalyzer.IsTautology(searchConditions, ResolveInsertedColumnFacts);
        }

        private static bool ContainsRejectPath(IList<TSqlStatement> statements)
        {
            var collector = new RejectPathCollector();
            foreach (var statement in statements)
            {
                statement.Accept(collector);
            }

            return collector.Found;
        }

        private sealed class RejectPathCollector : TSqlFragmentVisitor
        {
            public bool Found { get; private set; }

            public override void ExplicitVisit(RaiseErrorStatement node) =>
                Found |= node.SecondParameter is not IntegerLiteral { Value: var severity }
                    || !int.TryParse(severity, out var level) || level > 10;

            public override void ExplicitVisit(ThrowStatement node) => Found = true;

            public override void ExplicitVisit(RollbackTransactionStatement node) => Found = true;
        }

        private void InspectUpdateFunctionWithoutValueComparison(string triggerQualifiedName, StatementList statementList)
        {
            var collector = new IfStatementCollector();
            statementList.Accept(collector);

            foreach (var ifStatement in collector.Statements)
            {
                var updateCalls = new UpdateCallCollector();
                ifStatement.Predicate.Accept(updateCalls);

                foreach (var updateCall in updateCalls.Calls)
                {
                    var columnName = updateCall.Identifier.Value;
                    if (HasSameColumnValueComparison(ifStatement.Predicate, columnName, catalog)
                        || HasSameColumnValueComparison(ifStatement.ThenStatement, columnName, catalog))
                    {
                        continue;
                    }

                    Findings.Add(new TriggerCorrectnessFinding(
                        TriggerCorrectnessFindingKind.UpdateFunctionWithoutValueComparison, triggerQualifiedName, sourcePath,
                        updateCall.StartLine, updateCall.StartColumn,
                        $"IF UPDATE({columnName}) gates this branch with no comparison between inserted.{columnName}/deleted.{columnName} in the same predicate or the gated branch - UPDATE() reports whether the column was NAMED in the SET list, not whether its value changed, so a full-column UPDATE (an ORM's generated statement, e.g.) fires this branch on a genuine no-op save.",
                        FindingConfidence.High));
                }
            }
        }

        private static bool HasSameColumnValueComparison(TSqlFragment fragment, string columnName, DatabaseCatalog catalog)
        {
            var collector = new SameColumnComparisonCollector(columnName, catalog);
            fragment.Accept(collector);
            return collector.Found;
        }

        private sealed class IfStatementCollector : TSqlFragmentVisitor
        {
            public List<IfStatement> Statements { get; } = [];

            public override void ExplicitVisit(IfStatement node)
            {
                Statements.Add(node);
                base.ExplicitVisit(node);
            }
        }

        private sealed class UpdateCallCollector : TSqlFragmentVisitor
        {
            public List<UpdateCall> Calls { get; } = [];

            public override void ExplicitVisit(UpdateCall node) => Calls.Add(node);
        }

        private sealed class SameColumnComparisonCollector(string columnName, DatabaseCatalog catalog) : TSqlFragmentVisitor
        {
            public bool Found { get; private set; }

            private readonly Stack<HashSet<string>> _baseTableAliases = new();

            public override void ExplicitVisit(QuerySpecification node) =>
                VisitWithin(node.FromClause, () => base.ExplicitVisit(node));

            public override void ExplicitVisit(UpdateSpecification node) =>
                VisitWithin(node.FromClause, () => base.ExplicitVisit(node));

            public override void ExplicitVisit(DeleteSpecification node) =>
                VisitWithin(node.FromClause, () => base.ExplicitVisit(node));

            public override void ExplicitVisit(BooleanComparisonExpression node)
            {
                if (IsColumnNamed(node.FirstExpression, columnName) && IsColumnNamed(node.SecondExpression, columnName)
                    && !(IsBaseTableColumn(node.FirstExpression) && IsBaseTableColumn(node.SecondExpression)))
                {
                    Found = true;
                }

                base.ExplicitVisit(node);
            }

            private void VisitWithin(FromClause? fromClause, Action visit)
            {
                var aliases = (fromClause?.TableReferences ?? [])
                    .SelectMany(PredicateTreeWalker.FlattenNamedTables)
                    .Where(named => catalog.Find(catalog.ResolveSynonymName(SchemaObjectNameHelper.Qualify(named.SchemaObject)))
                        is { Kind: CatalogTableKind.Table })
                    .Select(named => named.Alias?.Value ?? named.SchemaObject.BaseIdentifier.Value)
                    .ToHashSet(catalog.IdentifierComparer);
                _baseTableAliases.Push(aliases);
                visit();
                _baseTableAliases.Pop();
            }

            private bool IsBaseTableColumn(ScalarExpression expression) =>
                expression is ColumnReferenceExpression { MultiPartIdentifier.Identifiers: [.., var qualifier, _] }
                && _baseTableAliases.Any(aliases => aliases.Contains(qualifier.Value));

            private bool IsColumnNamed(ScalarExpression expression, string columnName) =>
                expression is ColumnReferenceExpression { MultiPartIdentifier.Identifiers: [.., { } last] }
                && catalog.IdentifierComparer.Equals(last.Value, columnName);
        }
    }
}
