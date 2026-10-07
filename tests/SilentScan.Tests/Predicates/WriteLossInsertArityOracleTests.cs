using SilentScan.Core.Catalog;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", "silentscan/write-loss/approximate-to-exact-truncation")]
public sealed class WriteLossInsertArityOracleTests : OracleTestFixture
{
    private const string TableDdl = """
        CREATE TABLE dbo.WithIdentity (Id INT IDENTITY PRIMARY KEY, Seq INT NULL, Lon FLOAT NULL);
        CREATE TABLE dbo.WithComputed (Seq INT NULL, Doubled AS (Seq * 2), Lon FLOAT NULL);
        """;

    protected override string DatabaseNameSeed => nameof(WriteLossInsertArityOracleTests);

    protected override string Ddl => TableDdl;

    [Fact]
    public async Task PositionalInsertIntoIdentityTable_SkipsIdentityColumn_ScannerAlignsValuesAndEmitsNoFinding()
    {
        const string statement = "DECLARE @seq INT = 5; DECLARE @lon FLOAT = 1.9; INSERT INTO dbo.WithIdentity SELECT @seq, @lon;";

        Assert.Null(await SqlErrorNumberAsync(statement));
        var row = Assert.Single(await RowsAsync("SELECT Seq, Lon FROM dbo.WithIdentity"));
        Assert.Equal(5, row[0]);
        Assert.Equal(1.9d, row[1]);

        Assert.Empty(Extract(statement));
    }

    [Fact]
    public async Task PositionalInsertIntoComputedColumnTable_SkipsComputedColumn_ScannerAlignsValuesAndEmitsNoFinding()
    {
        const string statement = "DECLARE @seq INT = 5; DECLARE @lon FLOAT = 1.9; INSERT INTO dbo.WithComputed SELECT @seq, @lon;";

        Assert.Null(await SqlErrorNumberAsync(statement));
        var row = Assert.Single(await RowsAsync("SELECT Seq, Doubled, Lon FROM dbo.WithComputed"));
        Assert.Equal(5, row[0]);
        Assert.Equal(10, row[1]);
        Assert.Equal(1.9d, row[2]);

        Assert.Empty(Extract(statement));
    }

    [Fact]
    public async Task PositionalInsertIntoIdentityTable_FloatIntoFirstNonIdentityInt_SilentlyTruncates_ScannerFlagsThatColumn()
    {
        const string statement = "DECLARE @lon FLOAT = 1.9; INSERT INTO dbo.WithIdentity SELECT @lon, @lon;";

        Assert.Null(await SqlErrorNumberAsync(statement));
        var row = Assert.Single(await RowsAsync("SELECT Seq, Lon FROM dbo.WithIdentity"));
        Assert.Equal(1, row[0]);
        Assert.Equal(1.9d, row[1]);

        var finding = Assert.Single(Extract(statement));
        Assert.Equal(WriteLossKind.ApproximateToExactTruncation, finding.Kind);
        Assert.Equal("Seq", finding.ColumnName);
    }

    private static IReadOnlyList<WriteLossFinding> Extract(string statement)
    {
        var result = SqlScriptParser.ParseText("test.sql", $"{TableDdl}\nGO\n{statement}");
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        var catalog = CatalogBuilder.Build([result]);
        var lineage = LineageResolver.Resolve(catalog, [result]);
        return TypedPredicateExtractor.Extract(result, catalog, lineage).WriteLossFindings;
    }
}
