using SilentScan.Verify.Oracle;

namespace SilentScan.Tests.Oracle;

public sealed class ConvertImplicitDetectorContextTests
{
    private const string ShowPlanNs = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";

    private static string Plan(string relOpBody) => $"""
        <ShowPlanXML xmlns="{ShowPlanNs}"><BatchSequence><Batch><Statements><StmtSimple><QueryPlan>
          <RelOp>{relOpBody}</RelOp>
        </QueryPlan></StmtSimple></Statements></Batch></BatchSequence></ShowPlanXML>
        """;

    private const string ConvertOnColumn = """
        <Convert DataType="int" Implicit="1"><ScalarOperator><Identifier>
          <ColumnReference Database="[Db]" Schema="[dbo]" Table="[T]" Column="Code" />
        </Identifier></ScalarOperator></Convert>
        """;

    [Fact]
    public void FindColumnConversions_ConversionInsideAPredicate_IsContextPredicate()
    {
        var plan = Plan($"<Filter><Predicate><ScalarOperator><Compare>{ConvertOnColumn}</Compare></ScalarOperator></Predicate></Filter>");

        Assert.Equal("Predicate", Assert.Single(ConvertImplicitDetector.FindColumnConversions(plan)).Context);
    }

    [Fact]
    public void FindColumnConversions_ConversionInsideADefinedValue_IsContextDefinedValue()
    {
        var plan = Plan($"<ComputeScalar><DefinedValues><DefinedValue><ScalarOperator>{ConvertOnColumn}</ScalarOperator></DefinedValue></DefinedValues></ComputeScalar>");

        Assert.Equal("DefinedValue", Assert.Single(ConvertImplicitDetector.FindColumnConversions(plan)).Context);
    }

    [Fact]
    public void FindColumnConversions_ConversionOutsideAnyKnownContext_IsContextOther()
    {
        var plan = Plan($"<Sort>{ConvertOnColumn}</Sort>");

        Assert.Equal("Other", Assert.Single(ConvertImplicitDetector.FindColumnConversions(plan)).Context);
    }

    private const string ConvertOnColumnWithoutATable = """
        <Convert DataType="int" Implicit="1"><ScalarOperator><Identifier>
          <ColumnReference Column="RowKey" />
        </Identifier></ScalarOperator></Convert>
        """;

    [Fact]
    public void FindColumnConversions_ColumnReferenceWithoutATable_IsLeftOutOfTheAttributedList()
    {
        var plan = Plan($"<Filter><Predicate><ScalarOperator><Compare>{ConvertOnColumnWithoutATable}</Compare></ScalarOperator></Predicate></Filter>");

        Assert.Empty(ConvertImplicitDetector.FindColumnConversions(plan));
    }

    [Fact]
    public void FindColumnConversionsIncludingUnattributed_ColumnReferenceWithoutATable_IsReportedWithANullTable()
    {
        var plan = Plan($"<Filter><Predicate><ScalarOperator><Compare>{ConvertOnColumnWithoutATable}</Compare></ScalarOperator></Predicate></Filter>");

        var conversion = Assert.Single(ConvertImplicitDetector.FindColumnConversionsIncludingUnattributed(plan));
        Assert.Null(conversion.Table);
        Assert.Equal("RowKey", conversion.Column);
        Assert.Equal("Predicate", conversion.Context);
    }
}
