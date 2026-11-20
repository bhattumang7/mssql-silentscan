namespace SilentScan.Verify.Oracle;

public enum ModuleNotDecidedReason
{
    None,

    ModuleKindNotPlannable,

    ModuleDoesNotCompile,

    PlanRequestFailed,

    StatementNotLocated,

    ConversionNotAttributable,

    DynamicSql,
}
