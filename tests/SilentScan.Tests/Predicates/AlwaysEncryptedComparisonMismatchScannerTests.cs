using SilentScan.Core.Catalog;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;

namespace SilentScan.Tests.Predicates;

public sealed class AlwaysEncryptedComparisonMismatchScannerTests
{
    private const string Ddl = """
        CREATE COLUMN MASTER KEY Cmk
        WITH (KEY_STORE_PROVIDER_NAME = 'MSSQL_CERTIFICATE_STORE', KEY_PATH = 'CurrentUser/My/0000000000000000000000000000000000000000');
        GO
        CREATE COLUMN MASTER KEY EnclaveCmk
        WITH (KEY_STORE_PROVIDER_NAME = 'MSSQL_CERTIFICATE_STORE', KEY_PATH = 'CurrentUser/My/1111111111111111111111111111111111111111',
              ENCLAVE_COMPUTATIONS (SIGNATURE = 0x01020304));
        GO
        CREATE COLUMN ENCRYPTION KEY CekA
        WITH VALUES (COLUMN_MASTER_KEY = Cmk, ALGORITHM = 'RSA_OAEP', ENCRYPTED_VALUE = 0x01000000);
        GO
        CREATE COLUMN ENCRYPTION KEY CekB
        WITH VALUES (COLUMN_MASTER_KEY = Cmk, ALGORITHM = 'RSA_OAEP', ENCRYPTED_VALUE = 0x02000000);
        GO
        CREATE COLUMN ENCRYPTION KEY EnclaveCek
        WITH VALUES (COLUMN_MASTER_KEY = EnclaveCmk, ALGORITHM = 'RSA_OAEP', ENCRYPTED_VALUE = 0x03000000);
        GO
        CREATE TABLE dbo.Customer
        (
            CustomerId INT NOT NULL PRIMARY KEY,
            DetA       NVARCHAR(20) COLLATE Latin1_General_BIN2
                ENCRYPTED WITH (COLUMN_ENCRYPTION_KEY = CekA, ENCRYPTION_TYPE = DETERMINISTIC, ALGORITHM = 'AEAD_AES_256_CBC_HMAC_SHA_256') NOT NULL,
            DetA2      NVARCHAR(20) COLLATE Latin1_General_BIN2
                ENCRYPTED WITH (COLUMN_ENCRYPTION_KEY = CekA, ENCRYPTION_TYPE = DETERMINISTIC, ALGORITHM = 'AEAD_AES_256_CBC_HMAC_SHA_256') NOT NULL,
            DetB       NVARCHAR(20) COLLATE Latin1_General_BIN2
                ENCRYPTED WITH (COLUMN_ENCRYPTION_KEY = CekB, ENCRYPTION_TYPE = DETERMINISTIC, ALGORITHM = 'AEAD_AES_256_CBC_HMAC_SHA_256') NOT NULL,
            RndA       NVARCHAR(20) COLLATE Latin1_General_BIN2
                ENCRYPTED WITH (COLUMN_ENCRYPTION_KEY = CekA, ENCRYPTION_TYPE = RANDOMIZED, ALGORITHM = 'AEAD_AES_256_CBC_HMAC_SHA_256') NOT NULL,
            RndA2      NVARCHAR(20) COLLATE Latin1_General_BIN2
                ENCRYPTED WITH (COLUMN_ENCRYPTION_KEY = CekA, ENCRYPTION_TYPE = RANDOMIZED, ALGORITHM = 'AEAD_AES_256_CBC_HMAC_SHA_256') NOT NULL,
            RndEncA    NVARCHAR(20) COLLATE Latin1_General_BIN2
                ENCRYPTED WITH (COLUMN_ENCRYPTION_KEY = EnclaveCek, ENCRYPTION_TYPE = RANDOMIZED, ALGORITHM = 'AEAD_AES_256_CBC_HMAC_SHA_256') NOT NULL,
            RndEncA2   NVARCHAR(20) COLLATE Latin1_General_BIN2
                ENCRYPTED WITH (COLUMN_ENCRYPTION_KEY = EnclaveCek, ENCRYPTION_TYPE = RANDOMIZED, ALGORITHM = 'AEAD_AES_256_CBC_HMAC_SHA_256') NOT NULL,
            PlainName  NVARCHAR(100) NOT NULL
        );
        """;

    private static IReadOnlyList<AlwaysEncryptedComparisonMismatchFinding> Scan(string sql)
    {
        var result = SqlScriptParser.ParseText("test.sql", $"{Ddl}\n{sql}");
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        var catalog = CatalogBuilder.Build([result]);
        return AlwaysEncryptedComparisonMismatchScanner.Scan(result, catalog);
    }

    [Fact]
    public void LiteralAgainstEncryptedColumn_Fires()
    {
        var finding = Assert.Single(Scan("SELECT * FROM dbo.Customer WHERE DetA = N'123';"));
        Assert.Equal(AlwaysEncryptedComparisonMismatchKind.LiteralOperand, finding.Kind);
        Assert.Equal("DetA", finding.FirstColumnName);
    }

    [Fact]
    public void LiteralOnLeftSide_Fires()
    {
        var finding = Assert.Single(Scan("SELECT * FROM dbo.Customer WHERE N'123' = DetA;"));
        Assert.Equal(AlwaysEncryptedComparisonMismatchKind.LiteralOperand, finding.Kind);
        Assert.Equal("DetA", finding.FirstColumnName);
    }

    [Fact]
    public void NullLiteral_DoesNotFire()
    {
        Assert.Empty(Scan("SELECT * FROM dbo.Customer WHERE DetA = NULL;"));
    }

    [Fact]
    public void SameEncryptionAndKey_DoesNotFire()
    {
        Assert.Empty(Scan("SELECT * FROM dbo.Customer WHERE DetA = DetA2;"));
    }

    [Fact]
    public void SameEncryptionAndKey_NotEquals_DoesNotFire()
    {
        Assert.Empty(Scan("SELECT * FROM dbo.Customer WHERE DetA <> DetA2;"));
    }

    [Fact]
    public void DifferentColumnEncryptionKey_Fires()
    {
        var finding = Assert.Single(Scan("SELECT * FROM dbo.Customer WHERE DetA = DetB;"));
        Assert.Equal(AlwaysEncryptedComparisonMismatchKind.EncryptionStateMismatch, finding.Kind);
        Assert.Equal("DetA", finding.FirstColumnName);
        Assert.Equal("DetB", finding.SecondColumnName);
    }

    [Fact]
    public void DifferentEncryptionType_Fires()
    {
        var finding = Assert.Single(Scan("SELECT * FROM dbo.Customer WHERE DetA = RndA;"));
        Assert.Equal(AlwaysEncryptedComparisonMismatchKind.EncryptionStateMismatch, finding.Kind);
    }

    [Fact]
    public void EncryptedAgainstPlainColumn_Fires()
    {
        var finding = Assert.Single(Scan("SELECT * FROM dbo.Customer WHERE DetA = PlainName;"));
        Assert.Equal(AlwaysEncryptedComparisonMismatchKind.EncryptionStateMismatch, finding.Kind);
    }

    [Fact]
    public void BothPlainColumns_DoesNotFire()
    {
        Assert.Empty(Scan("SELECT * FROM dbo.Customer WHERE PlainName = PlainName;"));
    }

    [Fact]
    public void GreaterThanOperator_DifferentKey_Fires()
    {
        var finding = Assert.Single(Scan("SELECT * FROM dbo.Customer WHERE DetA > DetB;"));
        Assert.Equal(AlwaysEncryptedComparisonMismatchKind.EncryptionStateMismatch, finding.Kind);
    }

    [Fact]
    public void GreaterThanOperator_DeterministicSameKey_Fires()
    {
        var finding = Assert.Single(Scan("SELECT * FROM dbo.Customer WHERE DetA > DetA2;"));
        Assert.Equal(AlwaysEncryptedComparisonMismatchKind.DeterministicRangeComparison, finding.Kind);
    }

    [Theory]
    [InlineData("SELECT * FROM dbo.Customer WHERE DetA < DetA2;")]
    [InlineData("SELECT * FROM dbo.Customer WHERE DetA >= DetA2;")]
    [InlineData("SELECT * FROM dbo.Customer WHERE DetA <= DetA2;")]
    public void RangeOperators_DeterministicSameKey_Fire(string sql)
    {
        var finding = Assert.Single(Scan(sql));
        Assert.Equal(AlwaysEncryptedComparisonMismatchKind.DeterministicRangeComparison, finding.Kind);
    }

    [Fact]
    public void Between_DeterministicSameKey_FiresForBothBounds()
    {
        var findings = Scan("SELECT * FROM dbo.Customer WHERE DetA BETWEEN DetA2 AND DetA2;");
        Assert.Equal(2, findings.Count);
        Assert.All(findings, f => Assert.Equal(AlwaysEncryptedComparisonMismatchKind.DeterministicRangeComparison, f.Kind));
    }

    [Fact]
    public void RandomizedEqualityWithoutEnclave_Fires()
    {
        var finding = Assert.Single(Scan("SELECT * FROM dbo.Customer WHERE RndA = RndA2;"));
        Assert.Equal(AlwaysEncryptedComparisonMismatchKind.RandomizedWithoutEnclave, finding.Kind);
    }

    [Fact]
    public void RandomizedRangeWithoutEnclave_Fires()
    {
        var finding = Assert.Single(Scan("SELECT * FROM dbo.Customer WHERE RndA > RndA2;"));
        Assert.Equal(AlwaysEncryptedComparisonMismatchKind.RandomizedWithoutEnclave, finding.Kind);
    }

    [Fact]
    public void RandomizedEqualityWithEnclave_DoesNotFire()
    {
        Assert.Empty(Scan("SELECT * FROM dbo.Customer WHERE RndEncA = RndEncA2;"));
    }

    [Fact]
    public void RandomizedRangeWithEnclave_DoesNotFire()
    {
        Assert.Empty(Scan("SELECT * FROM dbo.Customer WHERE RndEncA > RndEncA2;"));
    }

    [Fact]
    public void JoinOnClause_Fires()
    {
        var sql = """
            SELECT * FROM dbo.Customer c1
            JOIN dbo.Customer c2 ON c1.DetA = c2.DetB;
            """;
        var finding = Assert.Single(Scan(sql));
        Assert.Equal(AlwaysEncryptedComparisonMismatchKind.EncryptionStateMismatch, finding.Kind);
    }
}
