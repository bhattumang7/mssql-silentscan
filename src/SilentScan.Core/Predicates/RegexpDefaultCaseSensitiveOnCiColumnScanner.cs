using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Catalog;
using SilentScan.Core.Common;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;

namespace SilentScan.Core.Predicates;

public static class RegexpDefaultCaseSensitiveOnCiColumnScanner
{
    public static IReadOnlyList<RegexpDefaultCaseSensitiveOnCiColumnFinding> Scan(SqlParseResult parseResult, DatabaseCatalog catalog)
    {
        var rule = CreateRule(parseResult.SourcePath);
        var walker = new ModuleWalker(parseResult.SourcePath, catalog, EmptyResolvedViews, rules: [rule]);
        parseResult.Fragment.Accept(walker);
        return Harvest(rule);
    }

    internal static Rule CreateRule(string sourcePath) => new(sourcePath);

    internal static IReadOnlyList<RegexpDefaultCaseSensitiveOnCiColumnFinding> Harvest(Rule rule) =>
        [
            .. rule.Findings
                .OrderBy(f => f.SourcePath, StringComparer.Ordinal)
                .ThenBy(f => f.Line)
                .ThenBy(f => f.Column),
        ];

    private static readonly IReadOnlyDictionary<string, ResolvedRelation> EmptyResolvedViews = new Dictionary<string, ResolvedRelation>();

    internal sealed class Rule(string sourcePath) : IModuleRule
    {
        public List<RegexpDefaultCaseSensitiveOnCiColumnFinding> Findings { get; } = [];

        public void OnEnterRegexpLikePredicate(RegexpLikePredicate node, ModuleWalker walker) =>
            Inspect("REGEXP_LIKE", node.Text, node.Pattern, node.Flags, node.StartLine, node.StartColumn, walker);

        public void OnEnterFunctionCall(FunctionCall node, ModuleWalker walker)
        {
            var name = node.FunctionName?.Value;
            var flagsIndex = name switch
            {
                _ when string.Equals(name, "REGEXP_REPLACE", StringComparison.OrdinalIgnoreCase) && node.Parameters.Count == 6 => 5,
                _ when string.Equals(name, "REGEXP_REPLACE", StringComparison.OrdinalIgnoreCase) && node.Parameters.Count is >= 3 and < 6 => -1,
                _ when string.Equals(name, "REGEXP_COUNT", StringComparison.OrdinalIgnoreCase) && node.Parameters.Count == 4 => 3,
                _ when string.Equals(name, "REGEXP_COUNT", StringComparison.OrdinalIgnoreCase) && node.Parameters.Count is >= 2 and < 4 => -1,
                _ when string.Equals(name, "REGEXP_SUBSTR", StringComparison.OrdinalIgnoreCase) && node.Parameters.Count is 5 or 6 => 4,
                _ when string.Equals(name, "REGEXP_SUBSTR", StringComparison.OrdinalIgnoreCase) && node.Parameters.Count is >= 2 and < 5 => -1,
                _ => (int?)null,
            };

            if (flagsIndex is not { } index || node.Parameters.Count < 2)
            {
                return;
            }

            var flags = index >= 0 ? node.Parameters[index] : null;
            Inspect(name!.ToUpperInvariant(), node.Parameters[0], node.Parameters[1], flags, node.StartLine, node.StartColumn, walker);
        }

        private void Inspect(
            string functionName, ScalarExpression subject, ScalarExpression pattern, ScalarExpression? flags,
            int line, int column, ModuleWalker walker)
        {
            if (subject is not ColumnReferenceExpression columnRef
                || pattern is not StringLiteral patternLiteral
                || !patternLiteral.Value.Any(char.IsAsciiLetter))
            {
                return;
            }

            if (flags is not null && flags is not StringLiteral)
            {
                return;
            }

            if (flags is StringLiteral flagsLiteral && LastCaseFlag(flagsLiteral.Value) == 'i')
            {
                return;
            }

            var resolved = walker.ResolveCatalogColumn(columnRef, walker.CurrentScopeChain());
            if (resolved is not { } r || r.Column.Type?.Collation is not { IsCaseSensitive: false })
            {
                return;
            }

            Findings.Add(new RegexpDefaultCaseSensitiveOnCiColumnFinding(functionName, r.TableQualifiedName, r.Column.Name, sourcePath, line, column));
        }

        private static char? LastCaseFlag(string flags)
        {
            char? last = null;
            foreach (var c in flags)
            {
                if (c is 'c' or 'i')
                {
                    last = c;
                }
            }

            return last;
        }
    }
}
