using System.Xml.Linq;
using SilentScan.Core.Catalog;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", "silentscan/index-shape/composite-leading-column-unconstrained")]
public sealed class CompositeIndexLeadingColumnAlternativeIndexOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(CompositeIndexLeadingColumnAlternativeIndexOracleTests);

    protected override string Ddl => StaticDdl + "\nGO\n";

    private const string StaticDdl =
        "CREATE TABLE dbo.Alt (Id INT NOT NULL PRIMARY KEY, A INT NOT NULL, B INT NOT NULL, C INT NOT NULL, Payload CHAR(200) NOT NULL);"
        + "CREATE NONCLUSTERED INDEX IX_Alt_A_B ON dbo.Alt(A, B);"
        + "CREATE NONCLUSTERED INDEX IX_Alt_C ON dbo.Alt(C);"
        + "CREATE TABLE dbo.NoAlt (Id INT NOT NULL PRIMARY KEY, A INT NOT NULL, B INT NOT NULL, C INT NOT NULL, Payload CHAR(200) NOT NULL);"
        + "CREATE NONCLUSTERED INDEX IX_NoAlt_A_B ON dbo.NoAlt(A, B);"
        + "CREATE TABLE dbo.Keys (K INT NOT NULL PRIMARY KEY);"
        + "CREATE TABLE dbo.JoinAlt (Id INT NOT NULL PRIMARY KEY, A INT NOT NULL, B INT NOT NULL, Payload CHAR(200) NOT NULL);"
        + "CREATE NONCLUSTERED INDEX IX_JoinAlt_A_B ON dbo.JoinAlt(A, B);"
        + "CREATE NONCLUSTERED INDEX IX_JoinAlt_B ON dbo.JoinAlt(B);";

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await ExecuteAsync(
            """
            INSERT INTO dbo.Alt (Id, A, B, C, Payload)
            SELECT TOP (50000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)),
                   ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) % 500,
                   ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) % 7,
                   CASE WHEN ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) <= 30 THEN 1000 ELSE 5 + ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) % 3 END,
                   'x'
            FROM sys.all_objects a CROSS JOIN sys.all_objects b;

            INSERT INTO dbo.NoAlt (Id, A, B, C, Payload)
            SELECT Id, A, B, C, Payload FROM dbo.Alt;

            UPDATE STATISTICS dbo.Alt WITH FULLSCAN;
            INSERT INTO dbo.Keys (K) VALUES (11), (12), (13);

            INSERT INTO dbo.JoinAlt (Id, A, B, Payload)
            SELECT Id, A, Id % 5000, Payload FROM dbo.Alt;

            UPDATE STATISTICS dbo.NoAlt WITH FULLSCAN;
            UPDATE STATISTICS dbo.JoinAlt WITH FULLSCAN;
            UPDATE STATISTICS dbo.Keys WITH FULLSCAN;
            """);
    }

    private static CatalogAndResult Parse(string query)
    {
        var result = SqlScriptParser.ParseText("test.sql", $"{StaticDdl}\nGO\n{query}");
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        return new CatalogAndResult(result, CatalogBuilder.Build([result]));
    }

    private sealed record CatalogAndResult(SqlParseResult Result, DatabaseCatalog Catalog);

    private static bool SeeksIndex(XDocument plan, string indexName) =>
        plan.Descendants().Any(e =>
            e.Name.LocalName == "RelOp"
            && (string?)e.Attribute("PhysicalOp") == "Index Seek"
            && e.Descendants().Any(c => c.Name.LocalName == "Object" && ((string?)c.Attribute("Index") ?? string.Empty).Contains(indexName, StringComparison.Ordinal)));

    [Fact]
    public async Task ConstraintOnAnotherIndexLeadingColumn_OptimizerSeeksThatIndex_ScannerDoesNotFlagTheComposite()
    {
        const string Query = "SELECT Payload FROM dbo.Alt WHERE B = 3 AND C = 1000;";

        var plan = await PlanInSessionAsync(string.Empty, Query);
        Assert.True(SeeksIndex(plan, "IX_Alt_C"));
        Assert.False(SeeksIndex(plan, "IX_Alt_A_B"));

        var parsed = Parse(Query);
        Assert.Empty(CompositeIndexLeadingColumnScanner.Scan(parsed.Result, parsed.Catalog));
    }

    [Fact]
    public async Task ConstraintOnlyOnNonLeadingColumnAndUnindexedColumn_OptimizerSeeksNothing_ScannerFlagsTheComposite()
    {
        const string Query = "SELECT Payload FROM dbo.NoAlt WHERE B = 3 AND C = 1000;";

        var plan = await PlanInSessionAsync(string.Empty, Query);
        Assert.False(SeeksIndex(plan, "IX_NoAlt_A_B"));
        Assert.DoesNotContain(plan.Descendants(), e => e.Name.LocalName == "RelOp" && (string?)e.Attribute("PhysicalOp") == "Index Seek");

        var parsed = Parse(Query);
        Assert.Single(CompositeIndexLeadingColumnScanner.Scan(parsed.Result, parsed.Catalog));
    }

    [Fact]
    public async Task JoinKeyOnColumnThatLeadsAnotherIndex_OptimizerSeeksThatIndex_ScannerDoesNotFlagTheComposite()
    {
        const string Query = "SELECT a.Payload FROM dbo.Keys k JOIN dbo.JoinAlt a ON a.B = k.K;";

        var plan = await PlanInSessionAsync(string.Empty, Query);
        Assert.True(SeeksIndex(plan, "IX_JoinAlt_B"));
        Assert.False(SeeksIndex(plan, "IX_JoinAlt_A_B"));

        var parsed = Parse(Query);
        Assert.Empty(CompositeIndexLeadingColumnScanner.Scan(parsed.Result, parsed.Catalog));
    }
}
