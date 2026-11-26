namespace SilentScan.Verify.Oracle;

public sealed record RescueScenario(string Id, string Family, string Table, string Predicate);

public static class RescueScenarios
{
    private const string NullabilityKind = "nullability";
    private const string CheckConstraintKind = "check-constraint";
    private const string FoldingKind = "folding";
    private const string IdentityCastKind = "identity-cast";
    private const string ComputedColumnKind = "computed-column";

    private const string Plain = "CREATE TABLE {t} (c INT NULL, d DATETIME NULL, s VARCHAR(10) NULL, pad CHAR(10) NULL, filler CHAR(200) NULL); CREATE INDEX ix_c ON {t} (c); CREATE INDEX ix_d ON {t} (d); CREATE INDEX ix_s ON {t} (s);";

    private const string NotNull = "CREATE TABLE {t} (c INT NOT NULL, d DATETIME NOT NULL, s VARCHAR(10) NOT NULL, pad CHAR(10) NULL, filler CHAR(200) NULL); CREATE INDEX ix_c ON {t} (c); CREATE INDEX ix_d ON {t} (d); CREATE INDEX ix_s ON {t} (s);";

    private const string Checked = "CREATE TABLE {t} (c INT NOT NULL CHECK (c BETWEEN 1 AND 10), d DATETIME NOT NULL, s VARCHAR(10) NOT NULL CHECK (s IN ('a','b')), pad CHAR(10) NULL, filler CHAR(200) NULL); CREATE INDEX ix_c ON {t} (c); CREATE INDEX ix_s ON {t} (s);";

    private const string Computed = "CREATE TABLE {t} (c INT NOT NULL, d DATETIME NOT NULL, s VARCHAR(10) NOT NULL, c2 AS (c * 2) PERSISTED, sy AS (YEAR(d)) PERSISTED, su AS (UPPER(s)) PERSISTED, pad CHAR(10) NULL, filler CHAR(200) NULL); CREATE INDEX ix_c ON {t} (c); CREATE INDEX ix_c2 ON {t} (c2); CREATE INDEX ix_sy ON {t} (sy); CREATE INDEX ix_su ON {t} (su);";

    private const string Unique = "CREATE TABLE {t} (c INT NOT NULL PRIMARY KEY, d DATETIME NOT NULL, s VARCHAR(10) NOT NULL UNIQUE, pad CHAR(10) NULL, filler CHAR(200) NULL);";

    private const string Filtered = "CREATE TABLE {t} (c INT NULL, d DATETIME NULL, s VARCHAR(10) NULL, pad CHAR(10) NULL, filler CHAR(200) NULL); CREATE INDEX ix_c ON {t} (c) WHERE c > 100;";

    public static readonly IReadOnlyList<RescueScenario> All =
    [
        new("not-null-isnull", NullabilityKind, NotNull, "ISNULL(c, 0) = 5"),
        new("nullable-isnull", NullabilityKind, Plain, "ISNULL(c, 0) = 5"),
        new("not-null-coalesce", NullabilityKind, NotNull, "COALESCE(c, 0) = 5"),
        new("not-null-is-null", NullabilityKind, NotNull, "c IS NULL"),
        new("not-null-is-not-null", NullabilityKind, NotNull, "c IS NOT NULL"),
        new("nullable-cast-same-type", IdentityCastKind, Plain, "CAST(s AS VARCHAR(10)) = 'a'"),
        new("not-null-cast-same-type", IdentityCastKind, NotNull, "CAST(s AS VARCHAR(10)) = 'a'"),
        new("cast-int-to-int", IdentityCastKind, Plain, "CAST(c AS INT) = 5"),
        new("cast-int-to-bigint", IdentityCastKind, Plain, "CAST(c AS BIGINT) = 5"),
        new("convert-int-to-int", IdentityCastKind, Plain, "CONVERT(INT, c) = 5"),
        new("cast-varchar-to-wider-varchar", IdentityCastKind, Plain, "CAST(s AS VARCHAR(20)) = 'a'"),
        new("cast-varchar-to-nvarchar", IdentityCastKind, Plain, "CAST(s AS NVARCHAR(10)) = N'a'"),
        new("cast-datetime-to-date", IdentityCastKind, Plain, "CAST(d AS DATE) = '2024-01-02'"),
        new("add-zero", "identity-arith", Plain, "c + 0 = 5"),
        new("multiply-one", "identity-arith", Plain, "c * 1 = 5"),
        new("add-constant", "identity-arith", Plain, "c + 1 = 5"),
        new("constant-folded-rhs", FoldingKind, Plain, "c = 2 + 3"),
        new("constant-function-rhs", FoldingKind, Plain, "c = ABS(-5)"),
        new("literal-int-vs-string", FoldingKind, Plain, "c = '5'"),
        new("literal-int-vs-decimal", FoldingKind, Plain, "c = 5.0"),
        new("varchar-vs-nvarchar-literal", FoldingKind, Plain, "s = N'a'"),
        new("tautology-and", FoldingKind, Plain, "1 = 1 AND c = 5"),
        new("tautology-or", FoldingKind, Plain, "c = 5 OR 1 = 1"),
        new("contradiction-and", FoldingKind, Plain, "c = 5 AND c = 6"),
        new("self-compare", FoldingKind, Plain, "c = c"),
        new("check-contradicts-equal", CheckConstraintKind, Checked, "c = 50"),
        new("check-contradicts-function", CheckConstraintKind, Checked, "ABS(c) = 50"),
        new("check-contradicts-in-list", CheckConstraintKind, Checked, "s = 'z'"),
        new("check-allows-equal", CheckConstraintKind, Checked, "c = 5"),
        new("computed-matches-expression", ComputedColumnKind, Computed, "c * 2 = 10"),
        new("computed-matches-year", ComputedColumnKind, Computed, "YEAR(d) = 2024"),
        new("computed-matches-upper", ComputedColumnKind, Computed, "UPPER(s) = 'A'"),
        new("computed-expression-differs", ComputedColumnKind, Computed, "c * 3 = 15"),
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
