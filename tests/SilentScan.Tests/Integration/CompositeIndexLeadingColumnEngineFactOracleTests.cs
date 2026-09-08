using Microsoft.Data.SqlClient;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class CompositeIndexLeadingColumnEngineFactOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(CompositeIndexLeadingColumnEngineFactOracleTests);

    protected override string Ddl => """
        CREATE TABLE dbo.Orders (CustomerId INT NOT NULL, OrderDate DATE NOT NULL, OrderId INT NOT NULL PRIMARY KEY);
        INSERT INTO dbo.Orders (CustomerId, OrderDate, OrderId)
        SELECT (ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) % 500) + 1,
               DATEADD(DAY, ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) % 1000, '2020-01-01'),
               ROW_NUMBER() OVER (ORDER BY (SELECT NULL))
        FROM sys.objects a CROSS JOIN sys.objects b;
        CREATE INDEX IX_Orders_Customer_Date ON dbo.Orders(CustomerId, OrderDate);
        UPDATE STATISTICS dbo.Orders WITH FULLSCAN;
        GO
        """;

    [Fact]
    public async Task PredicateOnlyOnTheNonLeadingKeyColumn_CannotSeekTheCompositeIndex()
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(DatabaseName));
        await connection.OpenAsync();

        await using (var onCommand = new SqlCommand("SET STATISTICS XML ON;", connection))
        {
            await onCommand.ExecuteNonQueryAsync();
        }

        var planXmlBuilder = new System.Text.StringBuilder();
        await using (var probeCommand = new SqlCommand(
            "SELECT OrderId FROM dbo.Orders WHERE OrderDate = '2020-06-01';", connection))
        await using (var reader = await probeCommand.ExecuteReaderAsync())
        {
            do
            {
                while (await reader.ReadAsync())
                {
                    if (reader.FieldCount == 1 && reader.GetFieldType(0) == typeof(string))
                    {
                        var value = reader.GetString(0);
                        if (value.Contains("ShowPlanXML", StringComparison.Ordinal))
                        {
                            planXmlBuilder.Append(value);
                        }
                    }
                }
            }
            while (await reader.NextResultAsync());
        }

        await using (var offCommand = new SqlCommand("SET STATISTICS XML OFF;", connection))
        {
            await offCommand.ExecuteNonQueryAsync();
        }

        var planXml = planXmlBuilder.ToString();
        Assert.NotEmpty(planXml);
        Assert.Contains("IX_Orders_Customer_Date", planXml, StringComparison.Ordinal);
        Assert.DoesNotContain("PhysicalOp=\"Index Seek\"", planXml, StringComparison.Ordinal);
        Assert.Contains("PhysicalOp=\"Index Scan\"", planXml, StringComparison.Ordinal);
    }
}
