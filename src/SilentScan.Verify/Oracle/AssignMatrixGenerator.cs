using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient;
using SilentScan.Core.Rules;
using SilentScan.Verify.Deployment;

namespace SilentScan.Verify.Oracle;

public sealed record AssignProbeType(string Syntax, IReadOnlyList<string> Values);

public sealed class AssignMatrixGenerator
{
    private const int MaxConcurrentProbes = 4;

    public static readonly IReadOnlyList<AssignProbeType> Types =
    [
        new("BIT", ["0", "1"]),
        new("TINYINT", ["0", "255"]),
        new("SMALLINT", ["300", "-32768"]),
        new("INT", ["100000", "2147483647"]),
        new("BIGINT", ["9007199254740993", "5000000000"]),
        new("DECIMAL(5,1)", ["1234.5", "0.1"]),
        new("DECIMAL(18,4)", ["1.2345", "12345678901234.5678"]),
        new("DECIMAL(38,10)", ["1.0123456789", "12345678901234567890123456.0123456789"]),
        new("SMALLMONEY", ["1.2345", "214748.3647"]),
        new("MONEY", ["1.2345", "922337203685477.5807"]),
        new("REAL", ["1.5", "123456789.123"]),
        new("FLOAT", ["1.5", "123456789.123456789"]),
        new("CHAR(10)", ["'abcdefghij'", "'a'"]),
        new("VARCHAR(5)", ["'abcde'", "'a'"]),
        new("VARCHAR(10)", ["'abcdefghij'", "'a'"]),
        new("VARCHAR(MAX)", ["REPLICATE(CAST('a' AS VARCHAR(MAX)), 5000)", "'a'"]),
        new("NCHAR(10)", ["N'abcdefghij'", "N'ā'"]),
        new("NVARCHAR(10)", ["N'abcdefghij'", "N'ā'", "N'日本語'"]),
        new("NVARCHAR(MAX)", ["REPLICATE(CAST(N'a' AS NVARCHAR(MAX)), 9000)", "N'ā'"]),
        new("DATE", ["'2024-01-02'"]),
        new("TIME(0)", ["'03:04:05'"]),
        new("TIME(7)", ["'03:04:05.1234567'"]),
        new("SMALLDATETIME", ["'2024-01-02 03:04:00'"]),
        new("DATETIME", ["'2024-01-02 03:04:05.123'"]),
        new("DATETIME2(0)", ["'2024-01-02 03:04:05'"]),
        new("DATETIME2(3)", ["'2024-01-02 03:04:05.123'"]),
        new("DATETIME2(7)", ["'2024-01-02 03:04:05.1234567'"]),
        new("DATETIMEOFFSET(0)", ["'2024-01-02 03:04:05 +05:30'"]),
        new("DATETIMEOFFSET(7)", ["'2024-01-02 03:04:05.1234567 +05:30'"]),
        new("BINARY(10)", ["0x0102030405060708090A"]),
        new("VARBINARY(10)", ["0x0102030405060708090A"]),
        new("VARBINARY(MAX)", ["CONVERT(VARBINARY(MAX), REPLICATE(CAST('a' AS VARCHAR(MAX)), 9000))"]),
        new("UNIQUEIDENTIFIER", ["'A0EEBC99-9C0B-4EF8-BB6D-6BB9BD380A11'"]),
    ];

    private readonly DatabaseProvisioner _provisioner;
    private readonly SqlServerOptions _options;

    public AssignMatrixGenerator(SqlServerOptions options)
    {
        _options = options;
        _provisioner = new DatabaseProvisioner(options);
    }

    public async Task<(IReadOnlyList<AssignCellOutcome> Cells, string ServerVersion)> GenerateAsync(
        IReadOnlyList<AssignProbeType> sources,
        IReadOnlyList<AssignProbeType> targets,
        CancellationToken cancellationToken = default)
    {
        var database = "SilentScanAssignMatrix_" + Guid.NewGuid().ToString("N")[..8];
        SqlConnection.ClearAllPools();
        await _provisioner.CreateFreshAsync(database, cancellationToken: cancellationToken);
        var cells = new ConcurrentBag<AssignCellOutcome>();
        try
        {
            var pairs = sources.SelectMany(s => targets.Select(t => (Source: s, Target: t))).ToList();
            await Parallel.ForEachAsync(
                pairs,
                new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrentProbes, CancellationToken = cancellationToken },
                async (pair, ct) =>
                {
                    var variable = await ProbeAsync(database, pair.Source, pair.Target, AssignContext.Variable, ct);
                    var columnOn = await ProbeAsync(database, pair.Source, pair.Target, AssignContext.ColumnAnsiWarningsOn, ct);
                    var columnOff = await ProbeAsync(database, pair.Source, pair.Target, AssignContext.ColumnAnsiWarningsOff, ct);
                    cells.Add(new AssignCellOutcome(pair.Source.Syntax, pair.Target.Syntax, variable, columnOn, columnOff));
                });

            var version = await ReadServerVersionAsync(database, cancellationToken);
            var order = sources.Select((s, i) => (s.Syntax, i)).ToDictionary(x => x.Syntax, x => x.i);
            var targetOrder = targets.Select((t, i) => (t.Syntax, i)).ToDictionary(x => x.Syntax, x => x.i);
            var ordered = cells
                .OrderBy(c => order[c.Source])
                .ThenBy(c => targetOrder[c.Target])
                .ToList();
            return (ordered, version);
        }
        finally
        {
            await _provisioner.DropIfExistsAsync(database, cancellationToken);
            SqlConnection.ClearAllPools();
        }
    }

    internal static string BuildBatch(AssignProbeType source, AssignProbeType target, AssignContext context)
    {
        var sql = new StringBuilder();
        sql.AppendLine("SET NOCOUNT ON;");
        sql.AppendLine("DECLARE @o TABLE (i INT, o VARCHAR(40));");
        if (context != AssignContext.Variable)
        {
            sql.AppendLine(CultureInfo.InvariantCulture, $"CREATE TABLE #c (v {target.Syntax});");
        }

        for (var i = 0; i < source.Values.Count; i++)
        {
            AppendValueProbe(sql, source, target, context, i);
        }

        sql.AppendLine("SELECT i, o FROM @o ORDER BY i;");
        return sql.ToString();
    }

    private static void AppendValueProbe(StringBuilder sql, AssignProbeType source, AssignProbeType target, AssignContext context, int i)
    {
        var s = $"@s{i}";
        var t = $"@t{i}";
        var e = $"@e{i}";
        sql.AppendLine(CultureInfo.InvariantCulture, $"DECLARE {s} {source.Syntax} = ({source.Values[i]}); DECLARE {t} {target.Syntax}; DECLARE {e} VARCHAR(40) = NULL;");
        if (context == AssignContext.ColumnAnsiWarningsOff)
        {
            sql.AppendLine("SET ANSI_WARNINGS OFF;");
        }

        sql.AppendLine("BEGIN TRY");
        if (context == AssignContext.Variable)
        {
            sql.AppendLine(CultureInfo.InvariantCulture, $"SET {t} = {s};");
        }
        else
        {
            sql.AppendLine(CultureInfo.InvariantCulture, $"TRUNCATE TABLE #c; INSERT #c (v) VALUES ({s}); SELECT {t} = v FROM #c;");
        }

        sql.AppendLine("END TRY");
        sql.AppendLine(CultureInfo.InvariantCulture, $"BEGIN CATCH SET {e} = 'Error:' + CAST(ERROR_NUMBER() AS VARCHAR(10)); END CATCH");
        if (context == AssignContext.ColumnAnsiWarningsOff)
        {
            sql.AppendLine("SET ANSI_WARNINGS ON;");
        }

        sql.AppendLine(CultureInfo.InvariantCulture, $"IF {e} IS NULL");
        sql.AppendLine("BEGIN");
        sql.AppendLine("BEGIN TRY");
        sql.AppendLine(CultureInfo.InvariantCulture, $"INSERT @o VALUES ({i}, CASE WHEN CONVERT(VARBINARY(MAX), {s}) = CONVERT(VARBINARY(MAX), CAST({t} AS {source.Syntax})) THEN '{AssignCellOutcome.EqualOutcome}' ELSE '{AssignCellOutcome.LossOutcome}' END);");
        sql.AppendLine("END TRY");
        sql.AppendLine(CultureInfo.InvariantCulture, $"BEGIN CATCH INSERT @o VALUES ({i}, 'Unknown'); END CATCH");
        sql.AppendLine("END");
        sql.AppendLine(CultureInfo.InvariantCulture, $"ELSE INSERT @o VALUES ({i}, {e});");
    }

    private Task<IReadOnlyList<string>> ProbeAsync(
        string database, AssignProbeType source, AssignProbeType target, AssignContext context, CancellationToken cancellationToken) =>
        MatrixProbeRunner.RunOutcomesAsync(_options, database, BuildBatch(source, target, context), source.Values.Count, AssignCellOutcome.ErrorPrefix, cancellationToken);

    private async Task<string> ReadServerVersionAsync(string database, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_options.BuildConnectionString(database));
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("SELECT CAST(SERVERPROPERTY('ProductVersion') AS VARCHAR(50));", connection);
        return (string)(await command.ExecuteScalarAsync(cancellationToken))!;
    }
}
