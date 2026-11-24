using SilentScan.Core.Catalog;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;

namespace SilentScan.Tests.Predicates;

public sealed class BuiltinOperandTypingTests
{
    [Theory]
    [InlineData("LEFT(@s, 1) = 'a'")]
    [InlineData("RIGHT(@s, 2) = 'ab'")]
    [InlineData("ABS(@n) = 1")]
    [InlineData("ROUND(@d, 2) = 1")]
    [InlineData("POWER(@d, 2) = 1")]
    [InlineData("CEILING(@d) = 1")]
    [InlineData("RADIANS(@d) = 1")]
    [InlineData("TRY_CONVERT(int, @s) = 1")]
    [InlineData("TRY_CAST(@s AS int) = 1")]
    [InlineData("TODATETIMEOFFSET(@dt, 60) = SYSDATETIMEOFFSET()")]
    [InlineData("DATABASE_PRINCIPAL_ID('dbo') = 1")]
    public void BuiltinCallOperands_AreTypedInsteadOfLedgeredAsUnresolved(string comparison)
    {
        var sql = $"""
            CREATE TABLE dbo.T (Id INT NOT NULL);
            GO
            CREATE PROCEDURE dbo.p @s VARCHAR(10), @n INT, @d DECIMAL(10, 3), @dt DATETIME AS
            SELECT t.Id FROM dbo.T AS t WHERE t.Id = 1 AND {comparison};
            """;
        var result = SqlScriptParser.ParseText("operand.sql", sql);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        var catalog = CatalogBuilder.Build([result]);

        var extraction = TypedPredicateExtractor.Extract(result, catalog, LineageResolver.Resolve(catalog, [result]));

        Assert.DoesNotContain(extraction.SkippedConstructs, e => e.ConstructKind == "predicate operand");
    }
}
