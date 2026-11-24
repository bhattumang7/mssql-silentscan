using SilentScan.Core.Catalog;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;
using SilentScan.Core.Reporting;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Lineage;

public sealed class MethodCallTypingTests
{
    private static LineageCatalog Resolve()
    {
        var result = SqlScriptParser.ParseText("methods.sql", MethodCallTypingDdl.Script);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        return LineageResolver.Resolve(CatalogBuilder.Build([result]), [result]);
    }

    private static string? TypeOf(LineageCatalog lineage, string view, string column) =>
        ColumnProvenanceAnalysis.TryGetScalarType(lineage.Find(view)!.FindColumn(column)!.Provenance)?.ToString();

    [Theory]
    [InlineData("NodeInt", "Int")]
    [InlineData("NodeUnsizedVarchar", "VarChar(1)")]
    [InlineData("NodeDecimal", "Decimal(9,2)")]
    [InlineData("NodeExists", "Bit")]
    [InlineData("NodeQuery", "Xml")]
    [InlineData("BodyValue", "NVarChar(20)")]
    [InlineData("Lvl", "SmallInt")]
    [InlineData("Ancestor", "HierarchyId")]
    [InlineData("NodeText", "NVarChar(4000)")]
    [InlineData("UnderRoot", "Bit")]
    [InlineData("Reparented", "HierarchyId")]
    [InlineData("Twice", "BigInt")]
    public void MethodCallOnTypedColumn_IsTypedByTheTargetTypesMethod(string column, string expected)
    {
        Assert.Equal(expected, TypeOf(Resolve(), "dbo.vw_Methods", column));
    }

    [Fact]
    public void NodesOnAnUnqualifiedXmlColumn_ResolvesTheRowsetColumn()
    {
        var lineage = Resolve();

        Assert.Equal("BigInt", TypeOf(lineage, "dbo.vw_UnqualifiedNodes", "K"));
        Assert.DoesNotContain(lineage.Skipped.Entries, e => e.Reason.Contains("nodes", StringComparison.Ordinal));
    }

    [Fact]
    public void UnregisteredSchemaQualifiedTableValuedFunction_StillLedgered()
    {
        var result = SqlScriptParser.ParseText("tvf.sql", """
            CREATE TABLE dbo.Src (Id INT NOT NULL);
            GO
            CREATE VIEW dbo.vw_Unknown AS SELECT u.x AS X FROM dbo.Src AS s CROSS APPLY dbo.NotRegistered(s.Id) AS u;
            """);
        var lineage = LineageResolver.Resolve(CatalogBuilder.Build([result]), [result]);

        Assert.Contains(lineage.Skipped.Entries, e => e.ConstructKind == "FROM table-valued function" && e.Reason.Contains("dbo.NotRegistered", StringComparison.Ordinal));
    }
    [Fact]
    public void PredicateOperandMethodCalls_AreTypedInThePredicatePass()
    {
        var result = SqlScriptParser.ParseText("pred.sql", """
            CREATE TABLE dbo.Org (Id INT NOT NULL PRIMARY KEY, Node HIERARCHYID NULL, Code VARCHAR(10) NOT NULL, Body XML NULL, INDEX IX_Org_Node (Node), INDEX IX_Org_Code (Code));
            GO
            CREATE PROCEDURE dbo.p AS
            SELECT e.Id FROM dbo.Org AS e JOIN dbo.Org AS m ON e.Node = m.Node.GetAncestor(1);
            SELECT e.Id FROM dbo.Org AS e JOIN dbo.Org AS m ON e.Code = m.Body.value('(/a)[1]', 'int');
            """);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));

        var report = ScanReportBuilder.BuildFromParseResults([result], CatalogBuilder.Build([result]));

        Assert.DoesNotContain(report.SkippedConstructs, s => s.Reason.Contains("GetAncestor", StringComparison.Ordinal) || s.Reason.Contains("'value'", StringComparison.Ordinal));
        var code = Assert.Single(report.Find<TypedPredicateFinding>("TypedPredicateExtractor"), f => f.ColumnName == "Code");
        Assert.Equal("Int", ((PredicateOperand.Value)code.OtherOperand).Type?.ToString());
    }
}
