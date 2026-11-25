using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using SilentScan.Core.Rules;
using SilentScan.Verify.Deployment;

namespace SilentScan.Verify.Oracle;

public sealed partial class SargShapeMatrixGenerator
{
    private const int MaxConcurrentProbes = 4;

    private readonly DatabaseProvisioner _provisioner;
    private readonly SqlServerOptions _options;

    public SargShapeMatrixGenerator(SqlServerOptions options)
    {
        _options = options;
        _provisioner = new DatabaseProvisioner(options);
    }

    public async Task<(IReadOnlyList<SargShapeCell> Cells, string ServerVersion)> GenerateAsync(
        IReadOnlyList<string> columnTypes, IReadOnlyList<SargShape> shapes, CancellationToken cancellationToken = default)
    {
        var database = "SilentScanSargMatrix_" + Guid.NewGuid().ToString("N")[..8];
        SqlConnection.ClearAllPools();
        await _provisioner.CreateFreshAsync(database, cancellationToken: cancellationToken);
        var cells = new ConcurrentBag<(int Order, SargShapeCell Cell)>();
        try
        {
            for (var i = 0; i < columnTypes.Count; i++)
            {
                await PrepareTableAsync(database, TableName(i), columnTypes[i], cancellationToken);
            }

            var work = columnTypes
                .SelectMany((type, ti) => shapes.Select((shape, si) => (TypeIndex: ti, Type: type, ShapeIndex: si, Shape: shape)))
                .Where(w => SargShapes.Applies(w.Shape, w.Type))
                .ToList();
            await Parallel.ForEachAsync(
                work,
                new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrentProbes, CancellationToken = cancellationToken },
                async (item, ct) =>
                {
                    var outcome = await ProbeAsync(database, TableName(item.TypeIndex), item.Type, item.Shape, ct);
                    cells.Add((item.ShapeIndex * 1000 + item.TypeIndex, new SargShapeCell(item.Shape.Id, item.Type, outcome)));
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

    internal static string Query(string table, string columnType, SargShape shape)
    {
        var predicate = shape.Template.Replace("<T>", SargShapes.VariableType(columnType), StringComparison.Ordinal);
        return $"DECLARE @p {SargShapes.VariableType(columnType)}; SELECT c FROM {table} WHERE {predicate}";
    }

    internal static string Classify(string planXml)
    {
        var seek = SeekPredicatesRegex().IsMatch(planXml);
        var scan = ScanOperatorRegex().IsMatch(planXml);
        var outcome = seek ? SargShapeMatrix.SeekOutcome : scan ? SargShapeMatrix.ScanOutcome : "Other";
        if (planXml.Contains("PlanAffectingConvert", StringComparison.Ordinal))
        {
            outcome += "+Convert";
        }

        return outcome;
    }

    private static string TableName(int index) => "sg" + index.ToString(CultureInfo.InvariantCulture);

    private async Task PrepareTableAsync(string database, string table, string columnType, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_options.BuildConnectionString(database));
        await connection.OpenAsync(cancellationToken);
        var sql = $"CREATE TABLE {table} (c {SargShapes.ColumnDeclaration(columnType)}, pad CHAR(10) NULL, filler CHAR(200) NULL); "
            + $"CREATE INDEX ix_{table} ON {table} (c); "
            + $"UPDATE STATISTICS {table} WITH ROWCOUNT = 1000000, PAGECOUNT = 100000;";
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 60 };
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<string> ProbeAsync(string database, string table, string columnType, SargShape shape, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_options.BuildConnectionString(database));
        await connection.OpenAsync(cancellationToken);
        try
        {
            await using (var on = new SqlCommand("SET SHOWPLAN_XML ON;", connection))
            {
                await on.ExecuteNonQueryAsync(cancellationToken);
            }

            string plan;
            await using (var query = new SqlCommand(Query(table, columnType, shape), connection) { CommandTimeout = 60 })
            {
                plan = (string?)await query.ExecuteScalarAsync(cancellationToken) ?? string.Empty;
            }

            return Classify(plan);
        }
        catch (SqlException ex)
        {
            return SargShapeMatrix.ErrorPrefix + ex.Number.ToString(CultureInfo.InvariantCulture);
        }
    }

    private async Task<string> ReadServerVersionAsync(string database, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_options.BuildConnectionString(database));
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("SELECT CAST(SERVERPROPERTY('ProductVersion') AS VARCHAR(50));", connection);
        return (string)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    [GeneratedRegex("<SeekPredicates>")]
    private static partial Regex SeekPredicatesRegex();

    [GeneratedRegex("PhysicalOp=\"(Clustered )?(Index|Table) Scan\"")]
    private static partial Regex ScanOperatorRegex();
}
