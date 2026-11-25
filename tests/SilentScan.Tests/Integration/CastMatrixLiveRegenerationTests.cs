using SilentScan.Core.Rules;
using SilentScan.Verify;
using SilentScan.Verify.Oracle;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class CastMatrixLiveRegenerationTests
{
    [Fact]
    public Task LiveMatrix_MatchesCheckedInMatrix() => AssertMatchesAsync(SqlServerOptions.LocalDocker);

    [Fact]
    public Task LatestEngineMatrix_MatchesCheckedInMatrix() => AssertMatchesAsync(SqlServerOptions.LocalDockerLatest);

    private static async Task AssertMatchesAsync(SqlServerOptions options)
    {
        var generator = new CastMatrixGenerator(options);
        var (live, resultTypes, _) = await generator.GenerateAsync(AssignMatrixGenerator.Types, AssignMatrixGenerator.Types);
        var checkedIn = CastMatrix.Instance;
        var mismatches = new List<string>();

        foreach (var cell in live)
        {
            var pinned = checkedIn.TryGet(cell.Source, cell.Target);
            if (pinned is null)
            {
                mismatches.Add($"{cell.Source} -> {cell.Target}: probed live but MISSING from the checked-in matrix.");
                continue;
            }

            foreach (var context in Enum.GetValues<CastContext>())
            {
                if (!pinned.For(context).SequenceEqual(cell.For(context)))
                {
                    mismatches.Add($"{cell.Source} -> {cell.Target} [{context}]: checked-in [{string.Join(",", pinned.For(context))}] vs live [{string.Join(",", cell.For(context))}].");
                }
            }
        }

        foreach (var result in resultTypes)
        {
            if (checkedIn.TryGetResultType(result.Target) != result)
            {
                mismatches.Add($"result type of {result.Target} differs from the checked-in value.");
            }
        }

        Assert.True(mismatches.Count == 0, "CastMatrix.json has drifted from the live oracle:\n" + string.Join("\n", mismatches.Take(40)));
        Assert.Equal(checkedIn.Entries.Count, live.Count);
        Assert.Equal(checkedIn.ResultTypes.Count, resultTypes.Count);
    }

    [Fact]
    public async Task RawEngine_AgreesWithMatrix_ForNarrowingStringAndOverflow()
    {
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(SqlServerOptions.LocalDocker.BuildConnectionString("tempdb"));
        await connection.OpenAsync();
        await using var command = new Microsoft.Data.SqlClient.SqlCommand(
            "DECLARE @s VARCHAR(10) = 'abcdefghij'; DECLARE @n INT = 300; SELECT CAST(CAST(@s AS VARCHAR(5)) AS VARCHAR(10)) + '|' + ISNULL(CAST(TRY_CAST(@n AS TINYINT) AS VARCHAR(5)), 'NULL');", connection);

        Assert.Equal("abcde|NULL", (string)(await command.ExecuteScalarAsync())!);
        Assert.True(CastMatrix.Instance.TryGet("VARCHAR(10)", "VARCHAR(5)")!.SilentlyLoses(CastContext.Cast));
    }
}
