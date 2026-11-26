using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient;
using SilentScan.Core.Rules;
using SilentScan.Verify.Deployment;

namespace SilentScan.Verify.Oracle;

public sealed class CastMatrixGenerator
{
    private const int MaxConcurrentProbes = 4;

    public static readonly IReadOnlyList<string> UnsizedTargets =
    [
        "CHAR", "VARCHAR", "NCHAR", "NVARCHAR", "BINARY", "VARBINARY", "DECIMAL", "NUMERIC", "FLOAT", "TIME", "DATETIME2", "DATETIMEOFFSET",
        "DECIMAL(40,2)", "DECIMAL(10,20)", "FLOAT(10)", "FLOAT(30)", "VARCHAR(9000)", "NVARCHAR(5000)", "VARCHAR(0)",
    ];

    private readonly DatabaseProvisioner _provisioner;
    private readonly SqlServerOptions _options;

    public CastMatrixGenerator(SqlServerOptions options)
    {
        _options = options;
        _provisioner = new DatabaseProvisioner(options);
    }

    public async Task<(IReadOnlyList<CastCellOutcome> Cells, IReadOnlyList<CastResultType> ResultTypes, string ServerVersion)> GenerateAsync(
        IReadOnlyList<AssignProbeType> sources,
        IReadOnlyList<AssignProbeType> targets,
        CancellationToken cancellationToken = default)
    {
        var database = "SilentScanCastMatrix_" + Guid.NewGuid().ToString("N")[..8];
        SqlConnection.ClearAllPools();
        await _provisioner.CreateFreshAsync(database, cancellationToken: cancellationToken);
        var cells = new ConcurrentBag<CastCellOutcome>();
        try
        {
            var pairs = sources.SelectMany(s => targets.Select(t => (Source: s, Target: t))).ToList();
            await Parallel.ForEachAsync(
                pairs,
                new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrentProbes, CancellationToken = cancellationToken },
                async (pair, ct) =>
                {
                    var cast = await ProbeAsync(database, pair.Source, pair.Target, CastContext.Cast, ct);
                    var tryCast = await ProbeAsync(database, pair.Source, pair.Target, CastContext.TryCast, ct);
                    cells.Add(new CastCellOutcome(pair.Source.Syntax, pair.Target.Syntax, cast, tryCast));
                });

            var order = sources.Select((s, i) => (s.Syntax, i)).ToDictionary(x => x.Syntax, x => x.i);
            var targetOrder = targets.Select((t, i) => (t.Syntax, i)).ToDictionary(x => x.Syntax, x => x.i);
            var ordered = cells.OrderBy(c => order[c.Source]).ThenBy(c => targetOrder[c.Target]).ToList();
            var resultTypes = new List<CastResultType>();
            foreach (var target in UnsizedTargets)
            {
                resultTypes.Add(new CastResultType(
                    target,
                    await DescribeAsync(database, $"SELECT CAST({OperandFor(target)} AS {target}) AS r", cancellationToken),
                    await DescribeAsync(database, $"DECLARE @v {OperandType(target)} = {OperandFor(target)}; SELECT CAST(@v AS {target}) AS r", cancellationToken)));
            }

            return (ordered, resultTypes, await ReadServerVersionAsync(database, cancellationToken));
        }
        finally
        {
            await _provisioner.DropIfExistsAsync(database, cancellationToken);
            SqlConnection.ClearAllPools();
        }
    }

    private static bool IsTemporal(string target) =>
        target.StartsWith("TIME", StringComparison.Ordinal) || target.StartsWith("DATETIME", StringComparison.Ordinal);

    private static string OperandFor(string target) => IsTemporal(target) ? "'2024-01-02 03:04:05'" : "1";

    private static string OperandType(string target) => IsTemporal(target) ? "VARCHAR(30)" : "INT";

    internal static string BuildBatch(AssignProbeType source, AssignProbeType target, CastContext context)
    {
        var function = context == CastContext.Cast ? "CAST" : "TRY_CAST";
        var sql = new StringBuilder();
        sql.AppendLine("SET NOCOUNT ON;");
        sql.AppendLine("DECLARE @o TABLE (i INT, o VARCHAR(40));");
        for (var i = 0; i < source.Values.Count; i++)
        {
            var s = $"@s{i}";
            var t = $"@t{i}";
            var e = $"@e{i}";
            sql.AppendLine(CultureInfo.InvariantCulture, $"DECLARE {s} {source.Syntax} = ({source.Values[i]}); DECLARE {t} {target.Syntax}; DECLARE {e} VARCHAR(40) = NULL;");
            sql.AppendLine("BEGIN TRY");
            sql.AppendLine(CultureInfo.InvariantCulture, $"SET {t} = {function}({s} AS {target.Syntax});");
            sql.AppendLine("END TRY");
            sql.AppendLine(CultureInfo.InvariantCulture, $"BEGIN CATCH SET {e} = '{CastCellOutcome.ErrorPrefix}' + CAST(ERROR_NUMBER() AS VARCHAR(10)); END CATCH");
            sql.AppendLine(CultureInfo.InvariantCulture, $"IF {e} IS NULL AND {t} IS NULL INSERT @o VALUES ({i}, '{CastCellOutcome.NullOutcome}');");
            sql.AppendLine("ELSE IF " + e + " IS NULL");
            sql.AppendLine("BEGIN");
            sql.AppendLine("BEGIN TRY");
            sql.AppendLine(CultureInfo.InvariantCulture, $"INSERT @o VALUES ({i}, CASE WHEN CONVERT(VARBINARY(MAX), {s}) = CONVERT(VARBINARY(MAX), CAST({t} AS {source.Syntax})) THEN '{CastCellOutcome.EqualOutcome}' ELSE '{CastCellOutcome.LossOutcome}' END);");
            sql.AppendLine("END TRY");
            sql.AppendLine(CultureInfo.InvariantCulture, $"BEGIN CATCH INSERT @o VALUES ({i}, 'Unknown'); END CATCH");
            sql.AppendLine("END");
            sql.AppendLine(CultureInfo.InvariantCulture, $"ELSE INSERT @o VALUES ({i}, {e});");
        }

        sql.AppendLine("SELECT i, o FROM @o ORDER BY i;");
        return sql.ToString();
    }

    private Task<IReadOnlyList<string>> ProbeAsync(
        string database, AssignProbeType source, AssignProbeType target, CastContext context, CancellationToken cancellationToken) =>
        MatrixProbeRunner.RunOutcomesAsync(_options, database, BuildBatch(source, target, context), source.Values.Count, CastCellOutcome.ErrorPrefix, cancellationToken);

    private async Task<string> DescribeAsync(string database, string batch, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_options.BuildConnectionString(database));
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("SELECT ISNULL(system_type_name, 'Error:' + CAST(error_number AS VARCHAR(10))) FROM sys.dm_exec_describe_first_result_set(@t, NULL, 0);", connection);
        command.Parameters.AddWithValue("@t", batch);
        try
        {
            return await command.ExecuteScalarAsync(cancellationToken) is string text ? text : "Error";
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
