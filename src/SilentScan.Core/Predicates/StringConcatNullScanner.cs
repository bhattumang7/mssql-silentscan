using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Catalog;
using SilentScan.Core.Common;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;
using SilentScan.Core.TypeInference;

namespace SilentScan.Core.Predicates;

public static class StringConcatNullScanner
{
    private static readonly HashSet<SqlTypeCategory> StringCategories =
    [
        SqlTypeCategory.Char, SqlTypeCategory.VarChar, SqlTypeCategory.NChar, SqlTypeCategory.NVarChar,
        SqlTypeCategory.Text, SqlTypeCategory.NText,
    ];

    public static IReadOnlyList<StringConcatNullFinding> Scan(SqlParseResult parseResult, DatabaseCatalog catalog)
    {
        var rule = CreateRule(parseResult.SourcePath, catalog, parseResult.Fragment);
        var walker = new ModuleWalker(parseResult.SourcePath, catalog, EmptyResolvedViews, rules: [rule]);
        parseResult.Fragment.Accept(walker);
    return Harvest(rule);
    }
    internal static Rule CreateRule(string sourcePath, DatabaseCatalog catalog, TSqlFragment fragment) =>
        new(sourcePath, catalog, ConcatNullYieldsNullFlowResolver.Resolve(fragment));

    internal static IReadOnlyList<StringConcatNullFinding> Harvest(Rule rule) =>
            [
            .. rule.Findings
                .OrderBy(f => f.SourcePath, StringComparer.Ordinal)
                .ThenBy(f => f.Line)
                .ThenBy(f => f.Column),
        ];


    private static readonly IReadOnlyDictionary<string, ResolvedRelation> EmptyResolvedViews = new Dictionary<string, ResolvedRelation>();

    private enum LeafKind
    {
        Unknown,

        String,

        Guarded,
    }

    private readonly record struct Leaf(LeafKind Kind, bool IsNullableColumn, string? TableQualifiedName, string? ColumnName, string? ReferenceKey = null);

    internal sealed class Rule(string sourcePath, DatabaseCatalog catalog, IReadOnlyDictionary<TSqlStatement, bool> concatNullYieldsNullOffByStatement) : IModuleRule
    {
        public List<StringConcatNullFinding> Findings { get; } = [];

        private bool _concatNullYieldsNullOff;

        private readonly Stack<QueryScope> _enclosingQueries = new();

        public void OnEnterSelectStatementScope(SelectStatement node, ModuleWalker walker) => UpdateCurrentState(node);

        public void OnEnterInsertStatementScope(InsertStatement node, ModuleWalker walker) => UpdateCurrentState(node);

        public void OnEnterDeleteStatementScope(DeleteStatement node, ScopeChain scopeChain, ModuleWalker walker) => UpdateCurrentState(node);

        public void OnEnterMergeStatementScope(MergeStatement node, ScopeChain scopeChain, ModuleWalker walker) => UpdateCurrentState(node);

        public void OnEnterQuerySpecificationScope(QuerySpecification node, ScopeChain scopeChain, ModuleWalker walker)
        {
            var nullRejected = CollectNullRejectedColumns(
                [node.WhereClause?.SearchCondition, node.HavingClause?.SearchCondition, .. NullRejection.PreservedInnerJoinConditions(node.FromClause)]);
            _enclosingQueries.Push(new QueryScope(nullRejected, DeclaredAliases(node.FromClause)));

            if (_concatNullYieldsNullOff)
            {
                return;
            }

            if (node.ForClause is XmlForClause { Options: { } xmlOptions }
                && xmlOptions.Any(o => o.OptionKind == XmlForClauseOptions.Path))
            {
                return;
            }

            foreach (var element in node.SelectElements.OfType<SelectScalarExpression>())
            {
                InspectTopLevel(element.Expression, scopeChain, nullRejected);
            }
        }

        public void OnLeaveQuerySpecificationScope(QuerySpecification node, ScopeChain scopeChain, ModuleWalker walker) =>
            _enclosingQueries.Pop();

        public void OnLeaveUpdateStatementScope(UpdateStatement node, ScopeChain scopeChain, ModuleWalker walker) =>
            _enclosingQueries.Pop();

        private HashSet<string> DeclaredAliases(FromClause? fromClause)
        {
            var collector = new DeclaredAliasCollector(catalog.IdentifierComparer);
            fromClause?.Accept(collector);
            return collector.Aliases;
        }

        private bool IsRejectedByAnEnclosingQuery(string referenceKey)
        {
            var parts = referenceKey.Split('.');
            if (parts.Length < 2)
            {
                return false;
            }

            var qualifier = parts[^2];
            foreach (var scope in _enclosingQueries)
            {
                if (scope.Aliases.Contains(qualifier))
                {
                    return scope.NullRejected.Contains(referenceKey);
                }
            }

            return false;
        }

        public void OnEnterUpdateStatementScope(UpdateStatement node, ScopeChain scopeChain, ModuleWalker walker)
        {
            UpdateCurrentState(node);

            var spec = node.UpdateSpecification;
            var nullRejected = CollectNullRejectedColumns(
                [spec.WhereClause?.SearchCondition, .. NullRejection.PreservedInnerJoinConditions(spec.FromClause)]);
            var aliases = DeclaredAliases(spec.FromClause);
            if (spec.Target is NamedTableReference target)
            {
                aliases.Add(target.Alias?.Value ?? target.SchemaObject.BaseIdentifier.Value);
            }

            _enclosingQueries.Push(new QueryScope(nullRejected, aliases));

            if (_concatNullYieldsNullOff)
            {
                return;
            }

            foreach (var setClause in spec.SetClauses.OfType<AssignmentSetClause>())
            {
                if (setClause.NewValue is ScalarExpression newValue)
                {
                    InspectTopLevel(newValue, scopeChain, nullRejected);
                }
            }
        }

        private void UpdateCurrentState(TSqlStatement statement) =>
            _concatNullYieldsNullOff = concatNullYieldsNullOffByStatement.GetValueOrDefault(statement);

        private void InspectTopLevel(
            ScalarExpression root,
            IReadOnlyList<(IReadOnlyDictionary<string, ScopeEntry> ByAlias, IReadOnlyList<ScopeEntry> Ordered)> scopeChain,
            IReadOnlySet<string> nullRejected)
        {
            var collector = new ConcatChainRootCollector();
            root.Accept(collector);
            foreach (var chainRoot in collector.Roots)
            {
                InspectChain(chainRoot, scopeChain, nullRejected);
            }
        }

        private void InspectChain(
            BinaryExpression chainRoot,
            IReadOnlyList<(IReadOnlyDictionary<string, ScopeEntry> ByAlias, IReadOnlyList<ScopeEntry> Ordered)> scopeChain,
            IReadOnlySet<string> nullRejected)
        {
            var leaves = new List<Leaf>();
            FlattenAddChain(chainRoot, scopeChain, leaves);

            if (leaves.Any(l => l.Kind == LeafKind.Unknown))
            {

                return;
            }

            if (!leaves.Any(l => l.Kind == LeafKind.String))
            {

                return;
            }

            var nullableLeaf = leaves.FirstOrDefault(l => l.Kind == LeafKind.String && l.IsNullableColumn
                && !(l.ReferenceKey is { } key && (nullRejected.Contains(key) || IsRejectedByAnEnclosingQuery(key))));
            if (nullableLeaf.TableQualifiedName is null)
            {
                return;
            }

            Findings.Add(new StringConcatNullFinding(
                nullableLeaf.TableQualifiedName, nullableLeaf.ColumnName!, sourcePath, chainRoot.StartLine, chainRoot.StartColumn));
        }

        private void FlattenAddChain(
            ScalarExpression expression,
            IReadOnlyList<(IReadOnlyDictionary<string, ScopeEntry> ByAlias, IReadOnlyList<ScopeEntry> Ordered)> scopeChain,
            List<Leaf> sink)
        {
            var unwrapped = Unwrap(expression);
            if (unwrapped is BinaryExpression { BinaryExpressionType: BinaryExpressionType.Add } add)
            {
                FlattenAddChain(add.FirstExpression, scopeChain, sink);
                FlattenAddChain(add.SecondExpression, scopeChain, sink);
                return;
            }

            sink.Add(ClassifyLeaf(unwrapped, scopeChain));
        }

        private Leaf ClassifyLeaf(
            ScalarExpression expression,
            IReadOnlyList<(IReadOnlyDictionary<string, ScopeEntry> ByAlias, IReadOnlyList<ScopeEntry> Ordered)> scopeChain)
        {
            switch (expression)
            {
                case StringLiteral:
                    return new Leaf(LeafKind.String, IsNullableColumn: false, TableQualifiedName: null, ColumnName: null);

                case ColumnReferenceExpression columnRef:
                    var resolved = BaseColumnResolver.ResolveBaseColumn(columnRef, sourcePath, scopeChain, catalog);
                    if (resolved is not { } r || r.Type is not { } columnType || !StringCategories.Contains(columnType.Category))
                    {

                        return new Leaf(LeafKind.Unknown, false, null, null);
                    }

                    var isNullable = r.IsNullableSide
                        || (catalog.Find(r.TableQualifiedName)?.FindColumn(r.ColumnName, catalog.IdentifierComparer)?.IsNullable ?? false);
                    return new Leaf(LeafKind.String, isNullable, r.TableQualifiedName, r.ColumnName, ReferenceKeyOf(columnRef));

                case FunctionCall { FunctionName.Value: var name } call
                    when string.Equals(name, "ISNULL", StringComparison.OrdinalIgnoreCase):
                    return ClassifyGuarded(call.Parameters, scopeChain);

                case CoalesceExpression coalesce:
                    return ClassifyGuarded(coalesce.Expressions, scopeChain);

                default:
                    return new Leaf(LeafKind.Unknown, false, null, null);
            }
        }

        private Leaf ClassifyGuarded(
            IEnumerable<ScalarExpression> arguments,
            IReadOnlyList<(IReadOnlyDictionary<string, ScopeEntry> ByAlias, IReadOnlyList<ScopeEntry> Ordered)> scopeChain) =>
            arguments.Any(arg => ClassifyLeaf(Unwrap(arg), scopeChain).Kind == LeafKind.Unknown)
                ? new Leaf(LeafKind.Unknown, false, null, null)
                : new Leaf(LeafKind.Guarded, false, null, null);

        private static string ReferenceKeyOf(ColumnReferenceExpression columnRef) =>
            columnRef.MultiPartIdentifier is { } parts
                ? string.Join('.', parts.Identifiers.Select(i => i.Value)).ToUpperInvariant()
                : string.Empty;

        private static HashSet<string> CollectNullRejectedColumns(IEnumerable<BooleanExpression?> filters) =>
            [.. filters.SelectMany(PredicateTreeWalker.FlattenAnd).SelectMany(NullRejection.Columns).Select(ReferenceKeyOf)];

        private static ScalarExpression Unwrap(ScalarExpression expression) =>
            expression is ParenthesisExpression parenthesis ? Unwrap(parenthesis.Expression) : expression;

        private sealed record QueryScope(HashSet<string> NullRejected, HashSet<string> Aliases);

        private sealed class DeclaredAliasCollector(StringComparer comparer) : TSqlFragmentVisitor
        {
            public HashSet<string> Aliases { get; } = new(comparer);

            public override void ExplicitVisit(NamedTableReference node) =>
                Aliases.Add(node.Alias?.Value ?? node.SchemaObject.BaseIdentifier.Value);

            public override void ExplicitVisit(QueryDerivedTable node)
            {
                if (node.Alias is { } alias)
                {
                    Aliases.Add(alias.Value);
                }
            }
        }

        private sealed class ConcatChainRootCollector : TSqlFragmentVisitor
        {
            public List<BinaryExpression> Roots { get; } = [];

            public override void ExplicitVisit(BinaryExpression node)
            {
                if (node.BinaryExpressionType == BinaryExpressionType.Add)
                {
                    Roots.Add(node);

                    return;
                }

                base.ExplicitVisit(node);
            }

            public override void ExplicitVisit(FunctionCall node)
            {
                var name = node.FunctionName.Value;
                if (string.Equals(name, "ISNULL", StringComparison.OrdinalIgnoreCase))
                {
                    if (node.Parameters.Count > 1)
                    {
                        node.Parameters[1].Accept(this);
                    }

                    return;
                }

                base.ExplicitVisit(node);
            }

            public override void ExplicitVisit(CoalesceExpression node)
            {
                _ = node;
            }

            public override void ExplicitVisit(QuerySpecification node)
            {
                _ = node;
            }
        }
    }
}
