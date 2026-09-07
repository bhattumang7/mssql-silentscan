using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Catalog;
using SilentScan.Core.Common;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;

namespace SilentScan.Core.Predicates;

public static class JsonArrayAggMissingOrderScanner
{
    public static IReadOnlyList<JsonArrayAggMissingOrderFinding> Scan(SqlParseResult parseResult)
    {
        var rule = CreateRule(parseResult.SourcePath);
        var walker = new ModuleWalker(parseResult.SourcePath, new DatabaseCatalog(), EmptyResolvedViews, rules: [rule]);
        parseResult.Fragment.Accept(walker);
        return Harvest(rule);
    }

    internal static Rule CreateRule(string sourcePath) => new(sourcePath);

    internal static IReadOnlyList<JsonArrayAggMissingOrderFinding> Harvest(Rule rule) =>
        [
            .. rule.Findings
                .OrderBy(f => f.SourcePath, StringComparer.Ordinal)
                .ThenBy(f => f.Line)
                .ThenBy(f => f.Column),
        ];

    private static readonly IReadOnlyDictionary<string, ResolvedRelation> EmptyResolvedViews = new Dictionary<string, ResolvedRelation>();

    internal sealed class Rule(string sourcePath) : IModuleRule
    {
        public List<JsonArrayAggMissingOrderFinding> Findings { get; } = [];

        public void OnEnterFunctionCall(FunctionCall node, ModuleWalker walker)
        {
            if (string.Equals(node.FunctionName?.Value, "JSON_ARRAYAGG", StringComparison.OrdinalIgnoreCase)
                && node.JsonOrderByClause is null)
            {
                Findings.Add(new JsonArrayAggMissingOrderFinding(sourcePath, node.StartLine, node.StartColumn));
            }
        }
    }
}
