namespace SilentScan.Core.Predicates;

public enum WriteLossTarget
{
    Column,

    Variable,

    FunctionReturn,

    ParameterDefault,
}

public static class WriteLossTargetPolicy
{
    public static bool IsSilentTarget(this WriteLossTarget target) => target != WriteLossTarget.Column;

    public static bool IsOracleVerified(this WriteLossTarget target, WriteLossKind kind) => target switch
    {
        WriteLossTarget.FunctionReturn or WriteLossTarget.ParameterDefault =>
            kind is WriteLossKind.LengthTruncation or WriteLossKind.NumericScaleNarrowing or WriteLossKind.ApproximateToExactTruncation
                or WriteLossKind.ExactToApproximatePrecisionLoss,
        _ => true,
    };
}
