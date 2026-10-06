using SilentScan.Tests.Support;
using SilentScan.Verify.Oracle;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", "silentscan/dml/self-referencing")]
public sealed class SelfReferencingDmlOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(SelfReferencingDmlOracleTests);

    protected override string Ddl => """
        CREATE TABLE dbo.T (Id INT NOT NULL PRIMARY KEY, Val INT NOT NULL, Flag BIT NOT NULL DEFAULT 0);
        GO
        CREATE TABLE dbo.Other (Id INT NOT NULL PRIMARY KEY, RefId INT NOT NULL);
        GO
        CREATE VIEW dbo.vT AS SELECT Id, Val, Flag FROM dbo.T;
        GO
        CREATE TABLE dbo.Hp (Id INT NOT NULL, Val INT NOT NULL);
        GO
        """;

    private static void AssertNoProtectiveOperator(string planXml)
    {
        Assert.DoesNotContain("Eager Spool", planXml);
        Assert.DoesNotContain("PhysicalOp=\"Sort\"", planXml);
    }

    private Task<string> CaptureAsync(string probe) => new PlanXmlCapture(Options).CaptureAsync(DatabaseName, probe);

    [Fact]
    public async Task InsertHoleFillingSelfReference_GainsAnEagerSpool()
    {
        var planXml = await CaptureAsync(
            """
            INSERT INTO dbo.T (Id, Val, Flag)
            SELECT Id + 1000, Val, 0 FROM dbo.T WHERE NOT EXISTS (SELECT 1 FROM dbo.T t2 WHERE t2.Id = dbo.T.Id + 1000);
            """);

        Assert.Contains("LogicalOp=\"Eager Spool\"", planXml);
    }

    [Theory]
    [InlineData("INSERT INTO dbo.T (Id, Val, Flag) SELECT MAX(Id) + 1, 0, 0 FROM dbo.T;")]
    [InlineData("INSERT INTO dbo.Hp (Id, Val) SELECT MAX(Id) + 1, 0 FROM dbo.Hp;")]
    [InlineData("INSERT INTO dbo.T (Id, Val, Flag) SELECT 9999, 0, 0 WHERE NOT EXISTS (SELECT 1 FROM dbo.T WHERE Id = 9999);")]
    [InlineData("INSERT INTO dbo.Hp (Id, Val) SELECT 9999, 0 WHERE NOT EXISTS (SELECT 1 FROM dbo.Hp WHERE Val = 5);")]
    [InlineData("INSERT INTO dbo.Hp (Id, Val) SELECT (SELECT MAX(Id) + 1 FROM dbo.Hp), 0;")]
    public async Task InsertWhoseSourceProducesAtMostOneRow_NeverGainsAProtectiveOperator(string probe)
    {
        AssertNoProtectiveOperator(await CaptureAsync(probe));
    }

    [Theory]
    [InlineData("INSERT INTO dbo.T (Id, Val, Flag) SELECT Id + 1000, Val, 0 FROM dbo.T;")]
    [InlineData("INSERT INTO dbo.Hp (Id, Val) SELECT Id + 1000, Val FROM dbo.Hp;")]
    public async Task InsertWhoseSourceCanProduceManyRows_GainsAnEagerSpool(string probe)
    {
        Assert.Contains("LogicalOp=\"Eager Spool\"", await CaptureAsync(probe));
    }

    [Fact]
    public async Task HeapInsertReadingTargetThroughAHashBuild_NeedsNoSpoolOrSort_WhileTheNotExistsFormGetsOne()
    {
        await ExecuteAsync(
            """
            INSERT INTO dbo.Hp (Id, Val) SELECT TOP (5000) ROW_NUMBER() OVER (ORDER BY (SELECT 1)), 1 FROM sys.all_objects a CROSS JOIN sys.all_objects b;
            INSERT INTO dbo.Other (Id, RefId) SELECT Id, Val FROM dbo.Hp;
            """);

        var hashBuild = await CaptureAsync(
            "INSERT INTO dbo.Hp (Id, Val) SELECT o.Id + 10000, o.RefId FROM dbo.Other o WHERE EXISTS (SELECT 1 FROM dbo.Hp h WHERE h.Id = o.Id);");
        var notExists = await CaptureAsync(
            "INSERT INTO dbo.Hp (Id, Val) SELECT h.Id + 10000, h.Val FROM dbo.Hp h WHERE NOT EXISTS (SELECT 1 FROM dbo.Hp x WHERE x.Id = h.Id + 10000);");

        Assert.Contains("PhysicalOp=\"Hash Match\"", hashBuild);
        AssertNoProtectiveOperator(hashBuild);
        Assert.Contains("LogicalOp=\"Eager Spool\"", notExists);
    }

    [Fact]
    public async Task InsertFromDifferentTable_NeverGainsAnEagerSpool()
    {
        var planXml = await CaptureAsync(
            "INSERT INTO dbo.T (Id, Val, Flag) SELECT Id + 5000, RefId, 0 FROM dbo.Other;");

        Assert.DoesNotContain("LogicalOp=\"Eager Spool\"", planXml);
    }

    [Fact]
    public async Task InsertReadingThroughAViewOverTheSameBaseTable_StillGainsAnEagerSpool()
    {
        var planXml = await CaptureAsync(
            """
            INSERT INTO dbo.T (Id, Val, Flag)
            SELECT Id + 2000, Val, 0 FROM dbo.vT WHERE NOT EXISTS (SELECT 1 FROM dbo.vT v2 WHERE v2.Id = dbo.vT.Id + 2000);
            """);

        Assert.Contains("LogicalOp=\"Eager Spool\"", planXml);
    }

    [Fact]
    public async Task UpdateFromSelfJoin_GainsAProtectiveSort_NoSpool()
    {
        var planXml = await CaptureAsync(
            "UPDATE t1 SET t1.Val = t2.Val FROM dbo.T t1 JOIN dbo.T t2 ON t1.Id = t2.Id - 1;");

        Assert.Contains("LogicalOp=\"Distinct Sort\"", planXml);
        Assert.DoesNotContain("Eager Spool", planXml);
    }

    [Fact]
    public async Task UpdateFromJoinToDifferentTable_NeverGainsTheProtectiveSort()
    {
        var planXml = await CaptureAsync(
            "UPDATE t1 SET t1.Val = o.RefId FROM dbo.T t1 JOIN dbo.Other o ON t1.Id = o.Id;");

        Assert.DoesNotContain("Distinct Sort", planXml);
        Assert.DoesNotContain("Eager Spool", planXml);
    }

    [Fact]
    public async Task UpdateWithNoFromClauseAndNoSelfReference_NeverGainsAnyProtectiveOperator()
    {
        var planXml = await CaptureAsync("UPDATE dbo.T SET Val = Val + 1 WHERE Flag = 1;");

        Assert.DoesNotContain("PhysicalOp=\"Sort\"", planXml);
        Assert.DoesNotContain("Eager Spool", planXml);
    }

    [Fact]
    public async Task DeleteWhereExistsSelfReference_GainsAnEagerSpool()
    {
        var planXml = await CaptureAsync(
            "DELETE FROM dbo.T WHERE EXISTS (SELECT 1 FROM dbo.T t2 WHERE t2.Id = T.Id - 1);");

        Assert.Contains("LogicalOp=\"Eager Spool\"", planXml);
    }

    [Fact]
    public async Task DeleteWhereExistsDifferentTable_NeverGainsAnEagerSpool()
    {
        var planXml = await CaptureAsync(
            "DELETE FROM dbo.T WHERE EXISTS (SELECT 1 FROM dbo.Other o WHERE o.Id = T.Id);");

        Assert.DoesNotContain("Eager Spool", planXml);
    }

    [Fact]
    public async Task MergeUsingSameTargetTable_GainsAProtectiveSort_NoSpool()
    {
        var planXml = await CaptureAsync(
            """
            MERGE dbo.T AS tgt
            USING (SELECT Id, Val FROM dbo.T) AS src
            ON tgt.Id = src.Id + 1
            WHEN MATCHED THEN UPDATE SET tgt.Val = src.Val
            WHEN NOT MATCHED BY TARGET THEN INSERT (Id, Val, Flag) VALUES (src.Id + 1, src.Val, 0);
            """);

        Assert.Contains("PhysicalOp=\"Sort\"", planXml);
        Assert.DoesNotContain("Eager Spool", planXml);
    }

    [Fact]
    public async Task MergeUsingDifferentSourceTable_NeverGainsTheProtectiveSort()
    {
        var planXml = await CaptureAsync(
            """
            MERGE dbo.T AS tgt
            USING (SELECT Id, RefId AS Val FROM dbo.Other) AS src
            ON tgt.Id = src.Id
            WHEN MATCHED THEN UPDATE SET tgt.Val = src.Val
            WHEN NOT MATCHED BY TARGET THEN INSERT (Id, Val, Flag) VALUES (src.Id, src.Val, 0);
            """);

        Assert.DoesNotContain("PhysicalOp=\"Sort\"", planXml);
        Assert.DoesNotContain("Eager Spool", planXml);
    }

    [Fact]
    public async Task UpdateFromSelfJoinWithLiteralTopOne_NeverGainsAnyProtectiveOperator()
    {
        var planXml = await CaptureAsync(
            "UPDATE TOP (1) t1 SET t1.Val = t2.Val FROM dbo.T t1 JOIN dbo.T t2 ON t1.Id = t2.Id - 1;");

        Assert.DoesNotContain("Eager Spool", planXml);
        Assert.DoesNotContain("Distinct Sort", planXml);
    }

    [Fact]
    public async Task UpdateFromSelfJoinWithTopTwo_StillGainsTheProtectiveSort()
    {
        var planXml = await CaptureAsync(
            "UPDATE TOP (2) t1 SET t1.Val = t2.Val FROM dbo.T t1 JOIN dbo.T t2 ON t1.Id = t2.Id - 1;");

        Assert.Contains("LogicalOp=\"Distinct Sort\"", planXml);
    }

    [Fact]
    public async Task DeleteWhereExistsSelfReferenceWithLiteralTopOne_NeverGainsAnEagerSpool()
    {
        var planXml = await CaptureAsync(
            "DELETE TOP (1) FROM dbo.T WHERE EXISTS (SELECT 1 FROM dbo.T t2 WHERE t2.Id = T.Id - 1);");

        Assert.DoesNotContain("Eager Spool", planXml);
    }

    [Fact]
    public async Task InsertHoleFillingSelfReferenceWithLiteralTopOne_NeverGainsAnEagerSpool()
    {
        var planXml = await CaptureAsync(
            """
            INSERT TOP (1) INTO dbo.T (Id, Val, Flag)
            SELECT Id + 1000, Val, 0 FROM dbo.T WHERE NOT EXISTS (SELECT 1 FROM dbo.T t2 WHERE t2.Id = dbo.T.Id + 1000);
            """);

        Assert.DoesNotContain("Eager Spool", planXml);
    }

    [Fact]
    public async Task MergeUsingSameTargetTableWithLiteralTopOne_NeverGainsTheProtectiveSort()
    {
        var planXml = await CaptureAsync(
            """
            MERGE TOP (1) dbo.T AS tgt
            USING (SELECT Id, Val FROM dbo.T) AS src
            ON tgt.Id = src.Id + 1
            WHEN MATCHED THEN UPDATE SET tgt.Val = src.Val
            WHEN NOT MATCHED BY TARGET THEN INSERT (Id, Val, Flag) VALUES (src.Id + 1, src.Val, 0);
            """);

        Assert.DoesNotContain("PhysicalOp=\"Sort\"", planXml);
        Assert.DoesNotContain("Eager Spool", planXml);
    }
}
