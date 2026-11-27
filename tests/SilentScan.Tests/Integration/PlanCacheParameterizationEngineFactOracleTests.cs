using SilentScan.Tests.Support;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class PlanCacheParameterizationEngineFactOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(PlanCacheParameterizationEngineFactOracleTests);

    protected override string Ddl => """
        ALTER DATABASE CURRENT SET PARAMETERIZATION FORCED;
        GO
        CREATE TABLE dbo.FpConvert (Id INT, Val INT, Nm VARCHAR(20), D DATETIME);
        CREATE TABLE dbo.FpOutput (Id INT, Val INT, Nm VARCHAR(20), D DATETIME);
        CREATE TABLE dbo.FpHaving (Id INT, Val INT, Nm VARCHAR(20), D DATETIME);
        CREATE TABLE dbo.FpLike (Id INT, Val INT, Nm VARCHAR(20), D DATETIME);
        CREATE TABLE dbo.FpOrder (Id INT, Val INT, Nm VARCHAR(20), D DATETIME);
        CREATE TABLE dbo.FpSelect (Id INT, Val INT, Nm VARCHAR(20), D DATETIME);
        CREATE TABLE dbo.FpSample (Id INT, Val INT, Nm VARCHAR(20), D DATETIME);
        CREATE TABLE dbo.FpTopTable (Id INT, Val INT, Nm VARCHAR(20), D DATETIME);
        CREATE TABLE dbo.FpPaging (Id INT, Val INT, Nm VARCHAR(20), D DATETIME);
        GO
        CREATE TABLE dbo.DynConcat (Id INT PRIMARY KEY, V INT);
        CREATE TABLE dbo.DynParam (Id INT PRIMARY KEY, V INT);
        CREATE TABLE dbo.DynExec (Id INT PRIMARY KEY, V INT);
        CREATE TABLE dbo.DynTrivial (Id INT PRIMARY KEY, V INT);
        INSERT INTO dbo.DynTrivial VALUES (1, 1), (2, 2), (3, 3);
        INSERT INTO dbo.DynConcat VALUES (1, 1), (2, 2), (3, 3);
        INSERT INTO dbo.DynParam VALUES (1, 1), (2, 2), (3, 3);
        INSERT INTO dbo.DynExec VALUES (1, 1), (2, 2), (3, 3);
        GO
        CREATE SCHEMA s1;
        GO
        CREATE SCHEMA s2;
        GO
        CREATE USER u1 WITHOUT LOGIN WITH DEFAULT_SCHEMA = s1;
        CREATE USER u2 WITHOUT LOGIN WITH DEFAULT_SCHEMA = s2;
        GRANT CREATE PROCEDURE TO u1;
        GRANT CREATE PROCEDURE TO u2;
        GRANT ALTER ON SCHEMA::s1 TO u1;
        GRANT ALTER ON SCHEMA::s2 TO u2;
        GRANT ALTER ON SCHEMA::dbo TO u1;
        GRANT SELECT ON SCHEMA::s1 TO u1;
        GRANT SELECT ON SCHEMA::s2 TO u2;
        GRANT SELECT ON SCHEMA::dbo TO u1;
        GRANT SELECT ON SCHEMA::dbo TO u2;
        CREATE TABLE s1.Q (Id INT);
        CREATE TABLE s2.Q (Id INT);
        CREATE TABLE dbo.QualifiedT (Id INT);
        CREATE TABLE dbo.Q (Id INT);
        GO
        CREATE PROCEDURE dbo.UnqualifiedInModule AS SELECT Id FROM Q /*tagmod*/;
        GO
        GRANT EXECUTE ON dbo.UnqualifiedInModule TO u1, u2;
        GO
        """;

    private const string CachedPlansWhere = "FROM sys.dm_exec_cached_plans cp CROSS APPLY sys.dm_exec_sql_text(cp.plan_handle) st CROSS APPLY sys.dm_exec_plan_attributes(cp.plan_handle) pa WHERE pa.attribute = 'dbid' AND CAST(pa.value AS INT) = DB_ID()";

    private async Task<string> PreparedTextAsync(string table, string statement)
    {
        await ExecuteAsync(statement);
        var rows = await RowsAsync($"SELECT st.text {CachedPlansWhere} AND cp.objtype = 'Prepared' AND st.text LIKE '(@%' AND st.text LIKE '%{table}%' AND st.text NOT LIKE '%dm_exec%';");
        Assert.Single(rows);
        return ((string)rows[0][0]!).Replace(" ", string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty);
    }

    private async Task<int> CachedPlanCountAsync(string textLike) =>
        await ScalarAsync<int>($"SELECT COUNT(*) {CachedPlansWhere} AND st.text LIKE '{textLike}' AND st.text NOT LIKE '%dm_exec%';");

    [Fact]
    [Trait("Rule", FindingRuleIds.ForcedParameterizationConvertStyleCodeLiteralRuleId)]
    public async Task ConvertStyleCodeLiteral_StaysWhileSiblingWhereLiteralIsParameterized()
    {
        var text = await PreparedTextAsync("FpConvert", "SELECT CONVERT(VARCHAR(20), D, 101) FROM dbo.FpConvert WHERE Id = 5;");

        Assert.Contains(",D,101)", text);
        Assert.Contains("Id=@0", text);
    }

    [Fact]
    [Trait("Rule", FindingRuleIds.ForcedParameterizationDmlOutputListLiteralRuleId)]
    public async Task OutputListLiteral_StaysWhileValuesLiteralIsParameterized()
    {
        var text = await PreparedTextAsync("FpOutput", "INSERT INTO dbo.FpOutput (Id) OUTPUT inserted.Id, 'insert' VALUES (1);");

        Assert.Contains(",'insert'", text);
        Assert.Contains("values(@0)", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Rule", FindingRuleIds.ForcedParameterizationHavingLiteralRuleId)]
    public async Task HavingLiteral_StaysWhileWhereLiteralIsParameterized()
    {
        var text = await PreparedTextAsync("FpHaving", "SELECT Nm, COUNT(*) FROM dbo.FpHaving WHERE Id = 3 GROUP BY Nm HAVING COUNT(*) > 5;");

        Assert.Contains(">5", text);
        Assert.Contains("Id=@0", text);
    }

    [Fact]
    [Trait("Rule", FindingRuleIds.ForcedParameterizationLikePatternLiteralRuleId)]
    public async Task LikePatternLiteral_StaysWhileWhereLiteralIsParameterized()
    {
        var text = await PreparedTextAsync("FpLike", "SELECT Id FROM dbo.FpLike WHERE Nm LIKE 'Smith%' AND Id = 3;");

        Assert.Contains("'Smith%'", text);
        Assert.Contains("Id=@0", text);
    }

    [Fact]
    [Trait("Rule", FindingRuleIds.ForcedParameterizationOrderByExpressionLiteralRuleId)]
    public async Task OrderByExpressionLiteral_StaysWhileWhereLiteralIsParameterized()
    {
        var text = await PreparedTextAsync("FpOrder", "SELECT Id FROM dbo.FpOrder WHERE Id = 3 ORDER BY (Val + 100);");

        Assert.Contains("(Val+100)", text);
        Assert.Contains("Id=@0", text);
    }

    [Fact]
    [Trait("Rule", FindingRuleIds.ForcedParameterizationSelectListLiteralRuleId)]
    public async Task SelectListLiteral_StaysWhileWhereLiteralIsParameterized()
    {
        var text = await PreparedTextAsync("FpSelect", "SELECT 'Active', Id FROM dbo.FpSelect WHERE Id = 3;");

        Assert.Contains("'Active'", text);
        Assert.Contains("Id=@0", text);
    }

    [Fact]
    [Trait("Rule", FindingRuleIds.ForcedParameterizationTableSampleSizeLiteralRuleId)]
    public async Task TableSampleSizeLiteral_StaysWhileWhereLiteralIsParameterized()
    {
        var text = await PreparedTextAsync("FpSample", "SELECT Id FROM dbo.FpSample TABLESAMPLE (10 PERCENT) WHERE Val = 3;");

        Assert.Contains("tablesample(10percent)", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Val=@0", text);
    }

    [Fact]
    [Trait("Rule", FindingRuleIds.ForcedParameterizationTopOrPagingLiteralRuleId)]
    public async Task TopAndOffsetFetchLiterals_StayWhileWhereLiteralIsParameterized()
    {
        var top = await PreparedTextAsync("FpTopTable", "SELECT TOP (7) Id FROM dbo.FpTopTable WHERE Id = 3;");

        Assert.Contains("top(7)", top, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Id=@0", top);

        var paging = await PreparedTextAsync("FpPaging", "SELECT Id FROM dbo.FpPaging WHERE Val = 3 ORDER BY Id OFFSET 20 ROWS FETCH NEXT 10 ROWS ONLY;");

        Assert.Contains("OFFSET20ROWS", paging, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Val=@0", paging);
    }

    [Fact]
    [Trait("Rule", FindingRuleIds.ConcatenatedValueInConstantSqlRuleId)]
    [Trait("Rule", FindingRuleIds.ExecStringConcatenatesParameterizableValueRuleId)]
    public async Task ConcatenatedExecStrings_CompileOnePlanPerValue_SpExecutesqlParameterControlCompilesOne()
    {
        await using var connection = await OpenConnectionAsync();
        foreach (var id in new[] { 1, 2, 3 })
        {
            await ExecuteAsync(connection, $"DECLARE @s NVARCHAR(200) = N'SELECT V FROM dbo.DynConcat WHERE Id = ' + CAST({id} AS NVARCHAR(10)) + N' OPTION (MAXDOP 1)'; EXEC(@s);");
            await ExecuteAsync(connection, $"EXEC sp_executesql N'SELECT V FROM dbo.DynParam WHERE Id = @i OPTION (MAXDOP 1)', N'@i INT', @i = {id};");
        }

        Assert.Equal(3, await CachedPlanCountAsync("%FROM dbo.DynConcat WHERE Id = [0-9]%"));
        Assert.Equal(1, await CachedPlanCountAsync("%FROM dbo.DynParam WHERE Id = @i%"));
    }

    [Fact]
    [Trait("Rule", FindingRuleIds.ConcatenatedValueInConstantSqlRuleId)]
    [Trait("Rule", FindingRuleIds.ExecStringConcatenatesParameterizableValueRuleId)]
    public async Task ConcatenatedExecStringOfATrivialStatement_IsAutoParameterizedIntoOneSharedPlan()
    {
        await using var connection = await OpenConnectionAsync();
        foreach (var id in new[] { 1, 2, 3 })
        {
            await ExecuteAsync(connection, $"DECLARE @s NVARCHAR(200) = N'SELECT V FROM dbo.DynTrivial WHERE Id = ' + CAST({id} AS NVARCHAR(10)); EXEC(@s);");
        }

        var rows = await RowsAsync($"SELECT COUNT(*) {CachedPlansWhere} AND cp.objtype = 'Prepared' AND st.text LIKE '(@%' AND st.text LIKE '%SELECT V FROM%DynTrivial%' AND st.text NOT LIKE '%dm_exec%';");

        Assert.Equal(1, (int)rows[0][0]!);
    }

    [Fact]
    [Trait("Rule", FindingRuleIds.QueryAntiPatternUnqualifiedTableReferenceRuleId)]
    public async Task UnqualifiedTableReference_CompilesOnePlanPerSchemaContext_QualifiedControlSharesOne()
    {
        await using var connection = await OpenConnectionAsync();
        await ExecuteAsync(connection, "EXECUTE AS USER = 'u1'; EXEC('SELECT Id FROM Q /*tagunq*/'); EXEC('SELECT Id FROM dbo.QualifiedT /*tagqual*/'); REVERT;");
        await ExecuteAsync(connection, "EXECUTE AS USER = 'u2'; EXEC('SELECT Id FROM Q /*tagunq*/'); EXEC('SELECT Id FROM dbo.QualifiedT /*tagqual*/'); REVERT;");

        Assert.Equal(2, await CachedPlanCountAsync("%FROM Q /*tagunq*/%"));
        Assert.Equal(1, await CachedPlanCountAsync("%FROM dbo.QualifiedT /*tagqual*/%"));
    }

    [Fact]
    [Trait("Rule", FindingRuleIds.QueryAntiPatternUnqualifiedTableReferenceRuleId)]
    public async Task UnqualifiedTableReference_InsideProcedureBody_SharesOnePlanAcrossDefaultSchemas()
    {
        await using var connection = await OpenConnectionAsync();
        await ExecuteAsync(connection, "EXECUTE AS USER = 'u1'; EXEC dbo.UnqualifiedInModule; REVERT;");
        await ExecuteAsync(connection, "EXECUTE AS USER = 'u2'; EXEC dbo.UnqualifiedInModule; REVERT;");

        Assert.Equal(1, await CachedPlanCountAsync("%FROM Q /*tagmod*/%"));
    }
}
