using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SilentScan.Core.TypeInference;

public static class BuiltinFunctionTypeResolver
{
    private static readonly Dictionary<string, SqlType> FixedReturnTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GETDATE"] = new SqlType(SqlTypeCategory.DateTime),
        ["GETUTCDATE"] = new SqlType(SqlTypeCategory.DateTime),
        ["SYSDATETIME"] = new SqlType(SqlTypeCategory.DateTime2, Scale: 7),
        ["SYSUTCDATETIME"] = new SqlType(SqlTypeCategory.DateTime2, Scale: 7),
        ["SYSDATETIMEOFFSET"] = new SqlType(SqlTypeCategory.DateTimeOffset, Scale: 7),
        ["NEWID"] = new SqlType(SqlTypeCategory.UniqueIdentifier),
        ["NEWSEQUENTIALID"] = new SqlType(SqlTypeCategory.UniqueIdentifier),
        ["TEXTPTR"] = new SqlType(SqlTypeCategory.VarBinary, Length: 16),

        ["ASCII"] = new SqlType(SqlTypeCategory.Int),
        ["ISNUMERIC"] = new SqlType(SqlTypeCategory.Int),
        ["ISDATE"] = new SqlType(SqlTypeCategory.Int),

        ["DATEDIFF"] = new SqlType(SqlTypeCategory.Int),
        ["DATEDIFF_BIG"] = new SqlType(SqlTypeCategory.BigInt),
        ["DATEPART"] = new SqlType(SqlTypeCategory.Int),
        ["DAY"] = new SqlType(SqlTypeCategory.Int),
        ["MONTH"] = new SqlType(SqlTypeCategory.Int),
        ["YEAR"] = new SqlType(SqlTypeCategory.Int),

        ["OBJECT_ID"] = new SqlType(SqlTypeCategory.Int),
        ["OBJECTPROPERTY"] = new SqlType(SqlTypeCategory.Int),

        ["ROW_NUMBER"] = new SqlType(SqlTypeCategory.BigInt),
        ["RANK"] = new SqlType(SqlTypeCategory.BigInt),
        ["DENSE_RANK"] = new SqlType(SqlTypeCategory.BigInt),
        ["NTILE"] = new SqlType(SqlTypeCategory.BigInt),
        ["COUNT"] = new SqlType(SqlTypeCategory.Int),
        ["COUNT_BIG"] = new SqlType(SqlTypeCategory.BigInt),

        ["SUSER_SNAME"] = new SqlType(SqlTypeCategory.NVarChar, Length: 128),
        ["SUSER_NAME"] = new SqlType(SqlTypeCategory.NVarChar, Length: 128),
        ["USER_NAME"] = new SqlType(SqlTypeCategory.NVarChar, Length: 128),
        ["APP_NAME"] = new SqlType(SqlTypeCategory.NVarChar, Length: 128),
        ["DB_NAME"] = new SqlType(SqlTypeCategory.NVarChar, Length: 128),
        ["HOST_NAME"] = new SqlType(SqlTypeCategory.NVarChar, Length: 128),
        ["ORIGINAL_LOGIN"] = new SqlType(SqlTypeCategory.NVarChar, Length: 4000),
        ["ERROR_NUMBER"] = new SqlType(SqlTypeCategory.Int),
        ["ERROR_SEVERITY"] = new SqlType(SqlTypeCategory.Int),
        ["ERROR_STATE"] = new SqlType(SqlTypeCategory.Int),
        ["ERROR_LINE"] = new SqlType(SqlTypeCategory.Int),
        ["ERROR_PROCEDURE"] = new SqlType(SqlTypeCategory.NVarChar, Length: 128),
        ["ERROR_MESSAGE"] = new SqlType(SqlTypeCategory.NVarChar, Length: 4000),


        ["UNICODE"] = new SqlType(SqlTypeCategory.Int),
        ["CHAR"] = new SqlType(SqlTypeCategory.Char, Length: 1),
        ["NCHAR"] = new SqlType(SqlTypeCategory.NChar, Length: 1),
        ["SPACE"] = new SqlType(SqlTypeCategory.VarChar, LengthKnown: false),
        ["QUOTENAME"] = new SqlType(SqlTypeCategory.NVarChar, Length: 258),
        ["SOUNDEX"] = new SqlType(SqlTypeCategory.VarChar, Length: 5),
        ["DIFFERENCE"] = new SqlType(SqlTypeCategory.Int),
        ["ISJSON"] = new SqlType(SqlTypeCategory.Int),
        ["DB_ID"] = new SqlType(SqlTypeCategory.SmallInt),
        ["SCHEMA_ID"] = new SqlType(SqlTypeCategory.Int),
        ["TYPE_ID"] = new SqlType(SqlTypeCategory.Int),
        ["USER_ID"] = new SqlType(SqlTypeCategory.SmallInt),
        ["SUSER_ID"] = new SqlType(SqlTypeCategory.Int),
        ["COL_LENGTH"] = new SqlType(SqlTypeCategory.SmallInt),
        ["COLUMNPROPERTY"] = new SqlType(SqlTypeCategory.Int),
        ["INDEXPROPERTY"] = new SqlType(SqlTypeCategory.Int),
        ["FILE_ID"] = new SqlType(SqlTypeCategory.SmallInt),
        ["FILE_IDEX"] = new SqlType(SqlTypeCategory.Int),
        ["FILEGROUP_ID"] = new SqlType(SqlTypeCategory.SmallInt),
        ["XACT_STATE"] = new SqlType(SqlTypeCategory.SmallInt),
        ["SCOPE_IDENTITY"] = new SqlType(SqlTypeCategory.Decimal, Precision: 38, Scale: 0),
        ["IDENT_CURRENT"] = new SqlType(SqlTypeCategory.Decimal, Precision: 38, Scale: 0),
        ["IDENT_INCR"] = new SqlType(SqlTypeCategory.Decimal, Precision: 38, Scale: 0),
        ["IDENT_SEED"] = new SqlType(SqlTypeCategory.Decimal, Precision: 38, Scale: 0),
        ["ROWCOUNT_BIG"] = new SqlType(SqlTypeCategory.BigInt),
        ["OBJECT_NAME"] = new SqlType(SqlTypeCategory.NVarChar, Length: 128),
        ["OBJECT_SCHEMA_NAME"] = new SqlType(SqlTypeCategory.NVarChar, Length: 128),
        ["SCHEMA_NAME"] = new SqlType(SqlTypeCategory.NVarChar, Length: 128),
        ["TYPE_NAME"] = new SqlType(SqlTypeCategory.NVarChar, Length: 128),
        ["COL_NAME"] = new SqlType(SqlTypeCategory.NVarChar, Length: 128),
        ["FILE_NAME"] = new SqlType(SqlTypeCategory.NVarChar, Length: 128),
        ["FILEGROUP_NAME"] = new SqlType(SqlTypeCategory.NVarChar, Length: 128),
        ["INDEX_COL"] = new SqlType(SqlTypeCategory.NVarChar, Length: 128),
        ["PROGRAM_NAME"] = new SqlType(SqlTypeCategory.NVarChar, Length: 128),
        ["DATENAME"] = new SqlType(SqlTypeCategory.NVarChar, Length: 30),
        ["DATEFROMPARTS"] = new SqlType(SqlTypeCategory.Date),
        ["DATETIMEFROMPARTS"] = new SqlType(SqlTypeCategory.DateTime),
        ["SMALLDATETIMEFROMPARTS"] = new SqlType(SqlTypeCategory.SmallDateTime),
        ["CURRENT_TIMEZONE"] = new SqlType(SqlTypeCategory.NVarChar, Length: 128),
        ["CURRENT_TIMEZONE_ID"] = new SqlType(SqlTypeCategory.NVarChar, Length: 128),
        ["JSON_VALUE"] = new SqlType(SqlTypeCategory.NVarChar, Length: 4000),
        ["JSON_MODIFY"] = new SqlType(SqlTypeCategory.NVarChar, IsMax: true),
        ["STRING_ESCAPE"] = new SqlType(SqlTypeCategory.NVarChar, IsMax: true),
        ["HASHBYTES"] = new SqlType(SqlTypeCategory.VarBinary, Length: 8000),
        ["CHECKSUM"] = new SqlType(SqlTypeCategory.Int),
        ["BINARY_CHECKSUM"] = new SqlType(SqlTypeCategory.Int),
        ["PI"] = new SqlType(SqlTypeCategory.Float),
        ["RAND"] = new SqlType(SqlTypeCategory.Float),
        ["SQRT"] = new SqlType(SqlTypeCategory.Float),
        ["SQUARE"] = new SqlType(SqlTypeCategory.Float),
        ["EXP"] = new SqlType(SqlTypeCategory.Float),
        ["LOG"] = new SqlType(SqlTypeCategory.Float),
        ["LOG10"] = new SqlType(SqlTypeCategory.Float),
        ["SIN"] = new SqlType(SqlTypeCategory.Float),
        ["COS"] = new SqlType(SqlTypeCategory.Float),
        ["TAN"] = new SqlType(SqlTypeCategory.Float),
        ["COT"] = new SqlType(SqlTypeCategory.Float),
        ["ASIN"] = new SqlType(SqlTypeCategory.Float),
        ["ACOS"] = new SqlType(SqlTypeCategory.Float),
        ["ATAN"] = new SqlType(SqlTypeCategory.Float),
        ["ATN2"] = new SqlType(SqlTypeCategory.Float),
        ["CONTEXT_INFO"] = new SqlType(SqlTypeCategory.VarBinary, Length: 128),
        ["SESSION_CONTEXT"] = new SqlType(SqlTypeCategory.SqlVariant),
        ["SERVERPROPERTY"] = new SqlType(SqlTypeCategory.SqlVariant),
        ["CONNECTIONPROPERTY"] = new SqlType(SqlTypeCategory.SqlVariant),
        ["DATABASEPROPERTYEX"] = new SqlType(SqlTypeCategory.SqlVariant),
        ["HAS_PERMS_BY_NAME"] = new SqlType(SqlTypeCategory.Int),
        ["IS_MEMBER"] = new SqlType(SqlTypeCategory.Int),
        ["IS_ROLEMEMBER"] = new SqlType(SqlTypeCategory.Int),
        ["IS_SRVROLEMEMBER"] = new SqlType(SqlTypeCategory.Int),
        ["SUSER_SID"] = new SqlType(SqlTypeCategory.VarBinary, Length: 85),
        ["ORIGINAL_DB_NAME"] = new SqlType(SqlTypeCategory.NVarChar, Length: 128),
        ["HOST_ID"] = new SqlType(SqlTypeCategory.Char, Length: 10),
        ["FORMATMESSAGE"] = new SqlType(SqlTypeCategory.NVarChar, Length: 4000),
        ["COMPRESS"] = new SqlType(SqlTypeCategory.VarBinary, IsMax: true),
        ["DECOMPRESS"] = new SqlType(SqlTypeCategory.VarBinary, IsMax: true),
        ["CURRENT_REQUEST_ID"] = new SqlType(SqlTypeCategory.Int),
        ["CURRENT_TRANSACTION_ID"] = new SqlType(SqlTypeCategory.BigInt),
        ["TRIGGER_NESTLEVEL"] = new SqlType(SqlTypeCategory.Int),
        ["GETANSINULL"] = new SqlType(SqlTypeCategory.SmallInt),
        ["OBJECTPROPERTYEX"] = new SqlType(SqlTypeCategory.SqlVariant),
        ["SQL_VARIANT_PROPERTY"] = new SqlType(SqlTypeCategory.SqlVariant),
        ["COLLATIONPROPERTY"] = new SqlType(SqlTypeCategory.SqlVariant),
        ["TYPEPROPERTY"] = new SqlType(SqlTypeCategory.Int),
        ["STATS_DATE"] = new SqlType(SqlTypeCategory.DateTime),
        ["APPLOCK_MODE"] = new SqlType(SqlTypeCategory.NVarChar, Length: 32),
        ["APPLOCK_TEST"] = new SqlType(SqlTypeCategory.Int),
        ["MIN_ACTIVE_ROWVERSION"] = new SqlType(SqlTypeCategory.VarBinary, Length: 8),
        ["REGEXP_COUNT"] = new SqlType(SqlTypeCategory.Int),
        ["REGEXP_INSTR"] = new SqlType(SqlTypeCategory.Int),
        ["BIT_COUNT"] = new SqlType(SqlTypeCategory.BigInt),
        ["GET_BIT"] = new SqlType(SqlTypeCategory.Bit),
        ["CURSOR_STATUS"] = new SqlType(SqlTypeCategory.SmallInt),
        ["EOMONTH"] = new SqlType(SqlTypeCategory.Date),
        ["DATABASE_PRINCIPAL_ID"] = new SqlType(SqlTypeCategory.Int),
        ["STDEV"] = new SqlType(SqlTypeCategory.Float),
        ["STDEVP"] = new SqlType(SqlTypeCategory.Float),
        ["VAR"] = new SqlType(SqlTypeCategory.Float),
        ["VARP"] = new SqlType(SqlTypeCategory.Float),
        ["APPROX_COUNT_DISTINCT"] = new SqlType(SqlTypeCategory.BigInt),
        ["CHECKSUM_AGG"] = new SqlType(SqlTypeCategory.Int),
        ["FORMAT"] = new SqlType(SqlTypeCategory.NVarChar, Length: 4000),
        ["JSON_PATH_EXISTS"] = new SqlType(SqlTypeCategory.Int),
        ["OBJECT_DEFINITION"] = new SqlType(SqlTypeCategory.NVarChar, IsMax: true),
        ["IDENTITYPROPERTY"] = new SqlType(SqlTypeCategory.SqlVariant),
        ["LOGINPROPERTY"] = new SqlType(SqlTypeCategory.SqlVariant),
        ["SESSIONPROPERTY"] = new SqlType(SqlTypeCategory.SqlVariant),
        ["CERTPROPERTY"] = new SqlType(SqlTypeCategory.SqlVariant),
        ["ASYMKEYPROPERTY"] = new SqlType(SqlTypeCategory.SqlVariant),
        ["PERMISSIONS"] = new SqlType(SqlTypeCategory.Int),
        ["CERT_ID"] = new SqlType(SqlTypeCategory.Int),
        ["ASYMKEY_ID"] = new SqlType(SqlTypeCategory.Int),
        ["KEY_ID"] = new SqlType(SqlTypeCategory.Int),
        ["HAS_DBACCESS"] = new SqlType(SqlTypeCategory.Int),
        ["INDEXKEY_PROPERTY"] = new SqlType(SqlTypeCategory.Int),
        ["KEY_GUID"] = new SqlType(SqlTypeCategory.UniqueIdentifier),
        ["CHANGE_TRACKING_CURRENT_VERSION"] = new SqlType(SqlTypeCategory.BigInt),
        ["USER_SID"] = new SqlType(SqlTypeCategory.VarBinary, Length: 85),
        ["CERTENCODED"] = new SqlType(SqlTypeCategory.VarBinary, Length: 8000),
    };

    private static readonly Dictionary<string, SqlType> GlobalVariableTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["@@SPID"] = new SqlType(SqlTypeCategory.SmallInt),
        ["@@ROWCOUNT"] = new SqlType(SqlTypeCategory.Int),
        ["@@ERROR"] = new SqlType(SqlTypeCategory.Int),
        ["@@TRANCOUNT"] = new SqlType(SqlTypeCategory.Int),
        ["@@NESTLEVEL"] = new SqlType(SqlTypeCategory.Int),
        ["@@FETCH_STATUS"] = new SqlType(SqlTypeCategory.Int),

        ["@@CURSOR_ROWS"] = new SqlType(SqlTypeCategory.Int),
        ["@@MAX_CONNECTIONS"] = new SqlType(SqlTypeCategory.Int),
        ["@@LANGID"] = new SqlType(SqlTypeCategory.SmallInt),
        ["@@PACK_RECEIVED"] = new SqlType(SqlTypeCategory.Int),
        ["@@CPU_BUSY"] = new SqlType(SqlTypeCategory.Int),
        ["@@IDENTITY"] = new SqlType(SqlTypeCategory.Decimal, Precision: 38, Scale: 0),
        ["@@CONNECTIONS"] = new SqlType(SqlTypeCategory.Int),
        ["@@DATEFIRST"] = new SqlType(SqlTypeCategory.TinyInt),
        ["@@DBTS"] = new SqlType(SqlTypeCategory.VarBinary, Length: 8),
        ["@@DEF_SORTORDER_ID"] = new SqlType(SqlTypeCategory.TinyInt),
        ["@@DEFAULT_LANGID"] = new SqlType(SqlTypeCategory.SmallInt),
        ["@@IDLE"] = new SqlType(SqlTypeCategory.Int),
        ["@@IO_BUSY"] = new SqlType(SqlTypeCategory.Int),
        ["@@LANGUAGE"] = new SqlType(SqlTypeCategory.NVarChar, Length: 128),
        ["@@LOCK_TIMEOUT"] = new SqlType(SqlTypeCategory.Int),
        ["@@MAX_PRECISION"] = new SqlType(SqlTypeCategory.TinyInt),
        ["@@MICROSOFTVERSION"] = new SqlType(SqlTypeCategory.Int),
        ["@@OPTIONS"] = new SqlType(SqlTypeCategory.Int),
        ["@@PACK_SENT"] = new SqlType(SqlTypeCategory.Int),
        ["@@PACKET_ERRORS"] = new SqlType(SqlTypeCategory.Int),
        ["@@PROCID"] = new SqlType(SqlTypeCategory.Int),
        ["@@REMSERVER"] = new SqlType(SqlTypeCategory.NVarChar, Length: 128),
        ["@@SERVERNAME"] = new SqlType(SqlTypeCategory.NVarChar, Length: 128),
        ["@@SERVICENAME"] = new SqlType(SqlTypeCategory.NVarChar, Length: 128),
        ["@@TEXTSIZE"] = new SqlType(SqlTypeCategory.Int),
        ["@@TIMETICKS"] = new SqlType(SqlTypeCategory.Int),
        ["@@TOTAL_ERRORS"] = new SqlType(SqlTypeCategory.Int),
        ["@@TOTAL_READ"] = new SqlType(SqlTypeCategory.Int),
        ["@@TOTAL_WRITE"] = new SqlType(SqlTypeCategory.Int),
        ["@@VERSION"] = new SqlType(SqlTypeCategory.NVarChar, Length: 300),
    };

    private static readonly Dictionary<string, int> MaxArgumentWidenedToBigIntFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["LEN"] = 0,
        ["DATALENGTH"] = 0,
        ["CHARINDEX"] = 1,
        ["PATINDEX"] = 1,
    };

    private static readonly Dictionary<ParameterlessCallType, SqlType> ParameterlessCallTypes = new()
    {
        [ParameterlessCallType.CurrentTimestamp] = new SqlType(SqlTypeCategory.DateTime),
        [ParameterlessCallType.CurrentDate] = new SqlType(SqlTypeCategory.Date),
        [ParameterlessCallType.CurrentUser] = new SqlType(SqlTypeCategory.NVarChar, Length: 128),
        [ParameterlessCallType.SessionUser] = new SqlType(SqlTypeCategory.NVarChar, Length: 128),
        [ParameterlessCallType.SystemUser] = new SqlType(SqlTypeCategory.NVarChar, Length: 128),
        [ParameterlessCallType.User] = new SqlType(SqlTypeCategory.NVarChar, Length: 128),
    };

    private static readonly Dictionary<string, int> ArgumentTypeFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ISNULL"] = 0,
        ["UPPER"] = 0,
        ["LOWER"] = 0,
        ["LTRIM"] = 0,
        ["RTRIM"] = 0,
        ["REVERSE"] = 0,
        ["REPLACE"] = 0,
        ["LEFT"] = 0,
        ["RIGHT"] = 0,
        ["SUBSTRING"] = 0,
        ["STUFF"] = 0,
        ["MIN"] = 0,
        ["MAX"] = 0,
        ["SUM"] = 0,
        ["AVG"] = 0,
        ["DATEADD"] = 2,
        ["TRIM"] = 0,
        ["TRANSLATE"] = 0,
    };

    private static readonly HashSet<string> IntegerWideningAggregates = new(StringComparer.OrdinalIgnoreCase) { "SUM", "AVG" };

    private static readonly HashSet<string> LengthUnknownAfterArgumentType = new(StringComparer.OrdinalIgnoreCase)
    {
        "LEFT", "RIGHT", "SUBSTRING", "STUFF", "REPLACE", "TRANSLATE",
    };

    public static bool ResultLengthDiffersFromArgument(string functionName) =>
        LengthUnknownAfterArgumentType.Contains(functionName);

    public static SqlType ClearLengthIfUnknown(SqlType argumentType) =>
        argumentType with { Length = null, LengthKnown = false };

    private static readonly HashSet<string> DemotesFixedWidthSourceCategory = new(StringComparer.OrdinalIgnoreCase)
    {
        "UPPER", "LOWER", "LTRIM", "RTRIM", "REVERSE", "REPLACE", "LEFT", "RIGHT", "SUBSTRING", "STUFF", "TRIM", "TRANSLATE",
    };

    public static bool DemotesFixedWidthArgumentCategory(string functionName) =>
        DemotesFixedWidthSourceCategory.Contains(functionName);

    public static SqlType DemoteFixedWidthCategory(SqlType argumentType) => argumentType.Category switch
    {
        SqlTypeCategory.Char => argumentType with { Category = SqlTypeCategory.VarChar },
        SqlTypeCategory.NChar => argumentType with { Category = SqlTypeCategory.NVarChar },
        SqlTypeCategory.Binary => argumentType with { Category = SqlTypeCategory.VarBinary },
        _ => argumentType,
    };

    public static bool RequiresDateAddResultAdjustment(string functionName) =>
        string.Equals(functionName, "DATEADD", StringComparison.OrdinalIgnoreCase);

    public static SqlType? AdjustArgumentTypeFunctionResult(string functionName, SqlType? argumentType)
    {
        if (argumentType is null)
        {
            return null;
        }

        if (WidensIntegerAggregateArgument(functionName))
        {
            var widened = WidenIntegerAggregateResult(argumentType);
            return IsAverageAggregate(functionName) && widened is { Category: SqlTypeCategory.Decimal, Scale: { } avgScale }
                ? widened with { Scale = Math.Max(avgScale, 6) }
                : widened;
        }

        if (RequiresDateAddResultAdjustment(functionName))
        {
            return ResolveDateAddResult(argumentType);
        }

        if (DemotesFixedWidthArgumentCategory(functionName))
        {
            argumentType = DemoteFixedWidthCategory(argumentType);
        }

        if (ResultLengthDiffersFromArgument(functionName))
        {
            argumentType = ClearLengthIfUnknown(argumentType);
        }

        return argumentType;
    }

    public static SqlType ResolveDateAddResult(SqlType thirdArgumentType) =>
        thirdArgumentType.IsDateTimeFamily
            ? thirdArgumentType
            : new SqlType(SqlTypeCategory.DateTime);

    public static SqlType? ResolveFixedReturnType(string functionName) =>
        FixedReturnTypes.GetValueOrDefault(functionName);

    public static IReadOnlyCollection<string> FixedReturnTypeFunctionNames => FixedReturnTypes.Keys;

    public static IReadOnlyCollection<string> GlobalVariableNames => GlobalVariableTypes.Keys;

    public static IReadOnlyCollection<ParameterlessCallType> TypedParameterlessCalls => ParameterlessCallTypes.Keys;

    public static int? TryGetMaxWidenedArgumentIndex(string functionName) =>
        MaxArgumentWidenedToBigIntFunctions.TryGetValue(functionName, out var index) ? index : null;

    public static IReadOnlyCollection<string> MaxWidenedFunctionNames => MaxArgumentWidenedToBigIntFunctions.Keys;

    public static SqlType? ResolveMaxWidenedReturnType(SqlType? decidingArgumentType) => decidingArgumentType switch
    {
        null => null,
        { IsMax: true } when decidingArgumentType.IsStringFamily || decidingArgumentType.IsBinaryFamily => new SqlType(SqlTypeCategory.BigInt),
        _ => new SqlType(SqlTypeCategory.Int),
    };

    public static SqlType? ResolveParameterlessCall(ParameterlessCallType callType) =>
        ParameterlessCallTypes.GetValueOrDefault(callType);

    public static int? TryGetArgumentTypeIndex(string functionName) =>
        ArgumentTypeFunctions.TryGetValue(functionName, out var index) ? index : null;

    public static bool WidensIntegerAggregateArgument(string functionName) =>
        IntegerWideningAggregates.Contains(functionName);

    public static SqlType WidenIntegerAggregateResult(SqlType argumentType) => argumentType.Category switch
    {
        SqlTypeCategory.TinyInt or SqlTypeCategory.SmallInt => new SqlType(SqlTypeCategory.Int),
        SqlTypeCategory.Decimal => argumentType with { Precision = 38 },
        SqlTypeCategory.Real => new SqlType(SqlTypeCategory.Float),
        _ => argumentType,
    };

    private static bool IsAverageAggregate(string functionName) =>
        string.Equals(functionName, "AVG", StringComparison.OrdinalIgnoreCase);

    public static SqlType? ResolveGlobalVariable(string name) =>
        GlobalVariableTypes.GetValueOrDefault(name);

    public static SqlType? ResolveStringAggResult(SqlType? valueType)
    {
        if (valueType is not { IsStringFamily: true })
        {
            return null;
        }

        var category = DemoteFixedWidthCategory(valueType).Category;
        if (valueType.IsMax)
        {
            return new SqlType(category, IsMax: true, Collation: valueType.Collation);
        }

        var cap = category is SqlTypeCategory.NChar or SqlTypeCategory.NVarChar ? UnicodeAggCapChars : NonUnicodeAggCapChars;
        return new SqlType(category, Length: cap, Collation: valueType.Collation);
    }

    private const int NonUnicodeAggCapChars = 8000;
    private const int UnicodeAggCapChars = 4000;
}
