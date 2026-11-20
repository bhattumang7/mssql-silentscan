namespace SilentScan.Verify.Oracle;

public sealed record PlannedStatement(
    string StatementText,
    int? StartLine,
    int? EndLine,
    IReadOnlyList<ConvertImplicitFinding> Conversions);
