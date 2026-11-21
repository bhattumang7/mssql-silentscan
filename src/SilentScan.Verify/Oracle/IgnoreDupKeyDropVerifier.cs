using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public sealed partial class IgnoreDupKeyDropVerifier : RuleVerifier<QueryAntiPatternFinding>
{
    public override string RuleId => FindingRuleIds.QueryAntiPatternMultiRowInsertIgnoreDupKeyDropRuleId;

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex PlainIdentifier();

    protected override Perturbation Describe(QueryAntiPatternFinding finding)
    {
        if (TableAddress.TryParse(finding.TableQualifiedName) is not { } table
            || finding.IndexName is not { } index
            || !PlainIdentifier().IsMatch(index))
        {
            return Perturbation.NotApplicable("The finding does not name a plain table and index the probe can address.");
        }

        var originalOn = false;
        return new Perturbation(
            [table.Touched],
            Difference.FailureSuppressed,
            async (connection, transaction, cancellationToken) =>
            {
                originalOn = await IndexOriginallyIgnoresDuplicatesAsync(connection, transaction, table, index, cancellationToken);
                return await TableAddress.ExecuteAsync(
                    connection, transaction, $"ALTER INDEX [{index}] ON {table.Table} REBUILD WITH (IGNORE_DUP_KEY = OFF);", cancellationToken);
            },
            (connection, transaction, cancellationToken) => TableAddress.ExecuteAsync(
                connection, transaction, $"ALTER INDEX [{index}] ON {table.Table} REBUILD WITH (IGNORE_DUP_KEY = {(originalOn ? "ON" : "OFF")});", cancellationToken),
            static _ => false);
    }

    private static async Task<bool> IndexOriginallyIgnoresDuplicatesAsync(
        SqlConnection connection, SqlTransaction transaction, TableAddress table, string index, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            $"SELECT ignore_dup_key FROM sys.indexes WHERE object_id = OBJECT_ID(N'{table.Table.Replace("'", "''", StringComparison.Ordinal)}') AND name = @name;",
            connection,
            transaction);
        command.Parameters.AddWithValue("@name", index);
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }
}
