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
        CREATE TABLE dbo.ChildNullableInt (RefId INT NULL);
        GO
        CREATE TABLE dbo.ChildNotNullInt (RefId INT NOT NULL);
        GO
        CREATE TABLE dbo.ChildNullableText (RefId VARCHAR(20) NULL);
        GO
        CREATE TABLE dbo.ChildNullableDate (RefId DATE NULL);
        GO
        CREATE TABLE dbo.ChildNullableIdentityHost (Id INT IDENTITY(1,1) NOT NULL, RefId INT NULL);
        GO
        CREATE TABLE dbo.ChildNullableXml (RefId XML NULL);
        GO
        """;

    private static NotInNullableSubqueryFinding Claim(string table, string column) =>
        new("Id", table, column, false, "probe.sql", 1, 1);

    private Task<EngineVerification> VerifyAsync(NotInNullableSubqueryFinding finding) =>
        new NotInNullableSubqueryVerifier().VerifyAsync(Options, DatabaseName, finding);

    [Theory]
    [InlineData("dbo.ChildNullableInt", "RefId")]
    [InlineData("dbo.ChildNullableText", "RefId")]
    [InlineData("dbo.ChildNullableDate", "RefId")]
    [InlineData("dbo.ChildNullableIdentityHost", "RefId")]
    public async Task NullableColumn_ClaimIsConfirmedByTheEngine(string table, string column)
    {
        var result = await VerifyAsync(Claim(table, column));

        Assert.Equal(EngineVerdict.Confirmed, result.Verdict);
    }

    [Fact]
    public async Task NotNullColumn_ClaimIsRefutedByTheEngine()
    {
        var result = await VerifyAsync(Claim("dbo.ChildNotNullInt", "RefId"));

        Assert.Equal(EngineVerdict.Refuted, result.Verdict);
    }

    [Fact]
    public async Task ColumnTypeTheProbeCannotBuildAValueFor_IsNotDecidedRatherThanGuessed()
    {
        var result = await VerifyAsync(Claim("dbo.ChildNullableXml", "RefId"));

        Assert.Equal(EngineVerdict.NotDecided, result.Verdict);
    }

    [Fact]
    public async Task MissingTable_IsNotDecided()
    {
        var result = await VerifyAsync(Claim("dbo.DoesNotExist", "RefId"));

        Assert.Equal(EngineVerdict.NotDecided, result.Verdict);
    }

    [Fact]
    public void RegistryFindsTheVerifierByRuleId()
    {
        var verifier = RuleVerifierRegistry.For(FindingRuleIds.NotInNullableSubqueryRuleId);

        Assert.IsType<NotInNullableSubqueryVerifier>(verifier);
        Assert.Equal(typeof(NotInNullableSubqueryFinding), verifier.FindingType);
    }
}
