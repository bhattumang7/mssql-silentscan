using Microsoft.Data.SqlClient;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class UntrustedCheckConstraintEngineFactOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(UntrustedCheckConstraintEngineFactOracleTests);

    protected override string Ddl => """
        CREATE TABLE dbo.Orders_Open (OrderId INT NOT NULL PRIMARY KEY, Status VARCHAR(10) NOT NULL
            CONSTRAINT CK_Orders_Open_Status CHECK (Status = 'Open'));
        CREATE TABLE dbo.Orders_Closed (OrderId INT NOT NULL PRIMARY KEY, Status VARCHAR(10) NOT NULL
            CONSTRAINT CK_Orders_Closed_Status CHECK (Status = 'Closed'));
        INSERT INTO dbo.Orders_Open (OrderId, Status) VALUES (1, 'Open');
        INSERT INTO dbo.Orders_Closed (OrderId, Status) VALUES (2, 'Closed');
        GO
        CREATE VIEW dbo.Orders AS
            SELECT OrderId, Status FROM dbo.Orders_Open
            UNION ALL
            SELECT OrderId, Status FROM dbo.Orders_Closed;
        GO
        ALTER TABLE dbo.Orders_Closed NOCHECK CONSTRAINT CK_Orders_Closed_Status;
        ALTER TABLE dbo.Orders_Closed WITH NOCHECK CHECK CONSTRAINT CK_Orders_Closed_Status;
        GO
        """;

    private const string Query = "SELECT OrderId FROM dbo.Orders WHERE Status = 'Open';";

    private static async Task<string> CapturePartitionedViewPlanAsync(SqlConnection connection)
    {
        await using (var onCommand = new SqlCommand("SET STATISTICS XML ON;", connection))
        {
            await onCommand.ExecuteNonQueryAsync();
        }

        var planXmlBuilder = new System.Text.StringBuilder();
        await using (var probeCommand = new SqlCommand(Query, connection))
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
        return planXml;
    }

    [Fact]
    public async Task UntrustedCheckConstraint_KeepsBothUnionAllBranchesEvenThoughOneCanNeverMatchThePredicate()
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(DatabaseName));
        await connection.OpenAsync();

        var planXml = await CapturePartitionedViewPlanAsync(connection);

        Assert.Contains("Table=\"[Orders_Open]\"", planXml, StringComparison.Ordinal);
        Assert.Contains("Table=\"[Orders_Closed]\"", planXml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TrustingTheCheckConstraint_EliminatesTheBranchThatCanNeverSatisfyThePredicate()
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(DatabaseName));
        await connection.OpenAsync();

        await using (var trustCommand = new SqlCommand(
            "ALTER TABLE dbo.Orders_Closed WITH CHECK CHECK CONSTRAINT CK_Orders_Closed_Status;", connection))
        {
            await trustCommand.ExecuteNonQueryAsync();
        }

        var planXml = await CapturePartitionedViewPlanAsync(connection);

        Assert.DoesNotContain("Table=\"[Orders_Closed]\"", planXml, StringComparison.Ordinal);
        Assert.Contains("Table=\"[Orders_Open]\"", planXml, StringComparison.Ordinal);
    }
}
