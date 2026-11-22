using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public sealed partial class CheckConstraintNullNotHandledVerifier : RuleVerifier<CheckConstraintFinding>
{
    public override string RuleId => FindingRuleIds.CheckConstraintNullNotHandledRuleId;

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex PlainIdentifier();

    protected override Perturbation Describe(CheckConstraintFinding finding)
    {
        if (TableAddress.TryParse(finding.TableQualifiedName) is not { } table
            || !PlainIdentifier().IsMatch(finding.ColumnName))
        {
            return Perturbation.NotApplicable("The finding does not name a plain table and column the probe can address.");
        }

        var column = $"[{finding.ColumnName}]";
        string? type = null;
        return new Perturbation(
            [table.Touched],
            Difference.FailureSuppressed,
            async (connection, transaction, cancellationToken) =>
            {
                await using var describe = new SqlCommand(
                    "SELECT system_type_name FROM sys.dm_exec_describe_first_result_set(@query, NULL, 0)", connection, transaction);
                describe.Parameters.AddWithValue("@query", $"SELECT {column} FROM {table.Table}");
                type = await describe.ExecuteScalarAsync(cancellationToken) as string;
                return type is null
                    ? 0
                    : await TableAddress.ExecuteAsync(
                        connection, transaction, $"ALTER TABLE {table.Table} ALTER COLUMN {column} {type} NOT NULL;", cancellationToken);
            },
            (connection, transaction, cancellationToken) => type is null
                ? Task.FromResult(0)
                : TableAddress.ExecuteAsync(
                    connection, transaction, $"ALTER TABLE {table.Table} ALTER COLUMN {column} {type} NULL;", cancellationToken),
            static _ => false);
    }
}
