using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SilentScan.Core.Parsing;

/// <summary>The outcome of parsing one .sql file: the fragment tree plus any parse errors ScriptDOM tolerated.</summary>
public sealed record SqlParseResult(string SourcePath, TSqlFragment Fragment, IReadOnlyList<ParseError> Errors)
{
    public bool HasErrors => Errors.Count > 0;
}
