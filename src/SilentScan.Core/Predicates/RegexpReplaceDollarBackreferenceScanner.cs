using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Catalog;
using SilentScan.Core.Common;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;

namespace SilentScan.Core.Predicates;

public static class RegexpReplaceDollarBackreferenceScanner
{
    public static IReadOnlyList<RegexpReplaceDollarBackreferenceFinding> Scan(SqlParseResult parseResult)
    {
        var rule = CreateRule(parseResult.SourcePath);
        var walker = new ModuleWalker(parseResult.SourcePath, new DatabaseCatalog(), EmptyResolvedViews, rules: [rule]);
        parseResult.Fragment.Accept(walker);
        return Harvest(rule);
    }

    internal static Rule CreateRule(string sourcePath) => new(sourcePath);

    internal static IReadOnlyList<RegexpReplaceDollarBackreferenceFinding> Harvest(Rule rule) =>
        [
            .. rule.Findings
                .OrderBy(f => f.SourcePath, StringComparer.Ordinal)
                .ThenBy(f => f.Line)
                .ThenBy(f => f.Column)
                .ThenBy(f => f.DollarToken, StringComparer.Ordinal),
        ];

    private static readonly IReadOnlyDictionary<string, ResolvedRelation> EmptyResolvedViews = new Dictionary<string, ResolvedRelation>();

    internal sealed class Rule(string sourcePath) : IModuleRule
    {
        public List<RegexpReplaceDollarBackreferenceFinding> Findings { get; } = [];

        public void OnEnterFunctionCall(FunctionCall node, ModuleWalker walker)
        {
            if (!string.Equals(node.FunctionName?.Value, "REGEXP_REPLACE", StringComparison.OrdinalIgnoreCase)
                || node.Parameters.Count < 3
                || node.Parameters[1] is not StringLiteral patternLiteral
                || node.Parameters[2] is not StringLiteral replacementLiteral)
            {
                return;
            }

            var groupCount = CountCaptureGroups(patternLiteral.Value);
            if (groupCount == 0)
            {
                return;
            }

            foreach (var token in FindDollarBackreferenceTokens(replacementLiteral.Value, groupCount))
            {
                Findings.Add(new RegexpReplaceDollarBackreferenceFinding(token, sourcePath, node.StartLine, node.StartColumn));
            }
        }

        private static int CountCaptureGroups(string pattern)
        {
            var groupCount = 0;
            var inCharacterClass = false;
            var i = 0;

            while (i < pattern.Length)
            {
                var c = pattern[i];

                if (inCharacterClass)
                {
                    if (c == '\\' && i + 1 < pattern.Length)
                    {
                        i += 2;
                        continue;
                    }

                    if (c == ']')
                    {
                        inCharacterClass = false;
                    }

                    i++;
                    continue;
                }

                if (c == '\\' && i + 1 < pattern.Length)
                {
                    i += 2;
                    continue;
                }

                if (c == '[')
                {
                    inCharacterClass = true;
                    i++;
                    if (i < pattern.Length && pattern[i] == '^')
                    {
                        i++;
                    }

                    if (i < pattern.Length && pattern[i] == ']')
                    {
                        i++;
                    }

                    continue;
                }

                if (c == '(')
                {
                    if (i + 2 < pattern.Length && pattern[i + 1] == '?' && pattern[i + 2] == ':')
                    {
                        i += 3;
                        continue;
                    }

                    groupCount++;
                }

                i++;
            }

            return groupCount;
        }

        private static List<string> FindDollarBackreferenceTokens(string replacement, int groupCount)
        {
            var tokens = new List<string>();
            var i = 0;

            while (i < replacement.Length)
            {
                if (replacement[i] != '$')
                {
                    i++;
                    continue;
                }

                var digitsStart = i + 1;
                var digitsEnd = digitsStart;
                while (digitsEnd < replacement.Length && char.IsAsciiDigit(replacement[digitsEnd]))
                {
                    digitsEnd++;
                }

                if (digitsEnd == digitsStart)
                {
                    i++;
                    continue;
                }

                var digits = replacement[digitsStart..digitsEnd];
                if (int.TryParse(digits, out var groupNumber) && groupNumber >= 1 && groupNumber <= groupCount)
                {
                    var token = "$" + digits;
                    if (!tokens.Contains(token))
                    {
                        tokens.Add(token);
                    }
                }

                i = digitsEnd;
            }

            return tokens;
        }
    }
}
