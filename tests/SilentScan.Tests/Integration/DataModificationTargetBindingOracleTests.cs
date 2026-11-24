using SilentScan.Tests.Support;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class DataModificationTargetBindingOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(DataModificationTargetBindingOracleTests);

    protected override string Ddl => """
        CREATE TABLE dbo.Target (Id INT NOT NULL PRIMARY KEY, Note VARCHAR(10) NULL);
        GO
        CREATE TABLE dbo.Feed (Id INT NOT NULL);
        """;

    private async Task<string> RunAndReadNotesAsync(string statement)
    {
        await using var connection = await OpenConnectionAsync();
        await ExecuteAsync(connection, """
            DELETE dbo.Target; DELETE dbo.Feed;
            INSERT dbo.Target (Id) VALUES (1), (2), (3);
            INSERT dbo.Feed (Id) VALUES (2);
            """);
        await ExecuteAsync(connection, statement);
        await using var command = new Microsoft.Data.SqlClient.SqlCommand(
            "SELECT STRING_AGG(CONCAT(Id, ':', ISNULL(Note, '-')), ',') WITHIN GROUP (ORDER BY Id) FROM dbo.Target;", connection);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task TargetAbsentFromFrom_ItsQualifiedColumnsBindToAnAddedInstanceJoinedToTheFromTables()
    {
        var notes = await RunAndReadNotesAsync(
            "UPDATE dbo.Target SET dbo.Target.Note = 'x' FROM dbo.Feed AS f WHERE dbo.Target.Id = f.Id;");

        Assert.Equal("1:-,2:x,3:-", notes);
    }

    [Fact]
    public async Task TargetNamingTheSameObjectAsAnAliasedFromEntry_BindsToThatEntryNotAnExtraCrossJoinedInstance()
    {
        var notes = await RunAndReadNotesAsync(
            "UPDATE dbo.Target SET Note = 'y' FROM dbo.Target AS t WHERE t.Id = 3;");

        Assert.Equal("1:-,2:-,3:y", notes);
    }
}
