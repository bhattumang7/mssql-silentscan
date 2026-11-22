using SilentScan.Core.Catalog;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;
using SilentScan.Verify;
using SilentScan.Verify.Deployment;
using SilentScan.Verify.Oracle;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.Tier1RegexpPatternNoLiteralPrefixRuleId)]
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
        CREATE TABLE dbo.Codes (BinaryCode VARCHAR(40) COLLATE Latin1_General_100_BIN2 NOT NULL, CaseCode VARCHAR(40) COLLATE Latin1_General_100_CS_AS NOT NULL);
        GO
        CREATE INDEX IX_Codes_BinaryCode ON dbo.Codes(BinaryCode);
        GO
        CREATE INDEX IX_Codes_CaseCode ON dbo.Codes(CaseCode);
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
    [InlineData("^abc[^x]")]
    [InlineData("^ab[^x]c")]
    [InlineData("^a[^x]")]
    [InlineData("^abc[[]")]
    [InlineData(@"^\e12")]
    [InlineData(@"^\g12")]
    [InlineData(@"^\y12")]
    [InlineData("^[[]bc")]
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
    [InlineData("^[^x]bc")]
    [InlineData("^ab[[:alpha:]]")]
    [InlineData(@"^\t12")]
    [InlineData(@"^\w12")]
    [InlineData(@"^\Qab\E")]
    [InlineData("^[[:alpha:]]")]
    public async Task VerifyAsync_PatternWithoutASeekableLeadingRun_ConfirmsNoIndexSeek(string pattern)
    {
        var finding = Finding(SargabilityFindingKind.RegexpPatternNoLiteralPrefix, $"REGEXP_LIKE(DisplayName, '{pattern}')");

        var result = await _verifier.VerifyAsync(_databaseName, finding, _catalog);

        Assert.Equal(Tier1Outcome.Confirmed, result.Outcome);
    }

    [Theory]
    [InlineData("dbo.Users", "DisplayName", "m", Tier1Outcome.Confirmed)]
    [InlineData("dbo.Users", "DisplayName", "i", Tier1Outcome.NotConfirmed)]
    [InlineData("dbo.Codes", "CaseCode", "i", Tier1Outcome.NotConfirmed)]
    [InlineData("dbo.Codes", "BinaryCode", "i", Tier1Outcome.Confirmed)]
    [InlineData("dbo.Codes", "BinaryCode", "ic", Tier1Outcome.Confirmed)]
    [InlineData("dbo.Codes", "BinaryCode", "c", Tier1Outcome.NotConfirmed)]
    [InlineData("dbo.Codes", "BinaryCode", "s", Tier1Outcome.NotConfirmed)]
    public async Task VerifyAsync_FlagsArgument_DecidesWhetherTheEngineStillSeeks(string table, string column, string flags, Tier1Outcome expected)
    {
        var finding = Finding(SargabilityFindingKind.RegexpPatternNoLiteralPrefix, $"REGEXP_LIKE({column}, '^John', '{flags}')") with { TableQualifiedName = table, ColumnName = column };

        var result = await _verifier.VerifyAsync(_databaseName, finding, _catalog);

        Assert.Equal(expected, result.Outcome);
    }
}
