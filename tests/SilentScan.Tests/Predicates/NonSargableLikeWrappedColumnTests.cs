using SilentScan.Core.Catalog;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;

namespace SilentScan.Tests.Predicates;

public sealed class NonSargableLikeWrappedColumnTests
{
    private static IReadOnlyList<SargabilityFinding> Scan(string columnType, string parameterType, string predicate)
    {
        var sql = $"CREATE TABLE dbo.t (c {columnType} NULL, filler CHAR(200) NULL);\nGO\nCREATE INDEX ix ON dbo.t (c);\nGO\nCREATE PROCEDURE dbo.p @p {parameterType} AS SELECT c FROM dbo.t WHERE {predicate};";
        var result = SqlScriptParser.ParseText("t.sql", sql);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        var catalog = CatalogBuilder.Build([result]);
        return NonSargablePredicateScanner.Scan(result, catalog, LineageResolver.Resolve(catalog, [result]));
    }

    [Theory]
    [InlineData("RTRIM(c) LIKE 'k123%'")]
    [InlineData("LTRIM(c) LIKE 'k123%'")]
    [InlineData("UPPER(c) LIKE 'K123%'")]
    [InlineData("ISNULL(c, '') LIKE 'k123%'")]
    [InlineData("COALESCE(c, '') LIKE 'k123%'")]
    [InlineData("REPLACE(c, 'a', 'b') LIKE 'k123%'")]
    [InlineData("CONCAT(c, '') LIKE 'k123%'")]
    [InlineData("NULLIF(c, '') LIKE 'k123%'")]
    public void FunctionWrappedColumnOnTheLeftOfLike_IsFlagged_BecauseTheEngineScans(string predicate) =>
        Assert.Contains(Scan("VARCHAR(20) COLLATE Latin1_General_CI_AS", "VARCHAR(20)", predicate), f => f.ColumnName == "c" && f.Kind != SargabilityFindingKind.LeadingWildcardLike);

    [Fact]
    public void BareColumnWithAPrefixPattern_IsNotFlagged() =>
        Assert.Empty(Scan("VARCHAR(20) COLLATE Latin1_General_CI_AS", "VARCHAR(20)", "c LIKE 'k123%'"));

    [Theory]
    [InlineData("CAST(c AS VARCHAR(40)) LIKE 'k123%'")]
    [InlineData("CAST(c AS NVARCHAR(20)) LIKE N'k123%'")]
    [InlineData("(CONVERT(VARCHAR(20), c, 0)) LIKE @p")]
    public void CastColumnOnTheLeftOfLike_IsFlagged_WhenTheCastStopsTheSeek(string predicate)
    {
        var finding = Assert.Single(Scan("VARCHAR(20) COLLATE SQL_Latin1_General_CP1_CI_AS", "VARCHAR(20)", predicate));

        Assert.Equal(SargabilityFindingKind.CastOrConvertOnColumn, finding.Kind);
        Assert.Equal("c", finding.ColumnName);
    }

    [Theory]
    [InlineData("CAST(c AS VARCHAR(20)) LIKE 'k123%'")]
    [InlineData("CAST(c AS VARCHAR(40)) LIKE '%k123'")]
    [InlineData("CAST(c AS VARCHAR(40)) NOT LIKE 'k123%'")]
    [InlineData("CAST(c AS VARCHAR(40)) LIKE c")]
    public void CastColumnOnTheLeftOfLike_IsNotFlagged_WhenRemovingTheCastWouldChangeNothing(string predicate) =>
        Assert.Empty(Scan("VARCHAR(20) COLLATE SQL_Latin1_General_CP1_CI_AS", "VARCHAR(20)", predicate));

    [Theory]
    [InlineData("c LIKE '[^a]%'")]
    [InlineData("c LIKE '[^^]%'")]
    public void NegatedLeadingClass_IsFlagged(string predicate) =>
        Assert.Contains(Scan("VARCHAR(20) COLLATE Latin1_General_CI_AS", "VARCHAR(20)", predicate), f => f.Kind == SargabilityFindingKind.LeadingWildcardLike);

    [Theory]
    [InlineData("c LIKE '[^]%'")]
    [InlineData("c LIKE '[^]a]%'")]
    [InlineData("c LIKE '[a-c]%'")]
    [InlineData("c LIKE '[a^]%'")]
    public void LeadingClassTheEngineSeeksThrough_IsNotFlagged(string predicate) =>
        Assert.Empty(Scan("VARCHAR(20) COLLATE Latin1_General_CI_AS", "VARCHAR(20)", predicate));

    [Theory]
    [InlineData("INT", "INT", "CONVERT(INT, c, 0) = @p")]
    [InlineData("INT", "INT", "CONVERT(INT, c, 1) = @p")]
    [InlineData("INT", "BIGINT", "CONVERT(BIGINT, c, 0) = @p")]
    [InlineData("DATETIME", "DATETIME", "CONVERT(DATETIME, c, 0) = @p")]
    [InlineData("DATETIME", "DATETIME", "CONVERT(DATETIME, c, 121) = @p")]
    [InlineData("DATETIME", "DATETIME2(3)", "CONVERT(DATETIME2(3), c, 121) = @p")]
    [InlineData("DATETIME", "DATE", "CONVERT(DATE, c, 23) = @p")]
    [InlineData("DECIMAL(9,2)", "DECIMAL(9,2)", "CONVERT(DECIMAL(9,2), c, 0) = @p")]
    [InlineData("DECIMAL(9,2)", "MONEY", "CONVERT(MONEY, c, 1) = @p")]
    public void StyledConvertTheEngineSeeksThrough_IsNotFlagged(string columnType, string parameterType, string predicate) =>
        Assert.Empty(Scan(columnType, parameterType, predicate));
}
