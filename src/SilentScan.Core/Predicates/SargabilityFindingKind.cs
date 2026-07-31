namespace SilentScan.Core.Predicates;

/// <summary>Tier-1 syntactic non-sargable predicate patterns (CLAUDE.md: "no types needed").</summary>
public enum SargabilityFindingKind
{
    /// <summary>YEAR(col) = ..., UPPER(col) = ..., ISNULL(col, x) = ...</summary>
    FunctionWrappedColumn,

    /// <summary>CONVERT(type, col) = ..., CAST(col AS type) = ...</summary>
    CastOrConvertOnColumn,

    /// <summary>col + 1 = ..., col * 2 &gt; ...</summary>
    ColumnArithmetic,

    /// <summary>col LIKE '%...'</summary>
    LeadingWildcardLike,

    /// <summary>col LIKE @p - the pattern isn't a literal, so a leading wildcard can't be ruled out statically.</summary>
    LikePatternNotLiteral,
}
