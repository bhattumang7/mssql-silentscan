using System.Text.RegularExpressions;

namespace SilentScan.Verify.Oracle;

[VerifierSupport]
internal static partial class ComputedColumnProbe
{
    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex PlainIdentifier();

    public static Perturbation Describe(string? tableQualifiedName, string columnName, string? expression)
    {
        if (TableAddress.TryParse(tableQualifiedName) is not { } table
            || string.IsNullOrEmpty(expression)
            || !PlainIdentifier().IsMatch(columnName))
        {
            return Perturbation.NotApplicable("The finding does not name a plain table, column and wrapping expression the probe can address.");
        }

        return new Perturbation(
            [table.Touched],
            Difference.PlanChanged,
            static (_, _, _) => Task.FromResult(0),
            async (connection, transaction, cancellationToken) =>
            {
                await TableAddress.ExecuteAsync(connection, transaction, "SET ARITHABORT ON;", cancellationToken);
                await TableAddress.ExecuteAsync(
                    connection, transaction, $"ALTER TABLE {table.Table} ADD silentscan_probe_column AS ({expression});", cancellationToken);
                return await TableAddress.ExecuteAsync(
                    connection, transaction, $"CREATE INDEX silentscan_probe_index ON {table.Table} (silentscan_probe_column);", cancellationToken);
            },
            static _ => false,
            touchesReadAfterCondition: true);
    }
}
