using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Catalog;
using SilentScan.Core.Common;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates.Normalization;
using SilentScan.Core.Rules;
using SilentScan.Core.TypeInference;

namespace SilentScan.Core.Predicates;

public static class ControlFlowRiskScanner
{
    public static IReadOnlyList<ControlFlowRiskFinding> Scan(SqlParseResult parseResult, DatabaseCatalog catalog)
    {
        var rule = CreateRule(parseResult.SourcePath, catalog);
        var walker = new ModuleWalker(parseResult.SourcePath, catalog, EmptyResolvedViews, rules: [rule]);
        parseResult.Fragment.Accept(walker);
    return Harvest(rule);
    }
    internal static Rule CreateRule(string sourcePath, DatabaseCatalog catalog) => new(sourcePath, catalog);

    internal static IReadOnlyList<ControlFlowRiskFinding> Harvest(Rule rule) =>
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
        private const int MaxCoveredDomainSpan = 1024;

        private readonly Lazy<Dictionary<(string Table, string Column), List<(string ConstraintName, NumericValueRangeSet Domain)>>> _checkDomains =
            new(() => CheckConstraintPredicateContradictionScanner.Rule.BuildDomains(catalog));

        public List<ControlFlowRiskFinding> Findings { get; } = [];

        private readonly Stack<QuerySpecification> _querySpecifications = new();

        public void OnEnterQuerySpecificationScope(QuerySpecification node, ScopeChain scopeChain, ModuleWalker walker) =>
            _querySpecifications.Push(node);

        public void OnLeaveQuerySpecificationScope(QuerySpecification node, ScopeChain scopeChain, ModuleWalker walker) =>
            _querySpecifications.Pop();

        private static string CurrentModule(ModuleWalker walker) => walker.CurrentProcScope ?? "(batch)";

        private static readonly HashSet<string> NonDeterministicFunctionNames =
            new(StringComparer.OrdinalIgnoreCase) { "NEWID", "RAND", "CRYPT_GEN_RANDOM" };

        private NamedTableReference? _currentTable;

        public void OnEnterNamedTableReference(NamedTableReference node, ModuleWalker walker) =>
            _currentTable = node;

        private bool HintIsOnSessionLocalTemporaryTable(TableHint node) =>
            _currentTable is { } table
            && table.TableHints.Contains(node)
            && table.SchemaObject.BaseIdentifier.Value is ['#', not '#', ..];

        public void OnEnterTableHint(TableHint node, ModuleWalker walker)
        {
            if (node.HintKind is TableHintKind.NoLock or TableHintKind.ReadUncommitted && !HintIsOnSessionLocalTemporaryTable(node))
            {
                Findings.Add(new ControlFlowRiskFinding(
                    ControlFlowRiskFindingKind.DirtyReadIsolationHint,
                    CurrentModule(walker), sourcePath, node.StartLine, node.StartColumn,
                    "NOLOCK/READUNCOMMITTED allows dirty reads of uncommitted, possibly-rolled-back data, and can miss or double-count rows during a concurrent page split.",
                    FindingConfidence.Low));
            }
            else if (node.HintKind == TableHintKind.ReadCommittedLock && catalog.IsReadCommittedSnapshotOn == true
                && !HintIsOnSessionLocalTemporaryTable(node))
            {
                Findings.Add(new ControlFlowRiskFinding(
                    ControlFlowRiskFindingKind.ReadCommittedLockRevertsRowVersioning,
                    CurrentModule(walker), sourcePath, node.StartLine, node.StartColumn,
                    "This database has READ_COMMITTED_SNAPSHOT ON, so READ COMMITTED reads are normally row-versioned and non-blocking - the READCOMMITTEDLOCK hint reverts just this one table reference back to blocking/locking reads, silently changing its concurrency behavior relative to the rest of the batch.",
                    FindingConfidence.High));
            }
        }

        public void OnEnterSetTransactionIsolationLevelStatement(SetTransactionIsolationLevelStatement node, ModuleWalker walker)
        {
            if (node.Level == IsolationLevel.ReadUncommitted)
            {
                Findings.Add(new ControlFlowRiskFinding(
                    ControlFlowRiskFindingKind.DirtyReadIsolationHint,
                    CurrentModule(walker), sourcePath, node.StartLine, node.StartColumn,
                    "SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED allows dirty reads for the rest of the session.",
                    FindingConfidence.Low));
            }
        }

        public void OnEnterGlobalVariableExpression(GlobalVariableExpression node, ModuleWalker walker)
        {
            if (string.Equals(node.Name, "@@IDENTITY", StringComparison.OrdinalIgnoreCase))
            {
                Findings.Add(new ControlFlowRiskFinding(
                    ControlFlowRiskFindingKind.LegacyIdentityIntrinsic,
                    CurrentModule(walker), sourcePath, node.StartLine, node.StartColumn,
                    "@@IDENTITY returns the last identity value inserted in this SESSION across ANY table/scope, including one inserted by a trigger fired as a side effect - prefer SCOPE_IDENTITY() unless that broader semantics is specifically wanted.",
                    FindingConfidence.Medium));
            }
        }

        public void OnEnterOperandPosition(TSqlFragment node, ModuleWalker walker)
        {
            if (node is not SimpleCaseExpression simpleCase)
            {
                return;
            }

            if (simpleCase.ElseExpression is null && !WhenValuesCoverInputDomain(simpleCase, walker))
            {
                Findings.Add(new ControlFlowRiskFinding(
                    ControlFlowRiskFindingKind.CaseExpressionMissingElse,
                    CurrentModule(walker), sourcePath, simpleCase.StartLine, simpleCase.StartColumn,
                    "This simple CASE has no ELSE - an input value matching none of the WHEN values " +
                    "silently evaluates to NULL, no error raised.",
                    FindingConfidence.High));
            }

            if (ContainsNonDeterministicCall(simpleCase.InputExpression) is { } functionName)
            {
                Findings.Add(new ControlFlowRiskFinding(
                    ControlFlowRiskFindingKind.NonDeterministicCaseInput,
                    CurrentModule(walker), sourcePath, simpleCase.StartLine, simpleCase.StartColumn,
                    $"{functionName}() as a CASE input is re-evaluated separately for each WHEN " +
                    "comparison, not once - every WHEN branch is effectively unreachable and this " +
                    "always falls through to ELSE (or NULL, with no ELSE).",
                    FindingConfidence.High));
            }
        }


        private bool WhenValuesCoverInputDomain(SimpleCaseExpression simpleCase, ModuleWalker walker)
        {
            var whenValues = new HashSet<decimal>();
            foreach (var clause in simpleCase.WhenClauses)
            {
                if (clause.WhenExpression is not IntegerLiteral literal || !decimal.TryParse(literal.Value, System.Globalization.CultureInfo.InvariantCulture, out var value))
                {
                    return false;
                }

                whenValues.Add(value);
            }

            var input = Unwrap(simpleCase.InputExpression);
            if (TryGetFunctionDomain(input) is { } functionDomain)
            {
                return CoversRange(whenValues, functionDomain.Min, functionDomain.Max);
            }

            if (IsBitExpression(input, walker))
            {
                return CoversRange(whenValues, 0, 1);
            }

            if (input is ColumnReferenceExpression restrictedColumn
                && _querySpecifications.Count > 0
                && RestrictedValues(_querySpecifications.Peek().WhereClause?.SearchCondition, restrictedColumn) is { } restricted
                && restricted.All(whenValues.Contains))
            {
                return true;
            }

            if (input is not ColumnReferenceExpression columnRef
                || walker.ResolveCatalogColumn(columnRef, walker.CurrentScopeChain()) is not { } resolved)
            {
                return false;
            }

            if (resolved.Column.Type?.Category == SqlTypeCategory.Bit)
            {
                return CoversRange(whenValues, 0, 1);
            }

            return resolved.Column.Type?.Category is SqlTypeCategory.TinyInt or SqlTypeCategory.SmallInt or SqlTypeCategory.Int or SqlTypeCategory.BigInt
                && _checkDomains.Value.TryGetValue((resolved.TableQualifiedName, resolved.Column.Name), out var domains)
                && domains.Any(d => d.Domain.EveryIntegerIsIn(whenValues, MaxCoveredDomainSpan));
        }

        private static ScalarExpression Unwrap(ScalarExpression expression) =>
            expression is ParenthesisExpression parenthesis ? Unwrap(parenthesis.Expression) : expression;

        private static string ReferenceKey(ColumnReferenceExpression column) =>
            string.Join('.', column.MultiPartIdentifier.Identifiers.Select(i => i.Value)).ToUpperInvariant();

        private static bool IsBitExpression(ScalarExpression expression, ModuleWalker walker)
        {
            switch (Unwrap(expression))
            {
                case ColumnReferenceExpression column:
                    return walker.ResolveCatalogColumn(column, walker.CurrentScopeChain()) is { } resolved
                        && resolved.Column.Type?.Category == SqlTypeCategory.Bit;
                case BinaryExpression
                {
                    BinaryExpressionType: BinaryExpressionType.BitwiseOr or BinaryExpressionType.BitwiseAnd or BinaryExpressionType.BitwiseXor,
                } binary:
                    return IsBitExpression(binary.FirstExpression, walker) && IsBitExpression(binary.SecondExpression, walker);
                default:
                    return false;
            }
        }

        private static HashSet<decimal>? RestrictedValues(BooleanExpression? condition, ColumnReferenceExpression column)
        {
            var key = ReferenceKey(column);
            HashSet<decimal>? result = null;
            foreach (var conjunct in Conjuncts(condition))
            {
                var values = conjunct switch
                {
                    BooleanComparisonExpression { ComparisonType: BooleanComparisonType.Equals } eq
                        when SameColumn(eq.FirstExpression, key) && IntegerValue(eq.SecondExpression) is { } v => [v],
                    BooleanComparisonExpression { ComparisonType: BooleanComparisonType.Equals } eq
                        when SameColumn(eq.SecondExpression, key) && IntegerValue(eq.FirstExpression) is { } v => [v],
                    InPredicate { NotDefined: false, Subquery: null } inPredicate when SameColumn(inPredicate.Expression, key)
                        && inPredicate.Values.Select(x => IntegerValue(x)).ToList() is { } list && list.All(x => x is not null) => list.Select(x => x!.Value).ToList(),
                    BooleanTernaryExpression { TernaryExpressionType: BooleanTernaryExpressionType.Between } between
                        when SameColumn(between.FirstExpression, key) && IntegerValue(between.SecondExpression) is { } low
                            && IntegerValue(between.ThirdExpression) is { } high && high >= low && high - low < MaxCoveredDomainSpan
                        => Enumerable.Range(0, (int)(high - low) + 1).Select(o => low + o).ToList(),
                    _ => null,
                };

                if (values is not null)
                {
                    result = result is null ? [.. values] : [.. result.Intersect(values)];
                }
            }

            return result;
        }

        private static IEnumerable<BooleanExpression> Conjuncts(BooleanExpression? expression)
        {
            switch (expression)
            {
                case null:
                    yield break;
                case BooleanParenthesisExpression paren:
                    foreach (var inner in Conjuncts(paren.Expression))
                    {
                        yield return inner;
                    }

                    break;
                case BooleanBinaryExpression { BinaryExpressionType: BooleanBinaryExpressionType.And } and:
                    foreach (var inner in Conjuncts(and.FirstExpression).Concat(Conjuncts(and.SecondExpression)))
                    {
                        yield return inner;
                    }

                    break;
                default:
                    yield return expression;
                    break;
            }
        }

        private static bool SameColumn(ScalarExpression expression, string key) =>
            Unwrap(expression) is ColumnReferenceExpression column && ReferenceKey(column) == key;

        private static decimal? IntegerValue(ScalarExpression expression) =>
            Unwrap(expression) is IntegerLiteral literal
                && decimal.TryParse(literal.Value, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : null;

        private static bool CoversRange(HashSet<decimal> values, int min, int max) =>
            Enumerable.Range(min, max - min + 1).All(v => values.Contains(v));

        private static (int Min, int Max)? TryGetFunctionDomain(ScalarExpression input)
        {
            if (input is not FunctionCall { FunctionName.Value: { } name } call)
            {
                return null;
            }

            if (name.Equals("MONTH", StringComparison.OrdinalIgnoreCase) && call.Parameters.Count == 1)
            {
                return (1, 12);
            }

            if (!name.Equals("DATEPART", StringComparison.OrdinalIgnoreCase)
                || call.Parameters is not [IdentifierLiteral part, _])
            {
                return null;
            }

            return part.Value.ToUpperInvariant() switch
            {
                "WEEKDAY" or "DW" or "W" => (1, 7),
                "MONTH" or "MM" or "M" => (1, 12),
                "QUARTER" or "QQ" or "Q" => (1, 4),
                "HOUR" or "HH" => (0, 23),
                _ => null,
            };
        }

        private static string? ContainsNonDeterministicCall(ScalarExpression expression)
        {
            var finder = new NonDeterministicCallFinder();
            expression.Accept(finder);
            return finder.FoundFunctionName;
        }

        private sealed class NonDeterministicCallFinder : TSqlFragmentVisitor
        {
            public string? FoundFunctionName { get; private set; }

            public override void ExplicitVisit(FunctionCall node)
            {
                if (FoundFunctionName is null
                    && node.FunctionName?.Value is { } name
                    && NonDeterministicFunctionNames.Contains(name)
                    && !(name.Equals("RAND", StringComparison.OrdinalIgnoreCase) && node.Parameters.Count > 0))
                {
                    FoundFunctionName = name.ToUpperInvariant();
                }

                base.ExplicitVisit(node);
            }
        }
    }
}
