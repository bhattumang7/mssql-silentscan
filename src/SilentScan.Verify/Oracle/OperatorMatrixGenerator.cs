using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Data.SqlClient;
using SilentScan.Core.Rules;
using SilentScan.Verify.Deployment;

namespace SilentScan.Verify.Oracle;

public sealed class OperatorMatrixGenerator
{
    private const int MaxConcurrentProbes = 4;

    public static readonly IReadOnlyList<(string Name, string Expression)> Operators =
    [
        ("+", "@l + @r"),
        ("-", "@l - @r"),
        ("*", "@l * @r"),
        ("/", "@l / @r"),
        ("%", "@l % @r"),
        ("&", "@l & @r"),
        ("|", "@l | @r"),
        ("^", "@l ^ @r"),
        ("CASE", "CASE WHEN @l IS NULL THEN @l ELSE @r END"),
        ("COALESCE", "COALESCE(@l, @r)"),
        ("ISNULL", "ISNULL(@l, @r)"),
        ("NULLIF", "NULLIF(@l, @r)"),
        ("IIF", "IIF(@l IS NULL, @l, @r)"),
        ("CHOOSE", "CHOOSE(1, @l, @r)"),
        ("UNION", "SELECT @l AS r UNION ALL SELECT @r"),
        ("COMPARE", "CASE WHEN @l = @r THEN 1 ELSE 0 END"),
        ("CONCAT", "CONCAT(@l, @r)"),
    ];

    private readonly DatabaseProvisioner _provisioner;
    private readonly SqlServerOptions _options;

    public OperatorMatrixGenerator(SqlServerOptions options)
    {
        _options = options;
        _provisioner = new DatabaseProvisioner(options);
    }

    public async Task<(IReadOnlyList<OperatorCell> Cells, string ServerVersion)> GenerateAsync(
        IReadOnlyList<AssignProbeType> types, CancellationToken cancellationToken = default)
    {
        var database = "SilentScanOperatorMatrix_" + Guid.NewGuid().ToString("N")[..8];
        SqlConnection.ClearAllPools();
        await _provisioner.CreateFreshAsync(database, cancellationToken: cancellationToken);
        var cells = new ConcurrentBag<OperatorCell>();
        try
        {
            var pairs = types.SelectMany(l => types.Select(r => (Left: l, Right: r))).ToList();
            await Parallel.ForEachAsync(
                pairs,
                new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrentProbes, CancellationToken = cancellationToken },
                async (pair, ct) => cells.Add(new OperatorCell(pair.Left.Syntax, pair.Right.Syntax, await ProbePairAsync(database, pair.Left, pair.Right, ct))));

            var order = types.Select((t, i) => (t.Syntax, i)).ToDictionary(x => x.Syntax, x => x.i);
            var ordered = cells.OrderBy(c => order[c.Left]).ThenBy(c => order[c.Right]).ToList();
            return (ordered, await ReadServerVersionAsync(database, cancellationToken));
        }
        finally
        {
            await _provisioner.DropIfExistsAsync(database, cancellationToken);
            SqlConnection.ClearAllPools();
        }
    }

    internal static string BuildBatch(AssignProbeType left, AssignProbeType right, string expression)
    {
        var body = expression.StartsWith("SELECT", StringComparison.Ordinal) ? expression : "SELECT " + expression + " AS r";
        return string.Create(CultureInfo.InvariantCulture, $"DECLARE @l {left.Syntax}; DECLARE @r {right.Syntax}; {body}");
    }

    private async Task<IReadOnlyList<string>> ProbePairAsync(string database, AssignProbeType left, AssignProbeType right, CancellationToken cancellationToken)
    {
        var results = new List<string>(Operators.Count);
        await using var connection = new SqlConnection(_options.BuildConnectionString(database));
        await connection.OpenAsync(cancellationToken);
        foreach (var op in Operators)
        {
            results.Add(await DescribeAsync(connection, BuildBatch(left, right, op.Expression), cancellationToken));
        }

        return results;
    }

    private static async Task<string> DescribeAsync(SqlConnection connection, string batch, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            "SELECT TOP 1 ISNULL(system_type_name, 'Error:' + CAST(error_number AS VARCHAR(10))) FROM sys.dm_exec_describe_first_result_set(@t, NULL, 0) ORDER BY column_ordinal;", connection)
        { CommandTimeout = 60 };
        command.Parameters.AddWithValue("@t", batch);
        try
        {
            return await command.ExecuteScalarAsync(cancellationToken) is string text ? text : OperatorMatrix.ErrorPrefix + "0";
        }
        catch (SqlException ex)
        {
            return OperatorMatrix.ErrorPrefix + ex.Number.ToString(CultureInfo.InvariantCulture);
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
