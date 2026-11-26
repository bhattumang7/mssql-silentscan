using System.Text.RegularExpressions;

namespace SilentScan.Verify.Oracle;

[VerifierSupport]
internal static partial class ScratchIndexOrderProbe
{
    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex PlainIdentifier();

    public static Perturbation Describe(string? tableQualifiedName, string? columnName)
    {
        if (TableAddress.TryParse(tableQualifiedName) is not { } table
            || columnName is not { } column
            || !PlainIdentifier().IsMatch(column))
        {
            return Perturbation.NotApplicable("The finding does not name a plain table and column the probe can address.");
        }

        return new Perturbation(
            [table.Touched],
            Difference.ResultChanged,
            static (_, _, _) => Task.FromResult(0),
            (connection, transaction, cancellationToken) => TableAddress.ExecuteAsync(
                connection, transaction, $"CREATE INDEX silentscan_probe_index ON {table.Table} ([{column}]);", cancellationToken),
            static _ => false,
            options: new PerturbationOptions(TouchesReadAfterCondition: true));
    }
}
