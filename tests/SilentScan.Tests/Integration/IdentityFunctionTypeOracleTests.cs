using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Catalog;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;
using SilentScan.Core.TypeInference;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class IdentityFunctionTypeOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(IdentityFunctionTypeOracleTests);

    protected override string Ddl => "CREATE TABLE dbo.Src (i INT NULL);";

    [Theory]
    [InlineData("SMALLINT")]
    [InlineData("BIGINT")]
    [InlineData("DECIMAL(12,0)")]
    [InlineData("INT")]
    public async Task IdentityFunction_IsTypedAsItsDeclaredTypeInSelectInto(string declared)
    {
        await ExecuteAsync($"SELECT IDENTITY({declared}, 1, 1) AS id, i INTO dbo.Made FROM dbo.Src;");
        var rows = await RowsAsync("SELECT TYPE_NAME(c.user_type_id), c.precision, c.scale FROM sys.columns c WHERE c.object_id = OBJECT_ID(N'dbo.Made') AND c.name = N'id';");
        await ExecuteAsync("DROP TABLE dbo.Made;");

        var parsed = new TSql170Parser(initialQuotedIdentifiers: true).Parse(new StringReader($"SELECT IDENTITY({declared}, 1, 1) AS id INTO dbo.Made FROM dbo.Src;"), out var errors);
        Assert.Empty(errors);
        var select = (SelectStatement)((TSqlScript)parsed).Batches[0].Statements[0];
        var expression = ((SelectScalarExpression)((QuerySpecification)select.QueryExpression).SelectElements[0]).Expression;
        var ours = ScalarExpressionResolver.ResolveScalarType(
            expression, [], "identity.sql", new ScalarExpressionResolver.ScalarTypeContext(null, null, new DatabaseCatalog()));

        Assert.NotNull(ours);
        Assert.Equal(((string)rows[0][0]!).ToUpperInvariant(), ours.Category.ToString().ToUpperInvariant());
        if (ours.Category == SqlTypeCategory.Decimal)
        {
            Assert.Equal((byte)rows[0][1]!, (byte)ours.Precision!.Value);
            Assert.Equal((byte)rows[0][2]!, (byte)ours.Scale!.Value);
        }
    }
}
