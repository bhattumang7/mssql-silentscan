using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Catalog;
using SilentScan.Core.Rules;

namespace SilentScan.Tests.Rules;

public sealed class LiteralTypeResolverTests
{
    private static Literal ParseLiteral(string expressionSql)
    {
        var parser = new TSql160Parser(true);
        using var reader = new StringReader($"SELECT {expressionSql};");
        var fragment = parser.Parse(reader, out var errors);
        Assert.Empty(errors);

        var script = (TSqlScript)fragment;
        var select = (SelectStatement)script.Batches[0].Statements[0];
        var spec = (QuerySpecification)select.QueryExpression;
        var scalar = (SelectScalarExpression)spec.SelectElements[0];
        return Assert.IsType<Literal>(scalar.Expression, exactMatch: false);
    }

    [Fact]
    public void Resolve_NationalStringLiteral_ResolvesToNVarChar()
    {
        var type = LiteralTypeResolver.Resolve(ParseLiteral("N'hello'"));

        Assert.Equal(SqlTypeCategory.NVarChar, type!.Category);
        Assert.Equal(5, type.Length);
    }

    [Fact]
    public void Resolve_StringLiteral_ResolvesToVarChar()
    {
        var type = LiteralTypeResolver.Resolve(ParseLiteral("'hello'"));

        Assert.Equal(SqlTypeCategory.VarChar, type!.Category);
        Assert.Equal(5, type.Length);
    }

    [Fact]
    public void Resolve_IntegerLiteral_ResolvesToInt()
    {
        var type = LiteralTypeResolver.Resolve(ParseLiteral("123"));

        Assert.Equal(SqlTypeCategory.Int, type!.Category);
    }

    [Fact]
    public void Resolve_DecimalLiteral_ResolvesToDecimalWithPrecisionAndScale()
    {
        var type = LiteralTypeResolver.Resolve(ParseLiteral("1.5"));

        Assert.Equal(SqlTypeCategory.Decimal, type!.Category);
        Assert.Equal(2, type.Precision);
        Assert.Equal(1, type.Scale);
    }

    [Fact]
    public void Resolve_OutOfIntRangeIntegerValuedLiteral_ParsesAsNumericWithZeroScale()
    {
        // Large enough to overflow int/bigint parsing as IntegerLiteral, so ScriptDOM
        // classifies it as NumericLiteral instead - exercises ResolveNumeric's no-dot branch.
        var type = LiteralTypeResolver.Resolve(ParseLiteral("99999999999999999999"));

        Assert.Equal(SqlTypeCategory.Decimal, type!.Category);
        Assert.Equal(20, type.Precision);
        Assert.Equal(0, type.Scale);
    }

    [Fact]
    public void Resolve_MoneyLiteral_ResolvesToMoney()
    {
        var type = LiteralTypeResolver.Resolve(ParseLiteral("$5.00"));

        Assert.Equal(SqlTypeCategory.Money, type!.Category);
    }

    [Fact]
    public void Resolve_BinaryLiteral_ResolvesToBinaryWithByteLength()
    {
        var type = LiteralTypeResolver.Resolve(ParseLiteral("0x1A2B"));

        Assert.Equal(SqlTypeCategory.Binary, type!.Category);
        Assert.Equal(2, type.Length);
    }

    [Fact]
    public void Resolve_NullLiteral_ReturnsNull()
    {
        var type = LiteralTypeResolver.Resolve(ParseLiteral("NULL"));

        Assert.Null(type);
    }
}
