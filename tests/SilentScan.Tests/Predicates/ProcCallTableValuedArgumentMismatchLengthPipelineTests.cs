using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
public sealed class ProcCallTableValuedArgumentMismatchLengthPipelineTests
{
    private static string CallerSql(string column, string value) => $"""
        CREATE TYPE dbo.tt_Rows AS TABLE (C {column} NOT NULL);
        GO
        CREATE PROCEDURE dbo.usp_Apply @Rows dbo.tt_Rows READONLY
        AS
        BEGIN
            SELECT C FROM @Rows;
        END
        GO
        CREATE PROCEDURE dbo.usp_Caller
        AS
        BEGIN
            DECLARE @rows dbo.tt_Rows;
            INSERT INTO @rows VALUES ({value});
            EXEC dbo.usp_Apply @Rows = @rows;
        END
        """;

    [Theory]
    [InlineData("VARCHAR(3)", "'abcdef'", 0)]
    [InlineData("VARCHAR(3)", "'abc'", 0)]
    [InlineData("DECIMAL(5,1)", "1.26", 1)]
    [InlineData("DECIMAL(5,1)", "1.2", 0)]
    public async Task OnlyTheSilentNarrowingsAreReported_ALongerStringIsAHardErrorAtTheInsert(string column, string value, int expected)
    {
        var report = await EngineAuthoritativeScan.ScanAsync(CallerSql(column, value));

        Assert.Equal(expected, report.Find<ProcCallTableValuedArgumentMismatchFinding>("ProcCallTableValuedArgumentMismatchScanner").Count);
    }
}
