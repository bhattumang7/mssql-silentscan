using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Parsing;

namespace SilentScan.Tests.Parsing;

/// <summary>
/// Phase 0 spike: prove ScriptDOM parses a table + two stacked views + a proc, and that we
/// can walk the AST to the WHERE predicate's column reference. See plan.md Phase 0.
/// </summary>
public sealed class SqlScriptParserTests
{
    private static readonly string FixturePath = Path.Combine(
        AppContext.BaseDirectory, "fixtures", "phase0_spike.sql");

    [Fact]
    public void ParseFile_Phase0Spike_ProducesNoErrors()
    {
        var result = new SqlScriptParser().ParseFile(FixturePath);

        Assert.False(result.HasErrors);
    }

    [Fact]
    public void ParseFile_Phase0Spike_ProducesFourBatches()
    {
        var result = new SqlScriptParser().ParseFile(FixturePath);

        var script = Assert.IsType<TSqlScript>(result.Fragment);
        Assert.Equal(4, script.Batches.Count);
    }

    [Fact]
    public void ParseFile_Phase0Spike_SecondViewSelectsFromFirstView()
    {
        var result = new SqlScriptParser().ParseFile(FixturePath);
        var script = (TSqlScript)result.Fragment;

        var view2 = script.Batches
            .SelectMany(b => b.Statements)
            .OfType<CreateViewStatement>()
            .Single(v => v.SchemaObjectName.BaseIdentifier.Value == "vw_OrdersLevel2");

        var querySpec = Assert.IsType<QuerySpecification>(view2.SelectStatement.QueryExpression);
        var fromTable = Assert.IsType<NamedTableReference>(querySpec.FromClause.TableReferences.Single());
        Assert.Equal("vw_OrdersLevel1", fromTable.SchemaObject.BaseIdentifier.Value);
    }

    [Fact]
    public void ParseFile_Phase0Spike_ExtractsWherePredicateColumnReference()
    {
        var result = new SqlScriptParser().ParseFile(FixturePath);
        var script = (TSqlScript)result.Fragment;

        var proc = script.Batches
            .SelectMany(b => b.Statements)
            .OfType<CreateProcedureStatement>()
            .Single();

        var beginEnd = proc.StatementList.Statements.OfType<BeginEndBlockStatement>().Single();
        var select = beginEnd.StatementList.Statements.OfType<SelectStatement>().Single();
        var querySpec = Assert.IsType<QuerySpecification>(select.QueryExpression);
        var where = Assert.IsType<BooleanComparisonExpression>(querySpec.WhereClause.SearchCondition);

        var columnRef = Assert.IsType<ColumnReferenceExpression>(where.FirstExpression);
        Assert.Equal("OrderCode", columnRef.MultiPartIdentifier.Identifiers.Last().Value);

        var parameterRef = Assert.IsType<VariableReference>(where.SecondExpression);
        Assert.Equal("@OrderCode", parameterRef.Name);
    }
}
