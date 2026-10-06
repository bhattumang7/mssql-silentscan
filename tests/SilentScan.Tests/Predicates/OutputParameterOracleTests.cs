using Microsoft.Data.SqlClient;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", "silentscan/control-flow/unassigned-output-parameter")]
public sealed class OutputParameterOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(OutputParameterOracleTests);

    protected override string Ddl => """
        CREATE PROCEDURE dbo.p_never_assigns @x INT OUTPUT AS
        BEGIN
            SET NOCOUNT ON;
            IF (1 = 0) SET @x = 42;
        END
        GO
        CREATE PROCEDURE dbo.p_always_assigns @x INT OUTPUT AS
        BEGIN
            SET NOCOUNT ON;
            SET @x = 42;
        END
        GO
        CREATE PROCEDURE dbo.p_conditional_select_assigns @x INT OUTPUT AS
        BEGIN
            SET NOCOUNT ON;
            DECLARE @T TABLE (Id INT, Val INT);
            SELECT @x = Val FROM @T WHERE Id = 1;
        END
        GO
        CREATE PROCEDURE dbo.p_conditional_aggregate_assigns @x INT OUTPUT AS
        BEGIN
            SET NOCOUNT ON;
            DECLARE @T TABLE (Id INT, Val INT);
            SELECT @x = SUM(Val) FROM @T WHERE Id = 1;
        END
        GO
        CREATE PROCEDURE dbo.p_status_seven AS RETURN 7;
        GO
        CREATE PROCEDURE dbo.p_return_status_assigns @x INT OUTPUT AS
        BEGIN
            SET NOCOUNT ON;
            EXEC @x = dbo.p_status_seven;
        END
        GO
        CREATE PROCEDURE dbo.p_assign_in_try_swallowing_catch @x INT OUTPUT AS
        BEGIN
            SET NOCOUNT ON;
            BEGIN TRY
                SET @x = 42;
            END TRY
            BEGIN CATCH
                DECLARE @message NVARCHAR(2048) = ERROR_MESSAGE();
            END CATCH
        END
        GO
        CREATE PROCEDURE dbo.p_throw_before_assign @x INT OUTPUT, @fail BIT AS
        BEGIN
            SET NOCOUNT ON;
            BEGIN TRY
                IF (@fail = 1) THROW 50001, 'boom', 1;
                SET @x = 42;
            END TRY
            BEGIN CATCH
                DECLARE @message NVARCHAR(2048) = ERROR_MESSAGE();
            END CATCH
        END
        GO
        CREATE PROCEDURE dbo.p_throw_after_assign @x INT OUTPUT AS
        BEGIN
            SET NOCOUNT ON;
            BEGIN TRY
                SET @x = 42;
                THROW 50001, 'boom', 1;
            END TRY
            BEGIN CATCH
                DECLARE @message NVARCHAR(2048) = ERROR_MESSAGE();
            END CATCH
        END
        GO
        """;

    private new async Task<SqlConnection> OpenConnectionAsync()
    {
        var connection = new SqlConnection(Options.BuildConnectionString(DatabaseName));
        await connection.OpenAsync();
        return connection;
    }

    [Fact]
    public async Task NeverAssignedOutputParameter_LeavesCallerVariableAtItsPriorRealValue_NotNull()
    {
        await using var connection = await OpenConnectionAsync();

        await using var command = new SqlCommand(
            "DECLARE @caller INT = 999; EXEC dbo.p_never_assigns @x = @caller OUTPUT; SELECT @caller;",
            connection);
        var result = await command.ExecuteScalarAsync();

        Assert.Equal(999, (int)result!);
    }

    [Fact]
    public async Task NeverAssignedOutputParameter_LeavesCallerVariableNull_WhenItStartedNull()
    {
        await using var connection = await OpenConnectionAsync();

        await using var command = new SqlCommand(
            "DECLARE @caller INT = NULL; EXEC dbo.p_never_assigns @x = @caller OUTPUT; SELECT @caller;",
            connection);
        var result = await command.ExecuteScalarAsync();

        Assert.True(result is null or DBNull);
    }

    [Fact]
    public async Task AlwaysAssignedOutputParameter_OverwritesWhateverTheCallerHeldBefore()
    {
        await using var connection = await OpenConnectionAsync();

        await using var command = new SqlCommand(
            "DECLARE @caller INT = 999; EXEC dbo.p_always_assigns @x = @caller OUTPUT; SELECT @caller;",
            connection);
        var result = await command.ExecuteScalarAsync();

        Assert.Equal(42, (int)result!);
    }

    [Fact]
    public async Task ConditionalNonAggregateSelectAssignment_ZeroMatchingRows_LeavesCallerVariableAtItsPriorRealValue_AndScannerNowFlagsSoleAssignment()
    {
        await using var connection = await OpenConnectionAsync();

        await using var command = new SqlCommand(
            "DECLARE @caller INT = 42; EXEC dbo.p_conditional_select_assigns @x = @caller OUTPUT; SELECT @caller;",
            connection);
        var result = await command.ExecuteScalarAsync();

        Assert.Equal(42, (int)result!);

        var findings = Scan(
            """
            DECLARE @T TABLE (Id INT, Val INT);
            SELECT @x = Val FROM @T WHERE Id = 1;
            """);

        var finding = Assert.Single(findings);
        Assert.Equal("@x", finding.ParameterName);
    }

    [Fact]
    public async Task ConditionalAggregateSelectAssignment_ZeroMatchingRows_StillAssignsNull_AndScannerStillDoesNotFlagSoleAssignment()
    {
        await using var connection = await OpenConnectionAsync();

        await using var command = new SqlCommand(
            "DECLARE @caller INT = 42; EXEC dbo.p_conditional_aggregate_assigns @x = @caller OUTPUT; SELECT @caller;",
            connection);
        var result = await command.ExecuteScalarAsync();

        Assert.True(result is null or DBNull);

        var findings = Scan(
            """
            DECLARE @T TABLE (Id INT, Val INT);
            SELECT @x = SUM(Val) FROM @T WHERE Id = 1;
            """);

        Assert.Empty(findings);
    }

    [Fact]
    public async Task NoFromClauseSelectSetVariable_AlwaysAssigns_AndScannerStillDoesNotFlagSoleAssignment()
    {
        await using var connection = await OpenConnectionAsync();

        await using var command = new SqlCommand(
            "DECLARE @caller INT = 999; SELECT @caller = 42; SELECT @caller;",
            connection);
        var result = await command.ExecuteScalarAsync();

        Assert.Equal(42, (int)result!);

        var findings = Scan("SELECT @x = 42;");

        Assert.Empty(findings);
    }

    private async Task<object?> CallerValueAfterAsync(string call)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = new SqlCommand($"DECLARE @caller INT = 999; {call}; SELECT @caller;", connection);
        var result = await command.ExecuteScalarAsync();
        return result is DBNull ? null : result;
    }

    [Fact]
    public async Task ExecReturnStatusIntoOutputParameter_OverwritesCallerVariable_AndScannerDoesNotFlagIt()
    {
        Assert.Equal(7, await CallerValueAfterAsync("EXEC dbo.p_return_status_assigns @x = @caller OUTPUT"));
        Assert.Equal(999, await CallerValueAfterAsync("EXEC dbo.p_never_assigns @x = @caller OUTPUT"));

        Assert.Empty(Scan("EXEC @x = dbo.p_status_seven;"));
        Assert.Single(Scan("EXEC dbo.p_status_seven;"));
    }

    [Fact]
    public async Task TryBodyThatCompletesWithoutError_NeverRunsCatch_AssignedValueReachesCaller_AndScannerDoesNotFlagIt()
    {
        Assert.Equal(42, await CallerValueAfterAsync("EXEC dbo.p_assign_in_try_swallowing_catch @x = @caller OUTPUT"));

        Assert.Empty(Scan(
            """
            BEGIN TRY
                SET @x = 42;
            END TRY
            BEGIN CATCH
                DECLARE @message NVARCHAR(2048) = ERROR_MESSAGE();
            END CATCH
            """));
    }

    [Fact]
    public async Task ThrowBeforeAssignmentInTry_LeavesCallerVariableUnchanged_NoThrowControlAssigns_AndScannerFlagsOnlyTheFormer()
    {
        Assert.Equal(999, await CallerValueAfterAsync("EXEC dbo.p_throw_before_assign @x = @caller OUTPUT, @fail = 1"));
        Assert.Equal(42, await CallerValueAfterAsync("EXEC dbo.p_throw_before_assign @x = @caller OUTPUT, @fail = 0"));
        Assert.Equal(42, await CallerValueAfterAsync("EXEC dbo.p_throw_after_assign @x = @caller OUTPUT"));

        Assert.Single(Scan(
            """
            BEGIN TRY
                IF (@fail = 1) THROW 50001, 'boom', 1;
                SET @x = 42;
            END TRY
            BEGIN CATCH
                DECLARE @message NVARCHAR(2048) = ERROR_MESSAGE();
            END CATCH
            """));
        Assert.Empty(Scan(
            """
            BEGIN TRY
                SET @x = 42;
                THROW 50001, 'boom', 1;
            END TRY
            BEGIN CATCH
                DECLARE @message NVARCHAR(2048) = ERROR_MESSAGE();
            END CATCH
            """));
    }

    private static IReadOnlyList<OutputParameterFinding> Scan(string procedureBody)
    {
        var sql = $"CREATE PROCEDURE dbo.p @x INT OUTPUT AS\nBEGIN\n{procedureBody}\nEND";
        var result = SqlScriptParser.ParseText("test.sql", sql);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        return OutputParameterScanner.Scan(result);
    }
}
