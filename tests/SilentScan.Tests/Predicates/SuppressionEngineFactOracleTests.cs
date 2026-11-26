using System.Xml.Linq;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.LocalVariablePredicateRuleId)]
[Trait("Rule", FindingRuleIds.CatchAllPredicateRuleId)]
[Trait("Rule", FindingRuleIds.FilteredIndexParameterMismatchRuleId)]
[Trait("Rule", FindingRuleIds.IndexDesignHeapWithNonclusteredIndexesRuleId)]
[Trait("Rule", FindingRuleIds.ControlFlowRiskReadCommittedLockRevertsRowVersioningRuleId)]
public sealed class SuppressionEngineFactOracleTests : OracleTestFixture
{
    private const int InvalidObjectName = 208;

    protected override string DatabaseNameSeed => "SilentScanSuppressionFacts";

    protected override string Ddl => """
        CREATE TABLE dbo.Skewed (Id INT NOT NULL, V INT NOT NULL, Pad CHAR(100) NOT NULL DEFAULT '', INDEX CX CLUSTERED (V));
        CREATE TABLE dbo.Sink (Id INT NOT NULL);
        CREATE TABLE dbo.HeapOnly (Id INT NOT NULL, A INT NOT NULL);
        GO
        INSERT INTO dbo.Skewed (Id, V)
        SELECT n, CASE WHEN n <= 9990 THEN 1 ELSE n END
        FROM (SELECT TOP (10000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n FROM sys.all_columns AS a CROSS JOIN sys.all_columns AS b) AS numbers;
        UPDATE STATISTICS dbo.Skewed WITH FULLSCAN;
        CREATE NONCLUSTERED INDEX IX_HeapOnly_A ON dbo.HeapOnly (A) WITH STATISTICS_ONLY = -1;
        GO
        """;

    [Fact]
    public async Task RecompileOnAnInsertSelect_EstimatesFromTheLocalVariablesValue()
    {
        const string Insert = "DECLARE @v INT = 1; INSERT INTO dbo.Sink (Id) SELECT Id FROM dbo.Skewed WHERE V = @v";

        var recompiled = await EstimatedSeekRowsAsync(Insert + " OPTION (RECOMPILE);");
        var guessed = await EstimatedSeekRowsAsync(Insert + ";");

        Assert.InRange(recompiled, 9000, 10000);
        Assert.InRange(guessed, 0, 2000);
    }

    [Fact]
    public async Task AHypotheticalIndex_HasNoStorageForRowsToPointFrom()
    {
        Assert.Equal(1, await ScalarAsync<int>("SELECT CAST(is_hypothetical AS INT) FROM sys.indexes WHERE name = N'IX_HeapOnly_A';"));
        Assert.Equal(0, await ScalarAsync<int>("""
            SELECT COUNT(*) FROM sys.dm_db_partition_stats AS p
            JOIN sys.indexes AS i ON i.object_id = p.object_id AND i.index_id = p.index_id
            WHERE i.name = N'IX_HeapOnly_A';
            """));
    }

    [Fact]
    public async Task ALocalTemporaryTable_IsInvisibleToEveryOtherSession()
    {
        await using var owner = await OpenConnectionAsync();
        await ExecuteAsync(owner, "CREATE TABLE #SessionOnly (Id INT NOT NULL);");

        Assert.Null(await SqlErrorNumberAsync(owner, "SELECT Id FROM #SessionOnly WITH (READCOMMITTEDLOCK);"));
        Assert.Equal(InvalidObjectName, await SqlErrorNumberAsync("UPDATE #SessionOnly SET Id = 1;"));
    }

    private async Task<double> EstimatedSeekRowsAsync(string batch)
    {
        var plan = XDocument.Parse(await CaptureActualPlanAsync(batch));
        XNamespace showplan = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";
        return plan.Descendants(showplan + "RelOp")
            .Where(operation => operation.Elements().Elements(showplan + "Object").Any(o => (string?)o.Attribute("Table") == "[Skewed]"))
            .Select(operation => double.Parse((string)operation.Attribute("EstimateRows")!, System.Globalization.CultureInfo.InvariantCulture))
            .Single();
    }
}
