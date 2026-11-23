using SilentScan.Core.Catalog;
using SilentScan.Core.Diagnostics;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;

namespace SilentScan.Tests.Predicates;

public sealed class SpExecuteSqlParameterMismatchLiteralScannerTests
{
    private static IReadOnlyList<SpExecuteSqlParameterMismatchFinding> Scan(string declared, string actual)
    {
        var sql = $"CREATE PROCEDURE dbo.Caller AS EXEC sp_executesql N'SELECT @p', N'@p {declared}', @p = {actual};";
        var parsed = SqlScriptParser.ParseText("test.sql", sql);
        Assert.False(parsed.HasErrors, string.Join("; ", parsed.Errors.Select(e => e.Message)));
        var catalog = CatalogBuilder.Build([parsed]);
        var graph = ProcCallGraphBuilder.Build([parsed], catalog, new SkipLedger());
        return SpExecuteSqlParameterMismatchScanner.Scan(graph);
    }

    [Theory]
    [InlineData("VARCHAR(3)", "'abcdef'", true)]
    [InlineData("VARCHAR(3)", "'abc'", false)]
    [InlineData("DECIMAL(5,1)", "1.26", true)]
    [InlineData("DECIMAL(5,1)", "1.20", false)]
    [InlineData("INT", "1.9", true)]
    [InlineData("DATE", "'2024-01-01 10:00'", true)]
    [InlineData("DATE", "'2024-01-01'", false)]
    [InlineData("NVARCHAR(3)", "N'abcdef'", true)]
    [InlineData("NVARCHAR(3)", "N'abc'", false)]
    public void LiteralBinding_FiresExactlyWhenTheEngineSilentlyLosesData(string declared, string actual, bool fires)
    {
        Assert.Equal(fires, Scan(declared, actual).Count > 0);
    }
}
