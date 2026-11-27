using System.Xml.Linq;
using Microsoft.Data.SqlClient;
using SilentScan.Core.Catalog;
using SilentScan.Core.Parsing;
using SilentScan.Tests.Support;
using SilentScan.Verify;

namespace SilentScan.Tests.Integration;

public abstract class EngineDivergenceOracleTestBase : OracleTestFixture
{
    protected override SqlServerOptions Options => SqlServerOptions.LocalDockerLatest;

    protected override string Ddl => "SELECT 1;";

    protected static (SqlParseResult Result, DatabaseCatalog Catalog) Parse(string sql)
    {
        var result = SqlScriptParser.ParseText("test.sql", sql);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        return (result, CatalogBuilder.Build([result]));
    }

    protected async Task<List<XElement>> ActualRelOpsAsync(string batch, string statementTextFragment)
    {
        var plans = new List<string>();
        await using var connection = await OpenConnectionAsync();
        await ExecuteAsync(connection, "SET STATISTICS XML ON;");
        await using (var command = new SqlCommand(batch, connection) { CommandTimeout = 120 })
        await using (var reader = await command.ExecuteReaderAsync())
        {
            do
            {
                while (await reader.ReadAsync())
                {
                    if (reader.FieldCount == 1 && reader.GetFieldType(0) == typeof(string)
                        && reader.GetString(0).Contains("ShowPlanXML", StringComparison.Ordinal))
                    {
                        plans.Add(reader.GetString(0));
                    }
                }
            }
            while (await reader.NextResultAsync());
        }

        await ExecuteAsync(connection, "SET STATISTICS XML OFF;");
        return plans
            .SelectMany(plan => XDocument.Parse(plan).Descendants().Where(e => e.Name.LocalName is "StmtSimple" or "StmtCond"))
            .Where(statement => statement.Attribute("StatementText")!.Value.Contains(statementTextFragment, StringComparison.Ordinal))
            .SelectMany(statement => statement.Descendants().Where(e => e.Name.LocalName == "RelOp"))
            .ToList();
    }
}
