using SilentScan.Tests.Support;
using SilentScan.Verify.Catalog;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class LiveDefaultSchemaReaderTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(LiveDefaultSchemaReaderTests);

    protected override string Ddl => """
        CREATE SCHEMA sa;
        GO
        CREATE USER u_sa WITHOUT LOGIN WITH DEFAULT_SCHEMA = sa;
        CREATE USER u_plain WITHOUT LOGIN;
        """;

    [Fact]
    public async Task ReadsEveryDefaultSchemaInUse_IncludingDbo()
    {
        var schemas = await new LiveDefaultSchemaReader(Options.BuildConnectionString(DatabaseName)).ReadAsync();

        Assert.Contains("sa", schemas);
        Assert.Contains("dbo", schemas);
    }
}
