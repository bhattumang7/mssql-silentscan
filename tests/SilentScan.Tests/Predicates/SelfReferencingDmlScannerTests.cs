using SilentScan.Core.Catalog;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;

namespace SilentScan.Tests.Predicates;

public sealed class SelfReferencingDmlScannerTests
{
    private const string Ddl = """
        CREATE TABLE dbo.T (Id INT NOT NULL PRIMARY KEY, Val INT NOT NULL, Flag BIT NOT NULL);
        CREATE TABLE dbo.Other (Id INT NOT NULL PRIMARY KEY, RefId INT NOT NULL);
        CREATE TABLE dbo.Ck (A INT NOT NULL, B INT NOT NULL, Val INT NOT NULL, PRIMARY KEY (A, B));
        GO
        CREATE VIEW dbo.vT AS SELECT Id, Val, Flag FROM dbo.T;
        GO
        CREATE VIEW dbo.vOther AS SELECT Id, RefId FROM dbo.Other;
        """;

    private static IReadOnlyList<SelfReferencingDmlFinding> Scan(string sql)
    {
        var result = SqlScriptParser.ParseText("test.sql", $"{Ddl}\nGO\n{sql}");
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));

        var catalog = CatalogBuilder.Build([result]);
        var (views, _) = ViewDefinitionExtractor.Extract([result], catalog.DefaultCollation, catalog.TypeAliases, ledger: null);
        var viewExpansionMap = ViewExpansionMap.Build(views, catalog);
        return SelfReferencingDmlScanner.Scan(result, catalog, viewExpansionMap);
    }

    [Fact]
    public void InsertHoleFillingWithNotExists_Fires()
    {
        var findings = Scan("INSERT INTO dbo.T (Id, Val, Flag) SELECT Id + 1000, Val, 0 FROM dbo.T t WHERE NOT EXISTS (SELECT 1 FROM dbo.T t2 WHERE t2.Id = t.Id + 1000);");

        var finding = Assert.Single(findings);
        Assert.Equal(SelfReferencingDmlFindingKind.DirectTableReference, finding.Kind);
        Assert.Equal("INSERT", finding.StatementKind);
        Assert.Equal("dbo.T", finding.TargetTableQualifiedName);
        Assert.Equal("dbo.T", finding.ReadSideQualifiedName);
    }

    [Fact]
    public void InsertFromDifferentTable_NeverFires()
    {
        var findings = Scan("INSERT INTO dbo.T (Id, Val, Flag) SELECT Id + 5000, RefId, 0 FROM dbo.Other;");

        Assert.Empty(findings);
    }

    [Fact]
    public void UpdateFromSelfJoin_Fires()
    {
        var findings = Scan("UPDATE t1 SET t1.Val = t2.Val FROM dbo.T t1 JOIN dbo.T t2 ON t1.Id = t2.Id - 1;");

        var finding = Assert.Single(findings);
        Assert.Equal(SelfReferencingDmlFindingKind.DirectTableReference, finding.Kind);
        Assert.Equal("UPDATE", finding.StatementKind);
        Assert.Equal("dbo.T", finding.TargetTableQualifiedName);
    }

    [Fact]
    public void UpdateFromJoinToDifferentTable_NeverFires()
    {
        var findings = Scan("UPDATE t1 SET t1.Val = o.RefId FROM dbo.T t1 JOIN dbo.Other o ON t1.Id = o.Id;");

        Assert.Empty(findings);
    }

    [Fact]
    public void UpdateNoFromClauseNoSelfReference_NeverFires()
    {
        var findings = Scan("UPDATE dbo.T SET Val = Val + 1 WHERE Flag = 1;");

        Assert.Empty(findings);
    }

    [Fact]
    public void UpdateWhereExistsSelfSubquery_Fires()
    {
        var findings = Scan("UPDATE dbo.T SET Val = Val + 1 WHERE EXISTS (SELECT 1 FROM dbo.T t2 WHERE t2.Id = T.Id - 1);");

        var finding = Assert.Single(findings);
        Assert.Equal("UPDATE", finding.StatementKind);
        Assert.Equal("dbo.T", finding.TargetTableQualifiedName);
    }

    [Fact]
    public void UpdateSetClauseSelfSubquery_Fires()
    {
        var findings = Scan("UPDATE dbo.T SET Val = (SELECT MAX(Val) FROM dbo.T t2 WHERE t2.Id < T.Id) WHERE Flag = 1;");

        Assert.Single(findings);
    }

    [Fact]
    public void UpdateThroughCteNamedLikeARealTable_NeverResolvesTargetAgainstTheRealTable()
    {
        var findings = Scan("WITH T AS (SELECT Id, Val, Flag FROM dbo.T) UPDATE T SET Val = (SELECT MAX(Val) FROM dbo.T);");

        Assert.Empty(findings);
    }

    [Fact]
    public void DeleteWhereExistsSelf_Fires()
    {
        var findings = Scan("DELETE FROM dbo.T WHERE EXISTS (SELECT 1 FROM dbo.T t2 WHERE t2.Id = T.Id - 1);");

        var finding = Assert.Single(findings);
        Assert.Equal(SelfReferencingDmlFindingKind.DirectTableReference, finding.Kind);
        Assert.Equal("DELETE", finding.StatementKind);
    }

    [Fact]
    public void DeleteWhereExistsDifferentTable_NeverFires()
    {
        var findings = Scan("DELETE FROM dbo.T WHERE EXISTS (SELECT 1 FROM dbo.Other o WHERE o.Id = T.Id);");

        Assert.Empty(findings);
    }

    [Fact]
    public void MergeUsingSameTargetTable_Fires()
    {
        var findings = Scan(
            """
            MERGE dbo.T AS tgt
            USING (SELECT Id, Val FROM dbo.T) AS src
            ON tgt.Id = src.Id + 1
            WHEN MATCHED THEN UPDATE SET tgt.Val = src.Val
            WHEN NOT MATCHED BY TARGET THEN INSERT (Id, Val, Flag) VALUES (src.Id + 1, src.Val, 0);
            """);

        var finding = Assert.Single(findings);
        Assert.Equal(SelfReferencingDmlFindingKind.DirectTableReference, finding.Kind);
        Assert.Equal("MERGE", finding.StatementKind);
    }

    [Fact]
    public void MergeUsingDifferentSourceTable_NeverFires()
    {
        var findings = Scan(
            """
            MERGE dbo.T AS tgt
            USING (SELECT Id, RefId AS Val FROM dbo.Other) AS src
            ON tgt.Id = src.Id
            WHEN MATCHED THEN UPDATE SET tgt.Val = src.Val
            WHEN NOT MATCHED BY TARGET THEN INSERT (Id, Val, Flag) VALUES (src.Id, src.Val, 0);
            """);

        Assert.Empty(findings);
    }

    [Fact]
    public void InsertThroughViewOverTargetTable_FiresAsThroughView()
    {
        var findings = Scan("INSERT INTO dbo.T (Id, Val, Flag) SELECT Id + 2000, Val, 0 FROM dbo.vT v WHERE NOT EXISTS (SELECT 1 FROM dbo.vT v2 WHERE v2.Id = v.Id + 2000);");

        var finding = Assert.Single(findings);
        Assert.Equal(SelfReferencingDmlFindingKind.ThroughView, finding.Kind);
        Assert.Equal("dbo.T", finding.TargetTableQualifiedName);
        Assert.Equal("dbo.vT", finding.ReadSideQualifiedName);
    }

    [Fact]
    public void InsertThroughViewOverDifferentTable_NeverFires()
    {
        var findings = Scan("INSERT INTO dbo.T (Id, Val, Flag) SELECT Id + 3000, RefId, 0 FROM dbo.vOther;");

        Assert.Empty(findings);
    }

    [Fact]
    public void OneFindingPerStatement_NotOnePerMatchingReference()
    {
        var findings = Scan(
            "UPDATE t1 SET t1.Val = t2.Val FROM dbo.T t1 JOIN dbo.T t2 ON t1.Id = t2.Id - 1 WHERE EXISTS (SELECT 1 FROM dbo.T t3 WHERE t3.Id = t1.Id - 2);");

        Assert.Single(findings);
    }

    [Fact]
    public void UpdateFromSelfJoinWithLiteralTopOne_NeverFires()
    {
        var findings = Scan("UPDATE TOP (1) t1 SET t1.Val = t2.Val FROM dbo.T t1 JOIN dbo.T t2 ON t1.Id = t2.Id - 1;");

        Assert.Empty(findings);
    }

    [Fact]
    public void UpdateFromSelfJoinWithTopTwo_StillFires()
    {
        var findings = Scan("UPDATE TOP (2) t1 SET t1.Val = t2.Val FROM dbo.T t1 JOIN dbo.T t2 ON t1.Id = t2.Id - 1;");

        Assert.Single(findings);
    }

    [Fact]
    public void UpdateFromSelfJoinWithTopOnePercent_StillFires()
    {
        var findings = Scan("UPDATE TOP (1) PERCENT t1 SET t1.Val = t2.Val FROM dbo.T t1 JOIN dbo.T t2 ON t1.Id = t2.Id - 1;");

        Assert.Single(findings);
    }

    [Fact]
    public void DeleteWithLiteralTopOne_NeverFires()
    {
        var findings = Scan("DELETE TOP (1) FROM dbo.T WHERE EXISTS (SELECT 1 FROM dbo.T t2 WHERE t2.Id = T.Id - 1);");

        Assert.Empty(findings);
    }

    [Fact]
    public void InsertSelectWithLiteralTopOne_NeverFires()
    {
        var findings = Scan("INSERT TOP (1) INTO dbo.T (Id, Val, Flag) SELECT Id + 1000, Val, 0 FROM dbo.T t WHERE NOT EXISTS (SELECT 1 FROM dbo.T t2 WHERE t2.Id = t.Id + 1000);");

        Assert.Empty(findings);
    }

    [Theory]
    [InlineData("INSERT INTO dbo.T (Id, Val, Flag) SELECT MAX(Id) + 1, 0, 0 FROM dbo.T;")]
    [InlineData("INSERT INTO dbo.T (Id, Val, Flag) SELECT 9999, 0, 0 WHERE NOT EXISTS (SELECT 1 FROM dbo.T WHERE Id = 9999);")]
    [InlineData("INSERT INTO dbo.T (Id, Val, Flag) SELECT (SELECT MAX(Id) + 1 FROM dbo.T), 0, 0;")]
    [InlineData("INSERT INTO dbo.T (Id, Val, Flag) SELECT COUNT(*) + 5000, 0, 0 FROM dbo.T HAVING COUNT(*) > 0;")]
    public void InsertWhoseSourceProducesAtMostOneRow_NeverFires(string sql)
    {
        Assert.Empty(Scan(sql));
    }

    [Theory]
    [InlineData("INSERT INTO dbo.T (Id, Val, Flag) SELECT MAX(Id) + 1, 0, 0 FROM dbo.T GROUP BY Val;")]
    [InlineData("INSERT INTO dbo.T (Id, Val, Flag) SELECT COUNT(*) OVER () + 5000, 0, 0 FROM dbo.T;")]
    [InlineData("INSERT INTO dbo.T (Id, Val, Flag) SELECT Id + 5000, 0, 0 FROM dbo.T;")]
    public void InsertWhoseSourceCanProduceManyRows_StillFires(string sql)
    {
        Assert.Single(Scan(sql));
    }

    [Theory]
    [InlineData("UPDATE dbo.T SET Val = (SELECT MAX(Val) FROM dbo.T) WHERE Id = 5;")]
    [InlineData("DECLARE @i INT = 5; UPDATE dbo.T SET Val = Val + 1 WHERE Id = @i AND EXISTS (SELECT 1 FROM dbo.T t2 WHERE t2.Val > 3);")]
    [InlineData("UPDATE dbo.T SET Val = 1 WHERE 5 = T.Id AND EXISTS (SELECT 1 FROM dbo.T t2 WHERE t2.Val > 3);")]
    [InlineData("DELETE FROM dbo.T WHERE Id = 5 AND EXISTS (SELECT 1 FROM dbo.T t2 WHERE t2.Val > 3);")]
    [InlineData("UPDATE dbo.Ck SET Val = (SELECT MAX(Val) FROM dbo.Ck) WHERE A = 1 AND B = (SELECT MAX(c3.B) FROM dbo.Ck c3 WHERE c3.A = 1);")]
    [InlineData("INSERT INTO dbo.T (Id, Val, Flag) SELECT 7000, o.RefId, 0 FROM dbo.Other o JOIN dbo.T t ON t.Id = o.RefId WHERE o.Id = 1;")]
    [InlineData("INSERT INTO dbo.T (Id, Val, Flag) SELECT 7000, o.RefId, 0 FROM dbo.Other o LEFT JOIN dbo.T t ON t.Id = o.RefId WHERE o.Id = 1;")]
    [InlineData("INSERT INTO dbo.T (Id, Val, Flag) SELECT t.Id + 7000, t.Val, 0 FROM dbo.T t WHERE t.Id = 5 AND EXISTS (SELECT 1 FROM dbo.T t2 WHERE t2.Val = t.Val);")]
    [InlineData("INSERT INTO dbo.T (Id, Val, Flag) SELECT t.Id + 7000, t.Val, 0 FROM dbo.T t WITH (FORCESCAN) WHERE t.Id = 5 AND EXISTS (SELECT 1 FROM dbo.T t2 WHERE t2.Val = t.Val);")]
    public void StatementPinnedToOneRowByAUniqueKey_NeverFires(string sql)
    {
        Assert.Empty(Scan(sql));
    }

    [Theory]
    [InlineData("UPDATE dbo.T SET Val = (SELECT MAX(Val) FROM dbo.T) WHERE Id > 5;")]
    [InlineData("UPDATE dbo.T SET Val = (SELECT MAX(Val) FROM dbo.T) WHERE Id = 5 OR Flag = 1;")]
    [InlineData("UPDATE dbo.T SET Val = (SELECT MAX(Val) FROM dbo.T) WHERE Id = Val;")]
    [InlineData("UPDATE dbo.T SET Val = (SELECT MAX(Val) FROM dbo.T) WHERE Id = (SELECT MAX(t2.Id) FROM dbo.T t2 WHERE t2.Val = T.Val);")]
    [InlineData("UPDATE dbo.T SET Val = (SELECT MAX(Val) FROM dbo.T) WHERE Id = (SELECT MAX(Id) FROM dbo.T t2 WHERE Val = T.Val);")]
    [InlineData("UPDATE dbo.Ck SET Val = (SELECT MAX(Val) FROM dbo.Ck) WHERE A = 1;")]
    [InlineData("UPDATE t1 SET t1.Val = t2.Val FROM dbo.T t1 JOIN dbo.T t2 ON t1.Id = t2.Id - 1 WHERE t1.Id = 5;")]
    [InlineData("DELETE FROM dbo.T WHERE Id > 5 AND EXISTS (SELECT 1 FROM dbo.T t2 WHERE t2.Val > 3);")]
    [InlineData("INSERT INTO dbo.T (Id, Val, Flag) SELECT 7000, o.RefId, 0 FROM dbo.Other o JOIN dbo.T t ON t.Val = o.RefId WHERE o.Id = 1;")]
    [InlineData("INSERT INTO dbo.T (Id, Val, Flag) SELECT 7000, o.RefId, 0 FROM dbo.Other o JOIN dbo.T t ON t.Id = o.RefId WHERE o.RefId = 1;")]
    [InlineData("INSERT INTO dbo.T (Id, Val, Flag) SELECT t.Id + 7000, t.Val, 0 FROM dbo.T t WHERE t.Val = 5 AND EXISTS (SELECT 1 FROM dbo.T t2 WHERE t2.Val = t.Val);")]
    public void StatementNotPinnedToOneRowByAUniqueKey_StillFires(string sql)
    {
        Assert.Single(Scan(sql));
    }

    [Fact]
    public void UpdateAssigningScalarAggregateOverTheTarget_StillFires()
    {
        Assert.Single(Scan("UPDATE dbo.T SET Val = (SELECT MAX(Val) FROM dbo.T);"));
    }

    [Fact]
    public void MergeWithLiteralTopOne_NeverFires()
    {
        var findings = Scan(
            """
            MERGE TOP (1) dbo.T AS tgt
            USING (SELECT Id, Val FROM dbo.T) AS src
            ON tgt.Id = src.Id + 1
            WHEN MATCHED THEN UPDATE SET tgt.Val = src.Val
            WHEN NOT MATCHED BY TARGET THEN INSERT (Id, Val, Flag) VALUES (src.Id + 1, src.Val, 0);
            """);

        Assert.Empty(findings);
    }

    [Fact]
    public void ReadSideReferenceIsACteSharingTheTargetsOwnBareName_NeverFires()
    {
        var findings = Scan(
            """
            ;WITH T AS (SELECT Id FROM dbo.Other)
            UPDATE dbo.T SET Val = (SELECT COUNT(*) FROM T);
            """);

        Assert.Empty(findings);
    }
}
