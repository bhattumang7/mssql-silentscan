using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public sealed partial class NotInNullableSubqueryVerifier : RuleVerifier<NotInNullableSubqueryFinding>
{
    public override string RuleId => FindingRuleIds.NotInNullableSubqueryRuleId;

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex PlainIdentifier();

    protected override async Task<EngineVerification> ObserveAsync(SqlConnection connection, NotInNullableSubqueryFinding finding, CancellationToken cancellationToken)
    {
        var tableParts = finding.SubqueryTableQualifiedName.Split('.').Select(p => p.Trim('[', ']')).ToList();
        var column = finding.SubqueryColumnName.Trim('[', ']');
        if (tableParts.Count is < 1 or > 3 || tableParts.Any(p => !PlainIdentifier().IsMatch(p)) || !PlainIdentifier().IsMatch(column))
        {
            return NotDecided($"'{finding.SubqueryTableQualifiedName}.{finding.SubqueryColumnName}' is not a plain persisted table column the probe can address.");
        }

        var table = string.Join('.', tableParts.Select(p => "[" + p + "]"));
        var quotedColumn = "[" + column + "]";

        var (baseType, isNullable) = await ReadColumnAsync(connection, table, column, cancellationToken);
        if (baseType is null)
        {
            return NotDecided($"{table}.{quotedColumn} does not exist in the target database.");
        }

        if (isNullable == false)
        {
            return new EngineVerification(EngineVerdict.Refuted, $"{table}.{quotedColumn} is declared NOT NULL in sys.columns, so the subquery can never yield a NULL.");
        }

        var literal = NonNullLiteral(baseType);
        if (literal is null)
        {
            return NotDecided($"No non-null probe value is defined for column type '{baseType}'.");
        }

        var empty = string.Create(
            CultureInfo.InvariantCulture,
            $"SELECT v INTO {{0}} FROM (SELECT {quotedColumn} AS v FROM {table} WHERE 1 = 0 UNION ALL SELECT {quotedColumn} FROM {table} WHERE 1 = 0) q;");
        var batch =
            string.Format(CultureInfo.InvariantCulture, empty, "#probe_inner") + "\n" +
            string.Format(CultureInfo.InvariantCulture, empty, "#probe_outer") + "\n" +
            "INSERT INTO #probe_inner (v) VALUES (NULL);\n" +
            $"INSERT INTO #probe_outer (v) VALUES ({literal});\n" +
            "SELECT (SELECT COUNT(*) FROM #probe_outer WHERE v NOT IN (SELECT v FROM #probe_inner))," +
            " (SELECT COUNT(*) FROM #probe_outer o WHERE NOT EXISTS (SELECT 1 FROM #probe_inner i WHERE i.v = o.v));";

        try
        {
            await using var command = new SqlCommand(batch, connection) { CommandTimeout = 120 };
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            var notInRows = reader.GetInt32(0);
            var notExistsRows = reader.GetInt32(1);
            return notInRows != notExistsRows
                ? new EngineVerification(EngineVerdict.Confirmed, $"With a NULL in the subquery, NOT IN returned {notInRows} row(s) where NOT EXISTS returned {notExistsRows}.")
                : new EngineVerification(EngineVerdict.Refuted, $"NOT IN and NOT EXISTS both returned {notInRows} row(s); the engine showed no NULL effect.");
        }
        catch (SqlException ex)
        {
            return NotDecided($"The probe failed: {ex.Message}");
        }
    }

    private static EngineVerification NotDecided(string detail) => new(EngineVerdict.NotDecided, detail);

    private static async Task<(string? BaseType, bool? IsNullable)> ReadColumnAsync(
        SqlConnection connection, string table, string column, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            "SELECT TYPE_NAME(c.system_type_id), c.is_nullable FROM sys.columns c WHERE c.object_id = OBJECT_ID(@table) AND c.name = @column;",
            connection);
        command.Parameters.AddWithValue("@table", table);
        command.Parameters.AddWithValue("@column", column);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? (reader.IsDBNull(0) ? null : reader.GetString(0), reader.GetBoolean(1))
            : (null, null);
    }

    private static string? NonNullLiteral(string baseType) => baseType.ToLowerInvariant() switch
    {
        "tinyint" or "smallint" or "int" or "bigint" or "bit" or "decimal" or "numeric" or "money" or "smallmoney" or "float" or "real" => "0",
        "char" or "varchar" => "'a'",
        "nchar" or "nvarchar" => "N'a'",
        "date" or "datetime" or "datetime2" or "smalldatetime" or "datetimeoffset" => "'2000-01-01'",
        "time" => "'01:02:03'",
        "uniqueidentifier" => "NEWID()",
        "binary" or "varbinary" => "0x01",
        _ => null,
    };
}
