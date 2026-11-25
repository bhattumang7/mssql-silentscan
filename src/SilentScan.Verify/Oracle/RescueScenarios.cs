namespace SilentScan.Verify.Oracle;

public sealed record RescueScenario(string Id, string Family, string Table, string Predicate);

public static class RescueScenarios
{
    private const string Plain = "CREATE TABLE {t} (c INT NULL, d DATETIME NULL, s VARCHAR(10) NULL, pad CHAR(10) NULL, filler CHAR(200) NULL); CREATE INDEX ix_c ON {t} (c); CREATE INDEX ix_d ON {t} (d); CREATE INDEX ix_s ON {t} (s);";

    private const string NotNull = "CREATE TABLE {t} (c INT NOT NULL, d DATETIME NOT NULL, s VARCHAR(10) NOT NULL, pad CHAR(10) NULL, filler CHAR(200) NULL); CREATE INDEX ix_c ON {t} (c); CREATE INDEX ix_d ON {t} (d); CREATE INDEX ix_s ON {t} (s);";

    private const string Checked = "CREATE TABLE {t} (c INT NOT NULL CHECK (c BETWEEN 1 AND 10), d DATETIME NOT NULL, s VARCHAR(10) NOT NULL CHECK (s IN ('a','b')), pad CHAR(10) NULL, filler CHAR(200) NULL); CREATE INDEX ix_c ON {t} (c); CREATE INDEX ix_s ON {t} (s);";

    private const string Computed = "CREATE TABLE {t} (c INT NOT NULL, d DATETIME NOT NULL, s VARCHAR(10) NOT NULL, c2 AS (c * 2) PERSISTED, sy AS (YEAR(d)) PERSISTED, su AS (UPPER(s)) PERSISTED, pad CHAR(10) NULL, filler CHAR(200) NULL); CREATE INDEX ix_c ON {t} (c); CREATE INDEX ix_c2 ON {t} (c2); CREATE INDEX ix_sy ON {t} (sy); CREATE INDEX ix_su ON {t} (su);";

    private const string Unique = "CREATE TABLE {t} (c INT NOT NULL PRIMARY KEY, d DATETIME NOT NULL, s VARCHAR(10) NOT NULL UNIQUE, pad CHAR(10) NULL, filler CHAR(200) NULL);";

    private const string Filtered = "CREATE TABLE {t} (c INT NULL, d DATETIME NULL, s VARCHAR(10) NULL, pad CHAR(10) NULL, filler CHAR(200) NULL); CREATE INDEX ix_c ON {t} (c) WHERE c > 100;";

    public static readonly IReadOnlyList<RescueScenario> All =
    [
        new("not-null-isnull", "nullability", NotNull, "ISNULL(c, 0) = 5"),
        new("nullable-isnull", "nullability", Plain, "ISNULL(c, 0) = 5"),
        new("not-null-coalesce", "nullability", NotNull, "COALESCE(c, 0) = 5"),
        new("not-null-is-null", "nullability", NotNull, "c IS NULL"),
        new("not-null-is-not-null", "nullability", NotNull, "c IS NOT NULL"),
        new("nullable-cast-same-type", "identity-cast", Plain, "CAST(s AS VARCHAR(10)) = 'a'"),
        new("not-null-cast-same-type", "identity-cast", NotNull, "CAST(s AS VARCHAR(10)) = 'a'"),
        new("cast-int-to-int", "identity-cast", Plain, "CAST(c AS INT) = 5"),
        new("cast-int-to-bigint", "identity-cast", Plain, "CAST(c AS BIGINT) = 5"),
        new("convert-int-to-int", "identity-cast", Plain, "CONVERT(INT, c) = 5"),
        new("cast-varchar-to-wider-varchar", "identity-cast", Plain, "CAST(s AS VARCHAR(20)) = 'a'"),
        new("cast-varchar-to-nvarchar", "identity-cast", Plain, "CAST(s AS NVARCHAR(10)) = N'a'"),
        new("cast-datetime-to-date", "identity-cast", Plain, "CAST(d AS DATE) = '2024-01-02'"),
        new("add-zero", "identity-arith", Plain, "c + 0 = 5"),
        new("multiply-one", "identity-arith", Plain, "c * 1 = 5"),
        new("add-constant", "identity-arith", Plain, "c + 1 = 5"),
        new("constant-folded-rhs", "folding", Plain, "c = 2 + 3"),
        new("constant-function-rhs", "folding", Plain, "c = ABS(-5)"),
        new("literal-int-vs-string", "folding", Plain, "c = '5'"),
        new("literal-int-vs-decimal", "folding", Plain, "c = 5.0"),
        new("varchar-vs-nvarchar-literal", "folding", Plain, "s = N'a'"),
        new("tautology-and", "folding", Plain, "1 = 1 AND c = 5"),
        new("tautology-or", "folding", Plain, "c = 5 OR 1 = 1"),
        new("contradiction-and", "folding", Plain, "c = 5 AND c = 6"),
        new("self-compare", "folding", Plain, "c = c"),
        new("check-contradicts-equal", "check-constraint", Checked, "c = 50"),
        new("check-contradicts-function", "check-constraint", Checked, "ABS(c) = 50"),
        new("check-contradicts-in-list", "check-constraint", Checked, "s = 'z'"),
        new("check-allows-equal", "check-constraint", Checked, "c = 5"),
        new("computed-matches-expression", "computed-column", Computed, "c * 2 = 10"),
        new("computed-matches-year", "computed-column", Computed, "YEAR(d) = 2024"),
        new("computed-matches-upper", "computed-column", Computed, "UPPER(s) = 'A'"),
        new("computed-expression-differs", "computed-column", Computed, "c * 3 = 15"),
        new("function-on-unique-key", "unique-key", Unique, "ABS(c) = 5"),
        new("function-on-unique-string", "unique-key", Unique, "UPPER(s) = 'A'"),
        new("filtered-index-covers", "filtered-index", Filtered, "c = 200"),
        new("filtered-index-does-not-cover", "filtered-index", Filtered, "c = 5"),
        new("like-no-wildcard", "like", Plain, "s LIKE 'abc'"),
        new("like-only-wildcard", "like", Plain, "s LIKE '%'"),
        new("like-prefix", "like", Plain, "s LIKE 'abc%'"),
        new("like-leading-wildcard", "like", Plain, "s LIKE '%abc'"),
        new("charindex-equals-one", "like", Plain, "CHARINDEX('abc', s) = 1"),
        new("left-prefix", "like", Plain, "LEFT(s, 3) = 'abc'"),
    ];
}
