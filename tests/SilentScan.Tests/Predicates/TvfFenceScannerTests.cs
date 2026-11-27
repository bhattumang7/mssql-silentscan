using SilentScan.Core.Catalog;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;

namespace SilentScan.Tests.Predicates;

public sealed class TvfFenceScannerTests
{
    private static readonly string FixturesDir = Path.Combine(AppContext.BaseDirectory, "fixtures", "tvf_fence");

    private static IReadOnlyList<TvfFenceFinding> ScanFixture(string fileName, int? compatibilityLevel = 130)
    {
        var path = Path.Combine(FixturesDir, fileName);
        var result = SqlScriptParser.ParseFile(path);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));

        var catalog = CatalogBuilder.Build([result]);
        catalog.CompatibilityLevel = compatibilityLevel;
        var (views, _) = ViewDefinitionExtractor.Extract([result], catalog.DefaultCollation, catalog.TypeAliases);
        var fenceMap = TvfFenceMap.Build(views, catalog);
        return TvfFenceScanner.Scan(result, catalog, fenceMap);
    }

    [Fact]
    public void CorrelatedApply_ArgumentReferencesOuterColumn_Fires()
    {
        var findings = ScanFixture("CORRELATED_APPLY_fires.sql");

        var finding = Assert.Single(findings);
        Assert.Equal(TvfFenceFindingKind.CorrelatedApply, finding.Kind);
        Assert.Equal("dbo.fn_CustomerTier", finding.FunctionQualifiedName);
        Assert.NotNull(finding.CorrelatedOuterColumns);
        Assert.Contains("CustomerId", finding.CorrelatedOuterColumns);
    }

    [Fact]
    public void CorrelatedApply_OverInlineTvf_DoesNotFire()
    {
        Assert.Empty(ScanFixture("CORRELATED_APPLY_clean.sql"));
    }

    [Fact]
    public void CorrelatedApply_UncorrelatedArgument_DoesNotFire()
    {
        var result = SqlScriptParser.ParseText("test.sql", """
            CREATE TABLE dbo.Orders (OrderId INT NOT NULL PRIMARY KEY, CustomerId INT NOT NULL);
            GO
            CREATE FUNCTION dbo.fn_CustomerTier(@CustomerId INT)
            RETURNS @Tier TABLE (TierName VARCHAR(20))
            AS
            BEGIN
                INSERT INTO @Tier (TierName) SELECT 'Gold';
                RETURN;
            END;
            GO
            SELECT o.OrderId, t.TierName
            FROM dbo.Orders o
            CROSS APPLY dbo.fn_CustomerTier(1) t;
            """);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));

        var catalog = CatalogBuilder.Build([result]);
        var findings = TvfFenceScanner.Scan(result, catalog, new Dictionary<string, TvfFenceOrigin>());

        Assert.Empty(findings);
    }

    private const string SoleSourceSetup = """
        CREATE TABLE dbo.Orders (OrderId INT NOT NULL PRIMARY KEY, CustomerId INT NOT NULL);
        GO
        CREATE FUNCTION dbo.fn_Tier(@CustomerId INT)
        RETURNS @Tier TABLE (CustomerId INT, TierName VARCHAR(20))
        AS
        BEGIN
            INSERT INTO @Tier (CustomerId, TierName) SELECT @CustomerId, 'Gold';
            RETURN;
        END;
        GO
        CREATE VIEW dbo.vw_Tier AS SELECT CustomerId, TierName FROM dbo.fn_Tier(1);
        GO
        CREATE VIEW dbo.vw_TierOuter AS SELECT CustomerId, TierName FROM dbo.vw_Tier;
        GO
        CREATE VIEW dbo.vw_TierJoined AS SELECT o.OrderId, t.TierName FROM dbo.Orders o JOIN dbo.fn_Tier(1) t ON t.CustomerId = o.CustomerId;
        GO
        CREATE FUNCTION dbo.itvf_Tier(@CustomerId INT) RETURNS TABLE AS RETURN (SELECT CustomerId, TierName FROM dbo.fn_Tier(@CustomerId));
        GO

        """;

    private static List<TvfFenceFinding> ScanText(string sql, int? compatibilityLevel = 130, bool interleavedExecutionDisabled = false)
    {
        var result = SqlScriptParser.ParseText("test.sql", SoleSourceSetup + sql);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));

        var catalog = CatalogBuilder.Build([result]);
        catalog.CompatibilityLevel = compatibilityLevel;
        if (interleavedExecutionDisabled)
        {
            catalog.DisabledScopedConfigurations.Add("INTERLEAVED_EXECUTION_TVF");
        }

        var (views, _) = ViewDefinitionExtractor.Extract([result], catalog.DefaultCollation, catalog.TypeAliases);
        var fenceMap = TvfFenceMap.Build(views, catalog);
        return TvfFenceScanner.Scan(result, catalog, fenceMap).Where(f => f.Kind == TvfFenceFindingKind.NestedUnderViewOrTvf).ToList();
    }

    [Fact]
    public void NestedUnderViewOrTvf_AtCompat160WithInterleavedExecutionDisabled_Fires()
    {
        const string sql = "SELECT o.OrderId FROM dbo.Orders o JOIN dbo.vw_Tier t ON t.CustomerId = o.CustomerId;";

        Assert.NotEmpty(ScanText(sql, 160, interleavedExecutionDisabled: true));
        Assert.Empty(ScanText(sql, 160));
    }

    [Theory]
    [InlineData("SELECT TierName FROM dbo.vw_Tier;")]
    [InlineData("SELECT TierName FROM dbo.vw_Tier WHERE CustomerId = 3;")]
    [InlineData("SELECT TierName FROM dbo.vw_TierOuter;")]
    [InlineData("SELECT * FROM dbo.itvf_Tier(5);")]
    [InlineData("DECLARE @t VARCHAR(20); SELECT @t = TierName FROM dbo.vw_Tier;")]
    [InlineData("IF EXISTS (SELECT 1 FROM dbo.vw_Tier WHERE TierName = 'Gold') PRINT 'x';")]
    [InlineData("DECLARE c CURSOR LOCAL FAST_FORWARD FOR SELECT TierName FROM dbo.vw_Tier;")]
    public void NestedUnderViewOrTvf_CallerReadsTheFenceAsItsOnlyRowSource_DoesNotFire(string sql)
    {
        Assert.Empty(ScanText(sql));
    }

    [Theory]
    [InlineData("SELECT o.OrderId FROM dbo.Orders o JOIN dbo.vw_Tier t ON t.CustomerId = o.CustomerId;")]
    [InlineData("SELECT o.OrderId FROM dbo.Orders o CROSS APPLY dbo.itvf_Tier(o.CustomerId) t;")]
    [InlineData("SELECT OrderId FROM dbo.Orders o WHERE EXISTS (SELECT 1 FROM dbo.vw_Tier t WHERE t.CustomerId = o.CustomerId);")]
    [InlineData("SELECT DISTINCT TierName FROM dbo.vw_Tier;")]
    [InlineData("SELECT TierName FROM dbo.vw_Tier ORDER BY TierName;")]
    [InlineData("SELECT TierName, COUNT(*) FROM dbo.vw_Tier GROUP BY TierName;")]
    [InlineData("SELECT TOP (5) TierName FROM dbo.vw_Tier;")]
    [InlineData("SELECT TierName INTO #x FROM dbo.vw_Tier;")]
    [InlineData("DECLARE @t TABLE (N VARCHAR(20)); INSERT INTO @t SELECT TierName FROM dbo.vw_Tier;")]
    [InlineData("SELECT TierName FROM dbo.vw_Tier UNION ALL SELECT TierName FROM dbo.vw_Tier;")]
    [InlineData("WITH c AS (SELECT TierName FROM dbo.vw_Tier) SELECT TierName FROM c;")]
    [InlineData("SELECT TierName FROM dbo.vw_Tier WHERE CustomerId IN (SELECT CustomerId FROM dbo.Orders);")]
    [InlineData("SELECT OrderId FROM dbo.vw_TierJoined;")]
    public void NestedUnderViewOrTvf_AnotherOperatorCanConsumeTheFixedEstimate_Fires(string sql)
    {
        Assert.NotEmpty(ScanText(sql));
    }

    [Fact]
    public void NestedUnderViewOrTvf_ViewWrapsCorrelatedFence_FiresAtOuterCallSite()
    {
        var findings = ScanFixture("NESTED_UNDER_VIEW_OR_TVF_fires.sql");

        var nested = Assert.Single(findings, f => f.Kind == TvfFenceFindingKind.NestedUnderViewOrTvf);
        Assert.Equal("dbo.vw_CustomerTier", nested.ReferencedObjectQualifiedName);
        Assert.Equal("dbo.fn_CustomerTier", nested.FunctionQualifiedName);
        Assert.Equal(1, nested.Depth);
        Assert.NotNull(nested.OriginSourcePath);

        Assert.Contains(findings, f => f.Kind == TvfFenceFindingKind.CorrelatedApply);
    }

    [Theory]
    [InlineData(140)]
    [InlineData(150)]
    [InlineData(170)]
    [InlineData(null)]
    public void NestedUnderViewOrTvf_AtOrAboveInterleavedExecutionCompatOrUnknown_DoesNotFire(int? compatibilityLevel)
    {
        var findings = ScanText("SELECT o.OrderId FROM dbo.Orders o JOIN dbo.vw_Tier t ON t.CustomerId = o.CustomerId;", compatibilityLevel);

        Assert.Empty(findings);
    }

    [Fact]
    public void NestedUnderViewOrTvf_OrdinaryViewOverBaseTable_DoesNotFire()
    {
        Assert.Empty(ScanFixture("NESTED_UNDER_VIEW_OR_TVF_clean.sql"));
    }

    [Fact]
    public void NestedUnderViewOrTvf_ViaInlineTvfFunctionCallSyntax_Fires()
    {
        var findings = ScanFixture("NESTED_UNDER_VIEW_OR_TVF_via_inline_tvf_fires.sql");

        var nested = Assert.Single(findings, f => f.Kind == TvfFenceFindingKind.NestedUnderViewOrTvf);
        Assert.Equal("dbo.itvf_CustomerTierWrapper", nested.ReferencedObjectQualifiedName);
        Assert.Equal("dbo.fn_CustomerTier", nested.FunctionQualifiedName);
        Assert.Equal(1, nested.Depth);
    }

    [Fact]
    public void NestedUnderViewOrTvf_ViaInlineTvfWrappingAnotherInlineTvf_DoesNotFire()
    {
        Assert.Empty(ScanFixture("NESTED_UNDER_VIEW_OR_TVF_via_inline_tvf_clean.sql"));
    }

    [Theory]
    [InlineData("SELECT v.Id FROM dbo.VMs v JOIN dbo.Other o ON o.Id = v.Id;", false)]
    [InlineData("INSERT dbo.Sink SELECT v.Id FROM dbo.VMs v JOIN dbo.Other o ON o.Id = v.Id;", true)]
    [InlineData("SELECT v.Id INTO #x FROM dbo.VMs v JOIN dbo.Other o ON o.Id = v.Id;", true)]
    [InlineData("IF EXISTS (SELECT 1 FROM dbo.VMs v JOIN dbo.Other o ON o.Id = v.Id) SELECT 1;", true)]
    [InlineData("DECLARE c CURSOR LOCAL FOR SELECT v.Id FROM dbo.VMs v JOIN dbo.Other o ON o.Id = v.Id;", true)]
    [InlineData("SELECT v.Id FROM dbo.VMs v JOIN dbo.Other o ON o.Id = v.Id OPTION (USE HINT('DISABLE_INTERLEAVED_EXECUTION_TVF'));", true)]
    [InlineData("SELECT v.Id FROM dbo.VMs v JOIN dbo.Other o ON o.Id = v.Id OPTION (QUERYTRACEON 11005);", true)]
    public void AtCompat140AndLater_OnlyStatementsWithoutInterleavedExecutionInheritTheFence(string statement, bool fires)
    {
        const string setup = """
            CREATE TABLE dbo.Other (Id INT NOT NULL);
            CREATE TABLE dbo.Sink (Id INT NOT NULL);
            GO
            CREATE FUNCTION dbo.Ms() RETURNS @t TABLE (Id INT) AS BEGIN INSERT @t VALUES (1); RETURN; END;
            GO
            CREATE VIEW dbo.VMs AS SELECT Id FROM dbo.Ms();
            GO
            """;
        var result = SqlScriptParser.ParseText("test.sql", $"{setup}\n{statement}");
        var catalog = CatalogBuilder.Build([result]);
        catalog.CompatibilityLevel = 160;
        var (views, _) = ViewDefinitionExtractor.Extract([result], catalog.DefaultCollation, catalog.TypeAliases);

        var findings = TvfFenceScanner.Scan(result, catalog, TvfFenceMap.Build(views, catalog));

        Assert.Equal(fires, findings.Any(f => f.Kind == TvfFenceFindingKind.NestedUnderViewOrTvf));
    }
}
