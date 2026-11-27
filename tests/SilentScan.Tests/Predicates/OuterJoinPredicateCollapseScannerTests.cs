using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;

namespace SilentScan.Tests.Predicates;

public sealed class OuterJoinPredicateCollapseScannerTests
{
    private const string Ddl = "CREATE TABLE dbo.A (Id INT NOT NULL PRIMARY KEY, X INT NULL); CREATE TABLE dbo.B (Id INT NOT NULL PRIMARY KEY, AId INT NULL, Y INT NULL);";

    private static IReadOnlyList<OuterJoinPredicateCollapseFinding> Scan(string sql)
    {
        var result = SqlScriptParser.ParseText("test.sql", $"{Ddl}\nGO\n{sql}");
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        return OuterJoinPredicateCollapseScanner.Scan(result);
    }

    [Theory]
    [InlineData("SELECT * FROM dbo.A a LEFT JOIN dbo.B b ON b.AId = a.Id WHERE LEN(b.Y) > 0;")]
    [InlineData("SELECT * FROM dbo.A a LEFT JOIN dbo.B b ON b.AId = a.Id WHERE ABS(b.Y) = 1;")]
    [InlineData("SELECT * FROM dbo.A a LEFT JOIN dbo.B b ON b.AId = a.Id WHERE UPPER(CONVERT(VARCHAR(5), b.Y)) = '1';")]
    [InlineData("SELECT * FROM dbo.A a LEFT JOIN dbo.B b ON b.AId = a.Id WHERE CONCAT(b.Y, 'x') = 'x';")]
    [InlineData("SELECT * FROM dbo.A a LEFT JOIN dbo.B b ON b.AId = a.Id WHERE IIF(b.Y IS NULL, 0, 1) = 0;")]
    [InlineData("SELECT * FROM dbo.A a LEFT JOIN dbo.B b ON b.AId = a.Id WHERE CASE WHEN b.Y IS NULL THEN 1 ELSE b.Y END = 1;")]
    [InlineData("SELECT * FROM dbo.A a LEFT JOIN dbo.B b ON b.AId = a.Id WHERE ISNULL(b.Y, 0) = 0;")]
    [InlineData("SELECT * FROM dbo.A a LEFT JOIN dbo.B b ON b.AId = a.Id WHERE b.Y IS NULL OR b.Y = 1;")]
    [InlineData("SELECT * FROM dbo.A a LEFT JOIN dbo.B b ON b.AId = a.Id WHERE (b.Y = 1 OR b.Y IS NULL) AND a.X = 1;")]
    [InlineData("SELECT * FROM dbo.A a LEFT JOIN dbo.B b ON b.AId = a.Id WHERE CONCAT_WS(',', b.Y, 'x') = 'x';")]
    [InlineData("SELECT * FROM dbo.A a LEFT JOIN dbo.B b ON b.AId = a.Id WHERE b.Y = 1 OR a.X = 2;")]
    [InlineData("SELECT * FROM dbo.A a LEFT JOIN dbo.B b ON b.AId = a.Id WHERE b.Y IS NULL;")]
    [InlineData("SELECT * FROM dbo.A a LEFT JOIN dbo.B b ON b.AId = a.Id WHERE COALESCE(b.Y, 0) = 0;")]
    [InlineData("SELECT * FROM dbo.A a LEFT JOIN dbo.B b ON b.AId = a.Id WHERE b.Y = 1 OR b.Y IS NULL;")]
    [InlineData("SELECT * FROM dbo.A a LEFT JOIN dbo.B b ON b.AId = a.Id WHERE NOT (b.Y IS NOT NULL);")]
    [InlineData("SELECT * FROM dbo.A a LEFT JOIN dbo.B b ON b.AId = a.Id WHERE b.Y IS DISTINCT FROM 7;")]
    [InlineData("SELECT * FROM dbo.A a LEFT JOIN dbo.B b ON b.AId = a.Id WHERE NOT (b.Y IS NOT DISTINCT FROM 7);")]
    [InlineData("SELECT * FROM dbo.A a LEFT JOIN dbo.B b ON b.AId = a.Id WHERE b.Y IS NOT DISTINCT FROM NULL;")]
    [InlineData("SELECT * FROM dbo.A a LEFT JOIN dbo.B b ON b.AId = a.Id WHERE b.Y NOT IN (SELECT X FROM dbo.A);")]
    [InlineData("SELECT * FROM dbo.A a LEFT JOIN dbo.B b ON b.AId = a.Id WHERE NOT (b.Y IN (SELECT X FROM dbo.A));")]
    public void PredicateThatKeepsNullSuppliedRows_NeverFires(string sql)
    {
        Assert.Empty(Scan(sql));
    }

    [Theory]
    [InlineData("SELECT * FROM dbo.A a LEFT JOIN dbo.B b ON b.AId = a.Id WHERE -b.Y = -1;")]
    [InlineData("SELECT * FROM dbo.A a LEFT JOIN dbo.B b ON b.AId = a.Id WHERE CAST(b.Y AS VARCHAR(5)) = '1';")]
    [InlineData("SELECT * FROM dbo.A a LEFT JOIN dbo.B b ON b.AId = a.Id WHERE (b.Y) = 1;")]
    [InlineData("SELECT * FROM dbo.A a LEFT JOIN dbo.B b ON b.AId = a.Id WHERE b.Y IN (1, 2);")]
    [InlineData("SELECT * FROM dbo.A a LEFT JOIN dbo.B b ON b.AId = a.Id WHERE b.Y IN (SELECT X FROM dbo.A);")]
    [InlineData("SELECT * FROM dbo.A a LEFT JOIN dbo.B b ON b.AId = a.Id WHERE NOT (b.Y NOT IN (SELECT X FROM dbo.A));")]
    [InlineData("SELECT * FROM dbo.A a LEFT JOIN dbo.B b ON b.AId = a.Id WHERE NOT (b.Y IN (1, 2));")]
    [InlineData("SELECT * FROM dbo.A a LEFT JOIN dbo.B b ON b.AId = a.Id WHERE b.Y NOT IN (1, 2);")]
    [InlineData("SELECT * FROM dbo.A a LEFT JOIN dbo.B b ON b.AId = a.Id WHERE b.Y LIKE '1%';")]
    [InlineData("SELECT * FROM dbo.A a LEFT JOIN dbo.B b ON b.AId = a.Id WHERE b.Y + 1 = 2;")]
    [InlineData("SELECT * FROM dbo.A a LEFT JOIN dbo.B b ON b.AId = a.Id WHERE CONVERT(INT, b.Y) = 1;")]
    [InlineData("SELECT * FROM dbo.A a LEFT JOIN dbo.B b ON b.AId = a.Id WHERE b.Y BETWEEN 1 AND 5;")]
    [InlineData("SELECT * FROM dbo.A a LEFT JOIN dbo.B b ON b.AId = a.Id WHERE NOT (b.Y = 1);")]
    [InlineData("SELECT * FROM dbo.A a LEFT JOIN dbo.B b ON b.AId = a.Id WHERE b.Y = 1;")]
    [InlineData("SELECT * FROM dbo.A a LEFT JOIN dbo.B b ON b.AId = a.Id WHERE b.Y <> 1;")]
    [InlineData("SELECT * FROM dbo.A a LEFT JOIN dbo.B b ON b.AId = a.Id WHERE b.Y IS NOT NULL;")]
    public void NullRejectingPredicateOnTheNullSuppliedSide_Fires(string sql)
    {
        Assert.NotEmpty(Scan(sql));
    }

    [Theory]
    [InlineData("SET CONCAT_NULL_YIELDS_NULL OFF;", "b.Y + 1 = 1", false)]
    [InlineData("SET CONCAT_NULL_YIELDS_NULL ON;", "b.Y + 1 = 1", true)]
    [InlineData("SET CONCAT_NULL_YIELDS_NULL OFF;", "b.Y = 1", true)]
    [InlineData("SET ANSI_NULLS OFF;", "b.Y = NULL", false)]
    [InlineData("SET ANSI_NULLS OFF;", "b.Y <> @v", false)]
    [InlineData("SET ANSI_NULLS OFF;", "NOT b.Y = @v", false)]
    [InlineData("SET ANSI_NULLS OFF;", "b.Y IN (NULL, 1)", false)]
    [InlineData("SET ANSI_NULLS OFF;", "b.Y NOT IN (1, 2)", false)]
    [InlineData("SET ANSI_NULLS OFF;", "b.Y <> 1", true)]
    [InlineData("SET ANSI_NULLS OFF;", "b.Y IN (1, 2)", true)]
    [InlineData("SET ANSI_NULLS ON;", "b.Y <> @v", true)]
    public void NullPreservingSessionSettings_AreHonoured(string setting, string predicate, bool fires)
    {
        var findings = Scan($"{setting}\nDECLARE @v INT;\nSELECT a.Id FROM dbo.A a LEFT JOIN dbo.B b ON b.AId = a.Id WHERE {predicate};");

        Assert.Equal(fires, findings.Count > 0);
    }
}
