using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Catalog;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;
using SilentScan.Core.TypeInference;
using SilentScan.Tests.Support;
using SilentScan.Verify.Catalog;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class NextValueForTypeOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(NextValueForTypeOracleTests);

    private const string Script = """
        CREATE SCHEMA seq;
        GO
        CREATE SEQUENCE dbo.s_default;
        GO
        CREATE SEQUENCE dbo.s_small AS SMALLINT;
        GO
        CREATE SEQUENCE seq.s_dec AS DECIMAL(9,0);
        GO
        CREATE SEQUENCE dbo.s_dropped AS TINYINT;
        GO
        DROP SEQUENCE dbo.s_dropped;
        """;

    protected override string Ddl => Script;

    public static TheoryData<string> Sequences() => ["dbo.s_default", "dbo.s_small", "seq.s_dec"];

    private static SqlType? Ours(string sequenceName)
    {
        var result = SqlScriptParser.ParseText("seq.sql", Script);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        var catalog = CatalogBuilder.Build([result]);
        var parsed = new TSql170Parser(initialQuotedIdentifiers: true).Parse(new StringReader($"SELECT NEXT VALUE FOR {sequenceName}"), out var errors);
        Assert.Empty(errors);
        var select = (SelectStatement)((TSqlScript)parsed).Batches[0].Statements[0];
        var expression = ((SelectScalarExpression)((QuerySpecification)select.QueryExpression).SelectElements[0]).Expression;
        return ScalarExpressionResolver.ResolveScalarType(
            expression, [], "seq.sql", new ScalarExpressionResolver.ScalarTypeContext(null, catalog.TypeAliases, catalog));
    }

    [Theory]
    [MemberData(nameof(Sequences))]
    public async Task NextValueFor_IsTypedAsTheSequenceTheEngineDescribes(string sequenceName)
    {
        var rows = await RowsAsync($"SELECT system_type_name FROM sys.dm_exec_describe_first_result_set(N'SELECT NEXT VALUE FOR {sequenceName} AS v', NULL, 0);");
        var parser = new TSql170Parser(initialQuotedIdentifiers: true);
        var declare = (DeclareVariableStatement)((TSqlScript)parser.Parse(new StringReader($"DECLARE @x {(string)rows[0][0]!};"), out _)).Batches[0].Statements[0];
        var expected = SqlTypeReferenceResolver.Resolve(declare.Declarations[0].DataType, columnCollation: null);

        Assert.Equal(expected, Ours(sequenceName));
    }

    [Fact]
    public void NextValueFor_OfADroppedOrUnknownSequence_StaysUnknown()
    {
        Assert.Null(Ours("dbo.s_dropped"));
        Assert.Null(Ours("dbo.never_created"));
    }

    [Theory]
    [MemberData(nameof(Sequences))]
    public async Task LiveCatalog_CarriesTheSequenceTypeTheEngineHolds(string sequenceName)
    {
        var catalog = await new LiveCatalogReader(Options.BuildConnectionString(DatabaseName)).ReadAsync();
        var rows = await RowsAsync($"SELECT system_type_name FROM sys.dm_exec_describe_first_result_set(N'SELECT NEXT VALUE FOR {sequenceName} AS v', NULL, 0);");
        var declare = (DeclareVariableStatement)((TSqlScript)new TSql170Parser(initialQuotedIdentifiers: true)
            .Parse(new StringReader($"DECLARE @x {(string)rows[0][0]!};"), out _)).Batches[0].Statements[0];

        Assert.True(catalog.TryGetSequenceType(sequenceName, out var live));
        Assert.Equal(SqlTypeReferenceResolver.Resolve(declare.Declarations[0].DataType, columnCollation: null), live);
        Assert.False(catalog.TryGetSequenceType("dbo.s_dropped", out _));
    }
}
