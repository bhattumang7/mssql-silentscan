using Microsoft.Data.SqlClient;
using SilentScan.Core.Catalog;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;
using SilentScan.Core.Reporting;
using SilentScan.Core.Rules;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.WriteLossNumericScaleNarrowingRuleId)]
public sealed class InsertExecWriteLossOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(InsertExecWriteLossOracleTests);

    protected override string Ddl => string.Empty;

    [Theory]
    [InlineData("DECLARE @t TABLE (Amount DECIMAL(10,2) NOT NULL); INSERT INTO @t EXEC dbo.usp_Get; SELECT Amount FROM @t;", 1.23)]
    [InlineData("INSERT INTO dbo.Dst EXEC dbo.usp_Get; SELECT Amount FROM dbo.Dst;", 1.23)]
    public async Task InsertExecIntoANarrowerDecimal_RoundsSilently_ScannerFlagsTheColumn(string body, double expected)
    {
        const string setup = """
            CREATE TABLE dbo.Dst (Amount DECIMAL(10,2) NOT NULL);
            GO
            CREATE PROCEDURE dbo.usp_Get AS SELECT CAST(1.23456 AS DECIMAL(10,5)) AS Amount;
            GO
            """;
        await using var connection = new SqlConnection(Options.BuildConnectionString(DatabaseName));
        await connection.OpenAsync();
        foreach (var batch in setup.Split("GO", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            await using var create = new SqlCommand(batch, connection);
            await create.ExecuteNonQueryAsync();
        }

        await using var command = new SqlCommand(body, connection);
        Assert.Equal((decimal)expected, (decimal)(await command.ExecuteScalarAsync())!);

        var script = SqlScriptParser.ParseText("insertexec.sql", setup + Environment.NewLine + "CREATE PROCEDURE dbo.usp_Caller AS BEGIN " + body + " END");
        Assert.False(script.HasErrors, string.Join("; ", script.Errors.Select(e => e.Message)));
        var report = ScanReportBuilder.BuildFromParseResults([script], CatalogBuilder.Build([script]));

        var finding = Assert.Single(report.Find<WriteLossFinding>("TypedPredicateExtractor"));
        Assert.Equal(WriteLossKind.NumericScaleNarrowing, finding.Kind);
        Assert.Equal("Amount", finding.ColumnName);
    }
}
