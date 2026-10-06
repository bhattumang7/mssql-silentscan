using SilentScan.Core.Catalog;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", "silentscan/index/key-lookup-prone")]
[Trait("Rule", "silentscan/index-shape/composite-leading-column-unconstrained")]
public sealed class OuterJoinPreservedSideConstraintOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(OuterJoinPreservedSideConstraintOracleTests);

    protected override string Ddl => StaticDdl + "\nGO\n";

    private const string StaticDdl =
        "CREATE TABLE dbo.Probe (Id INT NOT NULL PRIMARY KEY, K INT NOT NULL);"
        + "CREATE TABLE dbo.Facts (Id INT NOT NULL PRIMARY KEY, K1 INT NOT NULL, K2 INT NOT NULL, Payload CHAR(200) NOT NULL);"
        + "CREATE NONCLUSTERED INDEX IX_Facts_K2 ON dbo.Facts(K2);"
        + "CREATE TABLE dbo.Pairs (Id INT NOT NULL PRIMARY KEY, A INT NOT NULL, B INT NOT NULL, Payload CHAR(200) NOT NULL);"
        + "CREATE NONCLUSTERED INDEX IX_Pairs_A_B ON dbo.Pairs(A, B);";

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await ExecuteAsync(
            """
            INSERT INTO dbo.Probe (Id, K)
            SELECT TOP (3000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)), ROW_NUMBER() OVER (ORDER BY (SELECT NULL))
            FROM sys.all_objects a CROSS JOIN sys.all_objects b;

            INSERT INTO dbo.Facts (Id, K1, K2, Payload)
            SELECT TOP (50000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)),
                   ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) % 20000,
                   CASE WHEN ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) <= 30 THEN 1000 ELSE 5 + ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) % 3 END,
                   'x'
            FROM sys.all_objects a CROSS JOIN sys.all_objects b;

            INSERT INTO dbo.Pairs (Id, A, B, Payload)
            SELECT TOP (50000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)),
                   ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) % 500,
                   CASE WHEN ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) <= 30 THEN 1000 ELSE 5 + ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) % 3 END,
                   'x'
            FROM sys.all_objects a CROSS JOIN sys.all_objects b;

            UPDATE STATISTICS dbo.Probe WITH FULLSCAN;
            UPDATE STATISTICS dbo.Facts WITH FULLSCAN;
            UPDATE STATISTICS dbo.Pairs WITH FULLSCAN;
            """);
    }

    private static CatalogAndResult Parse(string query)
    {
        var result = SqlScriptParser.ParseText("test.sql", $"{StaticDdl}\nGO\n{query}");
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        return new CatalogAndResult(result, CatalogBuilder.Build([result]));
    }

    private sealed record CatalogAndResult(SqlParseResult Result, DatabaseCatalog Catalog);

    private static bool HasOperator(System.Xml.Linq.XDocument plan, string physicalOp) =>
        plan.Descendants().Any(e => e.Name.LocalName == "RelOp" && (string?)e.Attribute("PhysicalOp") == physicalOp);

    private static bool HasKeyLookup(System.Xml.Linq.XDocument plan) =>
        plan.Descendants().Any(e => e.Name.LocalName == "IndexScan" && (string?)e.Attribute("Lookup") == "1");

    [Fact]
    public async Task ConstantInOnClauseOfPreservedSide_OptimizerScansWithoutKeyLookup_ScannerDoesNotFlagIt()
    {
        const string Query = "SELECT f.Payload FROM dbo.Facts f LEFT JOIN dbo.Probe p ON p.K = f.K1 AND f.K2 = 1000;";

        var plan = await PlanInSessionAsync(string.Empty, Query);
        Assert.False(HasKeyLookup(plan), "the preserved side must be read in full, so no seek plus Key Lookup is possible");
        Assert.True(HasOperator(plan, "Clustered Index Scan"));

        var parsed = Parse(Query);
        Assert.DoesNotContain(IndexCoverageScanner.Scan(parsed.Result, parsed.Catalog), f => f.Kind == IndexCoverageFindingKind.KeyLookupProneIndex);
    }

    [Fact]
    public async Task ConstantInOnClauseOfRightJoinPreservedSide_OptimizerScansWithoutKeyLookup_ScannerDoesNotFlagIt()
    {
        const string Query = "SELECT f.Payload FROM dbo.Probe p RIGHT JOIN dbo.Facts f ON p.K = f.K1 AND f.K2 = 1000;";

        var plan = await PlanInSessionAsync(string.Empty, Query);
        Assert.False(HasKeyLookup(plan));

        var parsed = Parse(Query);
        Assert.DoesNotContain(IndexCoverageScanner.Scan(parsed.Result, parsed.Catalog), f => f.Kind == IndexCoverageFindingKind.KeyLookupProneIndex);
    }

    [Fact]
    public async Task ConstantInOnClauseOfNullSuppliedSide_OptimizerSeeksAndLooksUp_ScannerFlagsIt()
    {
        const string Query = "SELECT f.Payload FROM dbo.Probe p LEFT JOIN dbo.Facts f ON p.K = f.K1 AND f.K2 = 1000;";

        var plan = await PlanInSessionAsync(string.Empty, Query);
        Assert.True(HasKeyLookup(plan), "a constant on the null-supplying side filters that table, so the selective index is seeked");

        var parsed = Parse(Query);
        Assert.Single(IndexCoverageScanner.Scan(parsed.Result, parsed.Catalog), f => f.Kind == IndexCoverageFindingKind.KeyLookupProneIndex);
    }

    [Fact]
    public async Task ConstantInWhereOfPreservedSide_OptimizerSeeksAndLooksUp_ScannerFlagsIt()
    {
        const string Query = "SELECT f.Payload FROM dbo.Facts f LEFT JOIN dbo.Probe p ON p.K = f.K1 WHERE f.K2 = 1000;";

        var plan = await PlanInSessionAsync(string.Empty, Query);
        Assert.True(HasKeyLookup(plan));

        var parsed = Parse(Query);
        Assert.Single(IndexCoverageScanner.Scan(parsed.Result, parsed.Catalog), f => f.Kind == IndexCoverageFindingKind.KeyLookupProneIndex);
    }

    [Fact]
    public async Task NonLeadingConstantInOnClauseOfPreservedSide_PredicateIsNotPushedToTheScan_ScannerDoesNotFlagIt()
    {
        const string Query = "SELECT a.Id FROM dbo.Pairs a LEFT JOIN dbo.Probe p ON p.K = a.Id AND a.B = 1000;";

        var plan = await PlanInSessionAsync(string.Empty, Query);
        Assert.DoesNotContain(plan.Descendants(), e => e.Name.LocalName == "IndexScan" && e.Elements().Any(c => c.Name.LocalName == "Predicate"));

        var parsed = Parse(Query);
        Assert.Empty(CompositeIndexLeadingColumnScanner.Scan(parsed.Result, parsed.Catalog));
    }

    [Fact]
    public async Task NonLeadingConstantInOnClauseOfNullSuppliedSide_PredicateIsPushedToTheScan_ScannerFlagsIt()
    {
        const string Query = "SELECT a.Id FROM dbo.Probe p LEFT JOIN dbo.Pairs a ON p.K = a.Id AND a.B = 1000;";

        var plan = await PlanInSessionAsync(string.Empty, Query);
        Assert.Contains(plan.Descendants(), e => e.Name.LocalName == "IndexScan" && e.Elements().Any(c => c.Name.LocalName == "Predicate"));

        var parsed = Parse(Query);
        Assert.Single(CompositeIndexLeadingColumnScanner.Scan(parsed.Result, parsed.Catalog));
    }
}
