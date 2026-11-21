using SilentScan.Tests.Support;
using SilentScan.Verify.Oracle;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.SecurityPredicateIndexRuleId)]
public sealed class RlsPredicateSeekabilityOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(RlsPredicateSeekabilityOracleTests);

    protected override string Ddl => """
        CREATE SCHEMA Sec;
        GO
        CREATE TABLE dbo.Plain (Id INT NOT NULL PRIMARY KEY, TenantId INT NOT NULL, Pad CHAR(50) NOT NULL DEFAULT 'x');
        INSERT dbo.Plain (Id, TenantId) SELECT TOP (50000) ROW_NUMBER() OVER (ORDER BY (SELECT 1)), ROW_NUMBER() OVER (ORDER BY (SELECT 1)) % 500 FROM sys.all_objects a, sys.all_objects b;
        CREATE INDEX IX_Plain_Tenant ON dbo.Plain (TenantId);
        GO
        CREATE TABLE dbo.Bypass (Id INT NOT NULL PRIMARY KEY, TenantId INT NOT NULL, Pad CHAR(50) NOT NULL DEFAULT 'x');
        INSERT dbo.Bypass (Id, TenantId) SELECT Id, TenantId FROM dbo.Plain;
        CREATE INDEX IX_Bypass_Tenant ON dbo.Bypass (TenantId);
        GO
        CREATE FUNCTION Sec.fnEquality(@TenantId INT) RETURNS TABLE WITH SCHEMABINDING AS
        RETURN SELECT 1 AS Allowed WHERE @TenantId = CAST(SESSION_CONTEXT(N'TenantId') AS INT);
        GO
        CREATE FUNCTION Sec.fnBypass(@TenantId INT) RETURNS TABLE WITH SCHEMABINDING AS
        RETURN SELECT 1 AS Allowed WHERE @TenantId = CAST(SESSION_CONTEXT(N'TenantId') AS INT) OR CAST(SESSION_CONTEXT(N'IsAdmin') AS INT) = 1;
        GO
        CREATE SECURITY POLICY Sec.PlainPolicy ADD FILTER PREDICATE Sec.fnEquality(TenantId) ON dbo.Plain WITH (STATE = ON);
        GO
        CREATE SECURITY POLICY Sec.BypassPolicy ADD FILTER PREDICATE Sec.fnBypass(TenantId) ON dbo.Bypass WITH (STATE = ON);
        GO
        """;

    private Task<string> PlanTextAsync(string table) =>
        new PlanXmlCapture(Options).CaptureAsync(
            DatabaseName,
            $"SELECT Pad FROM dbo.{table};",
            ["EXEC sp_set_session_context N'TenantId', 7;", "EXEC sp_set_session_context N'IsAdmin', 0;"]);

    [Fact]
    public async Task IndexedBoundColumnWithPlainEquality_SeeksTheIndex()
    {
        var plan = await PlanTextAsync("Plain");

        Assert.Contains("IX_Plain_Tenant", plan, StringComparison.Ordinal);
        Assert.Contains("PhysicalOp=\"Index Seek\"", plan, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IndexedBoundColumnWithAnOrBypass_StillScans()
    {
        var plan = await PlanTextAsync("Bypass");

        Assert.DoesNotContain("PhysicalOp=\"Index Seek\"", plan, StringComparison.Ordinal);
        Assert.Contains("Scan", plan, StringComparison.Ordinal);
    }
}
