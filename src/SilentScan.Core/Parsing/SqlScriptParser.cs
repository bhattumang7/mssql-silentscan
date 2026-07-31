using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SilentScan.Core.Parsing;

/// <summary>
/// Thin wrapper around ScriptDOM's TSql160Parser (SQL Server 2022 / compat level 160,
/// matching the pinned Verify/Bench environment). Tolerates and surfaces parse errors
/// rather than throwing, since corpus scanning must survive individual bad files.
/// </summary>
public sealed class SqlScriptParser
{
    private readonly TSql160Parser _parser = new(initialQuotedIdentifiers: true);

    public SqlParseResult Parse(string sourcePath, TextReader reader)
    {
        var fragment = _parser.Parse(reader, out var errors);
        return new SqlParseResult(sourcePath, fragment, errors.ToList());
    }

    public SqlParseResult ParseFile(string path)
    {
        using var reader = new StreamReader(path);
        return Parse(path, reader);
    }

    public SqlParseResult ParseText(string sourcePath, string sql)
    {
        using var reader = new StringReader(sql);
        return Parse(sourcePath, reader);
    }
}
