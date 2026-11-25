using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Data.SqlClient;
using SilentScan.Core.Rules;
using SilentScan.Verify.Deployment;

namespace SilentScan.Verify.Oracle;

public sealed class BuiltinMatrixGenerator
{
    private const int MaxConcurrentProbes = 4;

    private readonly DatabaseProvisioner _provisioner;
    private readonly SqlServerOptions _options;

    public BuiltinMatrixGenerator(SqlServerOptions options)
    {
        _options = options;
        _provisioner = new DatabaseProvisioner(options);
    }

    public async Task<(IReadOnlyList<BuiltinCell> Cells, string ServerVersion)> GenerateAsync(
        IReadOnlyList<BuiltinProbe> probes, CancellationToken cancellationToken = default)
    {
        var database = "SilentScanBuiltinMatrix_" + Guid.NewGuid().ToString("N")[..8];
        SqlConnection.ClearAllPools();
        await _provisioner.CreateFreshAsync(database, cancellationToken: cancellationToken);
        var cells = new ConcurrentBag<(int Order, BuiltinCell Cell)>();
        try
        {
            var work = probes.SelectMany((p, i) => Shapes(p).Select(shape => (Index: i, Probe: p, Shape: shape))).ToList();
            var sequence = 0;
            await Parallel.ForEachAsync(
                work,
                new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrentProbes, CancellationToken = cancellationToken },
                async (item, ct) =>
                {
                    var cell = await ProbeAsync(database, item.Probe, item.Shape, Interlocked.Increment(ref sequence), ct);
                    cells.Add((item.Index * 1000 + item.Shape.Order, cell));
                });

            var ordered = cells.OrderBy(c => c.Order).Select(c => c.Cell).ToList();
            return (ordered, await ReadServerVersionAsync(database, cancellationToken));
        }
        finally
        {
            await _provisioner.DropIfExistsAsync(database, cancellationToken);
            SqlConnection.ClearAllPools();
        }
    }

    internal static IReadOnlyList<(int Order, IReadOnlyList<string> Types)> Shapes(BuiltinProbe probe)
    {
        if (probe.Positions.Count == 0)
        {
            return [(0, [])];
        }

        var combos = new List<IReadOnlyList<string>> { new List<string>() };
        foreach (var position in probe.Positions)
        {
            combos = [.. combos.SelectMany(prefix => position.Select(type => (IReadOnlyList<string>)[.. prefix, type]))];
        }

        return [.. combos.Select((c, i) => (i, c))];
    }

    internal static string ShapeText(IReadOnlyList<string> types) => types.Count == 0 ? "()" : string.Join(";", types);

    internal static string Expression(BuiltinProbe probe, IReadOnlyList<string> types)
    {
        var text = probe.Template;
        for (var i = 0; i < probe.Positions.Count; i++)
        {
            text = text.Replace("{" + i.ToString(CultureInfo.InvariantCulture) + "}", "a" + i.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }

        return text;
    }

    internal static string ColumnList(IReadOnlyList<string> types) =>
        types.Count == 0 ? "dummy INT NULL" : string.Join(", ", types.Select((t, i) => $"a{i.ToString(CultureInfo.InvariantCulture)} {t} NULL"));

    private async Task<BuiltinCell> ProbeAsync(
        string database, BuiltinProbe probe, (int Order, IReadOnlyList<string> Types) shape, int sequence, CancellationToken cancellationToken)
    {
        var table = "p" + sequence.ToString(CultureInfo.InvariantCulture);
        var expression = Expression(probe, shape.Types);
        await using var connection = new SqlConnection(_options.BuildConnectionString(database));
        await connection.OpenAsync(cancellationToken);
        var shapeText = ShapeText(shape.Types);
        try
        {
            await ExecuteAsync(connection, $"CREATE TABLE {table} ({ColumnList(shape.Types)});", cancellationToken);
            var (type, collation, nullable) = await DescribeAsync(connection, $"SELECT {expression} AS r FROM {table}", cancellationToken);
            var (deterministic, precise) = await ComputedPropertiesAsync(connection, table, expression, cancellationToken);
            return new BuiltinCell(probe.Function, shapeText, type, collation, nullable, deterministic, precise);
        }
        finally
        {
            await ExecuteAsync(connection, $"DROP TABLE IF EXISTS {table};", cancellationToken);
        }
    }

    private static async Task<(string Type, string? Collation, bool? Nullable)> DescribeAsync(SqlConnection connection, string batch, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            "SELECT TOP 1 system_type_name, collation_name, is_nullable, error_number FROM sys.dm_exec_describe_first_result_set(@t, NULL, 0) ORDER BY column_ordinal;", connection)
        { CommandTimeout = 60 };
        command.Parameters.AddWithValue("@t", batch);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return (BuiltinMatrix.ErrorPrefix + "0", null, null);
        }

        if (reader.IsDBNull(0))
        {
            return (BuiltinMatrix.ErrorPrefix + (reader.IsDBNull(3) ? "0" : reader.GetInt32(3).ToString(CultureInfo.InvariantCulture)), null, null);
        }

        return (reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetBoolean(2));
    }

    private static async Task<(string Deterministic, string Precise)> ComputedPropertiesAsync(
        SqlConnection connection, string table, string expression, CancellationToken cancellationToken)
    {
        try
        {
            await ExecuteAsync(connection, $"ALTER TABLE {table} ADD cc AS ({expression});", cancellationToken);
        }
        catch (SqlException ex)
        {
            var error = BuiltinMatrix.ErrorPrefix + ex.Number.ToString(CultureInfo.InvariantCulture);
            return (error, error);
        }

        await using var command = new SqlCommand(
            $"SELECT CAST(COLUMNPROPERTY(OBJECT_ID('{table}'), 'cc', 'IsDeterministic') AS VARCHAR(5)), CAST(COLUMNPROPERTY(OBJECT_ID('{table}'), 'cc', 'IsPrecise') AS VARCHAR(5));", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return (reader.IsDBNull(0) ? "NULL" : reader.GetString(0), reader.IsDBNull(1) ? "NULL" : reader.GetString(1));
    }

    private static async Task ExecuteAsync(SqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 60 };
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<string> ReadServerVersionAsync(string database, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_options.BuildConnectionString(database));
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("SELECT CAST(SERVERPROPERTY('ProductVersion') AS VARCHAR(50));", connection);
        return (string)(await command.ExecuteScalarAsync(cancellationToken))!;
    }
}
