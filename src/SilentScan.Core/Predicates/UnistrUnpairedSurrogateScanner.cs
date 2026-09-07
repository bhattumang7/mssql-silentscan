using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Catalog;
using SilentScan.Core.Common;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;

namespace SilentScan.Core.Predicates;

public static class UnistrUnpairedSurrogateScanner
{
    public static IReadOnlyList<UnistrUnpairedSurrogateFinding> Scan(SqlParseResult parseResult)
    {
        var rule = CreateRule(parseResult.SourcePath);
        var walker = new ModuleWalker(parseResult.SourcePath, new DatabaseCatalog(), EmptyResolvedViews, rules: [rule]);
        parseResult.Fragment.Accept(walker);
        return Harvest(rule);
    }

    internal static Rule CreateRule(string sourcePath) => new(sourcePath);

    internal static IReadOnlyList<UnistrUnpairedSurrogateFinding> Harvest(Rule rule) =>
        [
            .. rule.Findings
                .OrderBy(f => f.SourcePath, StringComparer.Ordinal)
                .ThenBy(f => f.Line)
                .ThenBy(f => f.Column),
        ];

    private static readonly IReadOnlyDictionary<string, ResolvedRelation> EmptyResolvedViews = new Dictionary<string, ResolvedRelation>();

    internal sealed class Rule(string sourcePath) : IModuleRule
    {
        public List<UnistrUnpairedSurrogateFinding> Findings { get; } = [];

        public void OnEnterFunctionCall(FunctionCall node, ModuleWalker walker)
        {
            if (!string.Equals(node.FunctionName?.Value, "UNISTR", StringComparison.OrdinalIgnoreCase)
                || node.Parameters.Count != 1
                || node.Parameters[0] is not StringLiteral literal)
            {
                return;
            }

            foreach (var unpaired in FindUnpairedSurrogates(literal.Value))
            {
                Findings.Add(new UnistrUnpairedSurrogateFinding(sourcePath, node.StartLine, node.StartColumn, unpaired.Text));
            }
        }

        private static List<UnistrEscapeParser.UnicodeEscape> FindUnpairedSurrogates(string value)
        {
            var escapes = UnistrEscapeParser.ParseEscapes(value);
            var result = new List<UnistrEscapeParser.UnicodeEscape>();

            for (var i = 0; i < escapes.Count; i++)
            {
                var current = escapes[i];
                if (current.CodePoint is < 0xD800 or > 0xDFFF)
                {
                    continue;
                }

                if (current.CodePoint is >= 0xD800 and <= 0xDBFF
                    && i + 1 < escapes.Count
                    && escapes[i + 1].StartIndex == current.EndIndex
                    && escapes[i + 1].CodePoint is >= 0xDC00 and <= 0xDFFF)
                {
                    i++;
                    continue;
                }

                result.Add(current);
            }

            return result;
        }
    }
}
