using SilentScan.Core.Catalog;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;

namespace SilentScan.Tests.Lineage;

public sealed class FullTextTableReferenceTests
{
    private const string Ddl = """
        CREATE TABLE dbo.Doc (Code VARCHAR(12) NOT NULL CONSTRAINT UQ_Doc UNIQUE, Id INT NOT NULL PRIMARY KEY, Txt NVARCHAR(200) NULL);
        GO
        CREATE VIEW dbo.vw_Ranked AS
        SELECT k.[KEY] AS DocKey, k.[RANK] AS DocRank, d.Id AS Id
        FROM CONTAINSTABLE(dbo.Doc, Txt, 'x') AS k
        JOIN dbo.Doc AS d ON d.Code = k.[KEY];
        """;

    private static LineageCatalog Resolve(string? keyColumn)
    {
        var result = SqlScriptParser.ParseText("ft.sql", Ddl);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        var catalog = CatalogBuilder.Build([result]);
        var doc = catalog.Find("dbo.Doc")!;
        catalog.AddOrReplace(doc with { HasFullTextIndex = keyColumn is not null, FullTextKeyColumnName = keyColumn });
        return LineageResolver.Resolve(catalog, [result]);
    }

    private static string? TypeOf(LineageCatalog lineage, string column) =>
        ColumnProvenanceAnalysis.TryGetScalarType(lineage.Find("dbo.vw_Ranked")!.FindColumn(column)!.Provenance)?.ToString();

    [Fact]
    public void ContainsTable_KeyTakesTheFullTextKeyColumnTypeAndRankIsInt()
    {
        var lineage = Resolve("Code");

        Assert.Equal("VarChar(12)", TypeOf(lineage, "DocKey"));
        Assert.Equal("Int", TypeOf(lineage, "DocRank"));
        Assert.DoesNotContain(lineage.Skipped.Entries, e => e.Reason.Contains("FullTextTableReference", StringComparison.Ordinal) || e.Reason.Contains("'KEY'", StringComparison.Ordinal));
    }

    [Fact]
    public void ContainsTable_KeyIsNotABaseColumnSoNoIndexClaimFlowsFromIt()
    {
        var lineage = Resolve("Code");

        Assert.IsNotType<ColumnProvenance.BaseColumn>(lineage.Find("dbo.vw_Ranked")!.FindColumn("DocKey")!.Provenance);
    }

    [Fact]
    public void ContainsTable_WithoutAKnownKeyColumn_LeavesKeyUnknownAndLedgersIt()
    {
        var lineage = Resolve(keyColumn: null);

        Assert.Null(TypeOf(lineage, "DocKey"));
        Assert.Equal("Int", TypeOf(lineage, "DocRank"));
        Assert.Contains(lineage.Skipped.Entries, e => e.Reason.Contains("full-text key", StringComparison.Ordinal));
    }
}
