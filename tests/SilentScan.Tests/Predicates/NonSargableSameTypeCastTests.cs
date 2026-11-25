using SilentScan.Core.Catalog;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Tests.Predicates;

public sealed class NonSargableSameTypeCastTests
{
    private static IReadOnlyList<SargabilityFinding> Scan(string columnType, string predicate)
    {
        var declared = columnType.EndsWith("NOT NULL", StringComparison.Ordinal) ? columnType : columnType + " NULL";
        var parameterType = columnType.Replace(" NOT NULL", string.Empty, StringComparison.Ordinal);
        var sql = $"CREATE TABLE dbo.t (c {declared}, filler CHAR(200) NULL);\nGO\nCREATE INDEX ix ON dbo.t (c);\nGO\nCREATE PROCEDURE dbo.p @p {parameterType} AS SELECT c FROM dbo.t WHERE {predicate};";
        var result = SqlScriptParser.ParseText("t.sql", sql);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        var catalog = CatalogBuilder.Build([result]);
        return NonSargablePredicateScanner.Scan(result, catalog, LineageResolver.Resolve(catalog, [result]));
    }

    [Theory]
    [InlineData("VARCHAR(10)")]
    [InlineData("NVARCHAR(10)")]
    public void CastToTheColumnsOwnType_IsNotFlagged_BecauseTheEnginePlansASeek(string columnType)
    {
        Assert.True(SargShapeMatrix.Instance.PreservesSeek("cast-same-type", columnType));

        Assert.Empty(Scan(columnType, $"CAST(c AS {columnType}) = @p"));
    }

    [Theory]
    [InlineData("VARCHAR(10)")]
    [InlineData("NVARCHAR(10)")]
    public void CastToTheColumnsOwnType_IsFlagged_WhenTheColumnIsNotNull_BecauseTheEngineScans(string columnType)
    {
        Assert.False(SargShapeMatrix.Instance.PreservesSeek("cast-same-type", columnType + " NOT NULL"));

        Assert.Contains(Scan(columnType + " NOT NULL", $"CAST(c AS {columnType}) = @p"), f => f.Kind == SargabilityFindingKind.CastOrConvertOnColumn);
    }

    [Fact]
    public void CastToAnotherType_StillFlagged_BecauseTheEngineScans()
    {
        Assert.False(SargShapeMatrix.Instance.PreservesSeek("cast-varchar", "INT"));

        Assert.Contains(Scan("INT", "CAST(c AS VARCHAR(30)) = '1'"), f => f.Kind == SargabilityFindingKind.CastOrConvertOnColumn);
    }
}
