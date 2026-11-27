namespace SilentScan.Tests.Predicates;

public sealed class DynamicDataMaskingOrderByAliasTests
{
    [Theory]
    [InlineData("SELECT Id AS Amt FROM dbo.Masked ORDER BY Amt DESC;", false)]
    [InlineData("SELECT Amt AS z FROM dbo.Masked ORDER BY z;", true)]
    [InlineData("SELECT Amt FROM dbo.Masked ORDER BY Amt;", true)]
    [InlineData("SELECT Id FROM dbo.Masked ORDER BY Amt;", true)]
    public void OrderByNameBoundToASelectAlias_IsJudgedByTheAliasedExpression(string sql, bool fires)
    {
        var result = SilentScan.Core.Parsing.SqlScriptParser.ParseText(
            "test.sql", "CREATE TABLE dbo.Masked (Id INT NOT NULL, Amt INT MASKED WITH (FUNCTION = 'default()') NOT NULL);\nGO\n" + sql);
        var catalog = SilentScan.Core.Catalog.CatalogBuilder.Build([result]);

        var findings = SilentScan.Core.Predicates.DynamicDataMaskingScanner.Scan(result, catalog);

        Assert.Equal(fires, findings.Any(f => f.ContextDescription == "ORDER BY"));
    }
}
