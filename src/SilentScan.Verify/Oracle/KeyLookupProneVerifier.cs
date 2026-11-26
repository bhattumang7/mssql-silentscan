using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public sealed partial class KeyLookupProneVerifier : RuleVerifier<IndexCoverageFinding>
{
    public override string RuleId => FindingRuleIds.IndexCoverageKeyLookupProneIndexRuleId;

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex PlainIdentifier();

    protected override Perturbation Describe(IndexCoverageFinding finding)
    {
        var columns = finding.IndexKeyColumns.Concat(finding.IndexIncludedColumns).Concat(finding.UncoveredColumns).ToList();
        if (TableAddress.TryParse(finding.TableQualifiedName) is not { } table
            || finding.IndexName is not { } index
            || !PlainIdentifier().IsMatch(index)
            || finding.UncoveredColumns.Count == 0
            || finding.IndexKeyColumns.Count == 0
            || columns.Any(column => !PlainIdentifier().IsMatch(column)))
        {
            return Perturbation.NotApplicable("The finding does not name a plain table, index and columns the probe can address.");
        }

        var keys = string.Join(", ", finding.IndexKeyColumns.Select(column => $"[{column}]"));
        var included = string.Join(", ", finding.IndexIncludedColumns.Concat(finding.UncoveredColumns).Distinct(StringComparer.OrdinalIgnoreCase).Select(column => $"[{column}]"));
        return new Perturbation(
            [table.Touched],
            Difference.PlanChanged,
            static (_, _, _) => Task.FromResult(0),
            async (connection, transaction, cancellationToken) =>
            {
                if (!await IsPlainNonclusteredAsync(connection, transaction, table, index, cancellationToken))
                {
                    return 0;
                }

                return await TableAddress.ExecuteAsync(
                    connection,
                    transaction,
                    $"CREATE NONCLUSTERED INDEX [{index}] ON {table.Table} ({keys}) INCLUDE ({included}) WITH (DROP_EXISTING = ON);",
                    cancellationToken);
            },
            static _ => false,
            options: new PerturbationOptions(TouchesReadAfterCondition: true));
    }

    private static async Task<bool> IsPlainNonclusteredAsync(
        SqlConnection connection, SqlTransaction transaction, TableAddress table, string index, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            $"""
            SELECT COUNT(*) FROM sys.indexes i
            WHERE i.object_id = OBJECT_ID(N'{table.Table.Replace("'", "''", StringComparison.Ordinal)}') AND i.name = @name
              AND i.type = 2 AND i.is_unique = 0 AND i.has_filter = 0 AND i.is_primary_key = 0 AND i.is_unique_constraint = 0
              AND NOT EXISTS (SELECT 1 FROM sys.index_columns ic WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_descending_key = 1);
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("@name", index);
        return await command.ExecuteScalarAsync(cancellationToken) is int count && count == 1;
    }
}
