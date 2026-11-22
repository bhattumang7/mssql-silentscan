using Microsoft.Data.SqlClient;
using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;
using SilentScan.Verify.Catalog;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.ColumnAnsiPaddingOffRuleId)]
public sealed class AnsiPaddingOffColumnScannerOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(AnsiPaddingOffColumnScannerOracleTests);

    protected override string Ddl => """
        SET ANSI_PADDING OFF;
        CREATE TABLE dbo.NonPadded (Code VARCHAR(20), Bin VARBINARY(20), FixedCode CHAR(10) NULL, FixedBin BINARY(10) NULL, RequiredCode CHAR(10) NOT NULL DEFAULT '', RequiredBin BINARY(10) NOT NULL DEFAULT 0x00, N INT);
        GO
        SET ANSI_PADDING ON;
        CREATE TABLE dbo.Padded (Code VARCHAR(20));
        GO
        """;

    [Fact]
    public async Task VarCharColumn_CreatedWithAnsiPaddingOff_KeepsTrimmingUnderAnsiPaddingOnSession_AndScannerFlagsIt()
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(DatabaseName));
        await connection.OpenAsync();

        await using var seedCommand = new SqlCommand(
            "SET ANSI_PADDING ON; INSERT INTO dbo.NonPadded (Code) VALUES ('abc   ');", connection);
        await seedCommand.ExecuteNonQueryAsync();

        await using var checkCommand = new SqlCommand("SELECT DATALENGTH(Code) FROM dbo.NonPadded;", connection);
        var dataLength = (int)(await checkCommand.ExecuteScalarAsync())!;
        Assert.Equal(3, dataLength);

        var catalog = await new LiveCatalogReader(Options.BuildConnectionString(DatabaseName)).ReadAsync();
        var findings = AnsiPaddingOffColumnScanner.Scan(catalog);

        var finding = Assert.Single(findings, f => f.TableQualifiedName == "dbo.NonPadded" && f.ColumnName == "Code");
        Assert.Equal(FindingConfidence.High, finding.Confidence);
    }

    [Fact]
    public async Task VarBinaryColumn_CreatedWithAnsiPaddingOff_ScannerFlagsIt()
    {
        var catalog = await new LiveCatalogReader(Options.BuildConnectionString(DatabaseName)).ReadAsync();
        var findings = AnsiPaddingOffColumnScanner.Scan(catalog);

        Assert.Contains(findings, f => f.TableQualifiedName == "dbo.NonPadded" && f.ColumnName == "Bin");
    }

    [Theory]
    [InlineData("FixedCode", "'ab  '", 2)]
    [InlineData("FixedBin", "0x4100", 1)]
    public async Task NullableFixedLengthColumn_CreatedWithAnsiPaddingOff_TrimsLikeItsVariableLengthCounterpart_AndScannerFlagsIt(string column, string value, int storedLength)
    {
        await ExecuteAsync($"SET ANSI_PADDING ON; INSERT INTO dbo.NonPadded ({column}) VALUES ({value});");
        Assert.Equal(storedLength, await ScalarAsync<int>($"SELECT TOP (1) DATALENGTH({column}) FROM dbo.NonPadded;"));

        var catalog = await new LiveCatalogReader(Options.BuildConnectionString(DatabaseName)).ReadAsync();
        var findings = AnsiPaddingOffColumnScanner.Scan(catalog);

        Assert.Contains(findings, f => f.TableQualifiedName == "dbo.NonPadded" && f.ColumnName == column);
    }

    [Theory]
    [InlineData("RequiredCode", "'ab  '", 10)]
    [InlineData("RequiredBin", "0x4100", 10)]
    public async Task RequiredFixedLengthColumn_CreatedWithAnsiPaddingOff_StaysPadded_AndIsNeverFlagged(string column, string value, int storedLength)
    {
        await ExecuteAsync($"SET ANSI_PADDING ON; INSERT INTO dbo.NonPadded ({column}) VALUES ({value});");
        Assert.Equal(storedLength, await ScalarAsync<int>($"SELECT TOP (1) DATALENGTH({column}) FROM dbo.NonPadded;"));

        var catalog = await new LiveCatalogReader(Options.BuildConnectionString(DatabaseName)).ReadAsync();
        var findings = AnsiPaddingOffColumnScanner.Scan(catalog);

        Assert.DoesNotContain(findings, f => f.TableQualifiedName == "dbo.NonPadded" && f.ColumnName == column);
    }

    [Fact]
    public async Task NonStringColumn_CreatedWithAnsiPaddingOff_NeverFlagged()
    {
        var catalog = await new LiveCatalogReader(Options.BuildConnectionString(DatabaseName)).ReadAsync();
        var findings = AnsiPaddingOffColumnScanner.Scan(catalog);

        Assert.DoesNotContain(findings, f => f.TableQualifiedName == "dbo.NonPadded" && f.ColumnName == "N");
    }

    [Fact]
    public async Task VarCharColumn_CreatedWithAnsiPaddingOn_NeverFlagged()
    {
        var catalog = await new LiveCatalogReader(Options.BuildConnectionString(DatabaseName)).ReadAsync();
        var findings = AnsiPaddingOffColumnScanner.Scan(catalog);

        Assert.DoesNotContain(findings, f => f.TableQualifiedName == "dbo.Padded");
    }
}
