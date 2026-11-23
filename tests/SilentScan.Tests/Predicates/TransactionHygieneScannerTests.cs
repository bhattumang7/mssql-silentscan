using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;

namespace SilentScan.Tests.Predicates;

public sealed class TransactionHygieneScannerTests
{
    private static IReadOnlyList<TransactionHygieneFinding> Scan(string procedureBody)
    {
        var sql = $"CREATE PROCEDURE dbo.p AS\nBEGIN\n{procedureBody}\nEND";
        var result = SqlScriptParser.ParseText("test.sql", sql);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        return TransactionHygieneScanner.Scan(result);
    }

    [Fact]
    public void ImplicitTransactionOpenedInATriggerBody_IsEndedWithTheTriggeringStatement()
    {
        var result = SqlScriptParser.ParseText("test.sql", "CREATE TRIGGER dbo.tr ON dbo.T AFTER INSERT AS BEGIN SET IMPLICIT_TRANSACTIONS ON; INSERT dbo.L VALUES (1); END");
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));

        Assert.Empty(TransactionHygieneScanner.Scan(result));
    }

    [Fact]
    public void ImplicitTransactionOpenedInAProcedureBody_StillFires()
    {
        Assert.Single(Scan("SET IMPLICIT_TRANSACTIONS ON; INSERT dbo.L VALUES (1);"));
    }

    [Theory]
    [InlineData("SELECT 1 FROM (VALUES (1)) v(a);", false)]
    [InlineData("SELECT value FROM STRING_SPLIT('a,b', ',');", false)]
    [InlineData("WITH c AS (SELECT 1 AS a) SELECT a FROM c;", false)]
    [InlineData("SELECT a FROM dbo.T;", true)]
    [InlineData("SELECT d.a FROM (SELECT a FROM dbo.T) d;", true)]
    [InlineData("WITH c AS (SELECT a FROM dbo.T) SELECT a FROM c;", true)]
    public void SelectOpensAnImplicitTransactionOnlyWhenItReadsARealTable(string select, bool fires)
    {
        var findings = Scan($"SET IMPLICIT_TRANSACTIONS ON; {select}");

        Assert.Equal(fires, findings.Count == 1);
    }

    [Theory]
    [InlineData("BEGIN TRANSACTION; UPDATE dbo.T SET a = 1;")]
    [InlineData("BEGIN TRANSACTION; UPDATE dbo.T SET a = 1; RETURN;")]
    [InlineData("BEGIN TRANSACTION; SAVE TRANSACTION sp1; UPDATE dbo.T SET a = 1; ROLLBACK TRANSACTION sp1; RETURN;")]
    public void UnresolvedExplicitTransaction_IsNotReportedBecauseTheEngineRaisesMsg266(string body)
    {
        Assert.Empty(Scan(body));
    }

    [Fact]
    public void ImplicitTransactionLeftOpenAtTheEnd_Fires()
    {
        var finding = Assert.Single(Scan("SET IMPLICIT_TRANSACTIONS ON; UPDATE dbo.T SET a = 1;"));

        Assert.Equal(TransactionHygieneFindingKind.ImplicitTransactionUnresolvedOnSomePath, finding.Kind);
    }

    [Fact]
    public void ImplicitTransactionLeftOpenAtAReturn_FiresAtTheReturn()
    {
        var finding = Assert.Single(Scan("SET IMPLICIT_TRANSACTIONS ON; UPDATE dbo.T SET a = 1; RETURN;"));

        Assert.Equal(TransactionHygieneFindingKind.ImplicitTransactionUnresolvedOnSomePath, finding.Kind);
    }

    [Theory]
    [InlineData("SET IMPLICIT_TRANSACTIONS ON; UPDATE dbo.T SET a = 1; COMMIT TRANSACTION;")]
    [InlineData("SET IMPLICIT_TRANSACTIONS ON; UPDATE dbo.T SET a = 1; ROLLBACK TRANSACTION;")]
    [InlineData("SET IMPLICIT_TRANSACTIONS ON; DECLARE @x INT = 1; SELECT @x;")]
    [InlineData("UPDATE dbo.T SET a = 1;")]
    [InlineData("SET IMPLICIT_TRANSACTIONS ON; UPDATE dbo.T SET a = 1; IF @@TRANCOUNT > 0 COMMIT TRANSACTION;")]
    [InlineData("SET IMPLICIT_TRANSACTIONS ON; UPDATE dbo.T SET a = 1; IF @@TRANCOUNT > 0 BEGIN COMMIT TRANSACTION; END;")]
    [InlineData("SET IMPLICIT_TRANSACTIONS ON; UPDATE dbo.T SET a = 1; WHILE @@TRANCOUNT > 0 COMMIT TRANSACTION;")]
    public void ImplicitTransactionResolvedOrNeverOpened_NeverFires(string body)
    {
        Assert.Empty(Scan(body));
    }

    [Theory]
    [InlineData("@@TRANCOUNT > 0")]
    [InlineData("XACT_STATE() <> 0")]
    public void ImplicitTransactionRolledBackUnderAGuardInACatch_NeverFires(string guard)
    {
        var body = $"SET IMPLICIT_TRANSACTIONS ON; BEGIN TRY UPDATE dbo.T SET a = 1; COMMIT TRANSACTION; END TRY BEGIN CATCH IF {guard} ROLLBACK TRANSACTION; END CATCH";

        Assert.Empty(Scan(body));
    }
}
