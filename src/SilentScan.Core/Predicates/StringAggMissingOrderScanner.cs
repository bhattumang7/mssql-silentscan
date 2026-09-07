using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Catalog;
using SilentScan.Core.Common;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;

namespace SilentScan.Core.Predicates;

public static class StringAggMissingOrderScanner
{
    public static IReadOnlyList<StringAggMissingOrderFinding> Scan(SqlParseResult parseResult)
    {
        var rule = CreateRule(parseResult.SourcePath);
        var walker = new ModuleWalker(parseResult.SourcePath, new DatabaseCatalog(), EmptyResolvedViews, rules: [rule]);
        parseResult.Fragment.Accept(walker);
        return Harvest(rule);
    }

    internal static Rule CreateRule(string sourcePath) => new(sourcePath);

    internal static IReadOnlyList<StringAggMissingOrderFinding> Harvest(Rule rule) =>
        [
            .. rule.Findings
                .OrderBy(f => f.SourcePath, StringComparer.Ordinal)
                .ThenBy(f => f.Line)
                .ThenBy(f => f.Column),
        ];

    private static readonly IReadOnlyDictionary<string, ResolvedRelation> EmptyResolvedViews = new Dictionary<string, ResolvedRelation>();

    internal sealed class Rule(string sourcePath) : IModuleRule
    {
        public List<StringAggMissingOrderFinding> Findings { get; } = [];

        public void OnEnterFunctionCall(FunctionCall node, ModuleWalker walker)
        {
            if (string.Equals(node.FunctionName?.Value, "STRING_AGG", StringComparison.OrdinalIgnoreCase)
                && node.WithinGroupClause is null)
            {
                Findings.Add(new StringAggMissingOrderFinding(sourcePath, node.StartLine, node.StartColumn));
            }
        }
    }
}
