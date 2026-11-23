using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Catalog;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;

namespace SilentScan.Core.Predicates;

public static class WindowFrameScanner
{
    public static IReadOnlyList<WindowFrameFinding> Scan(SqlParseResult parseResult)
    {
        var rule = CreateRule(parseResult.SourcePath);
        var walker = new ModuleWalker(parseResult.SourcePath, new DatabaseCatalog(), EmptyResolvedViews, rules: [rule]);
        parseResult.Fragment.Accept(walker);
    return Harvest(rule);
    }
    internal static Rule CreateRule(string sourcePath) => new(sourcePath);

    internal static IReadOnlyList<WindowFrameFinding> Harvest(Rule rule) =>
            [
            .. rule.Findings
                .OrderBy(f => f.Kind)
                .ThenBy(f => f.SourcePath, StringComparer.Ordinal)
                .ThenBy(f => f.Line)
                .ThenBy(f => f.Column),
        ];


    private static readonly IReadOnlyDictionary<string, ResolvedRelation> EmptyResolvedViews = new Dictionary<string, ResolvedRelation>();

    private static readonly HashSet<string> FrameIncapableFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "ROW_NUMBER",
        "RANK",
        "DENSE_RANK",
        "NTILE",
        "LAG",
        "LEAD",
        "PERCENT_RANK",
        "CUME_DIST",
    };

    internal sealed class Rule(string sourcePath) : IModuleRule
    {
        public List<WindowFrameFinding> Findings { get; } = [];

        public void OnEnterQuerySpecificationScope(QuerySpecification node, ScopeChain scopeChain, ModuleWalker walker)
        {
            if (node.WindowClause is not { WindowDefinition: { Count: > 0 } definitions })
            {
                return;
            }

            var unframed = definitions
                .Where(d => d is { RefWindowName: null, OrderByClause: not null, WindowFrameClause: null })
                .Select(d => d.WindowName.Value)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (unframed.Count == 0)
            {
                return;
            }

            var collector = new NamedOverCollector();
            foreach (var element in node.SelectElements)
            {
                element.Accept(collector);
            }

            foreach (var call in collector.Calls)
            {
                if (call.OverClause is { WindowName: { } name, OrderByClause: null, WindowFrameClause: null } over
                    && unframed.Contains(name.Value)
                    && !FrameIncapableFunctions.Contains(call.FunctionName?.Value ?? string.Empty))
                {
                    Findings.Add(new WindowFrameFinding(
                        WindowFrameFindingKind.ImplicitDefaultRangeFrame, sourcePath, over.StartLine, over.StartColumn));
                }
            }
        }

        public void OnEnterFunctionCall(FunctionCall node, ModuleWalker walker)
        {
            if (node.OverClause is not { OrderByClause: not null } overClause
                || FrameIncapableFunctions.Contains(node.FunctionName?.Value ?? string.Empty))
            {
                return;
            }

            if (overClause.WindowFrameClause is null)
            {
                Findings.Add(new WindowFrameFinding(
                    WindowFrameFindingKind.ImplicitDefaultRangeFrame, sourcePath, overClause.StartLine, overClause.StartColumn));
            }
        }

        private sealed class NamedOverCollector : TSqlFragmentVisitor
        {
            public List<FunctionCall> Calls { get; } = [];

            public override void ExplicitVisit(QuerySpecification node)
            {
            }

            public override void ExplicitVisit(FunctionCall node)
            {
                if (node.OverClause?.WindowName is not null)
                {
                    Calls.Add(node);
                }

                base.ExplicitVisit(node);
            }
        }
    }
}
