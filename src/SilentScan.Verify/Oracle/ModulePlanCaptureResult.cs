namespace SilentScan.Verify.Oracle;

public sealed record ModulePlanCaptureResult(string? PlanXml, ModuleNotDecidedReason Reason, string? Detail);
