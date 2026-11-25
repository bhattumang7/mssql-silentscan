namespace SilentScan.Verify.Oracle;

public enum SargFamily
{
    Any,

    Numeric,

    Text,

    Temporal,

    TemporalWithDate,
}

public sealed record SargShape(string Id, string Template, SargFamily Family);

public static class SargShapes
{
    public static readonly IReadOnlyList<string> ColumnTypes =
    [
        "BIT", "TINYINT", "SMALLINT", "INT", "BIGINT", "DECIMAL(5,1)", "DECIMAL(18,4)", "DECIMAL(38,10)", "SMALLMONEY", "MONEY", "REAL", "FLOAT",
        "CHAR(10)", "VARCHAR(10)", "NCHAR(10)", "NVARCHAR(10)", "DATE", "TIME(3)", "SMALLDATETIME", "DATETIME", "DATETIME2(3)", "DATETIME2(7)",
        "DATETIMEOFFSET(3)", "BINARY(10)", "VARBINARY(10)", "UNIQUEIDENTIFIER",
        "VARCHAR(10) COLLATE Latin1_General_100_CI_AS_SC_UTF8", "VARCHAR(10) COLLATE SQL_Latin1_General_CP1_CS_AS", "NVARCHAR(10) COLLATE Latin1_General_BIN2",
        "VARCHAR(10) COLLATE Latin1_General_BIN2",
    ];

    public static readonly IReadOnlyList<SargShape> All =
    [
        new("eq", "c = @p", SargFamily.Any),
        new("eq-reversed", "@p = c", SargFamily.Any),
        new("ne", "c <> @p", SargFamily.Any),
        new("gt", "c > @p", SargFamily.Any),
        new("le", "c <= @p", SargFamily.Any),
        new("between", "c BETWEEN @p AND @p", SargFamily.Any),
        new("in-list", "c IN (@p, @p)", SargFamily.Any),
        new("is-null", "c IS NULL", SargFamily.Any),
        new("is-not-null", "c IS NOT NULL", SargFamily.Any),
        new("not-eq", "NOT (c = @p)", SargFamily.Any),
        new("or-same-column", "c = @p OR c = @p", SargFamily.Any),
        new("or-other-column", "c = @p OR pad = 'x'", SargFamily.Any),
        new("and-other-column", "c = @p AND pad = 'x'", SargFamily.Any),
        new("eq-expression-rhs", "c = ISNULL(@p, @p)", SargFamily.Any),
        new("isnull-wrap", "ISNULL(c, @p) = @p", SargFamily.Any),
        new("coalesce-wrap", "COALESCE(c, @p) = @p", SargFamily.Any),
        new("cast-bigint", "CAST(c AS BIGINT) = 1", SargFamily.Numeric),
        new("cast-varchar", "CAST(c AS VARCHAR(30)) = '1'", SargFamily.Any),
        new("cast-same-type", "CAST(c AS <T>) = @p", SargFamily.Any),
        new("add-constant", "c + 1 = 5", SargFamily.Numeric),
        new("multiply-constant", "c * 2 = 10", SargFamily.Numeric),
        new("divide-constant", "c / 2 = 5", SargFamily.Numeric),
        new("modulo", "c % 2 = 0", SargFamily.Numeric),
        new("negate", "-c = -5", SargFamily.Numeric),
        new("abs", "ABS(c) = 5", SargFamily.Numeric),
        new("round", "ROUND(c, 0) = 5", SargFamily.Numeric),
        new("like-prefix", "c LIKE 'ab%'", SargFamily.Text),
        new("like-suffix", "c LIKE '%ab'", SargFamily.Text),
        new("like-contains", "c LIKE '%ab%'", SargFamily.Text),
        new("like-prefix-variable", "c LIKE @p + '%'", SargFamily.Text),
        new("like-prefix-variable-no-concat", "c LIKE @p", SargFamily.Text),
        new("like-char-class", "c LIKE '[ab]%'", SargFamily.Text),
        new("like-escape", "c LIKE 'a!%%' ESCAPE '!'", SargFamily.Text),
        new("left", "LEFT(c, 2) = 'ab'", SargFamily.Text),
        new("substring", "SUBSTRING(c, 1, 2) = 'ab'", SargFamily.Text),
        new("ltrim", "LTRIM(c) = 'ab'", SargFamily.Text),
        new("rtrim", "RTRIM(c) = 'ab'", SargFamily.Text),
        new("upper", "UPPER(c) = 'AB'", SargFamily.Text),
        new("lower", "LOWER(c) = 'ab'", SargFamily.Text),
        new("len", "LEN(c) = 2", SargFamily.Text),
        new("replace", "REPLACE(c, 'a', 'b') = 'bb'", SargFamily.Text),
        new("charindex-prefix", "CHARINDEX('ab', c) = 1", SargFamily.Text),
        new("collate-literal", "c = 'ab' COLLATE Latin1_General_CI_AS", SargFamily.Text),
        new("collate-column", "c COLLATE Latin1_General_CI_AS = 'ab'", SargFamily.Text),
        new("concat-plus", "c + 'x' = 'abx'", SargFamily.Text),
        new("year", "YEAR(c) = 2024", SargFamily.TemporalWithDate),
        new("month", "MONTH(c) = 1", SargFamily.TemporalWithDate),
        new("datepart-year", "DATEPART(year, c) = 2024", SargFamily.TemporalWithDate),
        new("datediff", "DATEDIFF(day, c, @p) = 0", SargFamily.TemporalWithDate),
        new("dateadd", "DATEADD(day, 1, c) = @p", SargFamily.TemporalWithDate),
        new("cast-date", "CAST(c AS DATE) = '2024-01-02'", SargFamily.TemporalWithDate),
        new("convert-date-style", "CONVERT(VARCHAR(10), c, 120) = '2024-01-02'", SargFamily.TemporalWithDate),
        new("half-open-range", "c >= @p AND c < DATEADD(day, 1, @p)", SargFamily.TemporalWithDate),
        new("eomonth", "EOMONTH(c) = '2024-01-31'", SargFamily.TemporalWithDate),
        new("hash-wrap", "CHECKSUM(c) = 1", SargFamily.Any),
    ];

    public static bool Applies(SargShape shape, string columnType)
    {
        var upper = columnType.ToUpperInvariant();
        var temporal = upper.StartsWith("DATE", StringComparison.Ordinal) || upper.StartsWith("TIME", StringComparison.Ordinal) || upper.StartsWith("SMALLDATETIME", StringComparison.Ordinal);
        var withDate = temporal && !upper.StartsWith("TIME", StringComparison.Ordinal);
        var text = upper.Contains("CHAR", StringComparison.Ordinal);
        var numeric = !temporal && !text && !upper.Contains("BINARY", StringComparison.Ordinal) && !upper.StartsWith("UNIQUE", StringComparison.Ordinal) && upper != "BIT";
        return shape.Family switch
        {
            SargFamily.Numeric => numeric,
            SargFamily.Text => text,
            SargFamily.Temporal => temporal,
            SargFamily.TemporalWithDate => withDate,
            _ => true,
        };
    }

    public static string VariableType(string columnType)
    {
        var index = columnType.IndexOf(" COLLATE ", StringComparison.OrdinalIgnoreCase);
        return index < 0 ? columnType : columnType[..index];
    }
}
