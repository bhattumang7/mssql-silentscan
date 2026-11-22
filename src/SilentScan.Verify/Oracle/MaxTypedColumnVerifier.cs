using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public sealed partial class MaxTypedColumnVerifier : RuleVerifier<MaxTypedColumnFinding>
{
    public override string RuleId => FindingRuleIds.MaxTypedColumnMaxLengthRuleId;

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex PlainIdentifier();

    [GeneratedRegex(@"^(n?varchar|varbinary)\(", RegexOptions.IgnoreCase)]
    private static partial Regex Family();

    protected override Perturbation Describe(MaxTypedColumnFinding finding)
    {
        if (TableAddress.TryParse(finding.TableQualifiedName) is not { } table
            || !PlainIdentifier().IsMatch(finding.ColumnName))
        {
            return Perturbation.NotApplicable("The finding does not name a plain table and column the probe can address.");
        }

        var column = $"[{finding.ColumnName}]";
        string? original = null;
        string? indexable = null;
        return new Perturbation(
            [table.Touched],
            Difference.PlanChanged,
            async (connection, transaction, cancellationToken) =>
            {
                await using var describe = new SqlCommand(
                    "SELECT system_type_name + CASE is_nullable WHEN 1 THEN N' NULL' ELSE N' NOT NULL' END FROM sys.dm_exec_describe_first_result_set(@query, NULL, 0)",
                    connection,
                    transaction);
                describe.Parameters.AddWithValue("@query", $"SELECT {column} FROM {table.Table}");
                original = await describe.ExecuteScalarAsync(cancellationToken) as string;
                if (original is null || Family().Match(original) is not { Success: true } family)
                {
                    original = null;
                    return 0;
                }

                indexable = $"{family.Groups[1].Value}(100){original[(original.IndexOf(')', StringComparison.Ordinal) + 1)..]}";
                await TableAddress.ExecuteAsync(
                    connection, transaction, $"ALTER TABLE {table.Table} ALTER COLUMN {column} {indexable};", cancellationToken);
                return await TableAddress.ExecuteAsync(
                    connection, transaction, $"CREATE INDEX silentscan_probe_index ON {table.Table} ({column});", cancellationToken);
            },
            async (connection, transaction, cancellationToken) =>
            {
                if (original is null)
                {
                    return 0;
                }

                await TableAddress.ExecuteAsync(connection, transaction, $"DROP INDEX silentscan_probe_index ON {table.Table};", cancellationToken);
                await TableAddress.ExecuteAsync(
                    connection, transaction, $"ALTER TABLE {table.Table} ALTER COLUMN {column} {original};", cancellationToken);
                try
                {
                    await TableAddress.ExecuteAsync(
                        connection, transaction, $"CREATE INDEX silentscan_probe_index ON {table.Table} ({column});", cancellationToken);
                }
                catch (SqlException ex) when (ex.Number is 1919 or 1946)
                {
                    return -1;
                }

                return -1;
            },
            static _ => false);
    }
}
