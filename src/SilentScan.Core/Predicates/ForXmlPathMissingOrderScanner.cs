using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Catalog;
using SilentScan.Core.Common;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;

namespace SilentScan.Core.Predicates;

public static class ForXmlPathMissingOrderScanner
{
    public static IReadOnlyList<ForXmlPathMissingOrderFinding> Scan(SqlParseResult parseResult) => Scan(parseResult, new DatabaseCatalog());

    public static IReadOnlyList<ForXmlPathMissingOrderFinding> Scan(SqlParseResult parseResult, DatabaseCatalog catalog)
    {
        var rule = CreateRule(parseResult.SourcePath, catalog);
        var walker = new ModuleWalker(parseResult.SourcePath, catalog, EmptyResolvedViews, rules: [rule]);
        parseResult.Fragment.Accept(walker);
        return Harvest(rule);
    }

    internal static Rule CreateRule(string sourcePath, DatabaseCatalog catalog) => new(sourcePath, catalog);

    internal static IReadOnlyList<ForXmlPathMissingOrderFinding> Harvest(Rule rule) =>
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

        public List<ForXmlPathMissingOrderFinding> Findings { get; } = [];

        public void OnEnterQuerySpecificationScope(QuerySpecification node, ScopeChain scopeChain, ModuleWalker walker)
        {
            if (node.ForClause is XmlForClause { Options: { } options }
                && options.Any(o => o.OptionKind == XmlForClauseOptions.Path)
                && node.OrderByClause is null
                && !SingleRowKeyPinning.QueryReadsAtMostOneRow(node, catalog))
            {
                Findings.Add(new ForXmlPathMissingOrderFinding(sourcePath, node.StartLine, node.StartColumn));
            }
        }
    }
}
