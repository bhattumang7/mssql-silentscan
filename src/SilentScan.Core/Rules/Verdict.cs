namespace SilentScan.Core.Rules;

/// <summary>
/// Pass 4 verdict (CLAUDE.md). This classifier implements SeekPreserved/RangeSeek/
/// ScanForced/Unknown - the type-precedence + collation driven outcomes. Two verdicts from
/// CLAUDE.md's full vocabulary are deliberately NOT produced here: NotSargableFunction is
/// Tier-1's concern (<see cref="Predicates.NonSargablePredicateScanner"/>, a purely syntactic
/// check reported as its own finding stream), and OperandClash (genuinely incompatible types
/// that would error rather than convert) has no implemented rule yet - a known gap, not a
/// silently wrong classification.
/// </summary>
public enum Verdict
{
    SeekPreserved,
    RangeSeek,
    ScanForced,
    Unknown,
}
