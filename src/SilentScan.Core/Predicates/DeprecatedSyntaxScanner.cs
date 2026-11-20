using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Catalog;
using SilentScan.Core.Common;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;

namespace SilentScan.Core.Predicates;

public static class DeprecatedSyntaxScanner
{
    public static IReadOnlyList<DeprecatedSyntaxFinding> Scan(SqlParseResult parseResult, DatabaseCatalog? catalog = null)
    {
        var rule = CreateRule(parseResult, catalog);
        var walker = new ModuleWalker(parseResult.SourcePath, catalog ?? new DatabaseCatalog(), EmptyResolvedViews, rules: [rule]);
        parseResult.Fragment.Accept(walker);
        return Harvest(parseResult, rule);
    }

    internal static Rule CreateRule(SqlParseResult parseResult, DatabaseCatalog? catalog = null)
    {
        var moduleQualifiedName = TryGetModuleQualifiedName(parseResult.Fragment);
        var ansiNullsIsOff = catalog is not null
            && moduleQualifiedName is { } qualifiedName
            && catalog.TryGetModuleUsesAnsiNulls(qualifiedName, out var usesAnsiNulls)
            && !usesAnsiNulls;

        var isAdHocScript = moduleQualifiedName is null && HasNoModuleDefinition(parseResult.Fragment);

        return new Rule(parseResult.SourcePath, ansiNullsIsOff, skipComparisonFindings: isAdHocScript);
    }

    internal static IReadOnlyList<DeprecatedSyntaxFinding> Harvest(SqlParseResult parseResult, Rule rule)
    {
        var findings = new List<DeprecatedSyntaxFinding>();

        findings.AddRange(rule.Findings);

        var moduleQualifiedName = TryGetModuleQualifiedName(parseResult.Fragment);
        var isAdHocScript = moduleQualifiedName is null && HasNoModuleDefinition(parseResult.Fragment);
        if (isAdHocScript && parseResult.Fragment is TSqlScript script)
        {
            findings.AddRange(ScanAdHocAnsiNullComparisons(script, parseResult.SourcePath));
        }

        return
        [
            .. findings
                .OrderBy(f => f.Kind)
                .ThenBy(f => f.SourcePath, StringComparer.Ordinal)
                .ThenBy(f => f.Line)
                .ThenBy(f => f.Column),
        ];
    }

    private static string? TryGetModuleQualifiedName(TSqlFragment fragment)
    {
        if (fragment is not TSqlScript script)
        {
            return null;
        }

        var collector = new ModuleNameCollector();
        script.Accept(collector);
        return collector.Names is [{ } only] ? only : null;
    }

    private static bool HasNoModuleDefinition(TSqlFragment fragment)
    {
        if (fragment is not TSqlScript script)
        {
            return false;
        }

        var collector = new ModuleNameCollector();
        script.Accept(collector);
        return collector.Names.Count == 0;
    }

    private static List<DeprecatedSyntaxFinding> ScanAdHocAnsiNullComparisons(TSqlScript script, string sourcePath)
    {
        var policy = new AnsiNullsFlowPolicy(sourcePath);
        var state = default(AnsiNullsFlowState);

        foreach (var batch in script.Batches)
        {
            state = ProcedureBodyFlowWalker.Walk(batch.Statements, state with { Depth = 0 }, policy);
        }

        return policy.Findings;
    }

    private readonly record struct AnsiNullsFlowState(bool IsOff, bool RestoreIsOff, int Depth);

    private sealed class AnsiNullsFlowPolicy(string sourcePath) : IStatementFlowPolicy<AnsiNullsFlowState>
    {
        public List<DeprecatedSyntaxFinding> Findings { get; } = [];

        public bool IsDeclined(AnsiNullsFlowState state) => false;

        public bool IsDone(AnsiNullsFlowState state) => false;

        public AnsiNullsFlowState PerStatement(TSqlStatement statement, AnsiNullsFlowState state)
        {
            if (statement is PredicateSetStatement { Options: var options, IsOn: var isOn } && (options & SetOptions.AnsiNulls) != 0)
            {
                return state with { IsOff = !isOn };
            }

            if (statement is BeginEndBlockStatement or IfStatement or WhileStatement or TryCatchStatement)
            {
                return state;
            }

            var walker = new ComparisonFindingVisitor(sourcePath, state.IsOff);
            statement.Accept(walker);
            Findings.AddRange(walker.Findings);
            return state;
        }

        public AnsiNullsFlowState OnReturn(AnsiNullsFlowState state, TSqlStatement statement) => state;

        public AnsiNullsFlowState OnThrow(AnsiNullsFlowState state) => state;

        public AnsiNullsFlowState OnGoTo(AnsiNullsFlowState state) => state;

        public AnsiNullsFlowState CloneForBranch(AnsiNullsFlowState state) =>
            state with { RestoreIsOff = state.IsOff, Depth = state.Depth + 1 };

        public AnsiNullsFlowState Merge(AnsiNullsFlowState a, AnsiNullsFlowState b)
        {
            var winner = a.Depth >= b.Depth ? a : b;
            return new AnsiNullsFlowState(winner.RestoreIsOff, winner.RestoreIsOff, winner.Depth - 1);
        }
    }

    private static readonly IReadOnlyDictionary<string, ResolvedRelation> EmptyResolvedViews = new Dictionary<string, ResolvedRelation>();

    private static bool IsNullLiteral(ScalarExpression expression) =>
        expression is NullLiteral;

    private static (DeprecatedSyntaxFindingKind Kind, string Detail)? ClassifyComparison(
        BooleanComparisonType comparisonType, bool comparesToNull, bool ansiNullsIsOff)
    {
        switch (comparisonType)
        {
            case BooleanComparisonType.Equals when comparesToNull && !ansiNullsIsOff:
                return (DeprecatedSyntaxFindingKind.EqualsNullComparison,
                    "\"= NULL\" never matches any row under the default ANSI_NULLS ON session setting, including a genuinely NULL value - use \"IS NULL\".");

            case BooleanComparisonType.NotEqualToBrackets or BooleanComparisonType.NotEqualToExclamation
                when comparesToNull && !ansiNullsIsOff:
                return (DeprecatedSyntaxFindingKind.NotEqualsNullComparison,
                    "\"<> NULL\"/\"!= NULL\" never matches any row under the default ANSI_NULLS ON session setting - use \"IS NOT NULL\".");

            default:
                return null;
        }
    }

    private static DeprecatedSyntaxFinding BuildComparisonFinding(DeprecatedSyntaxFindingKind kind, TSqlFragment node, string detail, string sourcePath) =>
        new(kind, sourcePath, sourcePath, node.StartLine, node.StartColumn, detail,
            FindingConfidence.High);

    private sealed class ComparisonFindingVisitor(string sourcePath, bool ansiNullsIsOff) : TSqlFragmentVisitor
    {
        public List<DeprecatedSyntaxFinding> Findings { get; } = [];

        public override void ExplicitVisit(BooleanComparisonExpression node)
        {
            var comparesToNull = IsNullLiteral(node.SecondExpression) || IsNullLiteral(node.FirstExpression);
            if (ClassifyComparison(node.ComparisonType, comparesToNull, ansiNullsIsOff) is { } result)
            {
                Findings.Add(BuildComparisonFinding(result.Kind, node, result.Detail, sourcePath));
            }

            base.ExplicitVisit(node);
        }
    }

    internal sealed class Rule : IModuleRule
    {
        private readonly string sourcePath;
        private readonly bool ansiNullsIsOff;
        private readonly bool skipComparisonFindings;

        public Rule(string sourcePath, bool ansiNullsIsOff, bool skipComparisonFindings = false)
        {
            this.sourcePath = sourcePath;
            this.ansiNullsIsOff = ansiNullsIsOff;
            this.skipComparisonFindings = skipComparisonFindings;
        }

        public List<DeprecatedSyntaxFinding> Findings { get; } = [];

        public void OnBooleanComparisonExpression(BooleanComparisonExpression node, ModuleWalker walker)
        {
            var comparesToNull = IsNullLiteral(node.SecondExpression) || IsNullLiteral(node.FirstExpression);
            if (!skipComparisonFindings && ClassifyComparison(node.ComparisonType, comparesToNull, ansiNullsIsOff) is { } result)
            {
                Add(result.Kind, node, result.Detail);
            }
        }

        private void Add(DeprecatedSyntaxFindingKind kind, TSqlFragment node, string detail) =>
            Findings.Add(BuildComparisonFinding(kind, node, detail, sourcePath));
    }
}
