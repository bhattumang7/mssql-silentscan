using SilentScan.Core.Predicates;

namespace SilentScan.Verify.Oracle;

public sealed record ModuleComparisonRow(
    ModuleComparisonOutcome Outcome,
    ModuleNotDecidedReason Reason,
    string Table,
    string Column,
    int? StatementStartLine,
    TypedPredicateFinding? Finding);
