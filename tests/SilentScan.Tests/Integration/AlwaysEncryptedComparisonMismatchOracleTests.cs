using Microsoft.Data.SqlClient;
using SilentScan.Core.Catalog;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class AlwaysEncryptedComparisonMismatchOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(AlwaysEncryptedComparisonMismatchOracleTests);

    private const string ScannerDdl = """
        CREATE TABLE dbo.Customer
        (
            CustomerId INT NOT NULL PRIMARY KEY,
            DetA       NVARCHAR(20) COLLATE Latin1_General_BIN2
                ENCRYPTED WITH (COLUMN_ENCRYPTION_KEY = AecmCekA, ENCRYPTION_TYPE = DETERMINISTIC, ALGORITHM = 'AEAD_AES_256_CBC_HMAC_SHA_256') NOT NULL,
            DetA2      NVARCHAR(20) COLLATE Latin1_General_BIN2
                ENCRYPTED WITH (COLUMN_ENCRYPTION_KEY = AecmCekA, ENCRYPTION_TYPE = DETERMINISTIC, ALGORITHM = 'AEAD_AES_256_CBC_HMAC_SHA_256') NOT NULL,
            DetB       NVARCHAR(20) COLLATE Latin1_General_BIN2
                ENCRYPTED WITH (COLUMN_ENCRYPTION_KEY = AecmCekB, ENCRYPTION_TYPE = DETERMINISTIC, ALGORITHM = 'AEAD_AES_256_CBC_HMAC_SHA_256') NOT NULL,
            RndA       NVARCHAR(20) COLLATE Latin1_General_BIN2
                ENCRYPTED WITH (COLUMN_ENCRYPTION_KEY = AecmCekA, ENCRYPTION_TYPE = RANDOMIZED, ALGORITHM = 'AEAD_AES_256_CBC_HMAC_SHA_256') NOT NULL,
            RndA2      NVARCHAR(20) COLLATE Latin1_General_BIN2
                ENCRYPTED WITH (COLUMN_ENCRYPTION_KEY = AecmCekA, ENCRYPTION_TYPE = RANDOMIZED, ALGORITHM = 'AEAD_AES_256_CBC_HMAC_SHA_256') NOT NULL,
            RndEncA    NVARCHAR(20) COLLATE Latin1_General_BIN2
                ENCRYPTED WITH (COLUMN_ENCRYPTION_KEY = AecmEnclaveCek, ENCRYPTION_TYPE = RANDOMIZED, ALGORITHM = 'AEAD_AES_256_CBC_HMAC_SHA_256') NOT NULL,
            RndEncA2   NVARCHAR(20) COLLATE Latin1_General_BIN2
                ENCRYPTED WITH (COLUMN_ENCRYPTION_KEY = AecmEnclaveCek, ENCRYPTION_TYPE = RANDOMIZED, ALGORITHM = 'AEAD_AES_256_CBC_HMAC_SHA_256') NOT NULL,
            PlainName  NVARCHAR(100) NOT NULL
        );
        """;

    private const string KeyDdl = """
        CREATE COLUMN MASTER KEY AecmCmk
        WITH (KEY_STORE_PROVIDER_NAME = 'MSSQL_CERTIFICATE_STORE', KEY_PATH = 'CurrentUser/My/5555555555555555555555555555555555555555');
        GO
        CREATE COLUMN MASTER KEY AecmEnclaveCmk
        WITH (KEY_STORE_PROVIDER_NAME = 'MSSQL_CERTIFICATE_STORE', KEY_PATH = 'CurrentUser/My/6666666666666666666666666666666666666666',
              ENCLAVE_COMPUTATIONS (SIGNATURE = 0x01020304));
        GO
        CREATE COLUMN ENCRYPTION KEY AecmCekA
        WITH VALUES (COLUMN_MASTER_KEY = AecmCmk, ALGORITHM = 'RSA_OAEP', ENCRYPTED_VALUE = 0x01000000);
        GO
        CREATE COLUMN ENCRYPTION KEY AecmCekB
        WITH VALUES (COLUMN_MASTER_KEY = AecmCmk, ALGORITHM = 'RSA_OAEP', ENCRYPTED_VALUE = 0x02000000);
        GO
        CREATE COLUMN ENCRYPTION KEY AecmEnclaveCek
        WITH VALUES (COLUMN_MASTER_KEY = AecmEnclaveCmk, ALGORITHM = 'RSA_OAEP', ENCRYPTED_VALUE = 0x03000000);
        GO
        """;

    protected override string Ddl => $"{KeyDdl}\n{ScannerDdl}";

    private static IReadOnlyList<AlwaysEncryptedComparisonMismatchFinding> Scan(string sql)
    {
        var result = SqlScriptParser.ParseText("test.sql", $"{KeyDdl}\n{ScannerDdl}\n{sql}");
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        var catalog = CatalogBuilder.Build([result]);
        return AlwaysEncryptedComparisonMismatchScanner.Scan(result, catalog);
    }

    private async Task<SqlException> ExecuteExpectingFailureAsync(string sql)
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(DatabaseName));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await Assert.ThrowsAsync<SqlException>(() => command.ExecuteNonQueryAsync());
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(DatabaseName));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task LiteralAgainstEncryptedColumn_FailsWithMsg206_AndScannerFlagsIt()
    {
        var sql = "SELECT * FROM dbo.Customer WHERE DetA = N'123';";
        var exception = await ExecuteExpectingFailureAsync(sql);

        Assert.Equal(206, exception.Number);

        var finding = Assert.Single(Scan(sql));
        Assert.Equal(AlwaysEncryptedComparisonMismatchKind.LiteralOperand, finding.Kind);
    }

    [Fact]
    public async Task DifferentColumnEncryptionKey_FailsWithMsg402_AndScannerFlagsIt()
    {
        var sql = "SELECT * FROM dbo.Customer WHERE DetA = DetB;";
        var exception = await ExecuteExpectingFailureAsync(sql);

        Assert.Equal(402, exception.Number);

        var finding = Assert.Single(Scan(sql));
        Assert.Equal(AlwaysEncryptedComparisonMismatchKind.EncryptionStateMismatch, finding.Kind);
    }

    [Fact]
    public async Task EncryptedAgainstPlainColumn_FailsWithMsg402_AndScannerFlagsIt()
    {
        var sql = "SELECT * FROM dbo.Customer WHERE DetA = PlainName;";
        var exception = await ExecuteExpectingFailureAsync(sql);

        Assert.Equal(402, exception.Number);

        var finding = Assert.Single(Scan(sql));
        Assert.Equal(AlwaysEncryptedComparisonMismatchKind.EncryptionStateMismatch, finding.Kind);
    }

    [Fact]
    public async Task SameKeyAndType_Succeeds_AndScannerDoesNotFlagIt()
    {
        var sql = "SELECT * FROM dbo.Customer WHERE DetA = DetA2;";
        var exception = await Record.ExceptionAsync(() => ExecuteAsync(sql));

        Assert.Null(exception);
        Assert.Empty(Scan(sql));
    }

    [Fact]
    public async Task NullLiteral_Succeeds_AndScannerDoesNotFlagIt()
    {
        var sql = "SELECT * FROM dbo.Customer WHERE DetA = NULL;";
        var exception = await Record.ExceptionAsync(() => ExecuteAsync(sql));

        Assert.Null(exception);
        Assert.Empty(Scan(sql));
    }

    [Fact]
    public async Task DeterministicRangeComparison_FailsWithMsg33277_AndScannerFlagsIt()
    {
        var sql = "SELECT * FROM dbo.Customer WHERE DetA > DetA2;";
        var exception = await ExecuteExpectingFailureAsync(sql);

        Assert.Equal(33277, exception.Number);

        var finding = Assert.Single(Scan(sql));
        Assert.Equal(AlwaysEncryptedComparisonMismatchKind.DeterministicRangeComparison, finding.Kind);
    }

    [Fact]
    public async Task RandomizedEqualityWithoutEnclave_FailsWithMsg33277_AndScannerFlagsIt()
    {
        var sql = "SELECT * FROM dbo.Customer WHERE RndA = RndA2;";
        var exception = await ExecuteExpectingFailureAsync(sql);

        Assert.Equal(33277, exception.Number);

        var finding = Assert.Single(Scan(sql));
        Assert.Equal(AlwaysEncryptedComparisonMismatchKind.RandomizedWithoutEnclave, finding.Kind);
    }

    [Fact]
    public async Task RandomizedRangeWithoutEnclave_FailsWithMsg33277_AndScannerFlagsIt()
    {
        var sql = "SELECT * FROM dbo.Customer WHERE RndA > RndA2;";
        var exception = await ExecuteExpectingFailureAsync(sql);

        Assert.Equal(33277, exception.Number);

        var finding = Assert.Single(Scan(sql));
        Assert.Equal(AlwaysEncryptedComparisonMismatchKind.RandomizedWithoutEnclave, finding.Kind);
    }

    [Fact]
    public async Task RandomizedEqualityWithEnclave_Succeeds_AndScannerDoesNotFlagIt()
    {
        var sql = "SELECT * FROM dbo.Customer WHERE RndEncA = RndEncA2;";
        var exception = await Record.ExceptionAsync(() => ExecuteAsync(sql));

        Assert.Null(exception);
        Assert.Empty(Scan(sql));
    }

    [Fact]
    public async Task RandomizedRangeWithEnclave_Succeeds_AndScannerDoesNotFlagIt()
    {
        var sql = "SELECT * FROM dbo.Customer WHERE RndEncA > RndEncA2;";
        var exception = await Record.ExceptionAsync(() => ExecuteAsync(sql));

        Assert.Null(exception);
        Assert.Empty(Scan(sql));
    }
}
