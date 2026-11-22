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
    [InlineData("SELECT * FROM dbo.A a LEFT JOIN dbo.B b ON b.AId = a.Id WHERE b.Y = 1 OR a.X = 2;")]
    [InlineData("SELECT * FROM dbo.A a LEFT JOIN dbo.B b ON b.AId = a.Id WHERE b.Y IS NULL;")]
    [InlineData("SELECT * FROM dbo.A a LEFT JOIN dbo.B b ON b.AId = a.Id WHERE COALESCE(b.Y, 0) = 0;")]
    [InlineData("SELECT * FROM dbo.A a LEFT JOIN dbo.B b ON b.AId = a.Id WHERE b.Y = 1 OR b.Y IS NULL;")]
    [InlineData("SELECT * FROM dbo.A a LEFT JOIN dbo.B b ON b.AId = a.Id WHERE NOT (b.Y IS NOT NULL);")]
    public void PredicateThatKeepsNullSuppliedRows_NeverFires(string sql)
    {
        Assert.Empty(Scan(sql));
    }

    [Theory]
    [InlineData("SELECT * FROM dbo.A a LEFT JOIN dbo.B b ON b.AId = a.Id WHERE b.Y = 1;")]
    [InlineData("SELECT * FROM dbo.A a LEFT JOIN dbo.B b ON b.AId = a.Id WHERE b.Y <> 1;")]
    [InlineData("SELECT * FROM dbo.A a LEFT JOIN dbo.B b ON b.AId = a.Id WHERE b.Y IS NOT NULL;")]
    public void NullRejectingPredicateOnTheNullSuppliedSide_Fires(string sql)
    {
        Assert.NotEmpty(Scan(sql));
    }
}
