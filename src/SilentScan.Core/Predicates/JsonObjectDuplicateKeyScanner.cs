using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Catalog;
using SilentScan.Core.Common;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;

namespace SilentScan.Core.Predicates;

public static class JsonObjectDuplicateKeyScanner
{
    public static IReadOnlyList<JsonObjectDuplicateKeyFinding> Scan(SqlParseResult parseResult)
    {
        var rule = CreateRule(parseResult.SourcePath);
        var walker = new ModuleWalker(parseResult.SourcePath, new DatabaseCatalog(), EmptyResolvedViews, rules: [rule]);
        parseResult.Fragment.Accept(walker);
        return Harvest(rule);
    }

    internal static Rule CreateRule(string sourcePath) => new(sourcePath);

    internal static IReadOnlyList<JsonObjectDuplicateKeyFinding> Harvest(Rule rule) =>
        [
            .. rule.Findings
                .OrderBy(f => f.SourcePath, StringComparer.Ordinal)
                .ThenBy(f => f.Line)
                .ThenBy(f => f.Column)
                .ThenBy(f => f.DuplicateKey, StringComparer.Ordinal),
        ];

    private static readonly IReadOnlyDictionary<string, ResolvedRelation> EmptyResolvedViews = new Dictionary<string, ResolvedRelation>();

    internal sealed class Rule(string sourcePath) : IModuleRule
    {
        public List<JsonObjectDuplicateKeyFinding> Findings { get; } = [];

        public void OnEnterFunctionCall(FunctionCall node, ModuleWalker walker)
        {
            if (!string.Equals(node.FunctionName?.Value, "JSON_OBJECT", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var duplicateKeys = node.JsonParameters
                .Select(p => p.JsonKeyName as StringLiteral)
                .Where(literal => literal is not null)
                .GroupBy(literal => literal!.Value, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key);

            foreach (var duplicateKey in duplicateKeys)
            {
                Findings.Add(new JsonObjectDuplicateKeyFinding(duplicateKey, sourcePath, node.StartLine, node.StartColumn));
            }
        }
    }
}
