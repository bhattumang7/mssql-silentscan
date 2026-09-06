using SilentScan.Core.Catalog;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;

namespace SilentScan.Tests.Predicates;

public sealed class AlwaysEncryptedComparisonMismatchScannerTests
{
    private const string Ddl = """
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
    public void GreaterThanOperator_IsNotChecked()
    {
        Assert.Empty(Scan("SELECT * FROM dbo.Customer WHERE DetA > DetB;"));
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
