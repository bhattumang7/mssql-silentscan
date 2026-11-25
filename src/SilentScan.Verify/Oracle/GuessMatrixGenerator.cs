using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using SilentScan.Core.Rules;
using SilentScan.Verify.Deployment;

namespace SilentScan.Verify.Oracle;

public sealed record GuessShape(string Id, string Template, bool StringOnly);

public sealed partial class GuessMatrixGenerator
{
    public const long TableRows = 1_000_000;

    public const string LiteralOperand = "Literal";

    public const string VariableOperand = "Variable";

    public static readonly IReadOnlyList<(string Type, string Literal)> ColumnTypes =
    [
        ("INT", "5"), ("BIGINT", "5"), ("DECIMAL(10,2)", "5.5"), ("FLOAT", "5.5"), ("BIT", "1"), ("VARCHAR(20)", "'abc'"), ("NVARCHAR(20)", "N'abc'"),
        ("DATE", "'2024-01-02'"), ("DATETIME2(3)", "'2024-01-02 03:04:05'"), ("UNIQUEIDENTIFIER", "'A0EEBC99-9C0B-4EF8-BB6D-6BB9BD380A11'"),
    ];

    public static readonly IReadOnlyList<GuessShape> Shapes =
    [
        new("eq", "c = {v}", false),
        new("ne", "c <> {v}", false),
        new("lt", "c < {v}", false),
        new("le", "c <= {v}", false),
        new("gt", "c > {v}", false),
        new("ge", "c >= {v}", false),
        new("between", "c BETWEEN {v} AND {v}", false),
        new("in-2", "c IN ({v}, {v})", false),
        new("in-5", "c IN ({v}, {v}, {v}, {v}, {v})", false),
        new("is-null", "c IS NULL", false),
        new("is-not-null", "c IS NOT NULL", false),
        new("and-2", "c = {v} AND d = {v}", false),
        new("and-3", "c = {v} AND d = {v} AND e = {v}", false),
        new("and-4", "c = {v} AND d = {v} AND e = {v} AND f = {v}", false),
        new("or-2", "c = {v} OR d = {v}", false),
        new("or-3", "c = {v} OR d = {v} OR e = {v}", false),
        new("range-and", "c > {v} AND c < {v}", false),
        new("like-prefix", "c LIKE 'ab%'", true),
        new("like-suffix", "c LIKE '%ab'", true),
        new("like-contains", "c LIKE '%ab%'", true),
        new("like-variable", "c LIKE {v}", true),
        new("not-eq", "NOT (c = {v})", false),
        new("function-eq", "ISNULL(c, {v}) = {v}", false),
    ];

    public static readonly IReadOnlyList<(string Id, string Hint)> Estimators =
    [
        ("Default", "FORCE_DEFAULT_CARDINALITY_ESTIMATION"),
        ("Legacy", "FORCE_LEGACY_CARDINALITY_ESTIMATION"),
    ];

    private const int MaxConcurrentProbes = 4;

    private readonly DatabaseProvisioner _provisioner;
    private readonly SqlServerOptions _options;

    public GuessMatrixGenerator(SqlServerOptions options)
    {
        _options = options;
        _provisioner = new DatabaseProvisioner(options);
    }

    public async Task<(IReadOnlyList<GuessCell> Cells, string ServerVersion)> GenerateAsync(CancellationToken cancellationToken = default)
    {
        var database = "SilentScanGuessMatrix_" + Guid.NewGuid().ToString("N")[..8];
        SqlConnection.ClearAllPools();
        await _provisioner.CreateFreshAsync(database, cancellationToken: cancellationToken);
        var cells = new ConcurrentBag<(int Order, GuessCell Cell)>();
        try
        {
            await ExecuteAsync(database, $"ALTER DATABASE [{database}] SET AUTO_CREATE_STATISTICS OFF; ALTER DATABASE [{database}] SET AUTO_UPDATE_STATISTICS OFF;", cancellationToken);
            for (var i = 0; i < ColumnTypes.Count; i++)
            {
                var t = ColumnTypes[i].Type;
                await ExecuteAsync(
                    database,
                    $"CREATE TABLE g{i} (c {t} NULL, d {t} NULL, e {t} NULL, f {t} NULL, filler CHAR(200) NULL); UPDATE STATISTICS g{i} WITH ROWCOUNT = {TableRows}, PAGECOUNT = 100000;",
                    cancellationToken);
            }

            var work = new List<(int Order, int TypeIndex, GuessShape Shape, string Operand, string Estimator, string Hint)>();
            var order = 0;
            for (var si = 0; si < Shapes.Count; si++)
            {
                for (var ti = 0; ti < ColumnTypes.Count; ti++)
                {
                    if (Shapes[si].StringOnly && !ColumnTypes[ti].Type.Contains("CHAR", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    foreach (var operand in new[] { LiteralOperand, VariableOperand })
                    {
                        foreach (var (id, hint) in Estimators)
                        {
                            work.Add((order++, ti, Shapes[si], operand, id, hint));
                        }
                    }
                }
            }

            await Parallel.ForEachAsync(
                work,
                new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrentProbes, CancellationToken = cancellationToken },
                async (w, ct) =>
                {
                    var type = ColumnTypes[w.TypeIndex];
                    var ratio = await ProbeAsync(database, $"g{w.TypeIndex}", type, w.Shape, w.Operand, w.Hint, ct);
                    cells.Add((w.Order, new GuessCell(w.Shape.Id, type.Type, w.Operand, w.Estimator, ratio)));
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

    internal static string Query(string table, (string Type, string Literal) type, GuessShape shape, string operand, string hint)
    {
        var value = operand == LiteralOperand ? type.Literal : "@p";
        var declaration = operand == LiteralOperand ? string.Empty : $"DECLARE @p {(shape.Id == "like-variable" ? "VARCHAR(20)" : type.Type)}; ";
        var predicate = shape.Template.Replace("{v}", value, StringComparison.Ordinal);
        return $"{declaration}SELECT filler FROM {table} WHERE {predicate} OPTION (USE HINT('{hint}'))";
    }

    internal static string? ReadRatio(string planXml)
    {
        var scan = ScanRelOpRegex().Match(planXml);
        if (!scan.Success)
        {
            return null;
        }

        var estimate = EstimateRowsRegex().Match(scan.Value);
        if (!estimate.Success)
        {
            return null;
        }

        var rows = double.Parse(estimate.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture);
        return (rows / TableRows).ToString("G6", CultureInfo.InvariantCulture);
    }

    private async Task<string> ProbeAsync(
        string database, string table, (string Type, string Literal) type, GuessShape shape, string operand, string hint, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_options.BuildConnectionString(database));
        await connection.OpenAsync(cancellationToken);
        try
        {
            await using (var on = new SqlCommand("SET SHOWPLAN_XML ON;", connection))
            {
                await on.ExecuteNonQueryAsync(cancellationToken);
            }

            await using var query = new SqlCommand(Query(table, type, shape, operand, hint), connection) { CommandTimeout = 60 };
            var plan = (string?)await query.ExecuteScalarAsync(cancellationToken) ?? string.Empty;
            return ReadRatio(plan) ?? GuessMatrix.NoTableScan;
        }
        catch (SqlException ex)
        {
            return GuessMatrix.ErrorPrefix + ex.Number.ToString(CultureInfo.InvariantCulture);
        }
    }

    private async Task ExecuteAsync(string database, string sql, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_options.BuildConnectionString(database));
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 120 };
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<string> ReadServerVersionAsync(string database, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_options.BuildConnectionString(database));
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("SELECT CAST(SERVERPROPERTY('ProductVersion') AS VARCHAR(50));", connection);
        return (string)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    [GeneratedRegex("<RelOp [^>]*PhysicalOp=\"Table Scan\"[^>]*>")]
    private static partial Regex ScanRelOpRegex();

    [GeneratedRegex("EstimateRows=\"([^\"]+)\"")]
    private static partial Regex EstimateRowsRegex();
}
