using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Catalog;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;

namespace SilentScan.Core.Predicates;

public static class RegexpAccentInsensitiveColumnScanner
{
    public static IReadOnlyList<RegexpAccentInsensitiveColumnFinding> Scan(SqlParseResult parseResult, DatabaseCatalog catalog)
    {
        var rule = CreateRule(parseResult.SourcePath);
        var walker = new ModuleWalker(parseResult.SourcePath, catalog, EmptyResolvedViews, rules: [rule]);
        parseResult.Fragment.Accept(walker);
        return Harvest(rule);
    }

    internal static Rule CreateRule(string sourcePath) => new(sourcePath);

    internal static IReadOnlyList<RegexpAccentInsensitiveColumnFinding> Harvest(Rule rule) =>
        [
            .. rule.Findings
                .OrderBy(f => f.SourcePath, StringComparer.Ordinal)
                .ThenBy(f => f.Line)
                .ThenBy(f => f.Column),
        ];

    private static readonly IReadOnlyDictionary<string, ResolvedRelation> EmptyResolvedViews = new Dictionary<string, ResolvedRelation>();

    private static readonly HashSet<string> ScalarFunctionNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "REGEXP_REPLACE", "REGEXP_COUNT", "REGEXP_SUBSTR",
    };

    private static readonly HashSet<string> TvfNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "REGEXP_MATCHES", "REGEXP_SPLIT_TO_TABLE",
    };

    internal sealed class Rule(string sourcePath) : IModuleRule
    {
        public List<RegexpAccentInsensitiveColumnFinding> Findings { get; } = [];

        public void OnEnterRegexpLikePredicate(RegexpLikePredicate node, ModuleWalker walker) =>
            Inspect("REGEXP_LIKE", node.Text, node.Pattern, node.StartLine, node.StartColumn, walker);

        public void OnEnterFunctionCall(FunctionCall node, ModuleWalker walker)
        {
            var name = node.FunctionName?.Value;
            if (name is null || !ScalarFunctionNames.Contains(name) || node.Parameters.Count < 2)
            {
                return;
            }

            Inspect(name.ToUpperInvariant(), node.Parameters[0], node.Parameters[1], node.StartLine, node.StartColumn, walker);
        }

        public void OnEnterFromClause(FromClause node, ModuleWalker walker)
        {
            foreach (var tableReference in node.TableReferences)
            {
                InspectTableReference(tableReference, walker);
            }
        }

        private void InspectTableReference(TableReference tableReference, ModuleWalker walker)
        {
            switch (tableReference)
            {
                case JoinTableReference join:
                    InspectTableReference(join.FirstTableReference, walker);
                    InspectTableReference(join.SecondTableReference, walker);
                    break;

                case JoinParenthesisTableReference parenthesis:
                    InspectTableReference(parenthesis.Join, walker);
                    break;

                case GlobalFunctionTableReference function:
                    InspectTvfReference(function, walker);
                    break;
            }
        }

        private void InspectTvfReference(GlobalFunctionTableReference function, ModuleWalker walker)
        {
            var name = function.Name?.Value;
            if (name is null || !TvfNames.Contains(name) || function.Parameters.Count < 2)
            {
                return;
            }

            Inspect(name.ToUpperInvariant(), function.Parameters[0], function.Parameters[1], function.StartLine, function.StartColumn, walker);
        }

        private void Inspect(
            string functionName, ScalarExpression subject, ScalarExpression pattern,
            int line, int column, ModuleWalker walker)
        {
            if (subject is not ColumnReferenceExpression columnRef
                || pattern is not StringLiteral patternLiteral
                || !patternLiteral.Value.Any(char.IsLetter))
            {
                return;
            }

            var resolved = walker.ResolveCatalogColumn(columnRef, walker.CurrentScopeChain());
            if (resolved is not { } r || r.Column.Type?.Collation is not { IsAccentInsensitive: true })
            {
                return;
            }

            Findings.Add(new RegexpAccentInsensitiveColumnFinding(functionName, r.TableQualifiedName, r.Column.Name, sourcePath, line, column));
        }
    }
}
