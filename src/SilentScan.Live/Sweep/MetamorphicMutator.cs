using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Parsing;

namespace SilentScan.Live.Sweep;

public static class MetamorphicMutator
{
    public sealed record Mutation(string Name, string MutatedSql);

    public static IReadOnlyList<Mutation> Mutate(string sql)
    {
        var parseResult = SqlScriptParser.ParseText("metamorphic-mutator", sql);
        if (parseResult.HasErrors)
        {
            return [];
        }

        var tokens = parseResult.Fragment.ScriptTokenStream;
        if (tokens is null || tokens.Count == 0)
        {
            return [];
        }

        var mutations = new List<Mutation>();

        TryAdd(mutations, "identifier-case-flip", RebuildWithTokenEdits(tokens, FlipIdentifierCase));
        TryAdd(mutations, "identifier-bracket-quoting", RebuildWithTokenEdits(tokens, BracketQuoteIdentifier));
        TryAdd(mutations, "comment-injection", RebuildWithTokenEdits(tokens, InjectCommentBeforeKeyword));
        TryAdd(mutations, "blank-line-injection", RebuildWithTokenEdits(tokens, InjectBlankLineBeforeKeyword));

        return mutations;
    }

    private static void TryAdd(List<Mutation> mutations, string name, string? mutatedSql)
    {
        if (mutatedSql is not null && mutatedSql != string.Empty)
        {
            mutations.Add(new Mutation(name, mutatedSql));
        }
    }

    private static string? RebuildWithTokenEdits(IList<TSqlParserToken> tokens, Func<TSqlParserToken, string?> edit)
    {
        var changed = false;
        var result = new System.Text.StringBuilder();

        foreach (var token in tokens)
        {
            var replacement = edit(token);
            if (replacement is not null)
            {
                changed = true;
                result.Append(replacement);
            }
            else
            {
                result.Append(token.Text);
            }
        }

        return changed ? result.ToString() : null;
    }

    private static bool IsPlainIdentifier(TSqlParserToken token) =>
        token.TokenType == TSqlTokenType.Identifier
        && token.Text.Length > 0
        && (char.IsLetter(token.Text[0]) || token.Text[0] == '_')
        && token.Text.All(c => char.IsLetterOrDigit(c) || c == '_');

    private static string? FlipIdentifierCase(TSqlParserToken token)
    {
        if (!IsPlainIdentifier(token))
        {
            return null;
        }

        var hasLower = token.Text.Any(char.IsLower);
        var hasUpper = token.Text.Any(char.IsUpper);
        if (hasLower && !hasUpper)
        {
            return token.Text.ToUpperInvariant();
        }

        if (hasUpper && !hasLower)
        {
            return token.Text.ToLowerInvariant();
        }

        return null;
    }

    private static readonly HashSet<string> BuiltInTypeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "varchar", "nvarchar", "char", "nchar", "varbinary", "binary",
        "decimal", "numeric", "float", "real",
        "int", "bigint", "smallint", "tinyint", "bit",
        "datetime", "datetime2", "date", "time", "datetimeoffset", "smalldatetime",
        "money", "smallmoney", "text", "ntext", "image", "xml",
        "uniqueidentifier", "sql_variant", "hierarchyid", "geography", "geometry",
        "table", "cursor", "max",
    };

    private static string? BracketQuoteIdentifier(TSqlParserToken token) =>
        IsPlainIdentifier(token) && !BuiltInTypeNames.Contains(token.Text) ? $"[{token.Text}]" : null;

    private static readonly HashSet<TSqlTokenType> InjectionKeywords =
    [
        TSqlTokenType.Select, TSqlTokenType.From, TSqlTokenType.Where,
    ];

    private static string? InjectCommentBeforeKeyword(TSqlParserToken token) =>
        InjectionKeywords.Contains(token.TokenType) ? $"/* metamorphic */ {token.Text}" : null;

    private static string? InjectBlankLineBeforeKeyword(TSqlParserToken token) =>
        InjectionKeywords.Contains(token.TokenType) ? $"\n\n{token.Text}" : null;
}
