using SilentScan.Core.Catalog;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;

namespace SilentScan.Tests.Predicates;

public sealed class IsNullReplacementEngineShapesTests
{
    private static IReadOnlyList<IsNullReplacementValueTruncationFinding> Scan(string checkColumnType, string replacement)
    {
        var sql = $"CREATE TABLE dbo.T (Id INT NOT NULL PRIMARY KEY, C {checkColumnType} NULL, F FLOAT NULL, D2 DATETIME2(3) NULL, Dtm DATETIME NULL, Dec182 DECIMAL(18,2) NULL, Dto3 DATETIMEOFFSET(3) NULL, Vb VARBINARY(10) NULL, Nv NVARCHAR(10) NULL);\nGO\nCREATE VIEW dbo.V AS SELECT ISNULL(C, {replacement}) AS X FROM dbo.T;";
        var result = SqlScriptParser.ParseText("test.sql", sql);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        var catalog = CatalogBuilder.Build([result]);
        return IsNullReplacementValueTruncationScanner.Scan(result, catalog);
    }

    [Theory]
    [InlineData("DECIMAL(5,1)", "1.25")]
    [InlineData("DECIMAL(5,1)", "Dec182")]
    [InlineData("DATETIME2(0)", "D2")]
    [InlineData("SMALLDATETIME", "Dtm")]
    [InlineData("REAL", "F")]
    [InlineData("INT", "2.7")]
    [InlineData("INT", "Dec182")]
    [InlineData("DATETIME", "D2")]
    [InlineData("DATE", "Dtm")]
    [InlineData("VARBINARY(2)", "Vb")]
    [InlineData("DATETIMEOFFSET(0)", "Dto3")]
    [InlineData("MONEY", "1.23456")]
    [InlineData("VARCHAR(3)", "Nv")]
    [InlineData("NVARCHAR(3)", "Nv")]
    public void ReplacementTheEngineNarrowsSilently_IsFlagged(string checkColumnType, string replacement) =>
        Assert.Single(Scan(checkColumnType, replacement));

    [Theory]
    [InlineData("DECIMAL(5,1)", "1.2")]
    [InlineData("DATETIME2(3)", "D2")]
    [InlineData("DATETIME", "Dtm")]
    [InlineData("VARBINARY(10)", "Vb")]
    [InlineData("NVARCHAR(10)", "Nv")]
    [InlineData("FLOAT", "F")]
    public void ReplacementTheEngineStoresExactly_IsNotFlagged(string checkColumnType, string replacement) =>
        Assert.Empty(Scan(checkColumnType, replacement));
}
