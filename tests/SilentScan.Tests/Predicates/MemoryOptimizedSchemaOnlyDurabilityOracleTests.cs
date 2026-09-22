using Microsoft.Data.SqlClient;
using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;
using SilentScan.Verify.Catalog;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", "silentscan/catalog/memory-optimized-schema-only-durability")]
public sealed class MemoryOptimizedSchemaOnlyDurabilityOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(MemoryOptimizedSchemaOnlyDurabilityOracleTests);

    protected override string Ddl =>
        """
        DECLARE @dataDir NVARCHAR(260) = (
            SELECT LEFT(physical_name, LEN(physical_name) - CHARINDEX('/', REVERSE(physical_name)) + 1)
            FROM sys.master_files WHERE database_id = DB_ID() AND file_id = 1);
        DECLARE @sql NVARCHAR(MAX) = N'
            ALTER DATABASE CURRENT ADD FILEGROUP MemoryOptimizedFg CONTAINS MEMORY_OPTIMIZED_DATA;
            ALTER DATABASE CURRENT ADD FILE (name=''MemoryOptimizedFile'', filename=''' + @dataDir + N'durability_oracle'') TO FILEGROUP MemoryOptimizedFg;';
        EXEC sp_executesql @sql;

        CREATE TABLE dbo.SchemaOnlyTable (Id INT NOT NULL PRIMARY KEY NONCLUSTERED) WITH (MEMORY_OPTIMIZED = ON, DURABILITY = SCHEMA_ONLY);
        CREATE TABLE dbo.SchemaAndDataTable (Id INT NOT NULL PRIMARY KEY NONCLUSTERED) WITH (MEMORY_OPTIMIZED = ON, DURABILITY = SCHEMA_AND_DATA);
        CREATE TABLE dbo.DefaultDurabilityTable (Id INT NOT NULL PRIMARY KEY NONCLUSTERED) WITH (MEMORY_OPTIMIZED = ON);
        """;

    private async Task<string> ReadDurabilityDescAsync(string tableName)
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(DatabaseName));
        await connection.OpenAsync();

        await using var command = new SqlCommand(
            "SELECT durability_desc FROM sys.tables WHERE name = @name;", connection);
        command.Parameters.AddWithValue("@name", tableName);

        var result = await command.ExecuteScalarAsync();
        return Assert.IsType<string>(result);
    }

    [Fact]
    public async Task ExplicitSchemaOnlyDurability_DeploysCleanly_AndCatalogReportsSchemaOnly()
    {
        var durabilityDesc = await ReadDurabilityDescAsync("SchemaOnlyTable");

        Assert.Equal("SCHEMA_ONLY", durabilityDesc);
    }

    [Fact]
    public async Task ExplicitSchemaAndDataDurability_CatalogReportsSchemaAndData()
    {
        var durabilityDesc = await ReadDurabilityDescAsync("SchemaAndDataTable");

        Assert.Equal("SCHEMA_AND_DATA", durabilityDesc);
    }

    [Fact]
    public async Task OmittedDurability_DefaultsToSchemaAndData()
    {
        var durabilityDesc = await ReadDurabilityDescAsync("DefaultDurabilityTable");

        Assert.Equal("SCHEMA_AND_DATA", durabilityDesc);
    }

    [Fact]
    public async Task RowsInASchemaOnlyTable_DoNotSurviveTheDatabaseGoingOfflineAndOnline_SchemaAndDataControlKeepsItsRow()
    {
        await ExecuteAsync("INSERT dbo.SchemaOnlyTable (Id) VALUES (1); INSERT dbo.SchemaAndDataTable (Id) VALUES (1);");

        using (var poolKey = new SqlConnection(Options.BuildConnectionString(DatabaseName)))
        {
            SqlConnection.ClearPool(poolKey);
        }

        await using (var master = new SqlConnection(Options.BuildConnectionString()))
        {
            await master.OpenAsync();
            await ExecuteAsync(master, $"ALTER DATABASE [{DatabaseName}] SET OFFLINE WITH ROLLBACK IMMEDIATE;");
            await ExecuteAsync(master, $"ALTER DATABASE [{DatabaseName}] SET ONLINE;");
        }

        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.SchemaOnlyTable;"));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.SchemaAndDataTable;"));
    }

    [Fact]
    public async Task LiveCatalogRead_FlagsOnlyTheSchemaOnlyTable()
    {
        var catalog = await new LiveCatalogReader(Options.BuildConnectionString(DatabaseName)).ReadAsync();

        var finding = Assert.Single(MemoryOptimizedSchemaOnlyDurabilityScanner.Scan(catalog));
        Assert.Equal("dbo.SchemaOnlyTable", finding.TableQualifiedName);
    }
}
