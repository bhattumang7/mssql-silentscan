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

    /// <summary>
    /// Locks in the full official T-SQL precedence order end-to-end
    /// (https://learn.microsoft.com/sql/t-sql/data-types/data-type-precedence-transact-sql),
    /// highest first, so a future misordering of SqlTypeCategory (like the Time/DateTime
    /// mix-up this test suite caught during Phase 3) fails loudly here instead of only
    /// showing up as a wrong-direction verdict somewhere downstream.
    /// </summary>
    [Fact]
    public void DetermineConvertedSide_FullOfficialPrecedenceOrder_EachAdjacentPairConvertsTheLowerOne()
    {
        SqlTypeCategory[] highestToLowest =
        [
            SqlTypeCategory.UserDefined,
            SqlTypeCategory.SqlVariant,
            SqlTypeCategory.Xml,
            SqlTypeCategory.DateTimeOffset,
            SqlTypeCategory.DateTime2,
            SqlTypeCategory.DateTime,
            SqlTypeCategory.SmallDateTime,
            SqlTypeCategory.Date,
            SqlTypeCategory.Time,
            SqlTypeCategory.Float,
            SqlTypeCategory.Real,
            SqlTypeCategory.Decimal,
            SqlTypeCategory.Money,
            SqlTypeCategory.SmallMoney,
            SqlTypeCategory.BigInt,
            SqlTypeCategory.Int,
            SqlTypeCategory.SmallInt,
            SqlTypeCategory.TinyInt,
            SqlTypeCategory.Bit,
            SqlTypeCategory.NText,
            SqlTypeCategory.Text,
            SqlTypeCategory.Image,
            SqlTypeCategory.Timestamp,
            SqlTypeCategory.UniqueIdentifier,
            SqlTypeCategory.NVarChar,
            SqlTypeCategory.NChar,
            SqlTypeCategory.VarChar,
            SqlTypeCategory.Char,
            SqlTypeCategory.VarBinary,
            SqlTypeCategory.Binary,
        ];

        for (var i = 0; i < highestToLowest.Length - 1; i++)
        {
            var higher = highestToLowest[i];
            var lower = highestToLowest[i + 1];

            var side = DataTypePrecedence.DetermineConvertedSide(lower, higher);

            Assert.True(
                side == ComparisonSide.Left,
                $"Expected {lower} (lower precedence) to convert against {higher} (higher precedence), got {side}.");
        }
    }
}
