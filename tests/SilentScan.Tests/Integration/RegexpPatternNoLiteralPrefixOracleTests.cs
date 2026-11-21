using SilentScan.Core.Catalog;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;
using SilentScan.Verify;
using SilentScan.Verify.Deployment;
using SilentScan.Verify.Oracle;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
[Trait("Rule", "silentscan/tier1/regexp-pattern-no-literal-prefix")]
public sealed class RegexpPatternNoLiteralPrefixOracleTests : IAsyncLifetime
{
    private static readonly SqlServerOptions Options = SqlServerOptions.LocalDockerLatest;

    private readonly string _databaseName = $"{nameof(RegexpPatternNoLiteralPrefixOracleTests)}_{Guid.NewGuid():N}";
    private readonly Tier1Verifier _verifier = new(Options);
    private DatabaseCatalog _catalog = null!;

    private const string Ddl = """
        ALTER DATABASE CURRENT SET COMPATIBILITY_LEVEL = 170;
        GO
        CREATE TABLE dbo.Users (DisplayName NVARCHAR(40) NOT NULL);
        GO
        CREATE INDEX IX_Users_DisplayName ON dbo.Users(DisplayName);
        GO
        """;

    public async Task InitializeAsync()
    {
        await new DatabaseProvisioner(Options).CreateFreshAsync(_databaseName);
        await new ScriptDeployer(Options).DeployAsync(Ddl, _databaseName);

        var parseResult = SqlScriptParser.ParseText("ddl.sql", Ddl);
        _catalog = CatalogBuilder.Build([parseResult]);
    }

    public async Task DisposeAsync() => await new DatabaseProvisioner(Options).DropIfExistsAsync(_databaseName);

    private static SargabilityFinding Finding(SargabilityFindingKind kind, string predicateFragmentText) =>
        new(kind, "DisplayName", Detail: null, "file.sql", 1, 1, TableQualifiedName: "dbo.Users", Indexed: true, PredicateFragmentText: predicateFragmentText);

    [Fact]
    public async Task VerifyAsync_LiteralPatternWithoutAnchoredPrefix_ConfirmsNoIndexSeek()
    {
        var finding = Finding(SargabilityFindingKind.RegexpPatternNoLiteralPrefix, "REGEXP_LIKE(DisplayName, '[Jj]ohn')");

        var result = await _verifier.VerifyAsync(_databaseName, finding, _catalog);

        Assert.Equal(Tier1Outcome.Confirmed, result.Outcome);
    }

    [Fact]
    public async Task VerifyAsync_AnchoredPureLiteralPattern_IsNotConfirmed()
    {
        var finding = Finding(SargabilityFindingKind.RegexpPatternNoLiteralPrefix, "REGEXP_LIKE(DisplayName, '^John')");

        var result = await _verifier.VerifyAsync(_databaseName, finding, _catalog);

        Assert.Equal(Tier1Outcome.NotConfirmed, result.Outcome);
    }

    [Fact]
    public async Task VerifyAsync_LiteralPatternWithAnchorButTrailingMetacharacter_ConfirmsNoIndexSeek()
    {
        var finding = Finding(SargabilityFindingKind.RegexpPatternNoLiteralPrefix, "REGEXP_LIKE(DisplayName, '^John$')");

        var result = await _verifier.VerifyAsync(_databaseName, finding, _catalog);

        Assert.Equal(Tier1Outcome.Confirmed, result.Outcome);
    }

    [Theory]
    [InlineData("^[a-c]")]
    [InlineData("^ab?")]
    [InlineData("^ab*")]
    [InlineData("^ab+")]
    [InlineData("^a{2,3}")]
    [InlineData("^a[bc]d")]
    [InlineData(@"^abc\.")]
    [InlineData(@"^[\d]")]
    public async Task VerifyAsync_AnchoredClassOrQuantifiedLiteral_IsNotConfirmed(string pattern)
    {
        var finding = Finding(SargabilityFindingKind.RegexpPatternNoLiteralPrefix, $"REGEXP_LIKE(DisplayName, '{pattern}')");

        var result = await _verifier.VerifyAsync(_databaseName, finding, _catalog);

        Assert.Equal(Tier1Outcome.NotConfirmed, result.Outcome);
    }

    [Theory]
    [InlineData("^abc.*")]
    [InlineData("^a.c")]
    [InlineData("^(abc)")]
    [InlineData("^a|^b")]
    [InlineData(@"^\d")]
    [InlineData("^a?b")]
    [InlineData("^a*")]
    [InlineData("^[^a]")]
    public async Task VerifyAsync_PatternWithoutASeekableLeadingRun_ConfirmsNoIndexSeek(string pattern)
    {
        var finding = Finding(SargabilityFindingKind.RegexpPatternNoLiteralPrefix, $"REGEXP_LIKE(DisplayName, '{pattern}')");

        var result = await _verifier.VerifyAsync(_databaseName, finding, _catalog);

        Assert.Equal(Tier1Outcome.Confirmed, result.Outcome);
    }
}
