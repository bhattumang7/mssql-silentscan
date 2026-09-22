using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Parsing;

namespace SilentScan.Live.Sweep;

public static class ModuleBatchNormalizer
{
    private static readonly HashSet<string> ModuleKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "PROCEDURE",
        "PROC",
        "VIEW",
        "FUNCTION",
        "TRIGGER",
    };

    public static string Normalize(string sql) =>
        string.Join("\nGO\n", GoBatchSplitter.Split(sql).SelectMany(SplitBeforeModuleDefinitions));

    private static List<string> SplitBeforeModuleDefinitions(string batch)
    {
        var parser = new TSql180Parser(initialQuotedIdentifiers: true);
        using var reader = new StringReader(batch);
        var significant = parser.GetTokenStream(reader, out _)
            .Where(token => token.TokenType is not (TSqlTokenType.WhiteSpace or TSqlTokenType.SingleLineComment or TSqlTokenType.MultilineComment))
            .ToList();

        var cutOffsets = new List<int>();
        for (var i = 1; i < significant.Count; i++)
        {
            if (StartsModuleDefinition(significant, i))
            {
                cutOffsets.Add(significant[i].Offset);
            }
        }

        var pieces = new List<string>();
        var previous = 0;
        foreach (var offset in cutOffsets)
        {
            pieces.Add(batch[previous..offset].Trim());
            previous = offset;
        }

        pieces.Add(batch[previous..].Trim());
        return pieces;
    }

    private static bool StartsModuleDefinition(List<TSqlParserToken> tokens, int index)
    {
        if (!IsWord(tokens, index, "CREATE") && !IsWord(tokens, index, "ALTER"))
        {
            return false;
        }

        if (IsWord(tokens, index, "ALTER") && IsWord(tokens, index - 1, "OR"))
        {
            return false;
        }

        var next = index + 1;
        if (IsWord(tokens, next, "OR") && IsWord(tokens, next + 1, "ALTER"))
        {
            next += 2;
        }

        return next < tokens.Count && tokens[next].Text is { } text && ModuleKeywords.Contains(text);
    }

    private static bool IsWord(List<TSqlParserToken> tokens, int index, string word) =>
        index < tokens.Count && string.Equals(tokens[index].Text, word, StringComparison.OrdinalIgnoreCase);
}
