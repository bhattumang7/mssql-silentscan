namespace SilentScan.Verify.Oracle;

public sealed record BuiltinProbe(string Function, string Template, IReadOnlyList<IReadOnlyList<string>> Positions);

public static class BuiltinProbes
{
    private static readonly string[] Strings =
        ["VARCHAR(20)", "NVARCHAR(20)", "VARCHAR(MAX)", "NVARCHAR(MAX)", "CHAR(10)", "VARCHAR(20) COLLATE Latin1_General_100_CI_AS_SC_UTF8"];

    private static readonly string[] Numbers =
        ["INT", "BIGINT", "SMALLINT", "TINYINT", "DECIMAL(10,2)", "FLOAT", "REAL", "MONEY"];

    private static readonly string[] Temporals =
        ["DATE", "DATETIME", "DATETIME2(3)", "DATETIMEOFFSET(3)", "SMALLDATETIME", "TIME(3)"];

    private static readonly string[] Binaries = ["VARBINARY(20)", "VARBINARY(MAX)", "BINARY(10)"];

    private static readonly string[] Int = ["INT"];

    private static readonly string[] None = [];

    private static readonly string[] Image = ["IMAGE"];

    public static readonly IReadOnlyList<BuiltinProbe> All = Build();

    private static BuiltinProbe P(string name, string template, params string[][] positions) => new(name, template, [.. positions.Where(p => p.Length > 0)]);

    private static List<BuiltinProbe> Build() =>
    [
        P("LEN", "LEN({0})", Strings), P("DATALENGTH", "DATALENGTH({0})", Strings), P("UPPER", "UPPER({0})", Strings), P("LOWER", "LOWER({0})", Strings),
        P("LTRIM", "LTRIM({0})", Strings), P("RTRIM", "RTRIM({0})", Strings), P("TRIM", "TRIM({0})", Strings), P("REVERSE", "REVERSE({0})", Strings),
        P("LEFT", "LEFT({0}, {1})", Strings, Int), P("RIGHT", "RIGHT({0}, {1})", Strings, Int), P("SUBSTRING", "SUBSTRING({0}, {1}, {1})", Strings, Int),
        P("REPLACE", "REPLACE({0}, {0}, {0})", Strings), P("REPLICATE", "REPLICATE({0}, {1})", Strings, Int), P("STUFF", "STUFF({0}, {1}, {1}, {0})", Strings, Int),
        P("CHARINDEX", "CHARINDEX({0}, {0})", Strings), P("PATINDEX", "PATINDEX({0}, {0})", Strings), P("CONCAT", "CONCAT({0}, {0})", Strings),
        P("CONCAT_WS", "CONCAT_WS(',', {0}, {0})", Strings), P("STRING_ESCAPE", "STRING_ESCAPE({0}, 'json')", Strings), P("TRANSLATE", "TRANSLATE({0}, {0}, {0})", Strings),
        P("FORMAT", "FORMAT({0}, 'N2')", Numbers), P("STR", "STR({0})", Numbers), P("SPACE", "SPACE({0})", Int), P("ASCII", "ASCII({0})", Strings),
        P("UNICODE", "UNICODE({0})", Strings), P("CHAR", "CHAR({0})", Int), P("NCHAR", "NCHAR({0})", Int), P("QUOTENAME", "QUOTENAME({0})", Strings),
        P("SOUNDEX", "SOUNDEX({0})", Strings), P("DIFFERENCE", "DIFFERENCE({0}, {0})", Strings), P("COMPRESS", "COMPRESS({0})", Strings), P("COMPRESS", "COMPRESS({0})", Binaries),
        P("DECOMPRESS", "DECOMPRESS({0})", Binaries), P("HASHBYTES", "HASHBYTES('SHA2_256', {0})", Strings), P("HASHBYTES", "HASHBYTES('SHA2_256', {0})", Binaries), P("CHECKSUM", "CHECKSUM({0})", Strings), P("CHECKSUM", "CHECKSUM({0})", Numbers),
        P("BINARY_CHECKSUM", "BINARY_CHECKSUM({0})", Strings), P("BINARY_CHECKSUM", "BINARY_CHECKSUM({0})", Numbers), P("ISNUMERIC", "ISNUMERIC({0})", Strings), P("ISDATE", "ISDATE({0})", Strings),
        P("ABS", "ABS({0})", Numbers), P("CEILING", "CEILING({0})", Numbers), P("FLOOR", "FLOOR({0})", Numbers), P("ROUND", "ROUND({0}, {1})", Numbers, Int),
        P("SIGN", "SIGN({0})", Numbers), P("SQRT", "SQRT({0})", Numbers), P("SQUARE", "SQUARE({0})", Numbers), P("POWER", "POWER({0}, {1})", Numbers, Int),
        P("EXP", "EXP({0})", Numbers), P("LOG", "LOG({0})", Numbers), P("LOG10", "LOG10({0})", Numbers), P("SIN", "SIN({0})", Numbers),
        P("ATN2", "ATN2({0}, {0})", Numbers), P("DEGREES", "DEGREES({0})", Numbers), P("RADIANS", "RADIANS({0})", Numbers), P("PI", "PI()", None),
        P("RAND", "RAND()", None), P("RAND_SEEDED", "RAND({0})", Int), P("GETDATE", "GETDATE()", None), P("SYSDATETIME", "SYSDATETIME()", None),
        P("GETUTCDATE", "GETUTCDATE()", None), P("SYSUTCDATETIME", "SYSUTCDATETIME()", None), P("SYSDATETIMEOFFSET", "SYSDATETIMEOFFSET()", None),
        P("CURRENT_TIMESTAMP", "CURRENT_TIMESTAMP", None), P("DATEADD", "DATEADD(day, {1}, {0})", Temporals, Int), P("DATEDIFF", "DATEDIFF(day, {0}, {0})", Temporals),
        P("DATEDIFF_BIG", "DATEDIFF_BIG(day, {0}, {0})", Temporals), P("DATEPART", "DATEPART(year, {0})", Temporals), P("DATENAME", "DATENAME(month, {0})", Temporals),
        P("DAY", "DAY({0})", Temporals), P("MONTH", "MONTH({0})", Temporals), P("YEAR", "YEAR({0})", Temporals), P("EOMONTH", "EOMONTH({0})", Temporals),
        P("DATEFROMPARTS", "DATEFROMPARTS({0}, {0}, {0})", Int), P("DATETRUNC", "DATETRUNC(day, {0})", Temporals), P("DATE_BUCKET", "DATE_BUCKET(day, 1, {0})", Temporals),
        P("TODATETIMEOFFSET", "TODATETIMEOFFSET({0}, '+00:00')", Temporals), P("SWITCHOFFSET", "SWITCHOFFSET({0}, '+00:00')", Temporals),
        P("CONVERT_STYLE", "CONVERT(VARCHAR(30), {0}, 121)", Temporals), P("TRY_CONVERT", "TRY_CONVERT(INT, {0})", Strings),
        P("PARSE", "PARSE({0} AS INT)", Strings), P("TRY_PARSE", "TRY_PARSE({0} AS DECIMAL(10,2))", Strings), P("NEWID", "NEWID()", None),
        P("SCOPE_IDENTITY", "SCOPE_IDENTITY()", None), P("IDENT_CURRENT", "IDENT_CURRENT('p')", None), P("ROWCOUNT_VAR", "@@ROWCOUNT", None),
        P("TRANCOUNT_VAR", "@@TRANCOUNT", None), P("ERROR_VAR", "@@ERROR", None), P("SPID_VAR", "@@SPID", None), P("VERSION_VAR", "@@VERSION", None),
        P("SYSTEM_USER", "SYSTEM_USER", None), P("USER_NAME", "USER_NAME()", None), P("SUSER_SNAME", "SUSER_SNAME()", None), P("DB_NAME", "DB_NAME()", None),
        P("OBJECT_ID", "OBJECT_ID({0})", Strings), P("OBJECT_NAME", "OBJECT_NAME({0})", Int), P("SCHEMA_NAME", "SCHEMA_NAME()", None),
        P("COL_LENGTH", "COL_LENGTH({0}, {0})", Strings), P("SERVERPROPERTY", "SERVERPROPERTY('Edition')", None), P("APP_NAME", "APP_NAME()", None),
        P("HOST_NAME", "HOST_NAME()", None), P("CONTEXT_INFO", "CONTEXT_INFO()", None), P("SESSION_CONTEXT", "SESSION_CONTEXT(N'k')", None),
        P("XACT_STATE", "XACT_STATE()", None), P("ISJSON", "ISJSON({0})", Strings), P("JSON_VALUE", "JSON_VALUE({0}, '$.a')", Strings),
        P("JSON_QUERY", "JSON_QUERY({0}, '$.a')", Strings), P("JSON_MODIFY", "JSON_MODIFY({0}, '$.a', {0})", Strings), P("JSON_PATH_EXISTS", "JSON_PATH_EXISTS({0}, '$.a')", Strings),
        P("GREATEST", "GREATEST({0}, {0})", Numbers), P("GREATEST", "GREATEST({0}, {0})", Temporals), P("GREATEST", "GREATEST({0}, {0})", Strings),
        P("LEAST", "LEAST({0}, {0})", Numbers), P("LEAST", "LEAST({0}, {0})", Temporals), P("LEAST", "LEAST({0}, {0})", Strings),
        P("SUM", "SUM({0})", Numbers), P("AVG", "AVG({0})", Numbers), P("MIN", "MIN({0})", Numbers), P("MIN", "MIN({0})", Strings), P("MIN", "MIN({0})", Temporals),
        P("MAX", "MAX({0})", Numbers), P("MAX", "MAX({0})", Strings), P("MAX", "MAX({0})", Temporals),
        P("COUNT", "COUNT({0})", Numbers), P("COUNT", "COUNT({0})", Strings), P("COUNT_BIG", "COUNT_BIG({0})", Numbers), P("STDEV", "STDEV({0})", Numbers), P("STDEVP", "STDEVP({0})", Numbers),
        P("VAR", "VAR({0})", Numbers), P("VARP", "VARP({0})", Numbers), P("STRING_AGG", "STRING_AGG({0}, ',')", Strings), P("CHECKSUM_AGG", "CHECKSUM_AGG({0})", Int),
        P("APPROX_COUNT_DISTINCT", "APPROX_COUNT_DISTINCT({0})", Numbers), P("APPROX_COUNT_DISTINCT", "APPROX_COUNT_DISTINCT({0})", Strings), P("ROW_NUMBER", "ROW_NUMBER() OVER (ORDER BY {0})", Int),
        P("RANK", "RANK() OVER (ORDER BY {0})", Int), P("DENSE_RANK", "DENSE_RANK() OVER (ORDER BY {0})", Int), P("NTILE", "NTILE(2) OVER (ORDER BY {0})", Int),
        P("LAG", "LAG({0}) OVER (ORDER BY {0})", Numbers), P("LAG", "LAG({0}) OVER (ORDER BY {0})", Strings), P("LEAD", "LEAD({0}) OVER (ORDER BY {0})", Numbers), P("LEAD", "LEAD({0}) OVER (ORDER BY {0})", Strings),
        P("FIRST_VALUE", "FIRST_VALUE({0}) OVER (ORDER BY {0})", Numbers), P("FIRST_VALUE", "FIRST_VALUE({0}) OVER (ORDER BY {0})", Strings), P("LAST_VALUE", "LAST_VALUE({0}) OVER (ORDER BY {0})", Numbers), P("LAST_VALUE", "LAST_VALUE({0}) OVER (ORDER BY {0})", Strings),
        P("SUM_OVER", "SUM({0}) OVER (ORDER BY {0})", Numbers), P("AVG_OVER", "AVG({0}) OVER (ORDER BY {0})", Numbers),
        P("OBJECT_DEFINITION", "OBJECT_DEFINITION(1)", None),
        P("IDENTITYPROPERTY", "IDENTITYPROPERTY(1,'IsIdentity')", None),
        P("PERMISSIONS", "PERMISSIONS()", None),
        P("LOGINPROPERTY", "LOGINPROPERTY('sa','IsLocked')", None),
        P("SESSIONPROPERTY", "SESSIONPROPERTY('ANSI_NULLS')", None),
        P("CERT_ID", "CERT_ID('c')", None),
        P("ASYMKEY_ID", "ASYMKEY_ID('k')", None),
        P("KEY_ID", "KEY_ID('k')", None),
        P("KEY_GUID", "KEY_GUID('k')", None),
        P("CHANGE_TRACKING_CURRENT_VERSION", "CHANGE_TRACKING_CURRENT_VERSION()", None),
        P("HAS_DBACCESS", "HAS_DBACCESS('master')", None),
        P("CERTPROPERTY", "CERTPROPERTY(1,'Expiry_Date')", None),
        P("ASYMKEYPROPERTY", "ASYMKEYPROPERTY(1,'algorithm_desc')", None),
        P("USER_SID", "USER_SID(1)", None),
        P("INDEXKEY_PROPERTY", "INDEXKEY_PROPERTY(1,1,1,'ColumnId')", None),
        P("CERTENCODED", "CERTENCODED(1)", None),
        P("ACOS", "ACOS(0.5)", None),
        P("APPLOCK_MODE", "APPLOCK_MODE('public','r','Session')", None),
        P("APPLOCK_TEST", "APPLOCK_TEST('public','r','Shared','Session')", None),
        P("ASIN", "ASIN(0.5)", None),
        P("ATAN", "ATAN(0.5)", None),
        P("BIT_COUNT", "BIT_COUNT(5)", None),
        P("COL_NAME", "COL_NAME(1,1)", None),
        P("COLLATIONPROPERTY", "COLLATIONPROPERTY('Latin1_General_CI_AS','CodePage')", None),
        P("COLUMNPROPERTY", "COLUMNPROPERTY(1,'c','AllowsNull')", None),
        P("CONNECTIONPROPERTY", "CONNECTIONPROPERTY('net_transport')", None),
        P("COS", "COS(0.5)", None),
        P("COT", "COT(0.5)", None),
        P("CURRENT_REQUEST_ID", "CURRENT_REQUEST_ID()", None),
        P("CURRENT_TIMEZONE", "CURRENT_TIMEZONE()", None),
        P("CURRENT_TIMEZONE_ID", "CURRENT_TIMEZONE_ID()", None),
        P("CURRENT_TRANSACTION_ID", "CURRENT_TRANSACTION_ID()", None),
        P("CURSOR_STATUS", "CURSOR_STATUS('global','c')", None),
        P("DATABASE_PRINCIPAL_ID", "DATABASE_PRINCIPAL_ID('dbo')", None),
        P("DATABASEPROPERTYEX", "DATABASEPROPERTYEX('master','Status')", None),
        P("DATETIMEFROMPARTS", "DATETIMEFROMPARTS(2024,1,2,3,4,5,6)", None),
        P("DB_ID", "DB_ID('master')", None),
        P("ERROR_LINE", "ERROR_LINE()", None),
        P("ERROR_MESSAGE", "ERROR_MESSAGE()", None),
        P("ERROR_NUMBER", "ERROR_NUMBER()", None),
        P("ERROR_PROCEDURE", "ERROR_PROCEDURE()", None),
        P("ERROR_SEVERITY", "ERROR_SEVERITY()", None),
        P("ERROR_STATE", "ERROR_STATE()", None),
        P("FILE_ID", "FILE_ID('master')", None),
        P("FILE_IDEX", "FILE_IDEX('master')", None),
        P("FILE_NAME", "FILE_NAME(1)", None),
        P("FILEGROUP_ID", "FILEGROUP_ID('PRIMARY')", None),
        P("FILEGROUP_NAME", "FILEGROUP_NAME(1)", None),
        P("FORMATMESSAGE", "FORMATMESSAGE(50000, 'a')", None),
        P("GET_BIT", "GET_BIT(0x01, 0)", None),
        P("GETANSINULL", "GETANSINULL()", None),
        P("HAS_PERMS_BY_NAME", "HAS_PERMS_BY_NAME(NULL,NULL,'VIEW SERVER STATE')", None),
        P("HOST_ID", "HOST_ID()", None),
        P("IDENT_INCR", "IDENT_INCR('t')", None),
        P("IDENT_SEED", "IDENT_SEED('t')", None),
        P("INDEX_COL", "INDEX_COL('t',1,1)", None),
        P("INDEXPROPERTY", "INDEXPROPERTY(1,'i','IndexID')", None),
        P("IS_MEMBER", "IS_MEMBER('public')", None),
        P("IS_ROLEMEMBER", "IS_ROLEMEMBER('public')", None),
        P("IS_SRVROLEMEMBER", "IS_SRVROLEMEMBER('sysadmin')", None),
        P("MIN_ACTIVE_ROWVERSION", "MIN_ACTIVE_ROWVERSION()", None),
        P("OBJECT_SCHEMA_NAME", "OBJECT_SCHEMA_NAME(1)", None),
        P("OBJECTPROPERTY", "OBJECTPROPERTY(1,'IsTable')", None),
        P("OBJECTPROPERTYEX", "OBJECTPROPERTYEX(1,'BaseType')", None),
        P("ORIGINAL_DB_NAME", "ORIGINAL_DB_NAME()", None),
        P("ORIGINAL_LOGIN", "ORIGINAL_LOGIN()", None),
        P("PROGRAM_NAME", "PROGRAM_NAME()", None),
        P("ROWCOUNT_BIG", "ROWCOUNT_BIG()", None),
        P("SCHEMA_ID", "SCHEMA_ID('dbo')", None),
        P("SMALLDATETIMEFROMPARTS", "SMALLDATETIMEFROMPARTS(2024,1,2,3,4)", None),
        P("SQL_VARIANT_PROPERTY", "SQL_VARIANT_PROPERTY(1,'BaseType')", None),
        P("STATS_DATE", "STATS_DATE(1,1)", None),
        P("SUSER_ID", "SUSER_ID()", None),
        P("SUSER_NAME", "SUSER_NAME()", None),
        P("SUSER_SID", "SUSER_SID()", None),
        P("TAN", "TAN(0.5)", None),
        P("TEXTPTR", "TEXTPTR({0})", Image),
        P("TRIGGER_NESTLEVEL", "TRIGGER_NESTLEVEL()", None),
        P("TYPE_ID", "TYPE_ID('int')", None),
        P("TYPE_NAME", "TYPE_NAME(56)", None),
        P("TYPEPROPERTY", "TYPEPROPERTY('int','Precision')", None),
        P("USER_ID", "USER_ID()", None),
    ];
}
