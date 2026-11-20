using SilentScan.Verify.Oracle;

namespace SilentScan.Tests.Oracle;

public sealed class PlanStatementLocatorTests
{
    private const string ShowPlanNs = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";

    private const string ConvertBody = """
        <QueryPlan><RelOp><Filter><Predicate><ScalarOperator><Compare CompareOp="EQ"><ScalarOperator>
          <Convert DataType="nvarchar" Length="40" Style="0" Implicit="1"><ScalarOperator><Identifier>
            <ColumnReference Database="[TestDb]" Schema="[dbo]" Table="[Orders]" Column="Code" />
          </Identifier></ScalarOperator></Convert>
        </ScalarOperator></Compare></ScalarOperator></Predicate></Filter></RelOp></QueryPlan>
        """;

    private static string Plan(string statements) => $"""
        <ShowPlanXML xmlns="{ShowPlanNs}">
          <BatchSequence><Batch><Statements>{statements}</Statements></Batch></BatchSequence>
        </ShowPlanXML>
        """;

    private static string Simple(string statementText, string body = "<QueryPlan><RelOp /></QueryPlan>", string statementType = "SELECT") =>
        $"""<StmtSimple StatementText="{Encode(statementText)}" StatementType="{statementType}">{body}</StmtSimple>""";

    private static string Encode(string text) => text.Replace("\r", "&#13;", StringComparison.Ordinal).Replace("\n", "&#10;", StringComparison.Ordinal);

    [Fact]
    public void Locate_IdenticalStatements_MapToSuccessiveLineRanges()
    {
        const string definition = "SELECT Id\nFROM dbo.T;\n\nSELECT Id\nFROM dbo.T;";
        var plan = Plan(Simple("SELECT Id\nFROM dbo.T") + Simple("SELECT Id\nFROM dbo.T"));

        var result = PlanStatementLocator.Locate(definition, plan);

        Assert.Equal(2, result.Count);
        Assert.Equal(1, result[0].StartLine);
        Assert.Equal(2, result[0].EndLine);
        Assert.Equal(4, result[1].StartLine);
        Assert.Equal(5, result[1].EndLine);
    }

    [Fact]
    public void Locate_LeadingSemicolonAndWhitespace_StartLinePointsAtKeyword()
    {
        const string definition = "DECLARE @x int;\n    SELECT Id FROM dbo.T;";
        var plan = Plan(Simple(";\n    SELECT Id FROM dbo.T"));

        var result = PlanStatementLocator.Locate(definition, plan);

        var statement = Assert.Single(result);
        Assert.Equal("SELECT Id FROM dbo.T", statement.StatementText);
        Assert.Equal(2, statement.StartLine);
        Assert.Equal(2, statement.EndLine);
    }

    [Fact]
    public void Locate_TextAbsentFromDefinition_ReturnsNullLinesWithoutDisturbingLaterMatches()
    {
        const string definition = "SELECT Id FROM dbo.T";
        var plan = Plan(Simple("SELECT Missing FROM dbo.Other") + Simple("SELECT Id FROM dbo.T") + Simple("   ;  "));

        var result = PlanStatementLocator.Locate(definition, plan);

        Assert.Equal(3, result.Count);
        Assert.Null(result[0].StartLine);
        Assert.Null(result[0].EndLine);
        Assert.Equal(1, result[1].StartLine);
        Assert.Equal(1, result[1].EndLine);
        Assert.Null(result[2].StartLine);
        Assert.Null(result[2].EndLine);
    }

    [Fact]
    public void Locate_CrlfDefinition_MatchesLfLineNumbers()
    {
        const string lfDefinition = "SELECT A\nFROM T1;\nSELECT B\nFROM T2";
        const string crlfDefinition = "SELECT A\r\nFROM T1;\r\nSELECT B\r\nFROM T2";
        var lfPlan = Plan(Simple("SELECT A\nFROM T1") + Simple("SELECT B\nFROM T2"));
        var crlfPlan = Plan(Simple("SELECT A\r\nFROM T1") + Simple("SELECT B\r\nFROM T2"));

        var lfResult = PlanStatementLocator.Locate(lfDefinition, lfPlan);
        var crlfResult = PlanStatementLocator.Locate(crlfDefinition, crlfPlan);

        Assert.Equal(lfResult.Select(s => (s.StartLine, s.EndLine)), crlfResult.Select(s => (s.StartLine, s.EndLine)));
        Assert.Equal(1, crlfResult[0].StartLine);
        Assert.Equal(2, crlfResult[0].EndLine);
        Assert.Equal(3, crlfResult[1].StartLine);
        Assert.Equal(4, crlfResult[1].EndLine);
    }

    [Fact]
    public void Locate_StmtCondWithInnerStatement_YieldsBothInDocumentOrder()
    {
        const string definition = "IF @x = 1\n    SELECT Id FROM dbo.T;\nSELECT 2";
        var plan = Plan(
            "<StmtCond StatementText=\"IF @x = 1\"><Condition /><Then><Statements>"
            + Simple("SELECT Id FROM dbo.T")
            + "</Statements></Then></StmtCond>"
            + Simple("SELECT 2"));

        var result = PlanStatementLocator.Locate(definition, plan);

        Assert.Equal(3, result.Count);
        Assert.Equal("IF @x = 1", result[0].StatementText);
        Assert.Equal(1, result[0].StartLine);
        Assert.Equal("SELECT Id FROM dbo.T", result[1].StatementText);
        Assert.Equal(2, result[1].StartLine);
        Assert.Equal("SELECT 2", result[2].StatementText);
        Assert.Equal(3, result[2].StartLine);
    }

    [Fact]
    public void Locate_ExecuteProcOuterStatement_IsSkipped()
    {
        const string definition = "SELECT Id FROM dbo.T";
        var plan = Plan(Simple("EXEC dbo.P", statementType: "EXECUTE PROC") + Simple("SELECT Id FROM dbo.T"));

        var result = PlanStatementLocator.Locate(definition, plan);

        var statement = Assert.Single(result);
        Assert.Equal("SELECT Id FROM dbo.T", statement.StatementText);
    }

    [Fact]
    public void Locate_ConvertImplicitInFirstStatement_IsAttributedOnlyToFirstStatement()
    {
        const string definition = "SELECT Code FROM dbo.Orders\nSELECT Id FROM dbo.Other";
        var plan = Plan(
            Simple("SELECT Code FROM dbo.Orders", ConvertBody)
            + Simple("SELECT Id FROM dbo.Other"));

        var result = PlanStatementLocator.Locate(definition, plan);

        Assert.Equal(2, result.Count);
        var conversion = Assert.Single(result[0].Conversions);
        Assert.Equal("Orders", conversion.Table);
        Assert.Equal("Code", conversion.Column);
        Assert.Empty(result[1].Conversions);
    }
}
