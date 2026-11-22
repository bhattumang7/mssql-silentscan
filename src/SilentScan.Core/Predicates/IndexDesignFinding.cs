using System.Text.Json.Serialization;
using SilentScan.Core.TypeInference;
using SilentScan.Core.Rules;

namespace SilentScan.Core.Predicates;

public enum IndexDesignFindingKind
{
    HeapWithNonclusteredIndexes,

    RandomClusteredKeyGuidDefault,

    DuplicateIndex,

    SubsumedIndex,

    UnindexedForeignKey,

    FilterColumnNotInIndex,

    FloatOrRealIndexKeyColumn,

    NoRecomputeStatistics,


    RowOrPageLockingDisabled,
}

public sealed record IndexDesignFinding(
    IndexDesignFindingKind Kind,
    string TableQualifiedName,
    string? IndexName,
    string DetailText,
    [property: JsonIgnore] string SourcePath,
    [property: JsonIgnore] int Line,
    FindingConfidence Confidence = FindingConfidence.High) : IFinding
{
    public string RuleId { get; } = FindingRuleIds.IndexDesignRuleId(Kind);

    public SourceSpan Location => new(SourcePath, Line, 1);
}

