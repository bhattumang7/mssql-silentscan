using System.Xml.Linq;
using SilentScan.Verify;
using SilentScan.Verify.Deployment;
using SilentScan.Verify.Oracle;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class ModulePlanCaptureTests : IAsyncLifetime
{
    private const string DatabaseName = "SilentScanModulePlanCaptureTest";

    private static readonly XNamespace ShowPlanNs = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";

    private readonly SqlServerOptions _options = SqlServerOptions.LocalDocker;
    private readonly DatabaseProvisioner _provisioner;
    private readonly ModulePlanCapture _capture;

    public ModulePlanCaptureTests()
    {
        _provisioner = new DatabaseProvisioner(_options);
        _capture = new ModulePlanCapture(_options);
    }

    public async Task InitializeAsync()
    {
        await _provisioner.CreateFreshAsync(DatabaseName);
        await new ScriptDeployer(_options).DeployAsync(
            """
            CREATE TABLE dbo.T (Id INT NOT NULL PRIMARY KEY, Code VARCHAR(20) NOT NULL, NCode NVARCHAR(20) NULL);
            GO
            CREATE PROCEDURE dbo.ProcCompare @p NVARCHAR(20) AS
            BEGIN
                SELECT Id FROM dbo.T WHERE Code = @p;
                SELECT Id FROM dbo.T WHERE Code = 'x';
            END;
            GO
            CREATE PROCEDURE dbo.ProcFlow @p NVARCHAR(20), @i INT AS
            BEGIN
                CREATE TABLE #t (Id INT);
                INSERT #t SELECT Id FROM dbo.T;
                IF @i > 0
                    SELECT Id FROM dbo.T WHERE Code = @p;
                SELECT t.Id FROM dbo.T AS t JOIN #t AS x ON x.Id = t.Id WHERE t.Code = 'a';
            END;
            GO
            CREATE VIEW dbo.vMismatch AS SELECT a.Id FROM dbo.T a JOIN dbo.T b ON a.Code = b.NCode;
            GO
            CREATE VIEW dbo.vMatch AS SELECT a.Id FROM dbo.T a JOIN dbo.T b ON a.Code = b.Code;
            GO
            CREATE FUNCTION dbo.fMismatch (@p INT) RETURNS TABLE AS RETURN SELECT a.Id + @p AS V FROM dbo.T a JOIN dbo.T b ON a.Code = b.NCode;
            GO
            CREATE FUNCTION dbo.fMatch (@p INT) RETURNS TABLE AS RETURN SELECT a.Id + @p AS V FROM dbo.T a JOIN dbo.T b ON a.Code = b.Code;
            GO
            CREATE FUNCTION dbo.fParam (@p NVARCHAR(20)) RETURNS TABLE AS RETURN SELECT a.Id FROM dbo.T a WHERE a.Code = @p;
            GO
            CREATE FUNCTION dbo.ScalarCount (@p NVARCHAR(20)) RETURNS INT AS
            BEGIN
                DECLARE @r INT;
                SELECT @r = COUNT(*) FROM dbo.T WHERE Code = @p;
                RETURN @r;
            END;
            GO
            CREATE FUNCTION dbo.MultiStatement (@p NVARCHAR(20)) RETURNS @o TABLE (Id INT) AS
            BEGIN
                INSERT @o SELECT Id FROM dbo.T WHERE Code = @p;
                RETURN;
            END;
            GO
            CREATE PROCEDURE dbo.InnerProc @p NVARCHAR(20) AS SELECT Id FROM dbo.T WHERE Code = @p;
            GO
            CREATE PROCEDURE dbo.OuterProc @p NVARCHAR(20) AS
            BEGIN
                SELECT 1 AS One;
                EXEC dbo.InnerProc @p = @p;
            END;
            GO
            CREATE PROCEDURE dbo.UsesScalar @p NVARCHAR(20) AS SELECT dbo.ScalarCount(@p) AS Total;
            GO
            CREATE PROCEDURE dbo.MissingObject AS SELECT * FROM dbo.NoSuchTable;
            GO
            """,
            DatabaseName);
    }

    public async Task DisposeAsync() =>
        await _provisioner.DropIfExistsAsync(DatabaseName);

    private static List<XElement> Statements(string planXml) =>
        XDocument.Parse(planXml)
            .Descendants()
            .Where(e => (e.Name == ShowPlanNs + "StmtSimple" || e.Name == ShowPlanNs + "StmtCond") && e.Attribute("StatementText") is not null)
            .ToList();

    private static XElement StatementContaining(string planXml, string text) =>
        StatementContaining(Statements(planXml), text);

    private static XElement StatementContaining(IReadOnlyList<XElement> statements, string text) =>
        Assert.Single(statements, s => ((string)s.Attribute("StatementText")!).Contains(text, StringComparison.Ordinal));

    private static IReadOnlyList<ConvertImplicitFinding> ColumnConversions(XElement statement) =>
        ConvertImplicitDetector.FindColumnConversions(statement.ToString());

    private async Task<string> CaptureOkAsync(string name, PlannableModuleKind kind)
    {
        var result = await _capture.CaptureAsync(DatabaseName, name, kind);

        Assert.Equal(ModuleNotDecidedReason.None, result.Reason);
        Assert.Null(result.Detail);
        return Assert.IsType<string>(result.PlanXml);
    }

    [Fact]
    public async Task CaptureAsync_Procedure_ConvertsColumnOnlyInTheStatementComparingToAMismatchedParameter()
    {
        var plan = await CaptureOkAsync("dbo.ProcCompare", PlannableModuleKind.Procedure);

        var mismatched = ColumnConversions(StatementContaining(plan, "Code = @p"));
        var matching = ColumnConversions(StatementContaining(plan, "Code = 'x'"));

        var conversion = Assert.Single(mismatched);
        Assert.Equal("T", conversion.Table);
        Assert.Equal("Code", conversion.Column);
        Assert.Empty(matching);
    }

    [Fact]
    public async Task CaptureAsync_Procedure_PlansStatementsInsideIfBranchesAndJoinsWithTemporaryTables()
    {
        var plan = await CaptureOkAsync("dbo.ProcFlow", PlannableModuleKind.Procedure);

        var branchStatement = StatementContaining(plan, "Code = @p");
        var tempJoin = StatementContaining(plan, "JOIN #t AS x");

        Assert.Equal("Code", Assert.Single(ColumnConversions(branchStatement)).Column);
        Assert.Contains(
            tempJoin.Descendants(ShowPlanNs + "ColumnReference"),
            c => string.Equals((string?)c.Attribute("Table"), "#t", StringComparison.Ordinal));
        Assert.Empty(ColumnConversions(tempJoin));
    }

    [Fact]
    public async Task CaptureAsync_View_ShowsTheConversionInsideTheViewDefinitionAndNotForAMatchingView()
    {
        var mismatch = await CaptureOkAsync("dbo.vMismatch", PlannableModuleKind.View);
        var match = await CaptureOkAsync("dbo.vMatch", PlannableModuleKind.View);

        var conversion = Assert.Single(ColumnConversions(Assert.Single(Statements(mismatch))));
        Assert.Equal("Code", conversion.Column);
        Assert.Empty(ColumnConversions(Assert.Single(Statements(match))));
    }

    [Fact]
    public async Task CaptureAsync_InlineTableFunction_ShowsTheConversionInsideTheFunctionBodyAndNotForAMatchingFunction()
    {
        var mismatch = await CaptureOkAsync("dbo.fMismatch", PlannableModuleKind.InlineTableFunction);
        var match = await CaptureOkAsync("dbo.fMatch", PlannableModuleKind.InlineTableFunction);

        var conversion = Assert.Single(ColumnConversions(Assert.Single(Statements(mismatch))));
        Assert.Equal("Code", conversion.Column);
        Assert.Empty(ColumnConversions(Assert.Single(Statements(match))));
    }

    [Fact]
    public async Task CaptureAsync_InlineTableFunctionWithAParameterPredicate_KeepsTheColumnConversionInsteadOfFoldingItAway()
    {
        var plan = await CaptureOkAsync("dbo.fParam", PlannableModuleKind.InlineTableFunction);

        var conversion = Assert.Single(ColumnConversions(Assert.Single(Statements(plan))));
        Assert.Equal("Code", conversion.Column);
    }

    [Fact]
    public async Task CaptureAsync_NestedExec_ExposesTheCalleeStatementsInsideTheCallingStatement()
    {
        var plan = await CaptureOkAsync("dbo.OuterProc", PlannableModuleKind.Procedure);

        var statements = Statements(plan);
        var calleeStatement = StatementContaining(statements, "Code = @p");
        var callStatement = StatementContaining(statements, "EXEC dbo.InnerProc");

        Assert.Equal("Code", Assert.Single(ColumnConversions(calleeStatement)).Column);
        Assert.Contains(calleeStatement, callStatement.Descendants());
        Assert.Contains(
            callStatement.Descendants(ShowPlanNs + "StoredProc"),
            e => ((string?)e.Attribute("ProcName"))?.Contains("InnerProc", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task CaptureAsync_MultiStatementTableFunction_ExposesItsBodyStatementsInsideTheCallingStatement()
    {
        var result = await new PlanXmlCapture(_options).CaptureAsync(
            DatabaseName, "SELECT * FROM [dbo].[MultiStatement](CAST(NULL AS NVARCHAR(20)));");

        var statements = Statements(result);
        var bodyStatement = StatementContaining(statements, "INSERT @o SELECT Id FROM dbo.T WHERE Code = @p");
        var callStatement = StatementContaining(statements, "SELECT * FROM [dbo].[MultiStatement]");

        Assert.Equal("Code", Assert.Single(ColumnConversions(bodyStatement)).Column);
        Assert.Contains(bodyStatement, callStatement.Descendants());
        Assert.NotEmpty(callStatement.Descendants(ShowPlanNs + "UDF"));
    }

    [Fact]
    public async Task CaptureAsync_ScalarFunction_HasNoStatementOfItsOwnAndItsBodyConversionSitsInTheCallingStatement()
    {
        var plan = await CaptureOkAsync("dbo.UsesScalar", PlannableModuleKind.Procedure);

        Assert.DoesNotContain(
            Statements(plan),
            s => ((string)s.Attribute("StatementText")!).Contains("SELECT @r", StringComparison.Ordinal));

        var callingStatement = StatementContaining(plan, "SELECT dbo.ScalarCount(@p)");
        Assert.Equal("Code", Assert.Single(ColumnConversions(callingStatement)).Column);
    }

    [Fact]
    public async Task CaptureAsync_ModuleReferencingAMissingObject_ReturnsModuleDoesNotCompileWithTheEngineMessage()
    {
        var result = await _capture.CaptureAsync(DatabaseName, "dbo.MissingObject", PlannableModuleKind.Procedure);

        Assert.Null(result.PlanXml);
        Assert.Equal(ModuleNotDecidedReason.ModuleDoesNotCompile, result.Reason);
        Assert.Contains("NoSuchTable", result.Detail, StringComparison.Ordinal);
    }
}
