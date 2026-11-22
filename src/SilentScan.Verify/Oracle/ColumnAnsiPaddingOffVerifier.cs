using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public sealed partial class ColumnAnsiPaddingOffVerifier : RuleVerifier<AnsiPaddingOffColumnFinding>
{
    public override string RuleId => FindingRuleIds.ColumnAnsiPaddingOffRuleId;

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex PlainIdentifier();

    protected override Perturbation Describe(AnsiPaddingOffColumnFinding finding)
    {
        if (TableAddress.TryParse(finding.TableQualifiedName) is not { } table
            || !PlainIdentifier().IsMatch(finding.ColumnName))
        {
            return Perturbation.NotApplicable("The finding does not name a plain table and column the probe can address.");
        }

        var column = $"[{finding.ColumnName}]";
        return new Perturbation(
            [table.Touched],
            Difference.ResultChanged,
            static (_, _, _) => Task.FromResult(0),
            async (connection, transaction, cancellationToken) =>
            {
                await using var describe = new SqlCommand(
                    "SELECT system_type_name + CASE is_nullable WHEN 1 THEN N' NULL' ELSE N' NOT NULL' END FROM sys.dm_exec_describe_first_result_set(@query, NULL, 0)",
                    connection,
                    transaction);
                describe.Parameters.AddWithValue("@query", $"SELECT {column} FROM {table.Table}");
                if (await describe.ExecuteScalarAsync(cancellationToken) is not string type)
                {
                    return 0;
                }

                return await TableAddress.ExecuteAsync(
                    connection, transaction, $"SET ANSI_PADDING ON; ALTER TABLE {table.Table} ALTER COLUMN {column} {type};", cancellationToken);
            },
            static _ => false,
            afterStatementQuery: $"SELECT DATALENGTH({column}) FROM {table.Table}");
    }
}
