using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public sealed partial class NotInNullableSubqueryVerifier : RuleVerifier<NotInNullableSubqueryFinding>
{
    private const int NullIntoNotNullColumnError = 515;

    public override string RuleId => FindingRuleIds.NotInNullableSubqueryRuleId;

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex PlainIdentifier();

    protected override Perturbation Describe(NotInNullableSubqueryFinding finding)
    {
        var tableParts = finding.SubqueryTableQualifiedName.Split('.').Select(p => p.Trim('[', ']')).ToList();
        var column = finding.SubqueryColumnName.Trim('[', ']');
        if (tableParts.Count is < 1 or > 2 || tableParts.Any(p => !PlainIdentifier().IsMatch(p)) || !PlainIdentifier().IsMatch(column))
        {
            return Perturbation.NotApplicable($"'{finding.SubqueryTableQualifiedName}.{finding.SubqueryColumnName}' is not a plain persisted table column the probe can address.");
        }

        var table = string.Join('.', tableParts.Select(p => "[" + p + "]"));
        var quotedColumn = "[" + column + "]";
        var touched = new TouchedObject(tableParts.Count == 2 ? tableParts[0] : null, tableParts[^1]);

        return new Perturbation(
            [touched],
            Difference.ResultEmptied,
            (connection, transaction, cancellationToken) => ExecuteAsync(connection, transaction, $"DELETE FROM {table} WHERE {quotedColumn} IS NULL;", cancellationToken),
            (connection, transaction, cancellationToken) => ExecuteAsync(connection, transaction, $"INSERT INTO {table} ({quotedColumn}) VALUES (NULL);", cancellationToken),
            ex => ex.Number == NullIntoNotNullColumnError && ex.Message.Contains($"'{column}'", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task ExecuteAsync(SqlConnection connection, SqlTransaction transaction, string sql, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(sql, connection, transaction) { CommandTimeout = 120 };
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
