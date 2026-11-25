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
    ];
}
