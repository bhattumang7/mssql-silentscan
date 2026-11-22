namespace SilentScan.Verify.Oracle;

[VerifierSupport]
internal static class ExtraRowProbe
{
    public static Perturbation Describe(string? qualifiedTable)
    {
        if (TableAddress.TryParse(qualifiedTable) is not { } table)
        {
            return Perturbation.NotApplicable("The finding does not name a plain table the probe can address.");
        }

        return new Perturbation(
            [table.Touched],
            Difference.ResultChanged,
            static (_, _, _) => Task.FromResult(0),
            (connection, transaction, cancellationToken) => TableAddress.ExecuteAsync(
                connection, transaction, $"INSERT INTO {table.Table} DEFAULT VALUES;", cancellationToken),
            static _ => false);
    }
}
