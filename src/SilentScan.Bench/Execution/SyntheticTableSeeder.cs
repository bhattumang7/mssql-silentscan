using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using SilentScan.Bench.Scenarios;

namespace SilentScan.Bench.Execution;

/// <summary>
/// Creates and seeds one synthetic table per <see cref="TypePairScenario"/> (CLAUDE.md
/// Benchmark protocol: "One synthetic table per type-pair under test"). Row generation runs
/// entirely server-side via a tally CTE over sys.all_columns (a cross join large enough to
/// cover the 10M-row ceiling) rather than sending one INSERT per row from the client, which
/// would dominate wall-clock time at scale and isn't what's being measured.
/// </summary>
public static partial class SyntheticTableSeeder
{
    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]{0,127}$")]
    private static partial Regex ValidIdentifier();

    public static async Task SeedAsync(SqlConnection connection, TypePairScenario scenario, string tableName, int rowCount, CancellationToken cancellationToken = default)
    {
        // Table names can't be parameterized in DDL; validate rather than trust the caller.
        if (!ValidIdentifier().IsMatch(tableName))
        {
            throw new ArgumentException($"'{tableName}' is not a safe SQL identifier for a synthetic table name.", nameof(tableName));
        }

        // Table/type text below is safe to interpolate: the name was just validated above,
        // and the type/seed-expression strings only ever come from TypePairScenario's fixed,
        // hardcoded scenario list - nothing here is derived from corpus or external input.
        await using (var createCommand = connection.CreateCommand())
        {
            createCommand.CommandText = $"""
                DROP TABLE IF EXISTS dbo.{tableName};
                CREATE TABLE dbo.{tableName}
                (
                    Id   INT              NOT NULL PRIMARY KEY,
                    Code {scenario.ColumnTypeDdl} NOT NULL
                );
                """;
            createCommand.CommandTimeout = 60;
            await createCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var seedCommand = connection.CreateCommand())
        {
            seedCommand.CommandText = $"""
                ;WITH Tally AS
                (
                    SELECT TOP ({rowCount.ToString(CultureInfo.InvariantCulture)}) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n
                    FROM sys.all_columns a CROSS JOIN sys.all_columns b
                )
                INSERT INTO dbo.{tableName} (Id, Code)
                SELECT n, {scenario.SeedValueExpression} FROM Tally;
                """;
            seedCommand.CommandTimeout = 300;
            await seedCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var indexCommand = connection.CreateCommand();
        indexCommand.CommandText = $"CREATE INDEX IX_{tableName}_Code ON dbo.{tableName}(Code);";
        indexCommand.CommandTimeout = 300;
        await indexCommand.ExecuteNonQueryAsync(cancellationToken);
    }
}
