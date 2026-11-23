using SilentScan.Core.Catalog;
using SilentScan.Core.Diagnostics;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;

namespace SilentScan.Tests.Predicates;

public sealed class ProcCallArgumentMismatchLiteralScannerTests
{
    private const string Callee = "CREATE PROCEDURE dbo.PS @s VARCHAR(3), @d DECIMAL(5,1), @i INT, @n NVARCHAR(3), @dt DATE AS SELECT 1;";

    private static IReadOnlyList<ProcCallArgumentMismatchFinding> Scan(string call)
    {
        var sql = Callee + "\nGO\nCREATE PROCEDURE dbo.Caller AS BEGIN EXEC dbo.PS " + call + "; END";
        var parsed = SqlScriptParser.ParseText("test.sql", sql);
        Assert.False(parsed.HasErrors, string.Join("; ", parsed.Errors.Select(e => e.Message)));

        var catalog = CatalogBuilder.Build([parsed]);
        var graph = ProcCallGraphBuilder.Build([parsed], catalog, new SkipLedger());
        return ProcCallArgumentMismatchScanner.Scan(graph);
    }

    [Theory]
    [InlineData("@s='abcdef', @d=1, @i=1, @n=N'x', @dt='2024-01-01'", true)]
    [InlineData("@s='a', @d=1.26, @i=1, @n=N'x', @dt='2024-01-01'", true)]
    [InlineData("@s='a', @d=1, @i=1.9, @n=N'x', @dt='2024-01-01'", true)]
    [InlineData("@s='a', @d=1, @i=1, @n=N'abcdef', @dt='2024-01-01'", true)]
    [InlineData("@s='a', @d=1, @i=1, @n=N'x', @dt='2024-01-01 10:00'", true)]
    [InlineData("@s='abc', @d=1.2, @i=1, @n=N'xyz', @dt='2024-01-01'", false)]
    [InlineData("@s='a', @d=1.20, @i=1, @n=N'x', @dt='2024-01-01'", false)]
    [InlineData("@s='a', @d=1, @i=2, @n=N'x', @dt='2024-01-01 00:00:00'", false)]
    public void LiteralArgument_FiresExactlyWhenTheEngineSilentlyLosesData(string call, bool fires)
    {
        Assert.Equal(fires, Scan(call).Count > 0);
    }
}
