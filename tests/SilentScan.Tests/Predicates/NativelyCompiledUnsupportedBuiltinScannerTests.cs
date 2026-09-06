using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;

namespace SilentScan.Tests.Predicates;

public sealed class NativelyCompiledUnsupportedBuiltinScannerTests
{
    private static IReadOnlyList<NativelyCompiledUnsupportedBuiltinFinding> Scan(string sql)
    {
        var result = SqlScriptParser.ParseText("test.sql", sql);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        return NativelyCompiledUnsupportedBuiltinScanner.Scan(result);
    }

    [Fact]
    public void UnsupportedFunction_InNativelyCompiledProcedure_Fires()
    {
        var findings = Scan(
            """
            CREATE PROCEDURE dbo.NormalizeCode
            WITH NATIVE_COMPILATION, SCHEMABINDING
            AS
            BEGIN ATOMIC WITH (TRANSACTION ISOLATION LEVEL = SNAPSHOT, LANGUAGE = N'us_english')
                DECLARE @code NVARCHAR(20) = UPPER(N'ab-12');
            END;
            """);

        var finding = Assert.Single(findings);
        Assert.Equal("dbo.NormalizeCode", finding.ModuleQualifiedName);
        Assert.Equal("UPPER", finding.FunctionName);
    }

    [Fact]
    public void UnsupportedFunction_InOrdinaryInterpretedProcedure_NeverFires()
    {
        var findings = Scan(
            """
            CREATE PROCEDURE dbo.NormalizeCode
            AS
            BEGIN
                DECLARE @code NVARCHAR(20) = UPPER(N'ab-12');
            END;
            """);

        Assert.Empty(findings);
    }

    [Fact]
    public void SupportedFunction_InNativelyCompiledProcedure_NeverFires()
    {
        var findings = Scan(
            """
            CREATE PROCEDURE dbo.NormalizeAmount
            WITH NATIVE_COMPILATION, SCHEMABINDING
            AS
            BEGIN ATOMIC WITH (TRANSACTION ISOLATION LEVEL = SNAPSHOT, LANGUAGE = N'us_english')
                DECLARE @amount FLOAT = ABS(-1.0);
            END;
            """);

        Assert.Empty(findings);
    }

    [Fact]
    public void UnsupportedFunction_InNativelyCompiledScalarFunction_Fires()
    {
        var findings = Scan(
            """
            CREATE FUNCTION dbo.NormalizeCode(@code NVARCHAR(20))
            RETURNS NVARCHAR(20)
            WITH NATIVE_COMPILATION, SCHEMABINDING
            AS
            BEGIN ATOMIC WITH (TRANSACTION ISOLATION LEVEL = SNAPSHOT, LANGUAGE = N'us_english')
                RETURN UPPER(@code);
            END;
            """);

        var finding = Assert.Single(findings);
        Assert.Equal("dbo.NormalizeCode", finding.ModuleQualifiedName);
        Assert.Equal("UPPER", finding.FunctionName);
    }

    [Theory]
    [InlineData("LEFT(N'abc', 1)", "LEFT")]
    [InlineData("RIGHT(N'abc', 1)", "RIGHT")]
    public void LeftOrRightCall_InNativelyCompiledProcedure_Fires(string expression, string expectedFunctionName)
    {
        var findings = Scan(
            $"""
            CREATE PROCEDURE dbo.TrimCode
            WITH NATIVE_COMPILATION, SCHEMABINDING
            AS
            BEGIN ATOMIC WITH (TRANSACTION ISOLATION LEVEL = SNAPSHOT, LANGUAGE = N'us_english')
                DECLARE @code NVARCHAR(20) = {expression};
            END;
            """);

        var finding = Assert.Single(findings);
        Assert.Equal("dbo.TrimCode", finding.ModuleQualifiedName);
        Assert.Equal(expectedFunctionName, finding.FunctionName);
    }

    [Fact]
    public void LeftCall_InOrdinaryInterpretedProcedure_NeverFires()
    {
        var findings = Scan(
            """
            CREATE PROCEDURE dbo.TrimCode
            AS
            BEGIN
                DECLARE @code NVARCHAR(20) = LEFT(N'abc', 1);
            END;
            """);

        Assert.Empty(findings);
    }

    [Fact]
    public void StringAgg_InNativelyCompiledProcedure_NeverFires()
    {
        var findings = Scan(
            """
            CREATE PROCEDURE dbo.SummarizeCodes
            WITH NATIVE_COMPILATION, SCHEMABINDING
            AS
            BEGIN ATOMIC WITH (TRANSACTION ISOLATION LEVEL = SNAPSHOT, LANGUAGE = N'us_english')
                DECLARE @codes NVARCHAR(100);
                SELECT @codes = STRING_AGG(name, N',') FROM sys.objects;
            END;
            """);

        Assert.Empty(findings);
    }

    [Theory]
    [InlineData("VARBINARY(100)", "COMPRESS(N'a')", "COMPRESS")]
    [InlineData("VARBINARY(100)", "DECOMPRESS(0x00)", "DECOMPRESS")]
    [InlineData("INT", "CHECKSUM(N'a')", "CHECKSUM")]
    [InlineData("INT", "BINARY_CHECKSUM(N'a')", "BINARY_CHECKSUM")]
    [InlineData("SYSNAME", "PARSENAME(N'a.b', 1)", "PARSENAME")]
    [InlineData("NVARCHAR(128)", "APP_NAME()", "APP_NAME")]
    [InlineData("SYSNAME", "TYPE_NAME(56)", "TYPE_NAME")]
    [InlineData("SYSNAME", "COL_NAME(1, 1)", "COL_NAME")]
    [InlineData("NVARCHAR(255)", "FORMATMESSAGE(N'a')", "FORMATMESSAGE")]
    [InlineData("INT", "OBJECT_ID(N'a')", "OBJECT_ID")]
    [InlineData("SYSNAME", "OBJECT_NAME(1)", "OBJECT_NAME")]
    [InlineData("INT", "DB_ID()", "DB_ID")]
    [InlineData("SYSNAME", "DB_NAME()", "DB_NAME")]
    [InlineData("INT", "SCHEMA_ID()", "SCHEMA_ID")]
    [InlineData("SYSNAME", "SCHEMA_NAME()", "SCHEMA_NAME")]
    [InlineData("INT", "PERMISSIONS()", "PERMISSIONS")]
    [InlineData("INT", "HAS_PERMS_BY_NAME(N'a', N'DATABASE', N'SELECT')", "HAS_PERMS_BY_NAME")]
    [InlineData("SYSNAME", "CURRENT_TIMEZONE()", "CURRENT_TIMEZONE")]
    [InlineData("NUMERIC(38,0)", "IDENT_CURRENT(N'a')", "IDENT_CURRENT")]
    [InlineData("DATETIME", "STATS_DATE(1, 1)", "STATS_DATE")]
    [InlineData("INT", "OBJECTPROPERTY(1, N'a')", "OBJECTPROPERTY")]
    [InlineData("INT", "COLLATIONPROPERTY(N'a', N'a')", "COLLATIONPROPERTY")]
    [InlineData("INT", "FILE_ID(N'a')", "FILE_ID")]
    [InlineData("INT", "INDEXPROPERTY(1, N'a', N'a')", "INDEXPROPERTY")]
    public void UnsupportedMetadataOrCryptoFunction_InNativelyCompiledProcedure_Fires(string variableType, string expression, string expectedFunctionName)
    {
        var findings = Scan(
            $"""
            CREATE PROCEDURE dbo.NormalizeCode
            WITH NATIVE_COMPILATION, SCHEMABINDING
            AS
            BEGIN ATOMIC WITH (TRANSACTION ISOLATION LEVEL = SNAPSHOT, LANGUAGE = N'us_english')
                DECLARE @x {variableType} = {expression};
            END;
            """);

        var finding = Assert.Single(findings);
        Assert.Equal(expectedFunctionName, finding.FunctionName);
    }

    [Theory]
    [InlineData("INT", "HAS_DBACCESS(N'a')", "HAS_DBACCESS")]
    [InlineData("VARCHAR(10)", "HOST_ID()", "HOST_ID")]
    [InlineData("SYSNAME", "HOST_NAME()", "HOST_NAME")]
    [InlineData("SYSNAME", "PROGRAM_NAME()", "PROGRAM_NAME")]
    [InlineData("BIGINT", "CURRENT_TRANSACTION_ID()", "CURRENT_TRANSACTION_ID")]
    [InlineData("SMALLINT", "XACT_STATE()", "XACT_STATE")]
    [InlineData("INT", "GETANSINULL()", "GETANSINULL")]
    [InlineData("NVARCHAR(30)", "DATENAME(month, SYSDATETIME())", "DATENAME")]
    [InlineData("SYSNAME", "INDEX_COL(N'a', 1, 1)", "INDEX_COL")]
    [InlineData("NVARCHAR(MAX)", "OBJECT_DEFINITION(1)", "OBJECT_DEFINITION")]
    [InlineData("SYSNAME", "OBJECT_SCHEMA_NAME(1)", "OBJECT_SCHEMA_NAME")]
    [InlineData("SYSNAME", "ORIGINAL_DB_NAME()", "ORIGINAL_DB_NAME")]
    [InlineData("NVARCHAR(255)", "DEFAULT_DOMAIN()", "DEFAULT_DOMAIN")]
    [InlineData("VARCHAR(32)", "APPLOCK_MODE(N'a', N'a')", "APPLOCK_MODE")]
    [InlineData("INT", "APPLOCK_TEST(N'a', N'a', N'a', N'a')", "APPLOCK_TEST")]
    [InlineData("VARBINARY(MAX)", "PWDENCRYPT(N'a')", "PWDENCRYPT")]
    [InlineData("INT", "PWDCOMPARE(N'a', 0x00)", "PWDCOMPARE")]
    [InlineData("UNIQUEIDENTIFIER", "KEY_GUID(N'a')", "KEY_GUID")]
    [InlineData("INT", "KEY_ID(N'a')", "KEY_ID")]
    [InlineData("SYSNAME", "KEY_NAME(1)", "KEY_NAME")]
    [InlineData("BIGINT", "CHANGE_TRACKING_CURRENT_VERSION()", "CHANGE_TRACKING_CURRENT_VERSION")]
    [InlineData("BIGINT", "CHANGE_TRACKING_MIN_VALID_VERSION(1)", "CHANGE_TRACKING_MIN_VALID_VERSION")]
    [InlineData("INT", "ASYMKEY_ID(N'a')", "ASYMKEY_ID")]
    [InlineData("INT", "COLUMNPROPERTY(1, N'a', N'a')", "COLUMNPROPERTY")]
    [InlineData("INT", "FILEPROPERTY(N'a', N'a')", "FILEPROPERTY")]
    [InlineData("INT", "TYPEPROPERTY(N'a', N'a')", "TYPEPROPERTY")]
    [InlineData("INT", "DATABASEPROPERTY(N'a', N'a')", "DATABASEPROPERTY")]
    [InlineData("SYSNAME", "CURRENT_TIMEZONE_ID()", "CURRENT_TIMEZONE_ID")]
    [InlineData("NUMERIC(38,0)", "IDENT_INCR(N'a')", "IDENT_INCR")]
    [InlineData("NUMERIC(38,0)", "IDENT_SEED(N'a')", "IDENT_SEED")]
    [InlineData("INT", "CURRENT_REQUEST_ID()", "CURRENT_REQUEST_ID")]
    [InlineData("FLOAT", "VECTOR_DISTANCE(N'cosine', CAST('[1,2]' AS VECTOR(2)), CAST('[1,2]' AS VECTOR(2)))", "VECTOR_DISTANCE")]
    [InlineData("FLOAT", "VECTOR_NORM(CAST('[1,2]' AS VECTOR(2)), N'norm2')", "VECTOR_NORM")]
    public void MoreUnsupportedFunctions_InNativelyCompiledProcedure_Fire(string variableType, string expression, string expectedFunctionName)
    {
        var findings = Scan(
            $"""
            CREATE PROCEDURE dbo.NormalizeCode
            WITH NATIVE_COMPILATION, SCHEMABINDING
            AS
            BEGIN ATOMIC WITH (TRANSACTION ISOLATION LEVEL = SNAPSHOT, LANGUAGE = N'us_english')
                DECLARE @x {variableType} = {expression};
            END;
            """);

        Assert.Contains(findings, f => f.FunctionName == expectedFunctionName);
    }

    [Fact]
    public void MultipleUnsupportedCalls_AllFire()
    {
        var findings = Scan(
            """
            CREATE PROCEDURE dbo.NormalizeCode
            WITH NATIVE_COMPILATION, SCHEMABINDING
            AS
            BEGIN ATOMIC WITH (TRANSACTION ISOLATION LEVEL = SNAPSHOT, LANGUAGE = N'us_english')
                DECLARE @a NVARCHAR(20) = UPPER(N'a');
                DECLARE @b NVARCHAR(20) = LOWER(N'b');
            END;
            """);

        Assert.Equal(2, findings.Count);
        Assert.Equal(["UPPER", "LOWER"], findings.Select(f => f.FunctionName));
    }
}
