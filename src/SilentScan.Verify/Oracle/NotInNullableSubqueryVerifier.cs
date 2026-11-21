using System.Data;
using System.Text.RegularExpressions;
using System.Xml.Linq;
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

    private sealed record StatementResult(List<string> Rows, int RecordsAffected)
    {
        public bool IsEmpty => Rows.Count == 0 && RecordsAffected <= 0;

        public bool SameAs(StatementResult other) =>
            RecordsAffected == other.RecordsAffected && Rows.SequenceEqual(other.Rows, StringComparer.Ordinal);
    }

    protected override async Task<EngineVerification> ObserveAsync(
        SqlConnection connection, NotInNullableSubqueryFinding finding, string statement, CancellationToken cancellationToken)
    {
        var tableParts = finding.SubqueryTableQualifiedName.Split('.').Select(p => p.Trim('[', ']')).ToList();
        var column = finding.SubqueryColumnName.Trim('[', ']');
        if (tableParts.Count is < 1 or > 2 || tableParts.Any(p => !PlainIdentifier().IsMatch(p)) || !PlainIdentifier().IsMatch(column))
        {
            return NotDecided($"'{finding.SubqueryTableQualifiedName}.{finding.SubqueryColumnName}' is not a plain persisted table column the probe can address.");
        }

        var table = string.Join('.', tableParts.Select(p => "[" + p + "]"));
        var quotedColumn = "[" + column + "]";

        try
        {
            if (!await PlanTouchesTableAsync(connection, statement, tableParts, cancellationToken))
            {
                return NotDecided($"The statement's plan never reads {table}, so a NULL placed there cannot be observed through it.");
            }

            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            try
            {
                await ExecuteAsync(connection, transaction, $"DELETE FROM {table} WHERE {quotedColumn} IS NULL;", cancellationToken);
                var withoutNull = await RunAsync(connection, transaction, statement, cancellationToken);

                try
                {
                    await ExecuteAsync(connection, transaction, $"INSERT INTO {table} ({quotedColumn}) VALUES (NULL);", cancellationToken);
                }
                catch (SqlException ex) when (ex.Number == NullIntoNotNullColumnError && ex.Message.Contains($"'{column}'", StringComparison.OrdinalIgnoreCase))
                {
                    return new EngineVerification(EngineVerdict.Refuted, $"The engine rejected a NULL in {table}.{quotedColumn}: {ex.Message}");
                }

                if (withoutNull.IsEmpty)
                {
                    return NotDecided("The statement returns nothing even with no NULL in the column, so a NULL has no result to collapse.");
                }

                var withNull = await RunAsync(connection, transaction, statement, cancellationToken);
                if (withNull.IsEmpty)
                {
                    return new EngineVerification(EngineVerdict.Confirmed, $"Adding a NULL to {table}.{quotedColumn} emptied the statement's result ({withoutNull.Rows.Count} row(s) before).");
                }

                return withNull.SameAs(withoutNull)
                    ? new EngineVerification(EngineVerdict.Refuted, $"Adding a NULL to {table}.{quotedColumn} did not change the statement's result.")
                    : NotDecided("A NULL changed the statement's result without emptying it, which NOT IN alone does not explain.");
            }
            finally
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
        }
        catch (SqlException ex)
        {
            return NotDecided($"The probe could not run: {ex.Message}");
        }
    }

    private static EngineVerification NotDecided(string detail) => new(EngineVerdict.NotDecided, detail);

    private static async Task ExecuteAsync(SqlConnection connection, SqlTransaction transaction, string sql, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(sql, connection, transaction) { CommandTimeout = 120 };
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<StatementResult> RunAsync(SqlConnection connection, SqlTransaction transaction, string statement, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(statement, connection, transaction) { CommandTimeout = 120 };
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<string>();
        do
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var values = new object?[reader.FieldCount];
                reader.GetValues(values);
                rows.Add(string.Join('|', values.Select(v => v is DBNull ? "<null>" : Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture))));
            }
        }
        while (await reader.NextResultAsync(cancellationToken));

        return new StatementResult(rows, reader.RecordsAffected);
    }

    private static async Task<bool> PlanTouchesTableAsync(
        SqlConnection connection, string statement, List<string> tableParts, CancellationToken cancellationToken)
    {
        await using (var on = new SqlCommand("SET SHOWPLAN_XML ON;", connection))
        {
            await on.ExecuteNonQueryAsync(cancellationToken);
        }

        string? planXml = null;
        try
        {
            await using var command = new SqlCommand(statement, connection) { CommandTimeout = 120 };
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            do
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    if (reader.FieldCount == 1 && reader.GetFieldType(0) == typeof(string) && reader.GetString(0) is { } value
                        && value.Contains("ShowPlanXML", StringComparison.Ordinal))
                    {
                        planXml = value;
                    }
                }
            }
            while (await reader.NextResultAsync(cancellationToken));
        }
        finally
        {
            await using var off = new SqlCommand("SET SHOWPLAN_XML OFF;", connection);
            await off.ExecuteNonQueryAsync(CancellationToken.None);
        }

        if (planXml is null)
        {
            return false;
        }

        var expectedTable = tableParts[^1];
        var expectedSchema = tableParts.Count == 2 ? tableParts[0] : null;
        return XDocument.Parse(planXml).Descendants().Any(e =>
            e.Name.LocalName == "Object"
            && string.Equals(Unbracket((string?)e.Attribute("Table")), expectedTable, StringComparison.OrdinalIgnoreCase)
            && (expectedSchema is null || string.Equals(Unbracket((string?)e.Attribute("Schema")), expectedSchema, StringComparison.OrdinalIgnoreCase)));
    }

    private static string? Unbracket(string? name) => name?.Trim('[', ']');
}
