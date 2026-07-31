namespace SilentScan.Verify.Oracle;

/// <summary>One CONVERT_IMPLICIT applied to a column reference, found in a plan XML.</summary>
public sealed record ConvertImplicitFinding(
    string? Database,
    string? Schema,
    string? Table,
    string? Column,
    string ConvertedToDataType);
