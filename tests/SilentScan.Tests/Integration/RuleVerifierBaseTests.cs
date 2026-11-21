using Microsoft.Data.SqlClient;
using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;
using SilentScan.Verify;
using SilentScan.Verify.Deployment;
using SilentScan.Verify.Oracle;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class RuleVerifierBaseTests : OracleTestFixture
{
    private const string Select = "SELECT V FROM dbo.T;";

    private static readonly TouchedObject[] TouchesT = [new("dbo", "T")];

    protected override string DatabaseNameSeed => nameof(RuleVerifierBaseTests);

    protected override string Ddl => """
        CREATE TABLE dbo.T (V INT NULL);
        GO
        CREATE TABLE dbo.Other (V INT NULL);
        GO
        CREATE TABLE dbo.W (Short VARCHAR(3) NULL, Long VARCHAR(10) NOT NULL);
        GO
        INSERT INTO dbo.W (Short, Long) VALUES (NULL, 'abcdef');
        INSERT INTO dbo.T (V) VALUES (1), (2);
        INSERT INTO dbo.Other (V) VALUES (7);
        GO
        """;

    private sealed class Configured(Perturbation perturbation) : RuleVerifier<NotInNullableSubqueryFinding>
    {
        public override string RuleId => FindingRuleIds.NotInNullableSubqueryRuleId;

        protected override Perturbation Describe(NotInNullableSubqueryFinding finding) => perturbation;
    }

    private static NotInNullableSubqueryFinding Claim() => new("Id", "dbo.T", "V", false, "claimed.sql", 1, 1);

    private static PerturbationStep Run(string sql) => async (connection, transaction, cancellationToken) =>
    {
        await using var command = new SqlCommand(sql, connection, transaction);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    };

    private static readonly PerturbationStep Nothing = (_, _, _) => Task.FromResult(0);

    private static readonly PerturbationStep NoOpWrite = Run("UPDATE dbo.T SET V = V;");

    private static Perturbation Of(Difference expected, PerturbationStep apply, Func<SqlException, bool>? refusalRefutes = null, IReadOnlyList<Touch>? touches = null) =>
        new(touches ?? TouchesT, expected, Nothing, apply, refusalRefutes ?? (_ => false));

    private Task<EngineVerification> VerifyAsync(Perturbation perturbation, string statement) =>
        new Configured(perturbation).VerifyAsync(Options, DatabaseName, Claim(), statement);

    [Fact]
    public async Task AppliedConditionEmptiesTheStatement_IsConfirmed()
    {
        var result = await VerifyAsync(Of(Difference.ResultEmptied, Run("DELETE FROM dbo.T;")), Select);

        Assert.Equal(EngineVerdict.Confirmed, result.Verdict);
    }

    [Fact]
    public async Task AppliedConditionLeavesTheStatementUnchanged_IsRefuted()
    {
        var result = await VerifyAsync(Of(Difference.ResultEmptied, NoOpWrite), Select);

        Assert.Equal(EngineVerdict.Refuted, result.Verdict);
    }

    [Fact]
    public async Task AppliedConditionChangesTheResult_IsConfirmedWhenAChangeIsExpected()
    {
        var result = await VerifyAsync(Of(Difference.ResultChanged, Run("UPDATE dbo.T SET V = V + 10;")), Select);

        Assert.Equal(EngineVerdict.Confirmed, result.Verdict);
    }

    [Fact]
    public async Task AppliedConditionLeavesTheResultUnchanged_IsRefutedWhenAChangeIsExpected()
    {
        var result = await VerifyAsync(Of(Difference.ResultChanged, NoOpWrite), Select);

        Assert.Equal(EngineVerdict.Refuted, result.Verdict);
    }

    [Fact]
    public async Task AppliedConditionTurnsAReturnedValueNull_IsConfirmedWhenANullIsExpected()
    {
        var result = await VerifyAsync(Of(Difference.NullAppeared, Run("UPDATE dbo.T SET V = NULL;")), "SELECT 'a' + CAST(V AS VARCHAR(5)) FROM dbo.T;");

        Assert.Equal(EngineVerdict.Confirmed, result.Verdict);
    }

    [Fact]
    public async Task AppliedConditionRemovesTheRowsInsteadOfNullingThem_IsRefutedWhenANullIsExpected()
    {
        var result = await VerifyAsync(Of(Difference.NullAppeared, Run("UPDATE dbo.T SET V = NULL;")), "SELECT 'a' + CAST(V AS VARCHAR(5)) FROM dbo.T WHERE V IS NOT NULL;");

        Assert.Equal(EngineVerdict.Refuted, result.Verdict);
    }

    [Fact]
    public async Task AppliedConditionChangesValuesWithoutAnyNull_IsRefutedWhenANullIsExpected()
    {
        var result = await VerifyAsync(Of(Difference.NullAppeared, Run("UPDATE dbo.T SET V = V + 10;")), "SELECT 'a' + CAST(V AS VARCHAR(5)) FROM dbo.T;");

        Assert.Equal(EngineVerdict.Refuted, result.Verdict);
    }

    private static Perturbation Rewriting(Func<string, string?> baseline, params TouchedObject[] touches) =>
        new(touches, Difference.ResultChanged, Nothing, Nothing, _ => false, baseline);

    private static string? IsNullToCoalesce(string statement) => statement.Replace("ISNULL(", "COALESCE(", StringComparison.Ordinal);

    [Fact]
    public async Task StatementDiffersFromItsRewrittenBaseline_IsConfirmed()
    {
        var result = await VerifyAsync(Rewriting(IsNullToCoalesce, new TouchedObject("dbo", "W")), "SELECT ISNULL(Short, Long) FROM dbo.W;");

        Assert.Equal(EngineVerdict.Confirmed, result.Verdict);
    }

    [Fact]
    public async Task StatementMatchesItsRewrittenBaseline_IsRefuted()
    {
        var result = await VerifyAsync(Rewriting(IsNullToCoalesce, new TouchedObject("dbo", "W")), "SELECT ISNULL(Long, Short) FROM dbo.W;");

        Assert.Equal(EngineVerdict.Refuted, result.Verdict);
    }

    [Fact]
    public async Task BaselineThatCannotBeBuiltFromTheStatement_IsNotDecided()
    {
        var result = await VerifyAsync(Rewriting(_ => null, new TouchedObject("dbo", "W")), "SELECT ISNULL(Short, Long) FROM dbo.W;");

        Assert.Equal(EngineVerdict.NotDecided, result.Verdict);
    }

    [Fact]
    public async Task DataStepThatChangedNoRows_IsNotDecidedNotRefuted()
    {
        var result = await VerifyAsync(Of(Difference.ResultEmptied, Run("DELETE FROM dbo.T WHERE V = -1;")), Select);

        Assert.Equal(EngineVerdict.NotDecided, result.Verdict);
        Assert.Contains("no rows", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RewriteThatLeavesTheStatementAsItWas_IsNotDecidedNotRefuted()
    {
        var result = await VerifyAsync(Rewriting(IsNullToCoalesce, new TouchedObject("dbo", "W")), "SELECT Long FROM dbo.W;");

        Assert.Equal(EngineVerdict.NotDecided, result.Verdict);
        Assert.Contains("rewrite", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void PerturbationThatNamesNothingItTouches_IsRejected()
    {
        Assert.ThrowsAny<ArgumentException>(() => new Perturbation([], Difference.ResultEmptied, Nothing, Nothing, _ => false));
    }

    [Fact]
    public async Task ExpressionTouchPresentOnceInTheStatement_IsJudged()
    {
        var perturbation = Of(Difference.ResultEmptied, Run("DELETE FROM dbo.T;"), touches: [new TouchedExpression("V + 0")]);

        var result = await VerifyAsync(perturbation, "SELECT V + 0 AS X FROM dbo.T;");

        Assert.Equal(EngineVerdict.Confirmed, result.Verdict);
    }

    [Fact]
    public async Task ExpressionTouchAbsentFromTheStatement_IsNotDecided()
    {
        var perturbation = Of(Difference.ResultEmptied, Run("DELETE FROM dbo.T;"), touches: [new TouchedExpression("V + 0")]);

        var result = await VerifyAsync(perturbation, "SELECT V AS X FROM dbo.T;");

        Assert.Equal(EngineVerdict.NotDecided, result.Verdict);
    }

    [Fact]
    public async Task ExpressionTouchAppearingTwice_IsNotDecidedBecauseTheFindingCannotBeLocated()
    {
        var perturbation = Of(Difference.ResultEmptied, Run("DELETE FROM dbo.T;"), touches: [new TouchedExpression("V + 0")]);

        var result = await VerifyAsync(perturbation, "SELECT V + 0 AS X, V + 0 AS Y FROM dbo.T;");

        Assert.Equal(EngineVerdict.NotDecided, result.Verdict);
    }

    [Fact]
    public async Task StatementRunAsDynamicSql_IsJudgedThroughTheTableItReads()
    {
        var result = await VerifyAsync(Of(Difference.ResultEmptied, Run("DELETE FROM dbo.T;")), "EXEC sp_executesql N'SELECT V FROM dbo.T;';");

        Assert.Equal(EngineVerdict.Confirmed, result.Verdict);
    }

    [Fact]
    public async Task EngineRefusesTheConditionAndTheRefusalIsTheClaim_IsRefuted()
    {
        var result = await VerifyAsync(
            Of(Difference.ResultEmptied, Run("INSERT INTO dbo.T (V) VALUES ('not a number');"), ex => ex.Number == 245),
            Select);

        Assert.Equal(EngineVerdict.Refuted, result.Verdict);
    }

    [Fact]
    public async Task EngineRefusesTheConditionForAnotherReason_IsNotDecided()
    {
        var result = await VerifyAsync(
            Of(Difference.ResultEmptied, Run("INSERT INTO dbo.T (V) VALUES ('not a number');"), _ => false),
            Select);

        Assert.Equal(EngineVerdict.NotDecided, result.Verdict);
    }

    [Fact]
    public async Task StatementThatVariesOnItsOwn_IsNotDecidedBecauseTheControlRunsDisagree()
    {
        var result = await VerifyAsync(Of(Difference.ResultEmptied, Run("DELETE FROM dbo.T;")), "SELECT NEWID() AS G, V FROM dbo.T;");

        Assert.Equal(EngineVerdict.NotDecided, result.Verdict);
        Assert.Contains("on its own", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StatementThatNeverReadsTheNamedObject_IsNotDecided()
    {
        var result = await VerifyAsync(Of(Difference.ResultEmptied, Run("DELETE FROM dbo.T;")), "SELECT V FROM dbo.Other;");

        Assert.Equal(EngineVerdict.NotDecided, result.Verdict);
    }

    [Fact]
    public async Task StatementThatReturnsNothingBeforeTheCondition_IsNotDecidedWhenEmptyingWasExpected()
    {
        var result = await VerifyAsync(Of(Difference.ResultEmptied, Run("DELETE FROM dbo.T;")), "SELECT V FROM dbo.T WHERE V > 100;");

        Assert.Equal(EngineVerdict.NotDecided, result.Verdict);
    }

    [Fact]
    public async Task StatementThatFailsWhenRun_IsNotDecided()
    {
        var result = await VerifyAsync(Of(Difference.ResultEmptied, Run("DELETE FROM dbo.T;")), "SELECT 1 / (V - V) FROM dbo.T;");

        Assert.Equal(EngineVerdict.NotDecided, result.Verdict);
    }

    [Fact]
    public async Task PerturbationThatDoesNotApply_IsNotDecidedWithItsReason()
    {
        var result = await VerifyAsync(Perturbation.NotApplicable("the column cannot be addressed"), Select);

        Assert.Equal(EngineVerdict.NotDecided, result.Verdict);
        Assert.Contains("cannot be addressed", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Verification_LeavesTheDatabaseUnchanged_OnEveryVerdictPath()
    {
        var before = await RowsAsync("SELECT V FROM dbo.T ORDER BY V;");

        await VerifyAsync(Of(Difference.ResultEmptied, Run("DELETE FROM dbo.T;")), Select);
        await VerifyAsync(Of(Difference.ResultChanged, Run("UPDATE dbo.T SET V = V + 10;")), Select);
        await VerifyAsync(Of(Difference.ResultEmptied, Run("INSERT INTO dbo.T (V) VALUES ('x');"), ex => ex.Number == 245), Select);

        var after = await RowsAsync("SELECT V FROM dbo.T ORDER BY V;");
        Assert.Equal(before.Select(r => r[0]), after.Select(r => r[0]));
    }

    [Fact]
    public async Task VerifierThatPerturbs_RefusesADatabaseTheHarnessDidNotCreate()
    {
        var options = SqlServerOptions.LocalDocker;
        var database = $"SilentScanUnmarked_{Guid.NewGuid():N}";
        await using (var connection = new SqlConnection(options.BuildConnectionString("master")))
        {
            await connection.OpenAsync();
            await using var create = new SqlCommand($"CREATE DATABASE [{database}];", connection);
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new Configured(Of(Difference.ResultEmptied, Nothing)).VerifyAsync(options, database, Claim(), Select));

            Assert.Contains(database, refusal.Message, StringComparison.Ordinal);
        }
        finally
        {
            SqlConnection.ClearAllPools();
            await new DatabaseProvisioner(options).DropIfExistsAsync(database);
        }
    }
}
