using Microsoft.Data.SqlClient;
using SilentScan.Verify;
using SilentScan.Verify.Deployment;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.CatchAllPredicateRuleId)]
public sealed partial class CatchAllOptionalParameterOptimizationOracleTests : IAsyncLifetime
{
    private static readonly SqlServerOptions Options = SqlServerOptions.LocalDockerLatest;

    private readonly string _databaseName = $"{nameof(CatchAllOptionalParameterOptimizationOracleTests)}_{Guid.NewGuid():N}";

    private const string Ddl = """
        CREATE TABLE dbo.Wide (Id INT NOT NULL, A INT NOT NULL);
        GO
        INSERT INTO dbo.Wide (Id, A)
        SELECT TOP (400000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)), ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) % 4000
        FROM sys.all_objects a CROSS JOIN sys.all_objects b CROSS JOIN sys.all_objects c;
        CREATE INDEX IX_Wide_A ON dbo.Wide(A);
        UPDATE STATISTICS dbo.Wide WITH FULLSCAN;
        GO
        CREATE PROCEDURE dbo.FindByA @p INT = NULL AS SELECT Id FROM dbo.Wide WHERE (A = @p OR @p IS NULL);
        GO
        CREATE TABLE dbo.Sink (Id INT NOT NULL);
        GO
        CREATE PROCEDURE dbo.UpdateByA @p INT = NULL AS UPDATE dbo.Wide SET Id = Id WHERE (A = @p OR @p IS NULL);
        GO
        CREATE PROCEDURE dbo.DeleteByA @p INT = NULL AS DELETE dbo.Wide WHERE (A = @p OR @p IS NULL);
        GO
        CREATE PROCEDURE dbo.InsertSelectByA @p INT = NULL AS INSERT dbo.Sink (Id) SELECT Id FROM dbo.Wide WHERE (A = @p OR @p IS NULL);
        GO
        CREATE PROCEDURE dbo.AssignByA @p INT = NULL AS DECLARE @x INT; SELECT @x = Id FROM dbo.Wide WHERE (A = @p OR @p IS NULL);
        GO
        CREATE PROCEDURE dbo.SetByA @p INT = NULL AS DECLARE @x INT; SET @x = (SELECT COUNT(*) FROM dbo.Wide WHERE (A = @p OR @p IS NULL));
        GO
        CREATE PROCEDURE dbo.DeclareByA @p INT = NULL AS DECLARE @x INT = (SELECT COUNT(*) FROM dbo.Wide WHERE (A = @p OR @p IS NULL));
        GO
        CREATE PROCEDURE dbo.IfByA @p INT = NULL AS IF EXISTS (SELECT 1 FROM dbo.Wide WHERE (A = @p OR @p IS NULL)) SELECT 1;
        GO
        CREATE PROCEDURE dbo.CursorByA @p INT = NULL AS
            DECLARE c CURSOR LOCAL FAST_FORWARD FOR SELECT Id FROM dbo.Wide WHERE (A = @p OR @p IS NULL);
            OPEN c; FETCH NEXT FROM c; CLOSE c; DEALLOCATE c;
        GO
        CREATE PROCEDURE dbo.TableVariableByA @p INT = NULL AS DECLARE @t TABLE (Id INT); INSERT @t SELECT Id FROM dbo.Wide WHERE (A = @p OR @p IS NULL);
        GO
        """;

    public async Task InitializeAsync()
    {
        await new DatabaseProvisioner(Options).CreateFreshAsync(_databaseName);
        await new ScriptDeployer(Options).DeployAsync(Ddl, _databaseName);
    }

    public async Task DisposeAsync() =>
        await new DatabaseProvisioner(Options).DropIfExistsAsync(_databaseName);

    private async Task<long> LogicalReadsAsync(int compatibilityLevel, string statement)
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(_databaseName));
        await connection.OpenAsync();

        await using (var setup = new SqlCommand(
            $"ALTER DATABASE [{_databaseName}] SET COMPATIBILITY_LEVEL = {compatibilityLevel}; DBCC FREEPROCCACHE; SET STATISTICS IO ON;", connection))
        {
            await setup.ExecuteNonQueryAsync();
        }

        long reads = 0;
        connection.InfoMessage += (_, e) =>
        {
            var match = LogicalReads().Match(e.Message);
            if (match.Success)
            {
                reads += long.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            }
        };

        await using var call = new SqlCommand(statement, connection);
        await call.ExecuteNonQueryAsync();
        return reads;
    }

    [Theory]
    [InlineData("dbo.FindByA", true)]
    [InlineData("dbo.AssignByA", false)]
    [InlineData("dbo.SetByA", false)]
    [InlineData("dbo.DeclareByA", false)]
    [InlineData("dbo.IfByA", false)]
    [InlineData("dbo.CursorByA", false)]
    [InlineData("dbo.TableVariableByA", false)]
    public async Task AtCompat170_OnlySomeStatementKindsGetAPlanPerBranch(string procedure, bool dispatched)
    {
        Assert.Equal(dispatched, await PlanDispatchesOnOptionalParameterAsync(170, $"EXEC {procedure} @p = 7;"));
    }

    [Fact]
    public async Task DisablingTheDatabaseScopedOptimization_MakesCompat170ScanLikeTheOlderLevels()
    {
        var enabled = await LogicalReadsAsync(170, "EXEC dbo.FindByA @p = 7;");

        await using (var connection = new SqlConnection(Options.BuildConnectionString(_databaseName)))
        {
            await connection.OpenAsync();
            await using var off = new SqlCommand("ALTER DATABASE SCOPED CONFIGURATION SET OPTIONAL_PARAMETER_OPTIMIZATION = OFF;", connection);
            await off.ExecuteNonQueryAsync();
        }

        var disabled = await LogicalReadsAsync(170, "EXEC dbo.FindByA @p = 7;");

        Assert.True(disabled > enabled * 5, $"optimization on read {enabled} pages, off read {disabled}");
    }

    [Fact]
    public async Task SelectiveCallOnAProcedureParameter_ScansAtCompat150_ButSeeksAtCompat170()
    {
        var before = await LogicalReadsAsync(150, "EXEC dbo.FindByA @p = 7;");
        var after = await LogicalReadsAsync(170, "EXEC dbo.FindByA @p = 7;");

        Assert.True(before > after * 5, $"compat 150 read {before} pages, compat 170 read {after}");
    }

    private async Task<bool> PlanDispatchesOnOptionalParameterAsync(int compatibilityLevel, string statement)
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(_databaseName));
        await connection.OpenAsync();

        await using (var setup = new SqlCommand(
            $"ALTER DATABASE [{_databaseName}] SET COMPATIBILITY_LEVEL = {compatibilityLevel}; DBCC FREEPROCCACHE; SET STATISTICS XML ON;", connection))
        {
            await setup.ExecuteNonQueryAsync();
        }

        await using var call = new SqlCommand(statement, connection);
        await using var reader = await call.ExecuteReaderAsync();
        var dispatched = false;
        do
        {
            while (await reader.ReadAsync())
            {
                if (reader.FieldCount > 0 && reader.GetValue(0) is string text && text.Contains("OptionalParameterPredicate", StringComparison.Ordinal))
                {
                    dispatched = true;
                }
            }
        }
        while (await reader.NextResultAsync());

        return dispatched;
    }

    [Theory]
    [InlineData("UpdateByA")]
    [InlineData("DeleteByA")]
    [InlineData("InsertSelectByA")]
    public async Task ModificationOnAProcedureParameter_IsDispatchedOnTheOptionalParameterAtCompat170Only(string procedure)
    {
        var statement = $"BEGIN TRAN; EXEC dbo.{procedure} @p = 7; ROLLBACK;";

        Assert.False(await PlanDispatchesOnOptionalParameterAsync(150, statement));
        Assert.True(await PlanDispatchesOnOptionalParameterAsync(170, statement));
    }

    [Fact]
    public async Task SelectiveCallOnALocalVariable_StillScansAtCompat170()
    {
        var variableReads = await LogicalReadsAsync(
            170, "DECLARE @p INT = 7; SELECT Id FROM dbo.Wide WHERE (A = @p OR @p IS NULL);");
        var parameterReads = await LogicalReadsAsync(170, "EXEC dbo.FindByA @p = 7;");

        Assert.True(variableReads > parameterReads * 5, $"local variable read {variableReads} pages, parameter read {parameterReads}");
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"Table 'Wide'\. Scan count \d+, logical reads (\d+)")]
    private static partial System.Text.RegularExpressions.Regex LogicalReads();
}
