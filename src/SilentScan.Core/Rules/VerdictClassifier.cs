using SilentScan.Core.Catalog;

namespace SilentScan.Core.Rules;

/// <summary>
/// Classifies a column-vs-other comparison per CLAUDE.md's type rules: only column-side
/// conversion loses the seek; collation family determines SCAN_FORCED vs RANGE_SEEK for
/// string-family conversions; unresolved collation is UNKNOWN, never a guess.
/// </summary>
public static class VerdictClassifier
{
    public static Verdict Classify(SqlType? columnType, SqlType? otherType)
    {
        if (columnType is null || otherType is null)
        {
            return Verdict.Unknown;
        }

        if (columnType.Category == otherType.Category)
        {
            return ClassifySameCategory(columnType, otherType);
        }

        var convertedSide = DataTypePrecedence.DetermineConvertedSide(columnType.Category, otherType.Category);
        if (convertedSide != ComparisonSide.Left)
        {
            // The OTHER side converts (or, defensively, neither) - harmless to the seek.
            return Verdict.SeekPreserved;
        }

        // The column converts. String-family conversions get the collation-aware nuance
        // (CLAUDE.md); every other cross-category column-side conversion is conservatively
        // SCAN_FORCED - OPERAND_CLASH (genuinely incompatible pairs) is not yet implemented.
        return columnType.IsStringFamily && otherType.IsStringFamily
            ? ClassifyByColumnCollation(columnType)
            : Verdict.ScanForced;
    }

    private static Verdict ClassifySameCategory(SqlType columnType, SqlType otherType)
    {
        if (!columnType.IsStringFamily || columnType.Collation is null || otherType.Collation is null)
        {
            // Same category, no collation to conflict on (or collation unresolved on a
            // non-comparison-relevant side) - length/precision differences alone don't
            // defeat sargability.
            return Verdict.SeekPreserved;
        }

        if (string.Equals(columnType.Collation.Name, otherType.Collation.Name, StringComparison.OrdinalIgnoreCase))
        {
            return Verdict.SeekPreserved;
        }

        // Same string category, genuinely different collations: T-SQL's coercibility
        // precedence rules for resolving which collation wins are a distinct, non-trivial
        // rule set this pass does not implement (CLAUDE.md precision discipline: never
        // guess). Scope note, not a bug: only the documented cross-category (varchar vs
        // nvarchar) collation rule is implemented in ClassifyByColumnCollation.
        return Verdict.Unknown;
    }

    private static Verdict ClassifyByColumnCollation(SqlType columnType)
    {
        if (columnType.Collation is null)
        {
            // CLAUDE.md: "If column collation unknown ... verdict UNKNOWN unless the
            // manifest pins a collation. Never guess silently."
            return Verdict.Unknown;
        }

        return columnType.Collation.IsSqlFamily ? Verdict.ScanForced : Verdict.RangeSeek;
    }
}
