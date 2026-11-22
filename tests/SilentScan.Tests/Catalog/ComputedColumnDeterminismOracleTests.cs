using SilentScan.Core.Catalog;
using SilentScan.Core.Parsing;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Catalog;

[Trait("Category", "Oracle")]
public sealed class ComputedColumnDeterminismOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(ComputedColumnDeterminismOracleTests);

    protected override string Ddl => """
        CREATE TABLE dbo.T (Id INT NOT NULL PRIMARY KEY, B VARBINARY(MAX) NULL, S VARCHAR(50) NULL, D DATETIME2 NULL);
        GO
        """;

    [Theory]
    [InlineData("DECOMPRESS(B)")]
    [InlineData("COMPRESS(S)")]
    [InlineData("PARSENAME(S, 1)")]
    [InlineData("ISDATE(S)")]
    [InlineData("DATENAME(MONTH, D)")]
    [InlineData("FORMAT(D, 'yyyy')")]
    [InlineData("UPPER(S)")]
    [InlineData("LEN(S)")]
    [InlineData("DATEPART(YEAR, D)")]
    [InlineData("CRYPT_GEN_RANDOM(4)")]
    [InlineData("PARSE(S AS INT)")]
    [InlineData("TRY_PARSE(S AS INT)")]
    [InlineData("TRY_CAST(S AS INT)")]
    [InlineData("CURRENT_TIMESTAMP")]
    [InlineData("ENCRYPTBYPASSPHRASE('p', S)")]
    [InlineData("DECRYPTBYPASSPHRASE('p', B)")]
    [InlineData("CAST(S AS DATETIME)")]
    [InlineData("CONVERT(VARCHAR(20), D, 100)")]
    [InlineData("CONVERT(VARCHAR(20), D)")]
    [InlineData("CONVERT(VARCHAR(20), D, 12)")]
    [InlineData("CONVERT(VARCHAR(20), D, 23)")]
    [InlineData("CONVERT(VARCHAR(20), D, 25)")]
    [InlineData("CONVERT(VARCHAR(20), D, 21)")]
    [InlineData("CONVERT(VARCHAR(20), D, 112)")]
    [InlineData("CONVERT(VARCHAR(20), D, 126)")]
    [InlineData("CAST(D AS VARCHAR(20))")]
    [InlineData("CONVERT(VARCHAR(20), CAST(D AS DATE), 111)")]
    [InlineData("SUSER_SID()")]
    [InlineData("ISJSON(S)")]
    [InlineData("JSON_VALUE(S, '$.a')")]
    [InlineData("STRING_ESCAPE(S, 'json')")]
    [InlineData("CHECKSUM(S)")]
    [InlineData("BINARY_CHECKSUM(S)")]
    [InlineData("TRIM(S)")]
    [InlineData("CONCAT(S, S)")]
    [InlineData("DATEDIFF(DAY, D, D)")]
    [InlineData("EOMONTH(D)")]
    [InlineData("TRANSLATE(S, 'a', 'b')")]
    [InlineData("ABS(Id)")]
    [InlineData("CHOOSE(Id, S, S)")]
    [InlineData("IIF(Id > 0, S, S)")]
    public async Task DeterminismVerdict_MatchesWhatTheEngineReportsForTheSameComputedColumn(string expression)
    {
        await ExecuteAsync($"ALTER TABLE dbo.T ADD Probe AS {expression};");
        var engineDeterministic = await ScalarAsync<int>("SELECT CAST(COLUMNPROPERTY(OBJECT_ID('dbo.T'), 'Probe', 'IsDeterministic') AS INT);");

        var parsed = SqlScriptParser.ParseText(
            "test.sql",
            $"CREATE TABLE dbo.T (Id INT NOT NULL PRIMARY KEY, B VARBINARY(MAX) NULL, S VARCHAR(50) NULL, D DATETIME2 NULL, Probe AS {expression});");
        Assert.False(parsed.HasErrors, string.Join("; ", parsed.Errors.Select(e => e.Message)));
        var scannerNonDeterministic = CatalogBuilder.Build([parsed]).Find("dbo.T")!.FindColumn("Probe")!.IsComputedNonDeterministic;

        Assert.Equal(engineDeterministic == 0, scannerNonDeterministic);
    }
}
