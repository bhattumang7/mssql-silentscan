using Microsoft.Data.SqlClient;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;
using SilentScan.Verify.Catalog;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.BoundedStringBuiltinTruncationReplicateResultTruncatedRuleId)]
[Trait("Rule", FindingRuleIds.BoundedStringBuiltinTruncationReplaceResultTruncatedRuleId)]
public sealed class BoundedStringBuiltinUtf8DatabaseOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(BoundedStringBuiltinUtf8DatabaseOracleTests);

    protected override string Ddl => "CREATE TABLE dbo.Anchor (Id INT);";

    protected override async Task SeedAsync()
    {
        SqlConnection.ClearAllPools();
        await using var connection = new SqlConnection(Options.BuildConnectionString());
        await connection.OpenAsync();
        await ExecuteAsync(connection, $"ALTER DATABASE [{DatabaseName}] COLLATE Latin1_General_100_CI_AS_SC_UTF8;");
    }

    [Theory]
    [InlineData("REPLICATE('é', 5000)", true, 8000)]
    [InlineData("REPLICATE('e', 5000)", false, 5000)]
    [InlineData("REPLACE('{0}', 'x', 'yy')", true, 8000)]
    public async Task NonUnicodeCapCountsUtf8Bytes(string expression, bool truncates, int expectedBytes)
    {
        var call = string.Format(
            System.Globalization.CultureInfo.InvariantCulture, expression, new string('é', 3000) + new string('x', 1500));

        Assert.Equal(expectedBytes, await ScalarAsync<int>($"SELECT DATALENGTH({call});"));

        var catalog = await new LiveCatalogReader(Options.BuildConnectionString(DatabaseName)).ReadAsync();
        var parsed = SqlScriptParser.ParseText("probe.sql", $"SELECT {call};");

        Assert.Equal(truncates, BoundedStringBuiltinTruncationScanner.Scan(parsed, catalog).Count > 0);
    }
}
