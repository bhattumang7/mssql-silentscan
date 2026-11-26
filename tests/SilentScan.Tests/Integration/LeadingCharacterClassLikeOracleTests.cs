using SilentScan.Verify;
using SilentScan.Verify.Deployment;
using SilentScan.Verify.Oracle;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.Tier1LeadingWildcardLikeRuleId)]
public sealed class LeadingCharacterClassLikeOracleTests : IAsyncLifetime
{
    private const string DatabaseName = "SilentScanLeadingClassLikeOracleTest";

    private readonly SqlServerOptions _options = SqlServerOptions.LocalDocker;
    private readonly DatabaseProvisioner _provisioner;

    public LeadingCharacterClassLikeOracleTests()
    {
        _provisioner = new DatabaseProvisioner(_options);
    }

    public async Task InitializeAsync()
    {
        await _provisioner.CreateFreshAsync(DatabaseName);
        await new ScriptDeployer(_options).DeployAsync(
            """
            CREATE TABLE dbo.Names (Id INT NOT NULL PRIMARY KEY, Narrow VARCHAR(40) NOT NULL, Wide NVARCHAR(40) NOT NULL);
            GO
            INSERT INTO dbo.Names(Id, Narrow, Wide)
            SELECT TOP (20000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)),
                   CONVERT(VARCHAR(40), NEWID()),
                   CONVERT(NVARCHAR(40), NEWID())
            FROM sys.all_objects a CROSS JOIN sys.all_objects b;
            GO
            CREATE INDEX IX_Names_Narrow ON dbo.Names(Narrow);
            GO
            CREATE INDEX IX_Names_Wide ON dbo.Names(Wide);
            GO
            UPDATE STATISTICS dbo.Names WITH FULLSCAN;
            GO
            CREATE PROCEDURE dbo.ProbeNarrowClass AS SELECT COUNT(*) FROM dbo.Names WHERE Narrow LIKE '[ab]0%';
            GO
            CREATE PROCEDURE dbo.ProbeWideClass AS SELECT COUNT(*) FROM dbo.Names WHERE Wide LIKE '[ab]0%';
            GO
            CREATE PROCEDURE dbo.ProbeRangeClass AS SELECT COUNT(*) FROM dbo.Names WHERE Narrow LIKE '[0-1]0A%';
            GO
            CREATE PROCEDURE dbo.ProbeNegatedClass AS SELECT COUNT(*) FROM dbo.Names WHERE Narrow LIKE '[^a]0A%';
            GO
            CREATE PROCEDURE dbo.ProbeEmptyNegatedClass AS SELECT COUNT(*) FROM dbo.Names WHERE Narrow LIKE '[^]0A%';
            GO
            CREATE PROCEDURE dbo.ProbeUnderscore AS SELECT COUNT(*) FROM dbo.Names WHERE Narrow LIKE '_0A%';
            GO
            CREATE PROCEDURE dbo.ProbePercent AS SELECT COUNT(*) FROM dbo.Names WHERE Narrow LIKE '%0A%';
            GO
            """,
            DatabaseName);
    }

    public async Task DisposeAsync() =>
        await _provisioner.DropIfExistsAsync(DatabaseName);

    private async Task<bool> HasIndexSeek(string probe, string indexName)
    {
        var planXml = await new PlanXmlCapture(_options).CaptureAsync(DatabaseName, probe);
        return IndexAccessDetector.HasIndexSeek(planXml, indexName);
    }

    [Fact]
    public async Task LeadingCharacterClassOnNarrowColumn_SeeksADerivedRange() =>
        Assert.True(await HasIndexSeek("EXEC dbo.ProbeNarrowClass;", "IX_Names_Narrow"));

    [Fact]
    public async Task LeadingCharacterClassOnWideColumn_SeeksADerivedRange() =>
        Assert.True(await HasIndexSeek("EXEC dbo.ProbeWideClass;", "IX_Names_Wide"));

    [Fact]
    public async Task LeadingCharacterRange_SeeksADerivedRange() =>
        Assert.True(await HasIndexSeek("EXEC dbo.ProbeRangeClass;", "IX_Names_Narrow"));

    [Fact]
    public async Task LeadingNegatedClass_DoesNotSeek() =>
        Assert.False(await HasIndexSeek("EXEC dbo.ProbeNegatedClass;", "IX_Names_Narrow"));

    [Fact]
    public async Task EmptyNegatedLeadingClass_StillSeeks() =>
        Assert.True(await HasIndexSeek("EXEC dbo.ProbeEmptyNegatedClass;", "IX_Names_Narrow"));

    [Fact]
    public async Task LeadingUnderscore_DoesNotSeek() =>
        Assert.False(await HasIndexSeek("EXEC dbo.ProbeUnderscore;", "IX_Names_Narrow"));

    [Fact]
    public async Task LeadingPercent_DoesNotSeek() =>
        Assert.False(await HasIndexSeek("EXEC dbo.ProbePercent;", "IX_Names_Narrow"));
}
