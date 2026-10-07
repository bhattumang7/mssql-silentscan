using SilentScan.Tests.Support;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
[Trait("Rule", "silentscan/scalar-udf/in-computed-column-or-constraint")]
public sealed class SchemaDependencyDefaultEvaluationOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(SchemaDependencyDefaultEvaluationOracleTests);

    protected override string Ddl => """
        CREATE FUNCTION dbo.fnConstBound() RETURNS INT WITH SCHEMABINDING AS BEGIN RETURN 5; END;
        GO
        CREATE FUNCTION dbo.fnConstUnbound() RETURNS INT AS BEGIN RETURN 5; END;
        GO
        CREATE FUNCTION dbo.fnClockBound() RETURNS DATETIME WITH SCHEMABINDING AS BEGIN RETURN GETDATE(); END;
        GO
        CREATE TABLE dbo.Src (V INT NOT NULL);
        INSERT INTO dbo.Src VALUES (1);
        GO
        CREATE FUNCTION dbo.fnReadBound() RETURNS INT WITH SCHEMABINDING AS BEGIN RETURN (SELECT MAX(V) FROM dbo.Src); END;
        GO
        CREATE FUNCTION dbo.fnComputed(@x INT) RETURNS INT WITH SCHEMABINDING AS BEGIN RETURN @x + 1; END;
        GO
        CREATE TABLE dbo.Rows (Id INT NOT NULL PRIMARY KEY);
        CREATE TABLE dbo.DConstBound (Id INT NOT NULL, D INT NOT NULL CONSTRAINT DF_DConstBound DEFAULT dbo.fnConstBound());
        CREATE TABLE dbo.DConstUnbound (Id INT NOT NULL, D INT NOT NULL CONSTRAINT DF_DConstUnbound DEFAULT dbo.fnConstUnbound());
        CREATE TABLE dbo.DClockBound (Id INT NOT NULL, D DATETIME NOT NULL CONSTRAINT DF_DClockBound DEFAULT dbo.fnClockBound());
        CREATE TABLE dbo.DReadBound (Id INT NOT NULL, D INT NOT NULL CONSTRAINT DF_DReadBound DEFAULT dbo.fnReadBound());
        CREATE TABLE dbo.CComputed (Id INT NOT NULL, C AS dbo.fnComputed(Id));
        GO
        INSERT INTO dbo.Rows (Id) SELECT TOP (500) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) FROM sys.all_objects a CROSS JOIN sys.all_objects b;
        GO
        """;

    private async Task<long> ExecutionCountAfterAsync(string insertSql, string function)
    {
        await ExecuteAsync(insertSql);
        return await ScalarAsync<long>(
            $"SELECT ISNULL(MAX(s.execution_count), 0) FROM sys.dm_exec_function_stats s WHERE s.database_id = DB_ID() AND s.object_id = OBJECT_ID('{function}');");
    }

    [Fact]
    public async Task DefaultCallingDeterministicDataFreeFunction_RunsOncePerStatement_WhileEveryOtherShapeRunsPerRow()
    {
        var constBound = await ExecutionCountAfterAsync("INSERT INTO dbo.DConstBound (Id) SELECT Id FROM dbo.Rows;", "dbo.fnConstBound");
        var constUnbound = await ExecutionCountAfterAsync("INSERT INTO dbo.DConstUnbound (Id) SELECT Id FROM dbo.Rows;", "dbo.fnConstUnbound");
        var clockBound = await ExecutionCountAfterAsync("INSERT INTO dbo.DClockBound (Id) SELECT Id FROM dbo.Rows;", "dbo.fnClockBound");
        var readBound = await ExecutionCountAfterAsync("INSERT INTO dbo.DReadBound (Id) SELECT Id FROM dbo.Rows;", "dbo.fnReadBound");
        var computed = await ExecutionCountAfterAsync("INSERT INTO dbo.CComputed (Id) SELECT Id FROM dbo.Rows; SELECT COUNT(*) FROM dbo.CComputed WHERE C IS NOT NULL;", "dbo.fnComputed");

        Assert.Equal(1, constBound);
        Assert.Equal(500, constUnbound);
        Assert.Equal(500, clockBound);
        Assert.Equal(500, readBound);
        Assert.Equal(500, computed);
    }
}
