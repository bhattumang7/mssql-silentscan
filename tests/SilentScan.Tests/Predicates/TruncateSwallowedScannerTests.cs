using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;

namespace SilentScan.Tests.Predicates;

public sealed class TruncateSwallowedScannerTests
{
    private static IReadOnlyList<TruncateSwallowedFinding> Scan(string sql)
    {
        var result = SqlScriptParser.ParseText("test.sql", sql);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        return TruncateSwallowedScanner.Scan(result);
    }

    [Theory]
    [InlineData("#t", false)]
    [InlineData("##g", false)]
    [InlineData("dbo.Foo", true)]
    [InlineData("Foo", true)]
    public void EmptyCatch_FiresOnlyForTablesAForeignKeyCanReference(string table, bool fires)
    {
        var findings = Scan($"""
            BEGIN TRY
                TRUNCATE TABLE {table};
            END TRY
            BEGIN CATCH
            END CATCH;
            """);

        Assert.Equal(fires, findings.Count == 1);
    }

    [Fact]
    public void EmptyCatch_Fires()
    {
        var findings = Scan("""
            BEGIN TRY
                TRUNCATE TABLE dbo.Foo;
            END TRY
            BEGIN CATCH
            END CATCH;
            """);

        Assert.Single(findings);
    }

    [Fact]
    public void CatchDoesUnrelatedWorkButNeverThrows_Fires()
    {
        var findings = Scan("""
            BEGIN TRY
                TRUNCATE TABLE dbo.Foo;
            END TRY
            BEGIN CATCH
                INSERT INTO dbo.ErrorLog (Message) VALUES (ERROR_MESSAGE());
            END CATCH;
            """);

        Assert.Single(findings);
    }

    [Theory]
    [InlineData("EXEC dbo.LogAndRethrow;")]
    [InlineData("EXEC('THROW 50000, ''x'', 1;');")]
    [InlineData("EXEC sp_executesql N'THROW 50000, ''x'', 1;';")]
    public void CatchExecutesCodeThatMayRaise_NeverFires(string statement)
    {
        var findings = Scan($"""
            BEGIN TRY
                TRUNCATE TABLE dbo.Foo;
            END TRY
            BEGIN CATCH
                {statement}
            END CATCH;
            """);

        Assert.Empty(findings);
    }

    [Fact]
    public void CatchExecutesOnlyASystemProcedure_StillFires()
    {
        var findings = Scan("""
            BEGIN TRY
                TRUNCATE TABLE dbo.Foo;
            END TRY
            BEGIN CATCH
                EXEC sp_who;
            END CATCH;
            """);

        Assert.Single(findings);
    }

    [Fact]
    public void CatchWithThrow_NeverFires()
    {
        var findings = Scan("""
            BEGIN TRY
                TRUNCATE TABLE dbo.Foo;
            END TRY
            BEGIN CATCH
                THROW;
            END CATCH;
            """);

        Assert.Empty(findings);
    }

    [Fact]
    public void CatchWithRaiserror_NeverFires()
    {
        var findings = Scan("""
            BEGIN TRY
                TRUNCATE TABLE dbo.Foo;
            END TRY
            BEGIN CATCH
                RAISERROR('failed', 16, 1);
            END CATCH;
            """);

        Assert.Empty(findings);
    }

    [Theory]
    [InlineData("RAISERROR('failed', 10, 1);", true)]
    [InlineData("RAISERROR('failed', 0, 1);", true)]
    [InlineData("RAISERROR('failed', 11, 1);", false)]
    [InlineData("RAISERROR('failed', 16, 1);", false)]
    [InlineData("DECLARE @s INT = 10; RAISERROR('failed', @s, 1);", false)]
    public void CatchWithRaiserror_OnlyASeverityAboveTenReachesTheCaller(string handler, bool fires)
    {
        var findings = Scan($"""
            BEGIN TRY
                TRUNCATE TABLE dbo.Foo;
            END TRY
            BEGIN CATCH
                {handler}
            END CATCH;
            """);

        Assert.Equal(fires, findings.Count == 1);
    }

    [Fact]
    public void ThrowNestedInsideIf_StillNeverFires()
    {
        var findings = Scan("""
            BEGIN TRY
                TRUNCATE TABLE dbo.Foo;
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() = 4712
                BEGIN
                    THROW;
                END
            END CATCH;
            """);

        Assert.Empty(findings);
    }

    [Fact]
    public void NoTruncateInTry_NeverFires()
    {
        var findings = Scan("""
            BEGIN TRY
                SELECT 1;
            END TRY
            BEGIN CATCH
            END CATCH;
            """);

        Assert.Empty(findings);
    }

    [Fact]
    public void TruncateOutsideAnyTry_NeverFires()
    {
        var findings = Scan("TRUNCATE TABLE dbo.Foo;");

        Assert.Empty(findings);
    }

    [Fact]
    public void NestedEmptyCatches_FiresOnceForInnerOnly()
    {
        var findings = Scan("""
            BEGIN TRY
                BEGIN TRY
                    TRUNCATE TABLE dbo.Foo;
                END TRY
                BEGIN CATCH
                END CATCH;
            END TRY
            BEGIN CATCH
            END CATCH;
            """);

        var finding = Assert.Single(findings);
        Assert.Equal(3, finding.Line);
    }

    [Fact]
    public void TripleNestedEmptyCatches_FiresOnceForInnermostOnly()
    {
        var findings = Scan("""
            BEGIN TRY
                BEGIN TRY
                    BEGIN TRY
                        TRUNCATE TABLE dbo.Foo;
                    END TRY
                    BEGIN CATCH
                    END CATCH;
                END TRY
                BEGIN CATCH
                END CATCH;
            END TRY
            BEGIN CATCH
            END CATCH;
            """);

        var finding = Assert.Single(findings);
        Assert.Equal(4, finding.Line);
    }

    [Fact]
    public void NestedInnerCatchRethrows_FiresOnceForOuterOnly()
    {
        var findings = Scan("""
            BEGIN TRY
                BEGIN TRY
                    TRUNCATE TABLE dbo.Foo;
                END TRY
                BEGIN CATCH
                    THROW;
                END CATCH;
            END TRY
            BEGIN CATCH
            END CATCH;
            """);

        var finding = Assert.Single(findings);
        Assert.Equal(3, finding.Line);
    }

    [Fact]
    public void NestedInnerCatchRaisesError_FiresOnceForOuterOnly()
    {
        var findings = Scan("""
            BEGIN TRY
                BEGIN TRY
                    TRUNCATE TABLE dbo.Foo;
                END TRY
                BEGIN CATCH
                    RAISERROR('failed', 16, 1);
                END CATCH;
            END TRY
            BEGIN CATCH
            END CATCH;
            """);

        var finding = Assert.Single(findings);
        Assert.Equal(3, finding.Line);
    }

    [Fact]
    public void NestedBothCatchesPropagate_NeverFires()
    {
        var findings = Scan("""
            BEGIN TRY
                BEGIN TRY
                    TRUNCATE TABLE dbo.Foo;
                END TRY
                BEGIN CATCH
                    THROW;
                END CATCH;
            END TRY
            BEGIN CATCH
                THROW;
            END CATCH;
            """);

        Assert.Empty(findings);
    }
}
