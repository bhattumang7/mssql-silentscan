using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Catalog;
using SilentScan.Core.Common;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;

namespace SilentScan.Core.Predicates;

public static class JsonArrayAggMissingOrderScanner
{
    public static IReadOnlyList<JsonArrayAggMissingOrderFinding> Scan(SqlParseResult parseResult) => Scan(parseResult, new DatabaseCatalog());

    public static IReadOnlyList<JsonArrayAggMissingOrderFinding> Scan(SqlParseResult parseResult, DatabaseCatalog catalog)
    {
        var rule = CreateRule(parseResult.SourcePath, catalog);
        var walker = new ModuleWalker(parseResult.SourcePath, catalog, EmptyResolvedViews, rules: [rule]);
        parseResult.Fragment.Accept(walker);
        return Harvest(rule);
    }

    internal static Rule CreateRule(string sourcePath, DatabaseCatalog catalog) => new(sourcePath, catalog);

    internal static IReadOnlyList<JsonArrayAggMissingOrderFinding> Harvest(Rule rule) =>
        [
            .. rule.Findings
                .OrderBy(f => f.SourcePath, StringComparer.Ordinal)
                .ThenBy(f => f.Line)
                .ThenBy(f => f.Column),
        ];

    private static readonly IReadOnlyDictionary<string, ResolvedRelation> EmptyResolvedViews = new Dictionary<string, ResolvedRelation>();

    internal sealed class Rule(string sourcePath, DatabaseCatalog catalog) : IModuleRule
    {
        private readonly Stack<QuerySpecification> _specifications = new();

        public List<JsonArrayAggMissingOrderFinding> Findings { get; } = [];

        public void OnEnterQuerySpecificationScope(QuerySpecification node, ScopeChain scopeChain, ModuleWalker walker) =>
            _specifications.Push(node);

        public void OnLeaveQuerySpecificationScope(QuerySpecification node, ScopeChain scopeChain, ModuleWalker walker) =>
            _specifications.Pop();

        public void OnEnterFunctionCall(FunctionCall node, ModuleWalker walker)
        {
            if (string.Equals(node.FunctionName?.Value, "JSON_ARRAYAGG", StringComparison.OrdinalIgnoreCase)
                && node.JsonOrderByClause is null
                && !(_specifications.TryPeek(out var specification) && SingleRowKeyPinning.QueryReadsAtMostOneRow(specification, catalog)))
            {
                Findings.Add(new JsonArrayAggMissingOrderFinding(sourcePath, node.StartLine, node.StartColumn));
            }
        }
    }
}
