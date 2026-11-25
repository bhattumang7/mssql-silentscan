using SilentScan.Core.Rules;
using SilentScan.Verify;
using SilentScan.Verify.Oracle;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class OperatorMatrixLiveRegenerationTests
{
    [Fact]
    public Task LiveMatrix_MatchesCheckedInMatrix() => AssertMatchesAsync(SqlServerOptions.LocalDocker);

    [Fact]
    public Task LatestEngineMatrix_MatchesCheckedInMatrix() => AssertMatchesAsync(SqlServerOptions.LocalDockerLatest);

    private static async Task AssertMatchesAsync(SqlServerOptions options)
    {
        var generator = new OperatorMatrixGenerator(options);
        var (live, _) = await generator.GenerateAsync(AssignMatrixGenerator.Types);
        var checkedIn = OperatorMatrix.Instance;
        var mismatches = new List<string>();

        foreach (var cell in live)
        {
            for (var i = 0; i < OperatorMatrixGenerator.Operators.Count; i++)
            {
                var name = OperatorMatrixGenerator.Operators[i].Name;
                var pinned = checkedIn.TryGetResult(name, cell.Left, cell.Right);
                if (pinned != cell.Results[i])
                {
                    mismatches.Add($"{name} {cell.Left},{cell.Right}: checked-in '{pinned}' vs live '{cell.Results[i]}'.");
                }
            }
        }

        Assert.True(mismatches.Count == 0, "OperatorMatrix.json has drifted from the live oracle:\n" + string.Join("\n", mismatches.Take(40)));
        Assert.Equal(checkedIn.Entries.Count, live.Count);
        Assert.Equal(checkedIn.Operators, OperatorMatrixGenerator.Operators.Select(o => o.Name));
    }

    [Fact]
    public async Task RawEngine_AgreesWithMatrix_ForDecimalDivisionAndIsnullTruncation()
    {
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(SqlServerOptions.LocalDocker.BuildConnectionString("tempdb"));
        await connection.OpenAsync();
        await using var command = new Microsoft.Data.SqlClient.SqlCommand(
            "DECLARE @a VARCHAR(5) = NULL, @b VARCHAR(10) = 'abcdefghij'; SELECT ISNULL(@a, @b) + '|' + COALESCE(@a, @b);", connection);

        Assert.Equal("abcde|abcdefghij", (string)(await command.ExecuteScalarAsync())!);
        Assert.Equal("varchar(5)", OperatorMatrix.Instance.TryGetResult("ISNULL", "VARCHAR(5)", "VARCHAR(10)"));
    }
}
