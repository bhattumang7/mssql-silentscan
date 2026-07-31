namespace SilentScan.Core.Rules;

/// <summary>Which side of a two-operand comparison the engine implicitly converts.</summary>
public enum ComparisonSide
{
    /// <summary>Types are identical in every comparison-relevant facet; no conversion occurs.</summary>
    Neither,

    Left,

    Right,

    /// <summary>Same category, e.g. varchar vs varchar with differing collations only.</summary>
    Both,
}
