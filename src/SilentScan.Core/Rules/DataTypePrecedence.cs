using SilentScan.Core.Catalog;

namespace SilentScan.Core.Rules;

/// <summary>
/// Implements the T-SQL data type precedence rule: in a comparison, the operand with
/// LOWER precedence is implicitly converted to the type of the HIGHER-precedence operand.
/// This is the single most important piece of logic in the tool — get the direction wrong
/// and every downstream finding is backwards. See CLAUDE.md "The type rules".
/// </summary>
public static class DataTypePrecedence
{
    /// <summary>
    /// Determines which side of a two-operand comparison the engine implicitly converts,
    /// given only the type categories (no collation/length facets).
    /// </summary>
    public static ComparisonSide DetermineConvertedSide(SqlTypeCategory left, SqlTypeCategory right)
    {
        if (left == right)
        {
            return ComparisonSide.Neither;
        }

        // The category with LOWER precedence rank converts TO the higher one, so the
        // side that converts is the side with the lower rank.
        return left < right ? ComparisonSide.Left : ComparisonSide.Right;
    }

    /// <summary>
    /// Determines which side converts for a full <see cref="SqlType"/> pair, accounting for
    /// same-category cases (e.g. varchar vs varchar) where collation may still differ but no
    /// data type conversion occurs — only a collation coercion, which is a distinct concern
    /// handled by <see cref="Collation"/>-aware rules downstream.
    /// </summary>
    public static ComparisonSide DetermineConvertedSide(SqlType left, SqlType right)
    {
        if (left.Category == right.Category)
        {
            return ComparisonSide.Neither;
        }

        return DetermineConvertedSide(left.Category, right.Category);
    }
}
