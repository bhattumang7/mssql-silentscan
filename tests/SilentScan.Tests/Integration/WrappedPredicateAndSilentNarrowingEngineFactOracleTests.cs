using SilentScan.Tests.Support;
using SilentScan.Verify;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class WrappedPredicateAndSilentNarrowingEngineFactOracleTests : OracleTestFixture
{
    private const int NoPlanForTheForcedSeek = 8622;

    private const int WrongSessionOptions = 1934;

    protected override SqlServerOptions Options => SqlServerOptions.LocalDockerLatest;

    protected override string DatabaseNameSeed => nameof(WrappedPredicateAndSilentNarrowingEngineFactOracleTests);

    protected override string Ddl => """
        CREATE TABLE dbo.N (Id INT IDENTITY PRIMARY KEY, C VARCHAR(20) NULL, K INT NOT NULL, Pad CHAR(100) NOT NULL DEFAULT '');
        CREATE INDEX IC ON dbo.N (C);
        CREATE INDEX IK ON dbo.N (K);
        GO
        CREATE TABLE dbo.C (Id INT IDENTITY PRIMARY KEY NONCLUSTERED, B INT NOT NULL, D DATETIME NOT NULL, Day1 AS CAST(D AS DATE) PERSISTED);
        CREATE INDEX ICB ON dbo.C (Day1, B);
        GO
        CREATE TABLE dbo.Long4001 (C VARCHAR(4001) COLLATE Latin1_General_CI_AS NOT NULL, INDEX IL (C));
        CREATE TABLE dbo.Long4000 (C VARCHAR(4000) COLLATE Latin1_General_CI_AS NOT NULL, INDEX IL (C));
        GO
        CREATE TABLE dbo.S (Id INT NOT NULL PRIMARY KEY, G INT NOT NULL, St INT NOT NULL);
        CREATE INDEX FX ON dbo.S (G) WHERE St = 1;
        GO
        CREATE VIEW dbo.IV WITH SCHEMABINDING AS SELECT G, COUNT_BIG(*) AS N FROM dbo.S GROUP BY G;
        GO
        CREATE UNIQUE CLUSTERED INDEX CI ON dbo.IV (G);
        GO
        CREATE TABLE dbo.E (Id INT IDENTITY PRIMARY KEY, G INT NOT NULL, Pad CHAR(100) NOT NULL DEFAULT '');
        INSERT INTO dbo.E (G) SELECT TOP (20000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) % 100 FROM sys.all_columns a CROSS JOIN sys.all_columns b;
        CREATE INDEX IG ON dbo.E (G);
        UPDATE STATISTICS dbo.E WITH FULLSCAN;
        GO
        CREATE TABLE dbo.F (Id INT IDENTITY PRIMARY KEY, G INT NOT NULL, St INT NOT NULL);
        CREATE INDEX FR ON dbo.F (G) WHERE St > 97;
        GO
        CREATE TABLE dbo.NoRowLocks (Id INT NOT NULL, CONSTRAINT PK_NoRowLocks PRIMARY KEY (Id) WITH (ALLOW_ROW_LOCKS = OFF));
        CREATE TABLE dbo.NoPageLocks (Id INT NOT NULL, CONSTRAINT PK_NoPageLocks PRIMARY KEY (Id) WITH (ALLOW_PAGE_LOCKS = OFF));
        CREATE TABLE dbo.NoLocks (Id INT NOT NULL, CONSTRAINT PK_NoLocks PRIMARY KEY (Id) WITH (ALLOW_ROW_LOCKS = OFF, ALLOW_PAGE_LOCKS = OFF));
        INSERT INTO dbo.NoRowLocks VALUES (1);
        INSERT INTO dbo.NoPageLocks VALUES (1);
        INSERT INTO dbo.NoLocks VALUES (1);
        GO
        CREATE TYPE dbo.Rows AS TABLE (V VARCHAR(3));
        GO
        CREATE PROCEDURE dbo.Wide AS SELECT CAST('abcdef' AS VARCHAR(10)) AS V;
        GO
        CREATE FUNCTION dbo.Rounded (@x DATETIME2(7)) RETURNS DATETIME2(0) AS BEGIN RETURN @x; END;
        GO
        CREATE PROCEDURE dbo.Defaulted @p DATETIME2(0) = '2024-12-31 23:59:59.9999999' AS SELECT CONVERT(VARCHAR(30), @p, 121);
        GO
        """;

    private async Task AssertForcedSeekAsync(string query, bool seekable)
    {
        Assert.Equal(seekable ? null : NoPlanForTheForcedSeek, await SqlErrorNumberAsync(query));
    }

    [Theory]
    [Trait("Rule", FindingRuleIds.Tier1FunctionWrappedColumnRuleId)]
    [InlineData("UPPER(LTRIM(C)) = 'V5'", false)]
    [InlineData("ISNULL(COALESCE(C, 'a'), 'b') = 'v5'", false)]
    [InlineData("C = LTRIM('v5')", true)]
    public Task AColumnUnderTwoFunctionsCannotSeek(string predicate, bool seekable) =>
        AssertForcedSeekAsync($"SELECT Id FROM dbo.N WITH (FORCESEEK, INDEX(IC)) WHERE {predicate};", seekable);

    [Theory]
    [Trait("Rule", FindingRuleIds.IndexHintHintedIndexNotSeekableRuleId)]
    [InlineData("ABS(K) = 5", false)]
    [InlineData("K + 1 = 5", false)]
    [InlineData("COALESCE(K, 0) = 5", false)]
    [InlineData("K <> 5", true)]
    [InlineData("ISNULL(K, 0) = 5", true)]
    public Task ALeadingColumnReachedOnlyThroughAWrapperCannotSeek(string predicate, bool seekable) =>
        AssertForcedSeekAsync($"SELECT Id FROM dbo.N WITH (FORCESEEK, INDEX(IK)) WHERE {predicate};", seekable);

    [Theory]
    [Trait("Rule", FindingRuleIds.ExpressionDerivedRuleId)]
    [InlineData("CAST(CAST(K AS BIGINT) AS BIGINT)", true)]
    [InlineData("CAST(CAST(K AS BIGINT) AS DECIMAL(18,0))", false)]
    public Task ARepeatedIdenticalCastKeepsTheSeek(string expression, bool seekable) =>
        AssertForcedSeekAsync($"SELECT Id FROM dbo.N WITH (FORCESEEK, INDEX(IK)) WHERE {expression} = 5;", seekable);

    [Theory]
    [Trait("Rule", FindingRuleIds.CompositeIndexLeadingColumnRuleId)]
    [InlineData("CAST(D AS DATE) = '20200105' AND B = 100", true)]
    [InlineData("D = '20200105' AND B = 100", false)]
    public Task AComputedLeadingColumnIsReachedThroughItsDefinition(string predicate, bool seekable) =>
        AssertForcedSeekAsync($"SELECT Id FROM dbo.C WITH (FORCESEEK, INDEX(ICB)) WHERE {predicate};", seekable);

    [Theory]
    [Trait("Rule", FindingRuleIds.VerdictScanForcedRuleId)]
    [InlineData("dbo.Long4001", false)]
    [InlineData("dbo.Long4000", true)]
    public Task AVarcharColumnLongerThanTheUnicodeLimitCannotSeekAnyUnicodeValue(string table, bool seekable) =>
        AssertForcedSeekAsync($"SELECT C FROM {table} WITH (FORCESEEK) WHERE C = N'v5';", seekable);

    [Theory]
    [Trait("Rule", FindingRuleIds.SetOptionAnsiWarningsOffBlocksIndexedFeatureRuleId)]
    [InlineData("SELECT G FROM dbo.IV WITH (NOEXPAND) WHERE G = 5;", true)]
    [InlineData("SELECT G FROM dbo.S WITH (INDEX(FX)) WHERE St = 1 AND G = 0;", true)]
    [InlineData("SELECT G FROM dbo.IV WHERE G = 5;", false)]
    [InlineData("SELECT G FROM dbo.S WHERE St = 1 AND G = 0;", false)]
    public async Task OnlyAHintedReferenceRaisesUnderAnsiWarningsOff(string query, bool raises)
    {
        Assert.Equal(raises ? WrongSessionOptions : null, await SqlErrorNumberAsync($"SET ANSI_WARNINGS OFF; {query}"));
    }

    [Fact]
    [Trait("Rule", FindingRuleIds.ProcCallTableValuedArgumentMismatchRuleId)]
    public async Task AnsiWarningsOffTruncatesIntoATableTypeVariable()
    {
        Assert.Equal("abc", await ScalarAsync<string>(
            "SET ANSI_WARNINGS OFF; DECLARE @r dbo.Rows; INSERT @r VALUES ('abcdef'); SELECT V FROM @r;"));
    }

    [Fact]
    [Trait("Rule", FindingRuleIds.TempTableExecShapeColumnTypeMismatchRuleId)]
    public async Task AnsiWarningsOffTruncatesAnInsertExecIntoATempTable()
    {
        Assert.Equal("abc", await ScalarAsync<string>(
            "CREATE TABLE #t (V VARCHAR(3)); SET ANSI_WARNINGS OFF; INSERT #t EXEC dbo.Wide; SELECT V FROM #t;"));
    }

    [Theory]
    [Trait("Rule", FindingRuleIds.WriteLossTemporalScaleNarrowingRuleId)]
    [InlineData("SELECT CONVERT(VARCHAR(30), dbo.Rounded('2024-12-31 23:59:59.9999999'), 121);")]
    [InlineData("EXEC dbo.Defaulted;")]
    public async Task AReturnValueAndAParameterDefaultRoundToTheDeclaredScale(string query)
    {
        Assert.Equal("2025-01-01 00:00:00", await ScalarAsync<string>(query));
    }

    [Theory]
    [Trait("Rule", FindingRuleIds.Tier1CharindexOrLeftOnColumnRuleId)]
    [InlineData("CHARINDEX(N'50%', N'500') = 1", "N'500' LIKE N'50%%'")]
    [InlineData("N'500' LIKE N'50[%]%'", "N'500' LIKE N'50%%'")]
    [InlineData("LEFT(N'ABC', 2) = N'AB '", "N'ABC' LIKE N'AB %'")]
    [InlineData("CHARINDEX(N'', N'abc') = 1", "N'abc' LIKE N'%'")]
    public async Task APrefixTestAndItsUnescapedLikeFormDisagree(string prefixTest, string likeForm)
    {
        Assert.NotEqual(await TruthAsync(prefixTest), await TruthAsync(likeForm));
    }

    private Task<int> TruthAsync(string predicate) => ScalarAsync<int>($"SELECT IIF({predicate}, 1, 0);");

    [Theory]
    [Trait("Rule", FindingRuleIds.LocalVariablePredicateRuleId)]
    [InlineData("G = @v", 200)]
    [InlineData("G > @v", 6000)]
    [InlineData("G <= @v", 6000)]
    public async Task ALocalVariableGetsTheDensityForEqualityAndAFixedGuessForARange(string predicate, int estimate)
    {
        var plan = await PlanInSessionAsync(string.Empty, $"DECLARE @v INT = 5; SELECT Pad FROM dbo.E WHERE {predicate};");

        var rows = plan.Descendants(ShowPlan.Ns + "StmtSimple")
            .Select(statement => (double?)statement.Attribute("StatementEstRows"))
            .Last(value => value is not null);

        Assert.Equal(estimate, rows!.Value, 1d);
    }

    [Theory]
    [Trait("Rule", FindingRuleIds.IndexDesignFilterColumnNotInIndexRuleId)]
    [InlineData("St > 97", false)]
    [InlineData("St > 98", true)]
    public async Task OnlyThePredicateTheFilterGuaranteesNeedsNoLookup(string predicate, bool lookup)
    {
        var plan = await PlanInSessionAsync(string.Empty, $"SELECT G FROM dbo.F WITH (INDEX(FR)) WHERE {predicate};");

        Assert.Equal(lookup, ShowPlan.HasKeyLookup(plan));
    }

    [Theory]
    [Trait("Rule", FindingRuleIds.IndexDesignRowOrPageLockingDisabledRuleId)]
    [InlineData("dbo.NoRowLocks", "PAGE")]
    [InlineData("dbo.NoPageLocks", "KEY")]
    [InlineData("dbo.NoLocks", "OBJECT")]
    public async Task EachDisabledLockOptionChangesTheGranularityOfAnUpdate(string table, string granularity)
    {
        var finest = await ScalarAsync<string>($"""
            BEGIN TRANSACTION;
            UPDATE {table} SET Id = Id WHERE Id = 1;
            SELECT TOP (1) CAST(resource_type AS VARCHAR(20))
            FROM sys.dm_tran_locks
            WHERE request_session_id = @@SPID AND request_mode = 'X'
            ORDER BY CASE resource_type WHEN 'KEY' THEN 1 WHEN 'PAGE' THEN 2 ELSE 3 END;
            ROLLBACK TRANSACTION;
            """);

        Assert.Equal(granularity, finest);
    }

    [Theory]
    [Trait("Rule", FindingRuleIds.UnistrUnpairedSurrogateRuleId)]
    [InlineData("N'\\D83D' + NCHAR(0xDE00)", "3DD800DE")]
    [InlineData("N'\\D83Dx'", "3DD87800")]
    public async Task AnEscapedSurrogatePairsWithARawOne(string argument, string stored)
    {
        Assert.Equal(stored, await ScalarAsync<string>($"SELECT CONVERT(VARCHAR(20), CONVERT(VARBINARY(20), UNISTR({argument})), 2);"));
    }

    [Theory]
    [Trait("Rule", FindingRuleIds.RegexpAccentInsensitiveColumnRuleId)]
    [InlineData("b", false)]
    [InlineData("h", false)]
    [InlineData("j", false)]
    [InlineData("k", false)]
    [InlineData("l", false)]
    [InlineData("m", false)]
    [InlineData("p", false)]
    [InlineData("q", false)]
    [InlineData("r", false)]
    [InlineData("t", false)]
    [InlineData("x", false)]
    [InlineData("a", true)]
    [InlineData("e", true)]
    public async Task OnlySomeLettersHaveAnotherAccentInsensitiveEqualInTheWesternCodePage(string letter, bool hasOther)
    {
        var others = await ScalarAsync<int>($"""
            SELECT COUNT(*)
            FROM (SELECT TOP (255) ROW_NUMBER() OVER (ORDER BY (SELECT 1)) AS I FROM sys.all_columns) AS n
            WHERE CHAR(I) COLLATE SQL_Latin1_General_CP1_CI_AI = '{letter}' AND LOWER(CHAR(I) COLLATE Latin1_General_BIN2) <> '{letter}';
            """);

        Assert.Equal(hasOther, others > 0);
    }

    [Theory]
    [Trait("Rule", FindingRuleIds.ConcatenatedValueInConstantSqlRuleId)]
    [Trait("Rule", FindingRuleIds.ExecStringConcatenatesParameterizableValueRuleId)]
    [InlineData("SELECT K FROM dbo.N WHERE Id = 5 FOR XML PATH;", true)]
    [InlineData("SELECT TOP (1) K FROM dbo.N WHERE Id = 5;", false)]
    public async Task ForXmlDoesNotStopSimpleParameterization(string query, bool parameterized)
    {
        var plan = await PlanInSessionAsync(string.Empty, query);

        Assert.Equal(parameterized, plan.Descendants(ShowPlan.Ns + "StmtSimple").Any(s => s.Attribute("ParameterizedText") is not null));
    }
}
