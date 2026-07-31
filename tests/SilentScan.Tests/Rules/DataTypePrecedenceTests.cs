using SilentScan.Core.Catalog;
using SilentScan.Core.Rules;

namespace SilentScan.Tests.Rules;

public sealed class DataTypePrecedenceTests
{
    [Fact]
    public void DetermineConvertedSide_VarcharColumnVsNVarcharValue_ColumnConverts()
    {
        // CLAUDE.md: "varchar column vs nvarchar value/param -> the COLUMN converts."
        var side = DataTypePrecedence.DetermineConvertedSide(SqlTypeCategory.VarChar, SqlTypeCategory.NVarChar);

        Assert.Equal(ComparisonSide.Left, side);
    }

    [Fact]
    public void DetermineConvertedSide_NVarcharColumnVsVarcharValue_ValueConverts()
    {
        // Direction matters: nvarchar column vs varchar value is harmless (seek preserved).
        var side = DataTypePrecedence.DetermineConvertedSide(SqlTypeCategory.NVarChar, SqlTypeCategory.VarChar);

        Assert.Equal(ComparisonSide.Right, side);
    }

    [Fact]
    public void DetermineConvertedSide_SameCategory_NeitherConverts()
    {
        var side = DataTypePrecedence.DetermineConvertedSide(SqlTypeCategory.Int, SqlTypeCategory.Int);

        Assert.Equal(ComparisonSide.Neither, side);
    }

    [Fact]
    public void DetermineConvertedSide_IntVsBigInt_LowerPrecedenceConverts()
    {
        var side = DataTypePrecedence.DetermineConvertedSide(SqlTypeCategory.Int, SqlTypeCategory.BigInt);

        Assert.Equal(ComparisonSide.Left, side);
    }

    [Fact]
    public void DetermineConvertedSide_SqlTypeOverload_SameCategoryDifferentFacets_Neither()
    {
        var left = new SqlType(SqlTypeCategory.VarChar, Length: 10);
        var right = new SqlType(SqlTypeCategory.VarChar, Length: 20);

        var side = DataTypePrecedence.DetermineConvertedSide(left, right);

        Assert.Equal(ComparisonSide.Neither, side);
    }

    [Fact]
    public void DetermineConvertedSide_SqlTypeOverload_DifferentCategory_DelegatesToPrecedence()
    {
        var left = new SqlType(SqlTypeCategory.VarChar, Length: 20);
        var right = new SqlType(SqlTypeCategory.NVarChar, Length: 20);

        var side = DataTypePrecedence.DetermineConvertedSide(left, right);

        Assert.Equal(ComparisonSide.Left, side);
    }
}
