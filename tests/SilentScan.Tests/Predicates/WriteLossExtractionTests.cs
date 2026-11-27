using SilentScan.Core.Catalog;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;
using SilentScan.Core.TypeInference;

namespace SilentScan.Tests.Predicates;

public sealed class WriteLossExtractionTests
{
    private static IReadOnlyList<WriteLossFinding> Extract(params string[] batches) =>
        ExtractAll(batches).WriteLossFindings;

    private static PredicateExtractionResult ExtractAll(params string[] batches)
    {
        var sql = string.Join("\nGO\n", batches);
        var result = SqlScriptParser.ParseText("test.sql", sql);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        var catalog = CatalogBuilder.Build([result]);
        var lineage = LineageResolver.Resolve(catalog, [result]);
        return TypedPredicateExtractor.Extract(result, catalog, lineage);
    }

    [Theory]
    [InlineData("TIME(0)", "'10:00:00.5'", true)]
    [InlineData("DATETIME2(0)", "'2024-01-01 10:00:00.999'", true)]
    [InlineData("DATETIME2(3)", "'2024-01-01 10:00:00.9999'", true)]
    [InlineData("TIME(0)", "'10:00:00'", false)]
    [InlineData("TIME(0)", "'10:00:00.000'", false)]
    [InlineData("DATETIME2(3)", "'2024-01-01 10:00:00.999'", false)]
    [InlineData("DATETIME2(7)", "'2024-01-01 10:00:00.9999999'", false)]
    public void Extract_StringLiteralWithFractionalSecondsIntoNarrowerTemporal_FiresOnlyWhenDigitsAreLost(string targetType, string literal, bool fires)
    {
        var findings = Extract(
            $"CREATE TABLE dbo.T (C {targetType} NULL);",
            $"INSERT INTO dbo.T (C) VALUES ({literal});");

        Assert.Equal(fires, findings.Any(f => f.Kind == WriteLossKind.TemporalScaleNarrowing));
    }

    [Theory]
    [InlineData("FLOAT", "BIGINT")]
    [InlineData("FLOAT", "MONEY")]
    [InlineData("FLOAT", "DECIMAL(16,0)")]
    [InlineData("FLOAT(53)", "DECIMAL(38,4)")]
    [InlineData("REAL", "INT")]
    [InlineData("REAL", "SMALLMONEY")]
    [InlineData("REAL", "DECIMAL(8,0)")]
    [InlineData("FLOAT(24)", "BIGINT")]
    public void Extract_ExactSourceWithMoreDigitsThanTheApproximateTargetKeeps_FlagsPrecisionLoss(string targetType, string sourceType)
    {
        var findings = Extract(
            $"CREATE TABLE dbo.Src (C {sourceType} NULL); CREATE TABLE dbo.Dst (C {targetType} NULL);",
            "INSERT INTO dbo.Dst (C) SELECT C FROM dbo.Src;");

        Assert.Equal(WriteLossKind.ExactToApproximatePrecisionLoss, Assert.Single(findings).Kind);
    }

    [Theory]
    [InlineData("FLOAT", "INT")]
    [InlineData("FLOAT", "SMALLMONEY")]
    [InlineData("FLOAT", "DECIMAL(15,4)")]
    [InlineData("REAL", "SMALLINT")]
    [InlineData("REAL", "DECIMAL(7,0)")]
    [InlineData("DECIMAL(38,0)", "BIGINT")]
    public void Extract_ExactSourceTheApproximateTargetKeeps_DoesNotFlagPrecisionLoss(string targetType, string sourceType) =>
        Assert.Empty(Extract(
            $"CREATE TABLE dbo.Src (C {sourceType} NULL); CREATE TABLE dbo.Dst (C {targetType} NULL);",
            "INSERT INTO dbo.Dst (C) SELECT C FROM dbo.Src;"));

    [Theory]
    [InlineData("FLOAT", "9007199254740993", true)]
    [InlineData("FLOAT", "12345.5", false)]
    [InlineData("REAL", "16777217", true)]
    [InlineData("REAL", "1000000000", false)]
    [InlineData("REAL", "-0.125", false)]
    public void Extract_ExactLiteralIntoApproximateColumn_FlagsOnlyWhenItsDigitsDoNotFit(string targetType, string literal, bool fires)
    {
        var findings = Extract($"CREATE TABLE dbo.Dst (C {targetType} NULL);", $"INSERT INTO dbo.Dst (C) VALUES ({literal});");

        Assert.Equal(fires, findings.Any(f => f.Kind == WriteLossKind.ExactToApproximatePrecisionLoss));
    }

    [Theory]
    [InlineData("SET ANSI_WARNINGS OFF;\n    INSERT INTO dbo.T (C) SELECT W FROM dbo.S;", true)]
    [InlineData("SET ANSI_WARNINGS OFF;\n    UPDATE dbo.T SET C = (SELECT MAX(W) FROM dbo.S);", true)]
    [InlineData("SET ANSI_WARNINGS OFF;\n    SET ANSI_WARNINGS ON;\n    INSERT INTO dbo.T (C) SELECT W FROM dbo.S;", false)]
    [InlineData("INSERT INTO dbo.T (C) SELECT W FROM dbo.S;\n    SET ANSI_WARNINGS OFF;", false)]
    [InlineData("IF @f = 1 SET ANSI_WARNINGS OFF;\n    INSERT INTO dbo.T (C) SELECT W FROM dbo.S;", false)]
    public void Extract_LongerValueIntoColumn_FlagsLengthTruncationOnlyWithAnsiWarningsOff(string body, bool fires)
    {
        var findings = Extract(
            "CREATE TABLE dbo.S (W VARCHAR(20) NULL); CREATE TABLE dbo.T (C VARCHAR(3) NULL);",
            $"CREATE PROCEDURE dbo.P @f INT AS\nBEGIN\n    {body}\nEND");

        Assert.Equal(fires, findings.Any(f => f.Kind == WriteLossKind.LengthTruncation));
    }

    [Fact]
    public void Extract_InsertValuesWithNonAsciiUnicodeLiteralIntoVarchar_FlagsUnicodeReplacement()
    {
        var findings = Extract(
            "CREATE TABLE dbo.T (VarCol VARCHAR(20) NULL);",
            "INSERT INTO dbo.T (VarCol) VALUES (N'日本語');");

        var finding = Assert.Single(findings);
        Assert.Equal(WriteLossKind.UnicodeToNonUnicodeReplacement, finding.Kind);
        Assert.Equal("dbo.T", finding.TableQualifiedName);
        Assert.Equal("VarCol", finding.ColumnName);
    }

    [Fact]
    public void Extract_InsertValuesWithAsciiOnlyUnicodeLiteralIntoVarchar_ProvablySafe_NoFinding()
    {

        var findings = Extract(
            "CREATE TABLE dbo.T (VarCol VARCHAR(20) NULL);",
            "INSERT INTO dbo.T (VarCol) VALUES (N'hello');");

        Assert.Empty(findings);
    }

    [Fact]
    public void Extract_InsertValuesWithNonAsciiLiteralWithinTargetCodePage_ProvablySafe_NoFinding()
    {

        var findings = Extract(
            "CREATE TABLE dbo.T (VarCol VARCHAR(20) COLLATE Latin1_General_100_CI_AS NULL);",
            "INSERT INTO dbo.T (VarCol) VALUES (N'café');");

        Assert.Empty(findings);
    }

    [Fact]
    public void Extract_InsertValuesWithUnicodeColumnIntoVarchar_NonLiteral_AlwaysFlagged()
    {

        var findings = Extract(
            "CREATE TABLE dbo.Src (NCol NVARCHAR(20) NULL); CREATE TABLE dbo.Dst (VarCol VARCHAR(20) NULL);",
            "INSERT INTO dbo.Dst (VarCol) SELECT NCol FROM dbo.Src;");

        var finding = Assert.Single(findings);
        Assert.Equal(WriteLossKind.UnicodeToNonUnicodeReplacement, finding.Kind);
        Assert.Equal("dbo.Dst", finding.TableQualifiedName);
    }

    [Fact]
    public void Extract_QuotenameOverVarcharColumnOfSameCodePageIntoVarchar_ProvablySafe_NoFinding()
    {
        var findings = Extract(
            "CREATE TABLE dbo.Src (VarCol VARCHAR(20) COLLATE Latin1_General_100_CI_AS NULL); CREATE TABLE dbo.Dst (VarCol VARCHAR(40) COLLATE Latin1_General_100_CI_AS NULL);",
            "INSERT INTO dbo.Dst (VarCol) SELECT QUOTENAME(VarCol) FROM dbo.Src;");

        Assert.Empty(findings);
    }

    [Fact]
    public void Extract_ConcatenatedAsciiLiteralsAndQuotenameOverVarcharIntoVarchar_ProvablySafe_NoFinding()
    {
        var findings = Extract(
            "CREATE TABLE dbo.Src (VarCol VARCHAR(20) NULL); CREATE TABLE dbo.Dst (VarCol VARCHAR(80) NULL);",
            "INSERT INTO dbo.Dst (VarCol) SELECT N'x.' + QUOTENAME(VarCol) FROM dbo.Src;");

        Assert.Empty(findings);
    }

    [Fact]
    public void Extract_QuotenameOverUnicodeColumnIntoVarchar_StillFlagged()
    {
        var findings = Extract(
            "CREATE TABLE dbo.Src (NCol NVARCHAR(20) NULL); CREATE TABLE dbo.Dst (VarCol VARCHAR(40) NULL);",
            "INSERT INTO dbo.Dst (VarCol) SELECT QUOTENAME(NCol) FROM dbo.Src;");

        var finding = Assert.Single(findings);
        Assert.Equal(WriteLossKind.UnicodeToNonUnicodeReplacement, finding.Kind);
    }

    [Fact]
    public void Extract_QuotenameOverVarcharColumnOfDifferentCodePageIntoVarchar_StillFlagged()
    {
        var findings = Extract(
            "CREATE TABLE dbo.Src (VarCol VARCHAR(20) COLLATE Japanese_CI_AS NULL); CREATE TABLE dbo.Dst (VarCol VARCHAR(40) COLLATE Latin1_General_100_CI_AS NULL);",
            "INSERT INTO dbo.Dst (VarCol) SELECT QUOTENAME(VarCol) FROM dbo.Src;");

        var finding = Assert.Single(findings);
        Assert.Equal(WriteLossKind.UnicodeToNonUnicodeReplacement, finding.Kind);
    }

    [Fact]
    public void Extract_InsertValuesWithFractionalNumericLiteralIntoInt_FlagsNumericScaleNarrowing()
    {

        var findings = Extract(
            "CREATE TABLE dbo.T (IntCol INT NULL);",
            "INSERT INTO dbo.T (IntCol) VALUES (7.9);");

        var finding = Assert.Single(findings);
        Assert.Equal(WriteLossKind.NumericScaleNarrowing, finding.Kind);
    }

    [Fact]
    public void Extract_InsertValuesWithFractionalScientificNotationLiteralIntoInt_FlagsApproximateTruncation()
    {

        var findings = Extract(
            "CREATE TABLE dbo.T (IntCol INT NULL);",
            "INSERT INTO dbo.T (IntCol) VALUES (7.9e0);");

        var finding = Assert.Single(findings);
        Assert.Equal(WriteLossKind.ApproximateToExactTruncation, finding.Kind);
    }

    [Fact]
    public void Extract_InsertValuesWithWholeNumberDecimalLiteralIntoInt_ProvablySafe_NoFinding()
    {
        var findings = Extract(
            "CREATE TABLE dbo.T (IntCol INT NULL);",
            "INSERT INTO dbo.T (IntCol) VALUES (7.0);");

        Assert.Empty(findings);
    }

    [Fact]
    public void Extract_InsertValuesWithHigherScaleDecimalLiteral_FlagsNumericScaleNarrowing()
    {
        var findings = Extract(
            "CREATE TABLE dbo.T (DecCol DECIMAL(10,2) NULL);",
            "INSERT INTO dbo.T (DecCol) VALUES (123.456);");

        var finding = Assert.Single(findings);
        Assert.Equal(WriteLossKind.NumericScaleNarrowing, finding.Kind);
    }

    [Fact]
    public void Extract_InsertValuesWithinTargetScale_ProvablySafe_NoFinding()
    {
        var findings = Extract(
            "CREATE TABLE dbo.T (DecCol DECIMAL(10,2) NULL);",
            "INSERT INTO dbo.T (DecCol) VALUES (123.40);");

        Assert.Empty(findings);
    }

    [Fact]
    public void Extract_InsertValuesWithDateTimeLiteralIntoDate_FlagsTemporalPrecisionLoss()
    {
        var findings = Extract(
            "CREATE TABLE dbo.T (DateCol DATE NULL);",
            "INSERT INTO dbo.T (DateCol) VALUES ('2024-01-15 13:45:00');");

        var finding = Assert.Single(findings);
        Assert.Equal(WriteLossKind.TemporalPrecisionLoss, finding.Kind);
    }

    [Fact]
    public void Extract_InsertValuesWithMidnightTimeLiteralIntoDate_ProvablySafe_NoFinding()
    {
        var findings = Extract(
            "CREATE TABLE dbo.T (DateCol DATE NULL);",
            "INSERT INTO dbo.T (DateCol) VALUES ('2024-01-15T00:00:00');");

        Assert.Empty(findings);
    }

    [Theory]
    [InlineData("DECLARE @d DATE = CONVERT(DATETIME, CONVERT(CHAR(8), 20240115));")]
    [InlineData("DECLARE @i INT = 20240115; DECLARE @d DATE = CAST(CAST(@i AS VARCHAR(8)) AS DATETIME);")]
    [InlineData("DECLARE @s DATE = '2024-01-01'; DECLARE @d DATE = CAST(@s AS DATETIME);")]
    [InlineData("DECLARE @d DATE = DATEADD(DAY, DATEDIFF(DAY, 0, GETDATE()), 0);")]
    [InlineData("DECLARE @d DATE = DATEADD(MONTH, DATEDIFF(MONTH, 0, GETDATE()), 0);")]
    [InlineData("DECLARE @d DATE = DATEADD(DAY, 1, DATEADD(DAY, DATEDIFF(DAY, 0, GETDATE()), 0));")]
    [InlineData("DECLARE @d DATE = DATEADD(DAY, DATEDIFF(DAY, '19000101', GETDATE()), '19000101');")]
    [InlineData("DECLARE @d DATE = DATEADD(DAY, 5, 1);")]
    public void Extract_DateTimeSourceThatIsProvablyMidnight_IntoDate_NoFinding(string statement)
    {
        Assert.Empty(Extract(statement));
    }

    [Theory]
    [InlineData("DECLARE @d DATE = GETDATE();")]
    [InlineData("DECLARE @t DATETIME = GETDATE(); DECLARE @d DATE = DATEADD(DAY, 1, @t);")]
    [InlineData("DECLARE @d DATE = DATEADD(HOUR, DATEDIFF(HOUR, 0, GETDATE()), 0);")]
    [InlineData("DECLARE @s VARCHAR(30) = 'x'; DECLARE @d DATE = CAST(@s AS DATETIME);")]
    [InlineData("DECLARE @d DATE = CAST(CAST(GETDATE() AS VARCHAR(8)) AS DATETIME);")]
    [InlineData("DECLARE @t DATETIME = GETDATE(); DECLARE @d DATE = CAST(@t AS DATETIME);")]
    public void Extract_DateTimeSourceThatMayCarryATime_IntoDate_FlagsTemporalPrecisionLoss(string statement)
    {
        var finding = Assert.Single(Extract(statement));

        Assert.Equal(WriteLossKind.TemporalPrecisionLoss, finding.Kind);
    }

    [Fact]
    public void Extract_InsertValuesWithDateOnlyLiteralIntoDate_ProvablySafe_NoFinding()
    {
        var findings = Extract(
            "CREATE TABLE dbo.T (DateCol DATE NULL);",
            "INSERT INTO dbo.T (DateCol) VALUES ('2024-01-15');");

        Assert.Empty(findings);
    }

    [Fact]
    public void Extract_InsertValuesWithSafeTypePair_NoFinding()
    {
        var findings = Extract(
            "CREATE TABLE dbo.T (IntCol INT NULL);",
            "INSERT INTO dbo.T (IntCol) VALUES (7);");

        Assert.Empty(findings);
    }

    [Fact]
    public void Extract_InsertWithoutExplicitColumnList_PairsPositionallyByDeclaredOrder()
    {
        var findings = Extract(
            "CREATE TABLE dbo.T (IntCol INT NULL, VarCol VARCHAR(20) NULL);",
            "INSERT INTO dbo.T VALUES (1, N'日本語');");

        var finding = Assert.Single(findings);
        Assert.Equal("VarCol", finding.ColumnName);
    }

    [Fact]
    public void Extract_InsertWithDefaultKeyword_SkippedWithoutFinding()
    {
        var findings = Extract(
            "CREATE TABLE dbo.T (IntCol INT NOT NULL DEFAULT 0);",
            "INSERT INTO dbo.T (IntCol) VALUES (DEFAULT);");

        Assert.Empty(findings);
    }

    [Fact]
    public void Extract_UpdateSetWithLossyLiteral_FlagsWriteLoss()
    {
        var findings = Extract(
            "CREATE TABLE dbo.T (DecCol DECIMAL(10,2) NULL);",
            "UPDATE dbo.T SET DecCol = 123.456 WHERE DecCol IS NULL;");

        var finding = Assert.Single(findings);
        Assert.Equal(WriteLossKind.NumericScaleNarrowing, finding.Kind);
        Assert.Equal("dbo.T", finding.TableQualifiedName);
        Assert.Equal("DecCol", finding.ColumnName);
    }

    [Fact]
    public void Extract_UpdateSetFromJoinedTable_ResolvesSourceThroughFromClause()
    {
        var findings = Extract(
            """
            CREATE TABLE dbo.Target (Id INT NOT NULL, VarCol VARCHAR(20) NULL);
            CREATE TABLE dbo.Src (Id INT NOT NULL, NCol NVARCHAR(20) NULL);
            """,
            "UPDATE t SET t.VarCol = s.NCol FROM dbo.Target t JOIN dbo.Src s ON s.Id = t.Id;");

        var finding = Assert.Single(findings);
        Assert.Equal(WriteLossKind.UnicodeToNonUnicodeReplacement, finding.Kind);
        Assert.Equal("dbo.Target", finding.TableQualifiedName);
    }

    [Fact]
    public void Extract_UpdateSetSafeAssignment_NoFinding()
    {
        var findings = Extract(
            "CREATE TABLE dbo.T (IntCol INT NULL);",
            "UPDATE dbo.T SET IntCol = 7 WHERE IntCol IS NULL;");

        Assert.Empty(findings);
    }

    [Fact]
    public void Extract_InsertSelectColumnToColumn_FlagsWriteLossThroughSelectList()
    {
        var findings = Extract(
            """
            CREATE TABLE dbo.Src (Amount DECIMAL(10,4) NOT NULL);
            CREATE TABLE dbo.Dst (Amount DECIMAL(10,2) NULL);
            """,
            "INSERT INTO dbo.Dst (Amount) SELECT Amount FROM dbo.Src;");

        var finding = Assert.Single(findings);
        Assert.Equal(WriteLossKind.NumericScaleNarrowing, finding.Kind);
        Assert.Equal("dbo.Dst", finding.TableQualifiedName);
    }

    [Fact]
    public void Extract_InsertSelectWithWhereClause_StillFindsPredicateAndWriteLoss()
    {

        var result = ExtractAll(
            """
            CREATE TABLE dbo.Src (Amount DECIMAL(10,4) NOT NULL, Flag INT NOT NULL);
            CREATE TABLE dbo.Dst (Amount DECIMAL(10,2) NULL);
            """,
            "INSERT INTO dbo.Dst (Amount) SELECT Amount FROM dbo.Src WHERE Flag = 1;");

        Assert.Single(result.WriteLossFindings);
    }

    [Fact]
    public void Extract_InsertSelectStar_FlagsNarrowedColumn()
    {
        var result = ExtractAll(
            """
            CREATE TABLE dbo.Src (Amount DECIMAL(10,4) NOT NULL);
            CREATE TABLE dbo.Dst (Amount DECIMAL(10,2) NULL);
            """,
            "INSERT INTO dbo.Dst SELECT * FROM dbo.Src;");

        Assert.Equal(WriteLossKind.NumericScaleNarrowing, Assert.Single(result.WriteLossFindings).Kind);
        Assert.DoesNotContain(result.SkippedConstructs, s => s.ConstructKind == "write source");
    }

    [Fact]
    public void Extract_InsertSelectUnion_FlagsNarrowingBranch()
    {
        var result = ExtractAll(
            "CREATE TABLE dbo.Dst (Amount DECIMAL(10,2) NULL);",
            "INSERT INTO dbo.Dst (Amount) SELECT 123.456 UNION SELECT 1.00;");

        Assert.Equal(WriteLossKind.NumericScaleNarrowing, Assert.Single(result.WriteLossFindings).Kind);
        Assert.DoesNotContain(result.SkippedConstructs, s => s.ConstructKind == "write source");
    }

    [Fact]
    public void Extract_InsertIntoUnresolvedTable_LedgeredNotAnalyzed()
    {
        var result = ExtractAll("INSERT INTO dbo.NeverDeclared (Col) VALUES (1);");

        Assert.Empty(result.WriteLossFindings);
        Assert.Contains(result.SkippedConstructs, s => s.ConstructKind == "write target");
    }

    [Fact]
    public void Extract_InsertWithUnresolvedTargetColumn_LedgeredNotAnalyzed()
    {
        var result = ExtractAll(
            "CREATE TABLE dbo.T (IntCol INT NULL);",
            "INSERT INTO dbo.T (NeverDeclaredCol) VALUES (1);");

        Assert.Empty(result.WriteLossFindings);
        Assert.Contains(result.SkippedConstructs, s => s.ConstructKind == "write target");
    }

    [Fact]
    public void Extract_InsertWithCteContainingSelectStar_DoesNotThrow()
    {

        var findings = Extract(
            "CREATE TABLE dbo.Src (a INT NULL, b INT NULL, c INT NULL); CREATE TABLE dbo.T (a INT NULL, b INT NULL, c INT NULL);",
            "WITH cte AS (SELECT * FROM dbo.Src) INSERT INTO dbo.T (a, b, c) SELECT a, b, c FROM cte;");

        Assert.Empty(findings);
    }

    [Fact]
    public void Extract_InsertWithCteContainingSelectStar_LossySource_FlagsWriteLoss()
    {

        var findings = Extract(
            "CREATE TABLE dbo.Src (NCol NVARCHAR(20) NULL); CREATE TABLE dbo.T (VarCol VARCHAR(20) NULL);",
            "WITH cte AS (SELECT * FROM dbo.Src) INSERT INTO dbo.T (VarCol) SELECT NCol FROM cte;");

        var finding = Assert.Single(findings);
        Assert.Equal(WriteLossKind.UnicodeToNonUnicodeReplacement, finding.Kind);
        Assert.Equal("dbo.T", finding.TableQualifiedName);
        Assert.Equal("VarCol", finding.ColumnName);
    }

    [Fact]
    public void Extract_SetVariableWithLossyLiteral_FlagsWriteLoss()
    {
        var findings = Extract("DECLARE @v DECIMAL(10,2); SET @v = 123.456;");

        var finding = Assert.Single(findings);
        Assert.Equal(WriteLossKind.NumericScaleNarrowing, finding.Kind);
        Assert.Null(finding.TableQualifiedName);
        Assert.Equal("@v", finding.ColumnName);
    }

    [Fact]
    public void Extract_SetVariableWithinDeclaredScale_NoFinding()
    {
        var findings = Extract("DECLARE @v DECIMAL(10,2); SET @v = 123.40;");

        Assert.Empty(findings);
    }

    [Fact]
    public void Extract_SetVariableFromScalarSubquery_NonLiteral_AlwaysFlagged()
    {
        var findings = Extract(
            "CREATE TABLE dbo.T (NCol NVARCHAR(20) NULL);",
            "DECLARE @v VARCHAR(20); SET @v = (SELECT TOP 1 NCol FROM dbo.T);");

        var finding = Assert.Single(findings);
        Assert.Equal(WriteLossKind.UnicodeToNonUnicodeReplacement, finding.Kind);
        Assert.Equal("@v", finding.ColumnName);
    }

    [Fact]
    public void Extract_SetVariableFromWiderVariable_FlagsLengthTruncation()
    {
        var findings = Extract("DECLARE @src VARCHAR(10) = 'HelloWorld'; DECLARE @v VARCHAR(3); SET @v = @src;");

        var finding = Assert.Single(findings);
        Assert.Equal(WriteLossKind.LengthTruncation, finding.Kind);
        Assert.Null(finding.TableQualifiedName);
        Assert.Equal("@v", finding.ColumnName);
    }

    [Fact]
    public void Extract_UpdateColumnFromWiderVariable_TableColumnTarget_NeverFlagsLengthTruncation()
    {
        var findings = Extract(
            "CREATE TABLE dbo.T (Col VARCHAR(3) NULL);",
            "DECLARE @src VARCHAR(10) = 'HelloWorld'; UPDATE dbo.T SET Col = @src;");

        Assert.Empty(findings);
    }

    [Fact]
    public void Extract_SetVariableFromStringAggWithNoMaxOperand_FlagsLengthTruncation()
    {
        var findings = Extract(
            "CREATE TABLE dbo.T (Col VARCHAR(10) NULL);",
            "DECLARE @v VARCHAR(20); SET @v = (SELECT STRING_AGG(Col, ',') FROM dbo.T);");

        var finding = Assert.Single(findings);
        Assert.Equal(WriteLossKind.LengthTruncation, finding.Kind);
        Assert.Null(finding.TableQualifiedName);
        Assert.Equal("@v", finding.ColumnName);
        Assert.Equal(8000, finding.SourceType.Length);
    }

    [Fact]
    public void Extract_SetVariableFromStringAggWithMaxOperand_ReportsTheMaxResultCut()
    {
        var findings = Extract(
            "CREATE TABLE dbo.T (Col VARCHAR(MAX) NULL);",
            "DECLARE @v VARCHAR(20); SET @v = (SELECT STRING_AGG(Col, ',') FROM dbo.T);");

        var finding = Assert.Single(findings);
        Assert.Equal(WriteLossKind.LengthTruncation, finding.Kind);
        Assert.True(finding.SourceType.IsMax);
    }

    [Fact]
    public void Extract_SetVariableUndeclared_LedgeredOrSkippedWithoutThrowing()
    {
        var findings = Extract("SET @never_declared = 123.456;");

        Assert.Empty(findings);
    }

    [Fact]
    public void Extract_SetVariableCompoundAssignmentWithLossyLiteral_FlagsWriteLoss()
    {
        var findings = Extract("DECLARE @v DECIMAL(10,2) = 0; SET @v += 123.456;");

        var finding = Assert.Single(findings);
        Assert.Equal(WriteLossKind.NumericScaleNarrowing, finding.Kind);
        Assert.Null(finding.TableQualifiedName);
        Assert.Equal("@v", finding.ColumnName);
    }

    [Fact]
    public void Extract_SetVariableCompoundAssignmentWithinScale_NoFinding()
    {
        var findings = Extract("DECLARE @v DECIMAL(10,2) = 0; SET @v += 1;");

        Assert.Empty(findings);
    }

    [Fact]
    public void Extract_UpdateSetCompoundAssignmentWithLossyLiteral_FlagsWriteLoss()
    {
        var findings = Extract(
            "CREATE TABLE dbo.T (DecCol DECIMAL(10,2) NULL);",
            "UPDATE dbo.T SET DecCol += 123.456 WHERE DecCol IS NOT NULL;");

        var finding = Assert.Single(findings);
        Assert.Equal(WriteLossKind.NumericScaleNarrowing, finding.Kind);
        Assert.Equal("dbo.T", finding.TableQualifiedName);
        Assert.Equal("DecCol", finding.ColumnName);
    }

    [Fact]
    public void Extract_UpdateSetCompoundAssignmentWithinScale_NoFinding()
    {
        var findings = Extract(
            "CREATE TABLE dbo.T (DecCol DECIMAL(10,2) NULL);",
            "UPDATE dbo.T SET DecCol += 1 WHERE DecCol IS NOT NULL;");

        Assert.Empty(findings);
    }

    [Fact]
    public void Extract_SetVariableFromCaseMergingDecimalBranchesOfDifferingScale_FlagsNumericScaleNarrowing()
    {
        var findings = Extract(
            "CREATE TABLE dbo.T (A DECIMAL(9,2) NULL, B DECIMAL(9,4) NULL);",
            "DECLARE @v DECIMAL(9,2); SET @v = (SELECT TOP 1 CASE WHEN A IS NULL THEN A ELSE B END FROM dbo.T);");

        var finding = Assert.Single(findings);
        Assert.Equal(WriteLossKind.NumericScaleNarrowing, finding.Kind);
        Assert.Null(finding.TableQualifiedName);
        Assert.Equal("@v", finding.ColumnName);
        Assert.Equal(4, finding.SourceType.Scale);
    }

    [Fact]
    public void Extract_SetVariableFromAvgOverRealColumn_FlagsNumericScaleNarrowing()
    {
        var findings = Extract(
            "CREATE TABLE dbo.T (R REAL NULL);",
            "DECLARE @v REAL; SET @v = (SELECT AVG(R) FROM dbo.T);");

        var finding = Assert.Single(findings);
        Assert.Equal(WriteLossKind.NumericScaleNarrowing, finding.Kind);
        Assert.Null(finding.TableQualifiedName);
        Assert.Equal("@v", finding.ColumnName);
        Assert.Equal(SqlTypeCategory.Float, finding.SourceType.Category);
    }

    [Fact]
    public void Extract_SetVariableFromCaseMergingCrossCategoryStringBranches_FlagsLengthTruncation()
    {
        var findings = Extract(
            "CREATE TABLE dbo.T (A CHAR(20) NULL, B VARCHAR(3) NULL);",
            "DECLARE @v VARCHAR(5); SET @v = (SELECT TOP 1 CASE WHEN A IS NULL THEN A ELSE B END FROM dbo.T);");

        var finding = Assert.Single(findings);
        Assert.Equal(WriteLossKind.LengthTruncation, finding.Kind);
        Assert.Null(finding.TableQualifiedName);
        Assert.Equal("@v", finding.ColumnName);
        Assert.Equal(20, finding.SourceType.Length);
    }

    [Theory]
    [InlineData("DECLARE @t DATETIME = GETDATE(); DECLARE @s SMALLDATETIME = @t;")]
    [InlineData("DECLARE @t DATETIME2(3) = SYSDATETIME(); DECLARE @s SMALLDATETIME = @t;")]
    [InlineData("DECLARE @s SMALLDATETIME = '2024-01-15 13:45:30';")]
    [InlineData("DECLARE @s SMALLDATETIME = '2024-01-15 13:45:00.500';")]
    public void Extract_SourceThatMayCarrySeconds_IntoSmallDateTime_FlagsSmallDateTimeRounding(string statement)
    {
        var finding = Assert.Single(Extract(statement), f => f.Kind == WriteLossKind.SmallDateTimeRounding);

        Assert.Equal(SqlTypeCategory.SmallDateTime, finding.TargetType.Category);
    }

    [Fact]
    public void Extract_SysDateTimeIntoNarrowerDateTime2_FlagsTheLostFractionalSeconds()
    {
        var findings = Extract("DECLARE @t DATETIME2(3) = SYSDATETIME();");

        var finding = Assert.Single(findings);
        Assert.Equal(7, finding.SourceType.Scale);
        Assert.Equal(3, finding.TargetType.Scale);
    }

    [Theory]
    [InlineData("DECLARE @s SMALLDATETIME = '2024-01-15 13:45';")]
    [InlineData("DECLARE @s SMALLDATETIME = '2024-01-15 13:45:00';")]
    [InlineData("DECLARE @s SMALLDATETIME = '2024-01-15';")]
    [InlineData("DECLARE @t SMALLDATETIME = '2024-01-15 13:45'; DECLARE @s SMALLDATETIME = @t;")]
    [InlineData("DECLARE @t DATE = GETDATE(); DECLARE @s SMALLDATETIME = @t;")]
    [InlineData("DECLARE @s SMALLDATETIME = DATEADD(DAY, DATEDIFF(DAY, 0, GETDATE()), 0);")]
    public void Extract_SourceThatCannotCarrySeconds_IntoSmallDateTime_NoFinding(string statement)
    {
        Assert.DoesNotContain(Extract(statement), f => f.Kind == WriteLossKind.SmallDateTimeRounding);
    }

    [Fact]
    public void Extract_InsertDateTime2ColumnIntoSmallDateTimeColumn_FlagsSmallDateTimeRounding()
    {
        var finding = Assert.Single(Extract(
            "CREATE TABLE dbo.Src (At DATETIME2(3) NOT NULL);",
            "CREATE TABLE dbo.Dst (At SMALLDATETIME NOT NULL);",
            "INSERT INTO dbo.Dst (At) SELECT At FROM dbo.Src;"));

        Assert.Equal(WriteLossKind.SmallDateTimeRounding, finding.Kind);
    }

    [Theory]
    [InlineData("DECLARE @c VARCHAR(20) = 'x'; DECLARE @i INT = 1; DECLARE @m VARCHAR(4000) = ISNULL(QUOTENAME(@c, ''''), 'NULL') + ', ' + ISNULL(CAST(@i AS VARCHAR(255)), 'NULL');")]
    [InlineData("DECLARE @i INT = 1; DECLARE @m VARCHAR(50) = COALESCE(CAST(@i AS NVARCHAR(20)), 'none');")]
    [InlineData("DECLARE @c VARCHAR(20) = 'x'; DECLARE @m VARCHAR(50) = ISNULL(QUOTENAME(@c), NULL);")]
    public void Extract_NullHandlingAroundRepresentableNvarcharPieces_IntoVarchar_NoUnicodeFinding(string statement)
    {
        Assert.DoesNotContain(Extract(statement), f => f.Kind == WriteLossKind.UnicodeToNonUnicodeReplacement);
    }

    [Theory]
    [InlineData("DECLARE @n NVARCHAR(20) = N'x'; DECLARE @m VARCHAR(50) = ISNULL(@n, 'NULL');")]
    [InlineData("DECLARE @n NVARCHAR(20) = N'x'; DECLARE @m VARCHAR(50) = ISNULL(QUOTENAME(@n), 'NULL');")]
    [InlineData("DECLARE @n NVARCHAR(20) = N'x'; DECLARE @m VARCHAR(50) = COALESCE(NULL, @n);")]
    public void Extract_NullHandlingAroundGenuinelyUnicodePiece_IntoVarchar_FlagsUnicodeReplacement(string statement)
    {
        Assert.Contains(Extract(statement), f => f.Kind == WriteLossKind.UnicodeToNonUnicodeReplacement && f.ColumnName == "@m");
    }

    [Theory]
    [InlineData("VARCHAR(3)", "DECLARE @v VARCHAR(10) = 'abcdef'; RETURN @v;", WriteLossKind.LengthTruncation)]
    [InlineData("NVARCHAR(3)", "DECLARE @v NVARCHAR(10) = N'abcdef'; RETURN @v;", WriteLossKind.LengthTruncation)]
    [InlineData("DECIMAL(5, 1)", "DECLARE @v DECIMAL(10, 4) = 1.2345; RETURN @v;", WriteLossKind.NumericScaleNarrowing)]
    public void Extract_ScalarFunctionReturnsWiderValueThanDeclaredReturnType_FlagsTheReturnedValue(string returnType, string body, WriteLossKind expected)
    {
        var finding = Assert.Single(Extract($"CREATE FUNCTION dbo.F() RETURNS {returnType} AS BEGIN {body} END"));

        Assert.Equal(expected, finding.Kind);
        Assert.Null(finding.TableQualifiedName);
        Assert.Equal("RETURN", finding.ColumnName);
        Assert.Equal(WriteLossTarget.FunctionReturn, finding.Target);
    }

    [Theory]
    [InlineData("VARCHAR(10)", "DECLARE @v VARCHAR(3) = 'abc'; RETURN @v;")]
    [InlineData("VARCHAR(3)", "DECLARE @v VARCHAR(3) = 'abc'; RETURN @v;")]
    [InlineData("DECIMAL(10, 4)", "DECLARE @v DECIMAL(5, 1) = 1.2; RETURN @v;")]
    [InlineData("INT", "DECLARE @v INT = 1; RETURN @v;")]
    public void Extract_ScalarFunctionReturnsValueThatFitsTheDeclaredReturnType_NoFinding(string returnType, string body)
    {
        Assert.Empty(Extract($"CREATE FUNCTION dbo.F() RETURNS {returnType} AS BEGIN {body} END"));
    }

    [Fact]
    public void Extract_ProcedureReturnStatus_NoFinding()
    {
        Assert.Empty(Extract("CREATE PROCEDURE dbo.P AS BEGIN DECLARE @v DECIMAL(10, 4) = 1.2345; RETURN 1; END"));
    }

    [Fact]
    public void Extract_MultiStatementTableFunctionBareReturn_NoFinding()
    {
        Assert.Empty(Extract("CREATE FUNCTION dbo.F() RETURNS @t TABLE (S VARCHAR(3)) AS BEGIN INSERT @t (S) VALUES ('a'); RETURN; END"));
    }

    [Theory]
    [InlineData("DECLARE @l VARCHAR(10) = 'abcdef'; DECLARE @v VARCHAR(3); SELECT @v = @l;", WriteLossKind.LengthTruncation)]
    [InlineData("CREATE TABLE #t (C VARCHAR(10)); DECLARE @v VARCHAR(3); SELECT @v = C FROM #t;", WriteLossKind.LengthTruncation)]
    [InlineData("DECLARE @d DECIMAL(10, 4) = 1.2345; DECLARE @v DECIMAL(5, 1); SELECT @v = @d;", WriteLossKind.NumericScaleNarrowing)]
    public void Extract_SelectAssignmentOfWiderValueIntoNarrowerVariable_FlagsTheVariable(string body, WriteLossKind expected)
    {
        var finding = Assert.Single(Extract(body));

        Assert.Equal(expected, finding.Kind);
        Assert.Equal("@v", finding.ColumnName);
        Assert.Equal(WriteLossTarget.Variable, finding.Target);
    }

    [Theory]
    [InlineData("DECLARE @l VARCHAR(3) = 'abc'; DECLARE @v VARCHAR(10); SELECT @v = @l;")]
    [InlineData("DECLARE @v INT; SELECT @v = 1;")]
    public void Extract_SelectAssignmentThatFits_NoFinding(string body)
    {
        Assert.Empty(Extract(body));
    }

    [Theory]
    [InlineData("@a VARCHAR(3) = 'abcdef'", WriteLossKind.LengthTruncation)]
    [InlineData("@a DECIMAL(5, 1) = 1.2345", WriteLossKind.NumericScaleNarrowing)]
    public void Extract_ParameterDefaultWiderThanDeclaredType_FlagsTheParameter(string parameter, WriteLossKind expected)
    {
        var finding = Assert.Single(Extract($"CREATE PROCEDURE dbo.P {parameter} AS SELECT @a;"));

        Assert.Equal(expected, finding.Kind);
        Assert.Equal("@a", finding.ColumnName);
        Assert.Equal(WriteLossTarget.ParameterDefault, finding.Target);
    }

    [Theory]
    [InlineData("@a VARCHAR(10) = 'abc'")]
    [InlineData("@a VARCHAR(3) = 'abc'")]
    [InlineData("@a DECIMAL(5, 2) = 1.25")]
    [InlineData("@a INT = 1")]
    [InlineData("@a VARCHAR(3)")]
    public void Extract_ParameterDefaultThatFits_NoFinding(string parameter)
    {
        Assert.Empty(Extract($"CREATE PROCEDURE dbo.P {parameter} AS SELECT @a;"));
    }

    [Fact]
    public void Extract_ScalarFunctionReturnsFloatThroughIntegerReturnType_FlagsApproximateTruncation()
    {
        var finding = Assert.Single(Extract("CREATE FUNCTION dbo.F() RETURNS INT AS BEGIN DECLARE @f FLOAT = 1.9; RETURN @f; END"));

        Assert.Equal(WriteLossKind.ApproximateToExactTruncation, finding.Kind);
        Assert.Equal(WriteLossTarget.FunctionReturn, finding.Target);
    }

    [Fact]
    public void Extract_FloatParameterDefaultIntoIntegerParameter_FlagsApproximateTruncation()
    {
        var finding = Assert.Single(Extract("CREATE PROCEDURE dbo.P @a INT = 1.9E0 AS SELECT @a;"));

        Assert.Equal(WriteLossKind.ApproximateToExactTruncation, finding.Kind);
        Assert.Equal(WriteLossTarget.ParameterDefault, finding.Target);
    }

    [Theory]
    [InlineData("CREATE PROCEDURE dbo.p @v DECIMAL(10,4) AS DECLARE @t TABLE (Amt DECIMAL(10,2)); INSERT INTO @t (Amt) VALUES (@v);", "@t")]
    [InlineData("CREATE FUNCTION dbo.f (@v DECIMAL(10,4)) RETURNS @r TABLE (Amt DECIMAL(10,2)) AS BEGIN INSERT INTO @r (Amt) VALUES (@v); RETURN; END;", "@r")]
    [InlineData("CREATE FUNCTION dbo.f (@v DECIMAL(10,4)) RETURNS @r TABLE (Amt DECIMAL(10,2)) AS BEGIN INSERT INTO @r SELECT @v; RETURN; END;", "@r")]
    public void Extract_InsertIntoTableVariable_IsAnalysedLikeATable(string module, string target)
    {
        var result = ExtractAll(module);

        var finding = Assert.Single(result.WriteLossFindings);
        Assert.Equal(WriteLossKind.NumericScaleNarrowing, finding.Kind);
        Assert.Equal(target, finding.TableQualifiedName);
        Assert.DoesNotContain(result.SkippedConstructs, s => s.ConstructKind == "write target");
    }

    [Fact]
    public void Extract_InsertIntoTableVariableOfSameScale_NoFinding()
    {
        var result = ExtractAll("CREATE PROCEDURE dbo.p @v DECIMAL(10,4) AS DECLARE @t TABLE (Amt DECIMAL(10,4)); INSERT INTO @t (Amt) VALUES (@v);");

        Assert.Empty(result.WriteLossFindings);
    }

    [Theory]
    [InlineData("DECLARE c CURSOR FOR SELECT w.Amount FROM dbo.Wide AS w UNION ALL SELECT n.Amount FROM dbo.Narrow AS n;")]
    [InlineData("DECLARE c CURSOR FOR SELECT n.Amount FROM dbo.Narrow AS n UNION SELECT w.Amount FROM dbo.Wide AS w;")]
    [InlineData("DECLARE c CURSOR FOR SELECT w.Amount FROM dbo.Wide AS w EXCEPT SELECT n.Amount FROM dbo.Narrow AS n;")]
    public void Extract_FetchIntoFromCursorOverSetOperation_JudgesTheCombinedColumnType(string declareCursor)
    {
        var result = ExtractAll(
            """
            CREATE TABLE dbo.Wide (Amount DECIMAL(10,4) NOT NULL);
            CREATE TABLE dbo.Narrow (Amount DECIMAL(8,2) NOT NULL);
            """,
            "DECLARE @v DECIMAL(10,2); " + declareCursor + " OPEN c; FETCH NEXT FROM c INTO @v;");

        var finding = Assert.Single(result.WriteLossFindings);
        Assert.Equal(WriteLossKind.NumericScaleNarrowing, finding.Kind);
        Assert.Equal("@v", finding.ColumnName);
        Assert.DoesNotContain(result.SkippedConstructs, s => s.ConstructKind == "write source");
    }

    [Fact]
    public void Extract_FetchIntoFromCursorOverUnionThatFits_FlagsNothing()
    {
        var result = ExtractAll(
            "CREATE TABLE dbo.Narrow (Amount DECIMAL(8,2) NOT NULL);",
            "DECLARE @v DECIMAL(10,2); DECLARE c CURSOR FOR SELECT n.Amount FROM dbo.Narrow AS n UNION ALL SELECT n2.Amount FROM dbo.Narrow AS n2; OPEN c; FETCH NEXT FROM c INTO @v;");

        Assert.Empty(result.WriteLossFindings);
    }
}
