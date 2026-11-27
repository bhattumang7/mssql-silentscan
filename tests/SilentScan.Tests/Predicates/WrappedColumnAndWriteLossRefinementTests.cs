using SilentScan.Core.Catalog;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;
using SilentScan.Core.Reporting;

namespace SilentScan.Tests.Predicates;

public sealed class WrappedColumnAndWriteLossRefinementTests
{
    private static ScanReport Scan(string sql)
    {
        var result = SqlScriptParser.ParseText("refinement.sql", sql);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        return ScanReportBuilder.BuildFromParseResults([result], CatalogBuilder.Build([result]));
    }

    private const string Table =
        "CREATE TABLE dbo.N (Id INT NOT NULL PRIMARY KEY, C VARCHAR(20) NULL, K INT NOT NULL, D DATETIME NOT NULL, INDEX IC (C), INDEX IK (K), INDEX ID (D));\nGO\n";

    [Theory]
    [InlineData("LEN(LTRIM(C)) = 3", true)]
    [InlineData("UPPER(LTRIM(C)) = 'V5'", true)]
    [InlineData("ISNULL(COALESCE(C, 'a'), 'b') = 'v5'", true)]
    [InlineData("ISNULL(ISNULL(K, 0), 0) = 5", false)]
    [InlineData("C = LTRIM('v5')", false)]
    [InlineData("D > DATEADD(day, ABS(1), GETDATE())", false)]
    public void AColumnNestedInsideTwoFunctions_IsReported(string where, bool fires)
    {
        var report = Scan(Table + $"SELECT Id FROM dbo.N WHERE {where};");

        Assert.Equal(fires, report.Find<SargabilityFinding>("NonSargablePredicateScanner").Count > 0);
    }

    [Theory]
    [InlineData("CAST(CAST(K AS BIGINT) AS BIGINT)", false)]
    [InlineData("CAST(CAST(K AS BIGINT) AS DECIMAL(18,0))", true)]
    public void ARepeatedIdenticalCastLayer_DoesNotMakeAColumnExpressionDerived(string expression, bool fires)
    {
        var report = Scan(
            Table + $"CREATE VIEW dbo.V AS SELECT {expression} AS X FROM dbo.N;\nGO\nSELECT X FROM dbo.V WHERE X = 5;");

        Assert.Equal(fires, report.Find<ExpressionDerivedFinding>("TypedPredicateExtractor").Count > 0);
    }

    [Theory]
    [InlineData("CREATE FUNCTION dbo.F (@x DATETIME2(7)) RETURNS DATETIME2(0) AS BEGIN RETURN @x; END;")]
    [InlineData("CREATE PROCEDURE dbo.P @p DATETIME2(0) = '2024-12-31 23:59:59.9999999' AS SELECT @p;")]
    public void TemporalScaleNarrowingOfAReturnOrParameterDefault_IsReported(string module)
    {
        var report = Scan(module);

        Assert.Contains(report.Find<WriteLossFinding>("TypedPredicateExtractor"), finding => finding.Kind == WriteLossKind.TemporalScaleNarrowing);
    }
}
