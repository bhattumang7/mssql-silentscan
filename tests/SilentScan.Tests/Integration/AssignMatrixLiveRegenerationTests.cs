using SilentScan.Core.Rules;
using SilentScan.Verify;
using SilentScan.Verify.Oracle;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class AssignMatrixLiveRegenerationTests
{
    [Fact]
    public Task LiveMatrix_MatchesCheckedInMatrix_ForEveryProbedCell() => AssertMatchesAsync(SqlServerOptions.LocalDocker);

    [Fact]
    public Task LatestEngineMatrix_MatchesCheckedInMatrix_ForEveryProbedCell() => AssertMatchesAsync(SqlServerOptions.LocalDockerLatest);

    private static async Task AssertMatchesAsync(SqlServerOptions options)
    {
        var generator = new AssignMatrixGenerator(options);
        var (live, _) = await generator.GenerateAsync(AssignMatrixGenerator.Types, AssignMatrixGenerator.Types);

        var checkedIn = AssignMatrix.Instance;
        var mismatches = new List<string>();

        foreach (var cell in live)
        {
            var pinned = checkedIn.TryGet(cell.Source, cell.Target);
            if (pinned is null)
            {
                mismatches.Add($"{cell.Source} -> {cell.Target}: probed live but MISSING from the checked-in matrix.");
                continue;
            }

            foreach (var context in Enum.GetValues<AssignContext>())
            {
                if (!pinned.For(context).SequenceEqual(cell.For(context)))
                {
                    mismatches.Add($"{cell.Source} -> {cell.Target} [{context}]: checked-in [{string.Join(",", pinned.For(context))}] vs live [{string.Join(",", cell.For(context))}].");
                }
            }
        }

        Assert.True(mismatches.Count == 0, "AssignMatrix.json has drifted from the live oracle:\n" + string.Join("\n", mismatches.Take(40)));
        Assert.Equal(checkedIn.Entries.Count, live.Count);
    }

    [Fact]
    public async Task RawEngine_AgreesWithMatrix_ForTruncatingAndWideningCells()
    {
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(SqlServerOptions.LocalDocker.BuildConnectionString("tempdb"));
        await connection.OpenAsync();

        var truncated = await ScalarAsync(connection, "DECLARE @s NVARCHAR(10) = N'abcdefghij'; DECLARE @t VARCHAR(5); SET @t = @s; SELECT @t;");
        var kept = await ScalarAsync(connection, "DECLARE @s NVARCHAR(10) = N'abcdefghij'; DECLARE @t NVARCHAR(10); SET @t = @s; SELECT @t;");

        Assert.Equal("abcde", truncated);
        Assert.Equal("abcdefghij", kept);
        Assert.True(AssignMatrix.Instance.TryGet("NVARCHAR(10)", "VARCHAR(5)")!.SilentlyLoses(AssignContext.Variable));
        Assert.False(AssignMatrix.Instance.TryGet("NVARCHAR(10)", "NVARCHAR(10)")!.SilentlyLoses(AssignContext.Variable));
    }

    private static async Task<string> ScalarAsync(Microsoft.Data.SqlClient.SqlConnection connection, string sql)
    {
        await using var command = new Microsoft.Data.SqlClient.SqlCommand(sql, connection);
        return (string)(await command.ExecuteScalarAsync())!;
    }
}
