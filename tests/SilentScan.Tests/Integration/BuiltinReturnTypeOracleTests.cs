using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Catalog;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;
using SilentScan.Core.TypeInference;
using SilentScan.Tests.Support;
using SilentScan.Verify;
using SilentScan.Verify.Catalog;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class BuiltinReturnTypeOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(BuiltinReturnTypeOracleTests);

    protected override SqlServerOptions Options => SqlServerOptions.LocalDockerLatest;

    private const string ViewDdl = """
        CREATE VIEW dbo.vw_Builtins AS
        SELECT LEN(s.VMax) AS LenVarcharMax,
               LEN(s.V8000) AS LenVarchar,
               LEN(s.NMax) AS LenNvarcharMax,
               DATALENGTH(s.BMax) AS DataLengthVarbinaryMax,
               DATALENGTH(s.T) AS DataLengthText,
               DATALENGTH(s.V8000) AS DataLengthVarchar,
               CHARINDEX('a', s.VMax) AS CharIndexInMax,
               CHARINDEX(s.VMax, 'abc') AS CharIndexOfMax,
               CHARINDEX('a', s.T) AS CharIndexInText,
               PATINDEX('%a%', s.NMax) AS PatIndexInMax,
               PATINDEX(s.NMax, N'abc') AS PatIndexOfMax,
               SYSDATETIME() AS NowDateTime2,
               SYSDATETIMEOFFSET() AS NowOffset,
               CURRENT_TIMESTAMP AS NowTimestamp,
               CURRENT_USER AS WhoCurrent,
               SYSTEM_USER AS WhoSystem,
               SCOPE_IDENTITY() AS LastIdentity,
               OBJECT_NAME(1) AS ObjName
        FROM dbo.Src AS s;
        """;

    protected override string Ddl => $"""
        CREATE TABLE dbo.Src (VMax VARCHAR(MAX) NULL, V8000 VARCHAR(8000) NULL, NMax NVARCHAR(MAX) NULL, BMax VARBINARY(MAX) NULL, T TEXT NULL);
        GO
        {ViewDdl}
        """;

    private static readonly IReadOnlySet<string> DefaultConstraintOnly = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "NEWSEQUENTIALID" };

    private static readonly Dictionary<string, string> FixedInvocations = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GETDATE"] = "GETDATE()", ["GETUTCDATE"] = "GETUTCDATE()", ["SYSDATETIME"] = "SYSDATETIME()", ["SYSUTCDATETIME"] = "SYSUTCDATETIME()",
        ["SYSDATETIMEOFFSET"] = "SYSDATETIMEOFFSET()", ["NEWID"] = "NEWID()", ["TEXTPTR"] = "TEXTPTR(s.T)",
        ["ISNUMERIC"] = "ISNUMERIC('1')", ["ISDATE"] = "ISDATE('x')", ["ASCII"] = "ASCII('a')",
        ["DATEDIFF"] = "DATEDIFF(day, 0, 1)", ["DATEDIFF_BIG"] = "DATEDIFF_BIG(day, 0, 1)", ["DATEPART"] = "DATEPART(day, 0)",
        ["DAY"] = "DAY(0)", ["MONTH"] = "MONTH(0)", ["YEAR"] = "YEAR(0)", ["OBJECT_ID"] = "OBJECT_ID('x')", ["OBJECTPROPERTY"] = "OBJECTPROPERTY(1, 'IsTable')",
        ["ROW_NUMBER"] = "ROW_NUMBER() OVER (ORDER BY (SELECT 1))", ["RANK"] = "RANK() OVER (ORDER BY (SELECT 1))",
        ["DENSE_RANK"] = "DENSE_RANK() OVER (ORDER BY (SELECT 1))", ["NTILE"] = "NTILE(2) OVER (ORDER BY (SELECT 1))",
        ["COUNT"] = "COUNT(*)", ["COUNT_BIG"] = "COUNT_BIG(*)",
        ["SUSER_SNAME"] = "SUSER_SNAME()", ["SUSER_NAME"] = "SUSER_NAME()", ["USER_NAME"] = "USER_NAME()", ["APP_NAME"] = "APP_NAME()",
        ["DB_NAME"] = "DB_NAME()", ["HOST_NAME"] = "HOST_NAME()", ["ORIGINAL_LOGIN"] = "ORIGINAL_LOGIN()",
        ["ERROR_NUMBER"] = "ERROR_NUMBER()", ["ERROR_SEVERITY"] = "ERROR_SEVERITY()", ["ERROR_STATE"] = "ERROR_STATE()", ["ERROR_LINE"] = "ERROR_LINE()",
        ["ERROR_PROCEDURE"] = "ERROR_PROCEDURE()", ["ERROR_MESSAGE"] = "ERROR_MESSAGE()",
        ["UNICODE"] = "UNICODE(N'a')", ["CHAR"] = "CHAR(65)", ["NCHAR"] = "NCHAR(65)", ["SPACE"] = "SPACE(3)", ["QUOTENAME"] = "QUOTENAME(N'a')",
        ["SOUNDEX"] = "SOUNDEX('a')", ["DIFFERENCE"] = "DIFFERENCE('a', 'b')", ["ISJSON"] = "ISJSON('{}')",
        ["DB_ID"] = "DB_ID()", ["SCHEMA_ID"] = "SCHEMA_ID()", ["TYPE_ID"] = "TYPE_ID('int')", ["USER_ID"] = "USER_ID()", ["SUSER_ID"] = "SUSER_ID()",
        ["COL_LENGTH"] = "COL_LENGTH('a', 'b')", ["COLUMNPROPERTY"] = "COLUMNPROPERTY(1, 'a', 'IsIdentity')", ["INDEXPROPERTY"] = "INDEXPROPERTY(1, 'a', 'IsClustered')",
        ["FILE_ID"] = "FILE_ID('a')", ["FILE_IDEX"] = "FILE_IDEX('a')", ["FILEGROUP_ID"] = "FILEGROUP_ID('a')", ["XACT_STATE"] = "XACT_STATE()",
        ["SCOPE_IDENTITY"] = "SCOPE_IDENTITY()", ["IDENT_CURRENT"] = "IDENT_CURRENT('a')", ["IDENT_INCR"] = "IDENT_INCR('a')", ["IDENT_SEED"] = "IDENT_SEED('a')",
        ["ROWCOUNT_BIG"] = "ROWCOUNT_BIG()", ["OBJECT_NAME"] = "OBJECT_NAME(1)", ["OBJECT_SCHEMA_NAME"] = "OBJECT_SCHEMA_NAME(1)", ["SCHEMA_NAME"] = "SCHEMA_NAME()",
        ["TYPE_NAME"] = "TYPE_NAME(1)", ["COL_NAME"] = "COL_NAME(1, 1)", ["FILE_NAME"] = "FILE_NAME(1)", ["FILEGROUP_NAME"] = "FILEGROUP_NAME(1)",
        ["INDEX_COL"] = "INDEX_COL('a', 1, 1)", ["PROGRAM_NAME"] = "PROGRAM_NAME()", ["DATENAME"] = "DATENAME(month, 0)", ["EOMONTH"] = "EOMONTH(SYSDATETIME())",
        ["DATEFROMPARTS"] = "DATEFROMPARTS(1, 2, 3)", ["DATETIMEFROMPARTS"] = "DATETIMEFROMPARTS(1, 2, 3, 4, 5, 6, 7)",
        ["SMALLDATETIMEFROMPARTS"] = "SMALLDATETIMEFROMPARTS(2000, 2, 3, 4, 5)", ["CURRENT_TIMEZONE"] = "CURRENT_TIMEZONE()", ["CURRENT_TIMEZONE_ID"] = "CURRENT_TIMEZONE_ID()",
        ["JSON_VALUE"] = "JSON_VALUE('{}', '$.a')", ["JSON_MODIFY"] = "JSON_MODIFY('{}', '$.a', 1)", ["STRING_ESCAPE"] = "STRING_ESCAPE('a', 'json')",
        ["HASHBYTES"] = "HASHBYTES('SHA2_256', 'a')", ["CHECKSUM"] = "CHECKSUM(1)", ["BINARY_CHECKSUM"] = "BINARY_CHECKSUM(1)",
        ["PI"] = "PI()", ["RAND"] = "RAND()", ["SQRT"] = "SQRT(4)", ["SQUARE"] = "SQUARE(4)", ["EXP"] = "EXP(1)", ["LOG"] = "LOG(2)", ["LOG10"] = "LOG10(2)",
        ["SIN"] = "SIN(1)", ["COS"] = "COS(1)", ["TAN"] = "TAN(1)", ["COT"] = "COT(1)", ["ASIN"] = "ASIN(1)", ["ACOS"] = "ACOS(1)", ["ATAN"] = "ATAN(1)", ["ATN2"] = "ATN2(1, 2)",
        ["CONTEXT_INFO"] = "CONTEXT_INFO()", ["SESSION_CONTEXT"] = "SESSION_CONTEXT(N'a')", ["SERVERPROPERTY"] = "SERVERPROPERTY('a')",
        ["CONNECTIONPROPERTY"] = "CONNECTIONPROPERTY('net_transport')", ["DATABASEPROPERTYEX"] = "DATABASEPROPERTYEX('a', 'b')",
        ["HAS_PERMS_BY_NAME"] = "HAS_PERMS_BY_NAME(NULL, NULL, 'VIEW SERVER STATE')", ["IS_MEMBER"] = "IS_MEMBER('a')", ["IS_ROLEMEMBER"] = "IS_ROLEMEMBER('a')",
        ["IS_SRVROLEMEMBER"] = "IS_SRVROLEMEMBER('a')", ["SUSER_SID"] = "SUSER_SID()", ["ORIGINAL_DB_NAME"] = "ORIGINAL_DB_NAME()", ["HOST_ID"] = "HOST_ID()",
        ["FORMATMESSAGE"] = "FORMATMESSAGE('a %s', 'b')", ["COMPRESS"] = "COMPRESS('a')", ["DECOMPRESS"] = "DECOMPRESS(0x00)",
        ["CURRENT_REQUEST_ID"] = "CURRENT_REQUEST_ID()", ["CURRENT_TRANSACTION_ID"] = "CURRENT_TRANSACTION_ID()", ["TRIGGER_NESTLEVEL"] = "TRIGGER_NESTLEVEL()",
        ["GETANSINULL"] = "GETANSINULL()", ["OBJECTPROPERTYEX"] = "OBJECTPROPERTYEX(1, 'a')", ["SQL_VARIANT_PROPERTY"] = "SQL_VARIANT_PROPERTY(1, 'BaseType')",
        ["COLLATIONPROPERTY"] = "COLLATIONPROPERTY('a', 'b')", ["TYPEPROPERTY"] = "TYPEPROPERTY('int', 'Precision')", ["STATS_DATE"] = "STATS_DATE(1, 1)",
        ["APPLOCK_MODE"] = "APPLOCK_MODE('public', 'a', 'Transaction')", ["APPLOCK_TEST"] = "APPLOCK_TEST('public', 'a', 'Shared', 'Transaction')",
        ["MIN_ACTIVE_ROWVERSION"] = "MIN_ACTIVE_ROWVERSION()", ["REGEXP_COUNT"] = "REGEXP_COUNT('a', 'a')", ["REGEXP_INSTR"] = "REGEXP_INSTR('a', 'a')",
        ["BIT_COUNT"] = "BIT_COUNT(1)", ["GET_BIT"] = "GET_BIT(1, 0)", ["CURSOR_STATUS"] = "CURSOR_STATUS('local', 'c')",
    };

    private static readonly Dictionary<string, string> GlobalVariableInvocations = BuiltinFunctionTypeResolver.GlobalVariableNames
        .ToDictionary(n => n, n => n, StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<ParameterlessCallType, string> ParameterlessInvocations = new()
    {
        [ParameterlessCallType.CurrentTimestamp] = "CURRENT_TIMESTAMP",
        [ParameterlessCallType.CurrentDate] = "CURRENT_DATE",
        [ParameterlessCallType.CurrentUser] = "CURRENT_USER",
        [ParameterlessCallType.SessionUser] = "SESSION_USER",
        [ParameterlessCallType.SystemUser] = "SYSTEM_USER",
        [ParameterlessCallType.User] = "USER",
    };

    private static async Task<string?> EngineTypeAsync(SqlConnection connection, string expression)
    {
        await using var command = new SqlCommand(
            "SELECT system_type_name, max_length, precision, scale FROM sys.dm_exec_describe_first_result_set(@sql, NULL, 0);", connection);
        command.Parameters.AddWithValue("@sql", $"SELECT {expression} AS X FROM dbo.Src AS s");
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), expression);
        var typeName = reader.GetString(0).Split('(')[0];
        return LiveTypeMapper.BuildType(typeName, reader.GetInt16(1), reader.GetByte(2), reader.GetByte(3), collationName: null)?.ToString();
    }

    private static string? Ours(SqlType? type) => type switch
    {
        null => null,
        { LengthKnown: false } => type.Category.ToString(),
        _ => (type with { Collation = null }).ToString(),
    };

    private static string? EngineComparable(string? engine, SqlType? ours) =>
        ours is { LengthKnown: false } && engine is not null ? engine.Split('(')[0] : engine;

    [Fact]
    public void InvocationTable_CoversEveryResolverEntry_ExceptDefaultConstraintOnlyFunctions()
    {
        var expected = BuiltinFunctionTypeResolver.FixedReturnTypeFunctionNames.Except(DefaultConstraintOnly, StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(expected, FixedInvocations.Keys.Order(StringComparer.OrdinalIgnoreCase));
        Assert.Equal(Enum.GetValues<ParameterlessCallType>().Order(), BuiltinFunctionTypeResolver.TypedParameterlessCalls.Order());
    }

    [Fact]
    public async Task EveryFixedReturnTypeGlobalVariableAndParameterlessCall_MatchesTheEngine()
    {
        await using var connection = await OpenConnectionAsync();
        var mismatches = new List<string>();

        async Task Compare(string label, string invocation, SqlType? ours)
        {
            var engine = await EngineTypeAsync(connection, invocation);
            if (EngineComparable(engine, ours) != Ours(ours))
            {
                mismatches.Add($"{label}: engine {engine}, ours {Ours(ours)}");
            }
        }

        foreach (var (name, invocation) in FixedInvocations)
        {
            await Compare(name, invocation, BuiltinFunctionTypeResolver.ResolveFixedReturnType(name));
        }

        foreach (var (name, invocation) in GlobalVariableInvocations)
        {
            await Compare(name, invocation, BuiltinFunctionTypeResolver.ResolveGlobalVariable(name));
        }

        foreach (var (callType, invocation) in ParameterlessInvocations)
        {
            await Compare(callType.ToString(), invocation, BuiltinFunctionTypeResolver.ResolveParameterlessCall(callType));
        }

        Assert.Empty(mismatches);
    }

    [Fact]
    public async Task BuiltinViewColumns_StaticLineageTypesMatchTheEngineBoundViewColumns()
    {
        var catalog = await new LiveCatalogReader(Options.BuildConnectionString(DatabaseName)).ReadAsync();
        var view = LineageResolver.Resolve(catalog, [SqlScriptParser.ParseText("view.sql", ViewDdl)]).Find("dbo.vw_Builtins")!;

        await using var connection = await OpenConnectionAsync();
        await using var command = new SqlCommand("""
            SELECT c.name, TYPE_NAME(c.user_type_id), c.max_length, c.precision, c.scale
            FROM sys.columns c WHERE c.object_id = OBJECT_ID(N'dbo.vw_Builtins') ORDER BY c.column_id;
            """, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var mismatches = new List<string>();
        var compared = 0;
        while (await reader.ReadAsync())
        {
            var name = reader.GetString(0);
            var engine = LiveTypeMapper.BuildType(reader.GetString(1), reader.GetInt16(2), reader.GetByte(3), reader.GetByte(4), collationName: null)?.ToString();
            var ours = Ours(ColumnProvenanceAnalysis.TryGetScalarType(view.FindColumn(name)!.Provenance));
            if (engine != ours)
            {
                mismatches.Add($"{name}: engine {engine}, ours {ours}");
            }

            compared++;
        }

        Assert.Equal(18, compared);
        Assert.Empty(mismatches);
    }
}
