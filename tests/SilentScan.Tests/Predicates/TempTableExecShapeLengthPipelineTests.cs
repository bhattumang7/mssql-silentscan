using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
public sealed class TempTableExecShapeLengthPipelineTests
{
    private static string Sql(string described, string declared) => $"""
        CREATE PROCEDURE dbo.Callee AS SELECT CAST(NULL AS {described}) AS V;
        GO
        CREATE PROCEDURE dbo.Caller AS
        BEGIN
            CREATE TABLE #t (V {declared});
            INSERT INTO #t EXEC dbo.Callee;
            SELECT V FROM #t;
        END
        """;

    [Theory]
    [InlineData("VARCHAR(10)", "VARCHAR(3)", 0)]
    [InlineData("NVARCHAR(10)", "NVARCHAR(3)", 0)]
    [InlineData("DECIMAL(5,2)", "DECIMAL(5,1)", 1)]
    [InlineData("DECIMAL(5,2)", "DECIMAL(5,2)", 0)]
    [InlineData("DATETIME2(3)", "DATE", 1)]
    public async Task OnlyTheSilentNarrowingsAreReported_AStringLengthOverflowIsAHardErrorAtTheInsert(string described, string declared, int expected)
    {
        var report = await EngineAuthoritativeScan.ScanAsync(Sql(described, declared));

        Assert.Equal(expected, report.Find<TempTableExecShapeFinding>("TempTableExecShapeScanner").Count);
    }
}
