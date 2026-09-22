using SilentScan.Core.Catalog;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;

namespace SilentScan.Tests.Predicates;

public sealed class IsNullReplacementValueTruncationScannerTests
{
    private static IReadOnlyList<IsNullReplacementValueTruncationFinding> Scan(string sql)
    {
        var ddl = """
            CREATE TABLE dbo.T (
                Id INT NOT NULL PRIMARY KEY,
                ShortCode VARCHAR(5) NULL,
                LongCode VARCHAR(50) NULL,
                SmallDecimal DECIMAL(9, 2) NULL,
                BigDecimal DECIMAL(18, 6) NULL,
                ShortBinary VARBINARY(5) NULL,
                LongBinary VARBINARY(50) NULL,
                SameLength VARCHAR(50) NULL,
                UnicodeCol NVARCHAR(50) NULL
            );
            """;
        var result = SqlScriptParser.ParseText("test.sql", $"{ddl}\nGO\n{sql}");
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));

        var catalog = CatalogBuilder.Build([result]);
        return IsNullReplacementValueTruncationScanner.Scan(result, catalog);
    }

    [Fact]
    public void ReplacementStringWiderThanCheckExpression_Fires()
    {
        var findings = Scan("SELECT ISNULL(ShortCode, LongCode) FROM dbo.T;");

        var finding = Assert.Single(findings);
        Assert.Equal(WriteLossKind.LengthTruncation, finding.Kind);
        Assert.Equal("ShortCode", finding.CheckExpressionDisplay);
        Assert.Equal("LongCode", finding.ReplacementValueDisplay);
    }

    [Fact]
    public void ReplacementDecimalWithMoreScaleThanCheckExpression_Fires()
    {
        var findings = Scan("SELECT ISNULL(SmallDecimal, BigDecimal) FROM dbo.T;");

        var finding = Assert.Single(findings);
        Assert.Equal(WriteLossKind.NumericScaleNarrowing, finding.Kind);
    }

    [Fact]
    public void ReplacementBinaryWiderThanCheckExpression_Fires()
    {
        var findings = Scan("SELECT ISNULL(ShortBinary, LongBinary) FROM dbo.T;");

        var finding = Assert.Single(findings);
        Assert.Equal(WriteLossKind.LengthTruncation, finding.Kind);
    }

    [Fact]
    public void ReplacementSameWidthAsCheckExpression_NeverFires()
    {
        var findings = Scan("SELECT ISNULL(LongCode, SameLength) FROM dbo.T;");

        Assert.Empty(findings);
    }

    [Fact]
    public void ReplacementNarrowerThanCheckExpression_NeverFires()
    {
        var findings = Scan("SELECT ISNULL(LongCode, ShortCode) FROM dbo.T;");

        Assert.Empty(findings);
    }

    [Fact]
    public void CoalesceInsteadOfIsnull_NeverFires()
    {
        var findings = Scan("SELECT COALESCE(ShortCode, LongCode) FROM dbo.T;");

        Assert.Empty(findings);
    }

    [Fact]
    public void ThreeArgumentIsnullShapedCall_NeverFires()
    {
        var findings = Scan("SELECT ISNULL(ShortCode, LongCode, 'extra') FROM dbo.T;");

        Assert.Empty(findings);
    }
}
