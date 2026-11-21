using Microsoft.Data.SqlClient;
using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;
using SilentScan.Verify.Oracle;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.NotInNullableSubqueryRuleId)]
public sealed class NotInNullableSubqueryVerifierTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(NotInNullableSubqueryVerifierTests);

    protected override string Ddl => """
        CREATE TABLE dbo.Parent (Id INT NOT NULL);
        GO
        CREATE TABLE dbo.EmptyParent (Id INT NOT NULL);
        GO
        CREATE TABLE dbo.ChildNullable (RefId INT NULL);
        GO
        CREATE TABLE dbo.ChildNotNull (RefId INT NOT NULL);
        GO
        CREATE TABLE dbo.ChildNullableText (RefId VARCHAR(20) NULL);
        GO
        CREATE TABLE dbo.ChildNeedsOtherColumn (Other INT NOT NULL, RefId INT NULL);
        GO
        INSERT INTO dbo.Parent (Id) VALUES (1), (2), (3);
        INSERT INTO dbo.ChildNullable (RefId) VALUES (1), (NULL);
        INSERT INTO dbo.ChildNotNull (RefId) VALUES (1);
        INSERT INTO dbo.ChildNullableText (RefId) VALUES ('1');
        INSERT INTO dbo.ChildNeedsOtherColumn (Other, RefId) VALUES (7, 1);
        GO
        """;

    private static NotInNullableSubqueryFinding Claim(string table, string column) =>
        new("Id", table, column, false, "probe.sql", 1, 1);

    private Task<EngineVerification> VerifyAsync(NotInNullableSubqueryFinding finding, string statement) =>
        new NotInNullableSubqueryVerifier().VerifyAsync(Options, DatabaseName, finding, statement);

    [Fact]
    public async Task NullableColumn_NullCollapsesTheStatement_IsConfirmed()
    {
        var result = await VerifyAsync(
            Claim("dbo.ChildNullable", "RefId"),
            "SELECT Id FROM dbo.Parent WHERE Id NOT IN (SELECT RefId FROM dbo.ChildNullable);");

        Assert.Equal(EngineVerdict.Confirmed, result.Verdict);
    }

    [Fact]
    public async Task NullableTextColumn_NullCollapsesTheStatement_IsConfirmed()
    {
        var result = await VerifyAsync(
            Claim("dbo.ChildNullableText", "RefId"),
            "SELECT Id FROM dbo.Parent WHERE CAST(Id AS VARCHAR(20)) NOT IN (SELECT RefId FROM dbo.ChildNullableText);");

        Assert.Equal(EngineVerdict.Confirmed, result.Verdict);
    }

    [Fact]
    public async Task NotNullColumn_EngineRejectsTheNull_IsRefuted()
    {
        var result = await VerifyAsync(
            Claim("dbo.ChildNotNull", "RefId"),
            "SELECT Id FROM dbo.Parent WHERE Id NOT IN (SELECT RefId FROM dbo.ChildNotNull);");

        Assert.Equal(EngineVerdict.Refuted, result.Verdict);
    }

    [Fact]
    public async Task NullableColumnExcludedByAnIsNotNullFilter_StatementIsUnchangedByANull_IsRefuted()
    {
        var result = await VerifyAsync(
            Claim("dbo.ChildNullable", "RefId"),
            "SELECT Id FROM dbo.Parent WHERE Id NOT IN (SELECT RefId FROM dbo.ChildNullable WHERE RefId IS NOT NULL);");

        Assert.Equal(EngineVerdict.Refuted, result.Verdict);
    }

    [Fact]
    public async Task DeleteStatement_NullEmptiesItsEffect_IsConfirmed()
    {
        var result = await VerifyAsync(
            Claim("dbo.ChildNullable", "RefId"),
            "DELETE FROM dbo.Parent WHERE Id NOT IN (SELECT RefId FROM dbo.ChildNullable);");

        Assert.Equal(EngineVerdict.Confirmed, result.Verdict);
    }

    [Fact]
    public async Task DeleteStatementBehindAnIsNotNullFilter_IsRefutedNotMistakenForAnEmptiedEffect()
    {
        var result = await VerifyAsync(
            Claim("dbo.ChildNullable", "RefId"),
            "DELETE FROM dbo.Parent WHERE Id NOT IN (SELECT RefId FROM dbo.ChildNullable WHERE RefId IS NOT NULL);");

        Assert.Equal(EngineVerdict.Refuted, result.Verdict);
    }

    [Fact]
    public async Task StatementNeverTouchesTheClaimedTable_IsNotDecided()
    {
        var result = await VerifyAsync(
            Claim("dbo.ChildNullable", "RefId"),
            "SELECT Id FROM dbo.Parent WHERE Id NOT IN (SELECT RefId FROM dbo.ChildNotNull);");

        Assert.Equal(EngineVerdict.NotDecided, result.Verdict);
    }

    [Fact]
    public async Task StatementThatReturnsNothingEvenWithoutANull_IsNotDecided()
    {
        var result = await VerifyAsync(
            Claim("dbo.ChildNullable", "RefId"),
            "SELECT Id FROM dbo.EmptyParent WHERE Id NOT IN (SELECT RefId FROM dbo.ChildNullable);");

        Assert.Equal(EngineVerdict.NotDecided, result.Verdict);
    }

    [Fact]
    public async Task NullInsertFailsOnAnotherColumn_IsNotDecidedRatherThanRefuted()
    {
        var result = await VerifyAsync(
            Claim("dbo.ChildNeedsOtherColumn", "RefId"),
            "SELECT Id FROM dbo.Parent WHERE Id NOT IN (SELECT RefId FROM dbo.ChildNeedsOtherColumn);");

        Assert.Equal(EngineVerdict.NotDecided, result.Verdict);
    }

    [Fact]
    public async Task MissingTable_IsNotDecided()
    {
        var result = await VerifyAsync(
            Claim("dbo.DoesNotExist", "RefId"),
            "SELECT Id FROM dbo.Parent WHERE Id NOT IN (SELECT RefId FROM dbo.DoesNotExist);");

        Assert.Equal(EngineVerdict.NotDecided, result.Verdict);
    }

    [Fact]
    public async Task Verification_LeavesTheDatabaseUnchanged()
    {
        var before = await ScalarAsync<int>("SELECT (SELECT COUNT(*) FROM dbo.ChildNullable) * 1000 + (SELECT COUNT(*) FROM dbo.ChildNotNull) * 10 + (SELECT COUNT(*) FROM dbo.Parent);");

        await VerifyAsync(
            Claim("dbo.ChildNullable", "RefId"),
            "SELECT Id FROM dbo.Parent WHERE Id NOT IN (SELECT RefId FROM dbo.ChildNullable);");
        await VerifyAsync(
            Claim("dbo.ChildNotNull", "RefId"),
            "SELECT Id FROM dbo.Parent WHERE Id NOT IN (SELECT RefId FROM dbo.ChildNotNull);");

        var after = await ScalarAsync<int>("SELECT (SELECT COUNT(*) FROM dbo.ChildNullable) * 1000 + (SELECT COUNT(*) FROM dbo.ChildNotNull) * 10 + (SELECT COUNT(*) FROM dbo.Parent);");
        Assert.Equal(before, after);
    }

    [Fact]
    public void Judges_OnlyFindingsCarryingThisRulesId()
    {
        var verifier = new NotInNullableSubqueryVerifier();

        Assert.True(verifier.Judges(Claim("dbo.ChildNullable", "RefId")));
    }

    [Fact]
    public void RegistryFindsTheVerifierByRuleId()
    {
        var verifier = RuleVerifierRegistry.For(FindingRuleIds.NotInNullableSubqueryRuleId);

        Assert.IsType<NotInNullableSubqueryVerifier>(verifier);
        Assert.Equal(typeof(NotInNullableSubqueryFinding), verifier.FindingType);
    }
}
