using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Catalog;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;

namespace SilentScan.Core.Predicates;

public static class SecurityScanner
{
    public static IReadOnlyList<SecurityFinding> Scan(SqlParseResult parseResult)
    {
        var rule = CreateRule(parseResult.SourcePath);
        var walker = new ModuleWalker(parseResult.SourcePath, new DatabaseCatalog(), EmptyResolvedViews, rules: [rule]);
        parseResult.Fragment.Accept(walker);
        return Harvest(rule);
    }

    internal static Rule CreateRule(string sourcePath) => new(sourcePath);

    internal static IReadOnlyList<SecurityFinding> Harvest(Rule rule) =>
        [
            .. rule.Findings
                .OrderBy(f => f.Kind)
                .ThenBy(f => f.SourcePath, StringComparer.Ordinal)
                .ThenBy(f => f.Line)
                .ThenBy(f => f.Column),
        ];

    private static readonly IReadOnlyDictionary<string, ResolvedRelation> EmptyResolvedViews = new Dictionary<string, ResolvedRelation>();

    internal sealed class Rule(string sourcePath) : IModuleRule
    {
        public List<SecurityFinding> Findings { get; } = [];

        public void OnEnterExecutableProcedureReference(ExecutableProcedureReference node, ModuleWalker walker)
        {
            if (node.ProcedureReference?.ProcedureReference?.Name.BaseIdentifier.Value is { } routineName
                && string.Equals(routineName, "sp_invoke_external_rest_endpoint", StringComparison.OrdinalIgnoreCase))
            {
                Findings.Add(new SecurityFinding(
                    SecurityFindingKind.ExternalRestEndpointCall,
                    sourcePath, node.StartLine, node.StartColumn,
                    "\"sp_invoke_external_rest_endpoint\" makes an outbound HTTPS call to an endpoint this statement supplies - a real outbound-network call surface. Make sure the endpoint and any data sent to it are safe/intentional.",
                    FindingConfidence.High));
            }
        }
    }
}
