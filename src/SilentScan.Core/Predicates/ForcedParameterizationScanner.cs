using System.Globalization;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Catalog;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;

namespace SilentScan.Core.Predicates;

public static class ForcedParameterizationScanner
{
    public static IReadOnlyList<ForcedParameterizationFinding> ScanDynamicStatements(
        SqlParseResult inner, IReadOnlyList<PlaceholderOccurrence> placeholders, string moduleName, FindingConfidence confidence)
    {
        var changing = placeholders
            .Where(p => p.ChangesPerExecution)
            .ToDictionary(p => p, p => "__silentscan_sym_L" + p.Origin.Line.ToString(CultureInfo.InvariantCulture) + "C" + p.Origin.Column.ToString(CultureInfo.InvariantCulture) + "__");
        if (changing.Count == 0)
        {
            return [];
        }

        var rule = new Rule(inner, new ChangingValues(changing), moduleName, confidence);
        var walker = new ModuleWalker(inner.SourcePath, new DatabaseCatalog(), EmptyResolvedViews, rules: [rule]);
        inner.Fragment.Accept(walker);
        var outputSites = rule.Findings
            .Where(f => f.Kind == ForcedParameterizationFindingKind.DmlOutputListLiteral)
            .Select(f => (f.Line, f.Column))
            .ToHashSet();
        return [.. rule.Findings.Where(f => f.Kind != ForcedParameterizationFindingKind.SelectListLiteral || !outputSites.Contains((f.Line, f.Column)))];
    }

    private static readonly IReadOnlyDictionary<string, ResolvedRelation> EmptyResolvedViews = new Dictionary<string, ResolvedRelation>();

    private sealed class ChangingValues(IReadOnlyDictionary<PlaceholderOccurrence, string> tokens)
    {
        public bool IsInLiteral(TSqlFragment fragment) =>
            fragment is Literal literal && literal.Value is { } text && tokens.Values.Any(t => text.Contains(t, StringComparison.OrdinalIgnoreCase));

        public bool IsBareNonStringValue(TSqlFragment fragment) =>
            BareToken(fragment) is { } name
            && tokens.Any(t => string.Equals(t.Value, name, StringComparison.OrdinalIgnoreCase) && !t.Key.Type.IsStringFamily);

        public bool IsValue(TSqlFragment fragment) => IsInLiteral(fragment) || IsBareNonStringValue(fragment);

        public TSqlFragment? FindValue(TSqlFragment root, bool anyBareToken)
        {
            var finder = new Finder(f => IsValue(f) || (anyBareToken && BareToken(f) is { } name && tokens.Values.Contains(name, StringComparer.OrdinalIgnoreCase)));
            root.Accept(finder);
            return finder.Found;
        }

        private static string? BareToken(TSqlFragment fragment) =>
            fragment is ColumnReferenceExpression { MultiPartIdentifier.Identifiers: [var identifier] } ? identifier.Value : null;

        private sealed class Finder(Func<TSqlFragment, bool> matches) : TSqlFragmentVisitor
        {
            public TSqlFragment? Found { get; private set; }

            public override void Visit(TSqlFragment fragment)
            {
                if (Found is null && matches(fragment))
                {
                    Found = fragment;
                }
            }
        }
    }

    private sealed class Rule(SqlParseResult inner, ChangingValues values, string moduleName, FindingConfidence confidence) : IModuleRule
    {
        public List<ForcedParameterizationFinding> Findings { get; } = [];

        public void OnLikePredicate(LikePredicate node, ModuleWalker walker)
        {
            if (values.IsValue(node.SecondExpression))
            {
                Add(ForcedParameterizationFindingKind.LikePatternLiteral, node.SecondExpression, "LIKE pattern");
            }
        }

        public void OnEnterTopRowFilter(TopRowFilter node, ModuleWalker walker) =>
            AddRowCount(node.Expression, "TOP row count");

        public void OnEnterOffsetClause(OffsetClause node, ModuleWalker walker)
        {
            AddRowCount(node.OffsetExpression, "OFFSET row count");
            AddRowCount(node.FetchExpression, "FETCH row count");
        }

        public void OnEnterSelectScalarExpression(SelectScalarExpression node, ModuleWalker walker)
        {
            if (values.IsValue(node.Expression))
            {
                Add(ForcedParameterizationFindingKind.SelectListLiteral, node.Expression, "select-list value");
            }
        }

        public void OnEnterHavingClause(HavingClause node, ModuleWalker walker)
        {
            foreach (var operand in FindDirectComparisonOperands(node.SearchCondition).Where(values.IsValue))
            {
                Add(ForcedParameterizationFindingKind.HavingLiteral, operand, "HAVING comparand");
            }
        }

        public void OnEnterOrderByClause(OrderByClause node, ModuleWalker walker)
        {
            foreach (var expression in node.OrderByElements.Select(e => e.Expression).Where(e => e is not (Literal or ColumnReferenceExpression)))
            {
                if (values.FindValue(expression, anyBareToken: false) is { } value)
                {
                    Add(ForcedParameterizationFindingKind.OrderByExpressionLiteral, value, "value inside an ORDER BY expression");
                }
            }
        }

        public void OnEnterGroupByClause(GroupByClause node, ModuleWalker walker)
        {
            foreach (var expression in node.GroupingSpecifications.OfType<ExpressionGroupingSpecification>()
                         .Select(specification => specification.Expression)
                         .Where(e => e is not ColumnReferenceExpression))
            {
                if (values.FindValue(expression, anyBareToken: false) is { } value)
                {
                    Add(ForcedParameterizationFindingKind.GroupByExpressionLiteral, value, "value inside a GROUP BY expression");
                }
            }
        }

        public void OnEnterNamedTableReference(NamedTableReference node, ModuleWalker walker)
        {
            if (node.TableSampleClause?.SampleNumber is { } sample && values.FindValue(sample, anyBareToken: true) is { } value)
            {
                Add(ForcedParameterizationFindingKind.TableSampleSizeLiteral, value, "TABLESAMPLE size");
            }
        }

        public void OnEnterOutputClause(OutputClause node, ModuleWalker walker)
        {
            foreach (var column in node.SelectColumns.OfType<SelectScalarExpression>().Where(c => values.IsValue(c.Expression)))
            {
                Add(ForcedParameterizationFindingKind.DmlOutputListLiteral, column.Expression, "OUTPUT clause value");
            }
        }

        public void OnEnterConvertCall(ConvertCall node, ModuleWalker walker)
        {
            if (node.Style is { } style && values.FindValue(style, anyBareToken: true) is { } value)
            {
                Add(ForcedParameterizationFindingKind.ConvertStyleCodeLiteral, value, "CONVERT style code");
            }
        }

        private void AddRowCount(ScalarExpression? expression, string label)
        {
            if (expression is not null && values.FindValue(expression, anyBareToken: true) is { } value)
            {
                Add(ForcedParameterizationFindingKind.TopOrPagingLiteral, value, label);
            }
        }

        private void Add(ForcedParameterizationFindingKind kind, TSqlFragment site, string label)
        {
            if (ForcedParameterizationPosition.EnclosingStatement(inner.Fragment, site.StartOffset) is not { } statement
                || ForcedParameterizationPosition.IsRecompiled(statement)
                || ForcedParameterizationPosition.UsesVariables(statement)
                || Findings.Exists(f => f.Kind == kind && f.Line == site.StartLine && f.Column == site.StartColumn))
            {
                return;
            }

            Findings.Add(new ForcedParameterizationFinding(
                kind, moduleName, inner.SourcePath, site.StartLine, site.StartColumn,
                $"A changing {label} is spliced into this dynamic SQL string - the engine leaves that position unparameterized even under PARAMETERIZATION FORCED, so each distinct value compiles its own plan.",
                confidence, SourceText(site)));
        }

        private static string SourceText(TSqlFragment fragment) =>
            string.Concat(Enumerable.Range(fragment.FirstTokenIndex, fragment.LastTokenIndex - fragment.FirstTokenIndex + 1).Select(i => fragment.ScriptTokenStream[i].Text));

        private static IEnumerable<ScalarExpression> FindDirectComparisonOperands(BooleanExpression expression) => expression switch
        {
            BooleanComparisonExpression comparison => [comparison.FirstExpression, comparison.SecondExpression],
            BooleanBinaryExpression binary => FindDirectComparisonOperands(binary.FirstExpression).Concat(FindDirectComparisonOperands(binary.SecondExpression)),
            BooleanParenthesisExpression parenthesis => FindDirectComparisonOperands(parenthesis.Expression),
            _ => [],
        };
    }
}
