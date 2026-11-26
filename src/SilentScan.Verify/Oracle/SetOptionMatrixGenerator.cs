using System.Globalization;
using Microsoft.Data.SqlClient;
using SilentScan.Core.Rules;
using SilentScan.Verify.Deployment;

namespace SilentScan.Verify.Oracle;

public sealed record SetOptionProbe(string Id, string Option, string Construct, string OnStatement, string OffStatement, string Batch);

public sealed class SetOptionMatrixGenerator
{
    public static readonly IReadOnlyList<SetOptionProbe> Probes =
    [
        new("concat-null", "CONCAT_NULL_YIELDS_NULL", "string concatenation with NULL", "SET CONCAT_NULL_YIELDS_NULL ON", "SET CONCAT_NULL_YIELDS_NULL OFF",
            "SELECT COALESCE('[' + NULL + ']', 'NULL-RESULT')"),
        new("equals-null", "ANSI_NULLS", "= NULL comparison", "SET ANSI_NULLS ON", "SET ANSI_NULLS OFF",
            "DECLARE @n INT = NULL; SELECT CASE WHEN @n = NULL THEN 'match' ELSE 'nomatch' END"),
        new("not-equals-null", "ANSI_NULLS", "<> NULL comparison", "SET ANSI_NULLS ON", "SET ANSI_NULLS OFF",
            "DECLARE @n INT = 1; SELECT CASE WHEN @n <> NULL THEN 'match' ELSE 'nomatch' END"),
        new("padding-trailing-space", "ANSI_PADDING", "trailing space kept in varchar column", "SET ANSI_PADDING ON", "SET ANSI_PADDING OFF",
            "CREATE TABLE #t (v VARCHAR(10)); INSERT #t VALUES ('ab   '); SELECT CAST(DATALENGTH(v) AS VARCHAR(5)) FROM #t"),
        new("padding-binary", "ANSI_PADDING", "trailing zeros kept in varbinary column", "SET ANSI_PADDING ON", "SET ANSI_PADDING OFF",
            "CREATE TABLE #t (v VARBINARY(10)); INSERT #t VALUES (0x010200); SELECT CAST(DATALENGTH(v) AS VARCHAR(5)) FROM #t"),
        new("warnings-truncation", "ANSI_WARNINGS", "string truncation on insert", "SET ANSI_WARNINGS ON", "SET ANSI_WARNINGS OFF",
            "CREATE TABLE #t (v VARCHAR(3)); INSERT #t VALUES ('abcdef'); SELECT v FROM #t"),
        new("warnings-overflow-null", "ANSI_WARNINGS", "aggregate over NULL", "SET ANSI_WARNINGS ON", "SET ANSI_WARNINGS OFF",
            "CREATE TABLE #t (v INT); INSERT #t VALUES (NULL); SELECT COALESCE(CAST(SUM(v) AS VARCHAR(10)), 'NULL-RESULT') FROM #t"),
        new("warnings-divide-by-zero", "ANSI_WARNINGS", "divide by zero", "SET ANSI_WARNINGS ON", "SET ANSI_WARNINGS OFF",
            "SELECT COALESCE(CAST(1 / 0 AS VARCHAR(10)), 'NULL-RESULT')"),
        new("arithabort-divide-by-zero", "ARITHABORT", "divide by zero with ANSI_WARNINGS off", "SET ANSI_WARNINGS OFF; SET ARITHABORT ON", "SET ANSI_WARNINGS OFF; SET ARITHABORT OFF",
            "SELECT COALESCE(CAST(1 / 0 AS VARCHAR(10)), 'NULL-RESULT')"),
        new("arithignore-divide-by-zero", "ARITHIGNORE", "divide by zero with arithmetic aborts off", "SET ANSI_WARNINGS OFF; SET ARITHABORT OFF; SET ARITHIGNORE ON", "SET ANSI_WARNINGS OFF; SET ARITHABORT OFF; SET ARITHIGNORE OFF",
            "SELECT COALESCE(CAST(1 / 0 AS VARCHAR(10)), 'NULL-RESULT')"),
        new("numeric-roundabort", "NUMERIC_ROUNDABORT", "decimal rounding on assignment", "SET NUMERIC_ROUNDABORT ON", "SET NUMERIC_ROUNDABORT OFF",
            "DECLARE @d DECIMAL(5,1) = 1.234; SELECT CAST(@d AS VARCHAR(10))"),
        new("quoted-identifier-literal", "QUOTED_IDENTIFIER", "double-quoted token", "SET QUOTED_IDENTIFIER ON", "SET QUOTED_IDENTIFIER OFF",
            "SELECT \"abc\""),
        new("dateformat-dmy", "DATEFORMAT", "ambiguous date literal day and month", "SET DATEFORMAT mdy", "SET DATEFORMAT dmy",
            "SELECT CONVERT(VARCHAR(10), CAST('02/03/2024' AS DATETIME), 120)"),
        new("dateformat-iso-datetime", "DATEFORMAT", "yyyy-mm-dd datetime literal", "SET DATEFORMAT mdy", "SET DATEFORMAT dmy",
            "SELECT CONVERT(VARCHAR(10), CAST('2024-02-03' AS DATETIME), 120)"),
        new("dateformat-iso-date", "DATEFORMAT", "yyyy-mm-dd date literal", "SET DATEFORMAT mdy", "SET DATEFORMAT dmy",
            "SELECT CONVERT(VARCHAR(10), CAST('2024-02-03' AS DATE), 120)"),
        new("datefirst-weekday", "DATEFIRST", "DATEPART weekday", "SET DATEFIRST 7", "SET DATEFIRST 1",
            "SELECT CAST(DATEPART(weekday, '2024-01-07') AS VARCHAR(5))"),
        new("language-month-name", "LANGUAGE", "DATENAME month", "SET LANGUAGE us_english", "SET LANGUAGE german",
            "SELECT DATENAME(month, '2024-05-01')"),
        new("rowcount-limit", "ROWCOUNT", "row limit on SELECT", "SET ROWCOUNT 0", "SET ROWCOUNT 1",
            "SELECT CAST(COUNT(*) AS VARCHAR(5)) FROM (SELECT 1 AS x UNION ALL SELECT 2 UNION ALL SELECT 3) AS q; "),
        new("rowcount-limit-rows", "ROWCOUNT", "row limit on SELECT returning rows", "SET ROWCOUNT 0", "SET ROWCOUNT 1",
            "DECLARE @n INT; SELECT x INTO #r FROM (SELECT 1 AS x UNION ALL SELECT 2 UNION ALL SELECT 3) AS q; SELECT @n = COUNT(*) FROM #r; SELECT CAST(@n AS VARCHAR(5))"),
        new("textsize-truncation", "TEXTSIZE", "large value read size", "SET TEXTSIZE 2147483647", "SET TEXTSIZE 4",
            "SELECT CAST(DATALENGTH(CAST(REPLICATE(CAST('a' AS VARCHAR(MAX)), 100) AS VARCHAR(MAX))) AS VARCHAR(10))"),
        new("implicit-transactions-trancount", "IMPLICIT_TRANSACTIONS", "transaction opened by a statement", "SET IMPLICIT_TRANSACTIONS ON", "SET IMPLICIT_TRANSACTIONS OFF",
            "CREATE TABLE #t (v INT); INSERT #t VALUES (1); SELECT CAST(@@TRANCOUNT AS VARCHAR(5))"),
        new("nocount-rowcount-global", "NOCOUNT", "row count after statement", "SET NOCOUNT ON", "SET NOCOUNT OFF",
            "CREATE TABLE #t (v INT); INSERT #t VALUES (1); SELECT CAST(@@ROWCOUNT AS VARCHAR(5))"),
    ];

    private readonly DatabaseProvisioner _provisioner;
    private readonly SqlServerOptions _options;

    public SetOptionMatrixGenerator(SqlServerOptions options)
    {
        _options = options;
        _provisioner = new DatabaseProvisioner(options);
    }

    public async Task<(IReadOnlyList<SetOptionCell> Cells, string ServerVersion)> GenerateAsync(CancellationToken cancellationToken = default)
    {
        var database = "SilentScanSetOption_" + Guid.NewGuid().ToString("N")[..8];
        SqlConnection.ClearAllPools();
        await _provisioner.CreateFreshAsync(database, cancellationToken: cancellationToken);
        try
        {
            var cells = new List<SetOptionCell>();
            foreach (var probe in Probes)
            {
                var on = await RunAsync(database, probe.OnStatement, probe.Batch, cancellationToken);
                var off = await RunAsync(database, probe.OffStatement, probe.Batch, cancellationToken);
                cells.Add(new SetOptionCell(probe.Id, probe.Option, probe.Construct, on, off));
            }

            return (cells, await ReadServerVersionAsync(database, cancellationToken));
        }
        finally
        {
            await _provisioner.DropIfExistsAsync(database, cancellationToken);
            SqlConnection.ClearAllPools();
        }
    }

    internal static string BuildBatch(string statement, string batch) =>
        string.Create(CultureInfo.InvariantCulture, $"SET NOCOUNT OFF; {statement}; {batch}");

    private async Task<string> RunAsync(string database, string statement, string batch, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_options.BuildConnectionString(database));
        await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = new SqlCommand(BuildBatch(statement, batch), connection) { CommandTimeout = 60 };
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            string? last = null;
            do
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    last = (await reader.IsDBNullAsync(0, cancellationToken)) ? "NULL" : Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture);
                }
            }
            while (await reader.NextResultAsync(cancellationToken));

            return last ?? "NoRows";
        }
        catch (SqlException ex)
        {
            return "Error:" + ex.Number.ToString(CultureInfo.InvariantCulture);
        }
    }

    private async Task<string> ReadServerVersionAsync(string database, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_options.BuildConnectionString(database));
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("SELECT CAST(SERVERPROPERTY('ProductVersion') AS VARCHAR(50));", connection);
        return (string)(await command.ExecuteScalarAsync(cancellationToken))!;
    }
}
