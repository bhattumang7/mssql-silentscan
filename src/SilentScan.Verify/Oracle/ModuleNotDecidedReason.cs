namespace SilentScan.Verify.Oracle;

public enum ModuleNotDecidedReason
{
    None,

    ModuleKindNotPlannable,

    PlanRequestFailed,

    StatementNotLocated,

    DynamicSql,
}
