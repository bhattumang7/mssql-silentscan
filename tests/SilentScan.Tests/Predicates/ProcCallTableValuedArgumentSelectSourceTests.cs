using SilentScan.Core.Catalog;
using SilentScan.Core.Diagnostics;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;

namespace SilentScan.Tests.Predicates;

public sealed class ProcCallTableValuedArgumentSelectSourceTests
{
    private static (IReadOnlyList<ProcCallTableValuedArgumentMismatchFinding> Findings, SkipLedger Ledger) Scan(string populate)
    {
        var sql = $"""
            CREATE TABLE dbo.Src (Amount DECIMAL(10,4) NOT NULL, Small DECIMAL(8,2) NOT NULL);
            GO
            CREATE TYPE dbo.tt_Amounts AS TABLE (Amount DECIMAL(10,2) NOT NULL);
            GO
            CREATE PROCEDURE dbo.usp_ApplyAmounts @Amounts dbo.tt_Amounts READONLY
            AS
            BEGIN
                SELECT Amount FROM @Amounts;
            END
            GO
            CREATE PROCEDURE dbo.usp_Caller
            AS
            BEGIN
                DECLARE @rows dbo.tt_Amounts;
                {populate}
                EXEC dbo.usp_ApplyAmounts @Amounts = @rows;
            END
            """;
        var result = SqlScriptParser.ParseText("tvp.sql", sql);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        var catalog = CatalogBuilder.Build([result]);
        var ledger = new SkipLedger();
        var graph = ProcCallGraphBuilder.Build([result], catalog, ledger);
        return (ProcCallTableValuedArgumentMismatchScanner.Scan(result, graph, catalog, ledger), ledger);
    }

    [Theory]
    [InlineData("INSERT INTO @rows (Amount) SELECT s.Amount FROM dbo.Src AS s;")]
    [InlineData("INSERT INTO @rows SELECT Amount FROM dbo.Src;")]
    [InlineData("INSERT INTO @rows (Amount) (SELECT s.Amount FROM dbo.Src AS s WHERE s.Small > 1);")]
    public void InsertSelectIntoTvpWithNarrowerScale_IsFlagged(string populate)
    {
        var (findings, ledger) = Scan(populate);

        var finding = Assert.Single(findings);
        Assert.Equal("Amount", finding.ColumnName);
        Assert.Equal(WriteLossKind.NumericScaleNarrowing, finding.Kind);
        Assert.DoesNotContain(ledger.Entries, e => e.ConstructKind == "table-valued parameter population");
    }

    [Fact]
    public void InsertSelectIntoTvpWhereTheColumnFits_FlagsNothing()
    {
        var (findings, ledger) = Scan("INSERT INTO @rows (Amount) SELECT s.Small FROM dbo.Src AS s;");

        Assert.Empty(findings);
        Assert.DoesNotContain(ledger.Entries, e => e.ConstructKind == "table-valued parameter population");
    }
}
