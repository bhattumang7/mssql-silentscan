namespace SilentScan.Verify.Oracle;

public enum ScopeObservableKind
{
    Witness,

    Binding,
}

public sealed record ScopeScenario(
    string Id,
    string Family,
    string Step,
    ScopeObservableKind Kind,
    bool CaseSensitiveDatabase,
    string Setup,
    string Body);

public static class ScopeScenarios
{
    private const string OneColumnTable = "CREATE TABLE {P}t (id INT); INSERT {P}t VALUES (1);";

    private const string TwoColumnTable = "CREATE TABLE {P}t (a INT, b INT); INSERT {P}t VALUES (1, 20), (2, 10);";

    private const string TwoTablesSameColumn =
        "CREATE TABLE {P}t (id INT, v INT); INSERT {P}t VALUES (1, 100); CREATE TABLE {P}u (id INT, v INT); INSERT {P}u VALUES (1, 200);";

    private const string UpdateTables =
        "CREATE TABLE {P}t (id INT, v INT); INSERT {P}t VALUES (1, 1); CREATE TABLE {P}u (id INT, v INT); INSERT {P}u VALUES (1, 1);";

    private const string ReportBoth =
        "SELECT CAST((SELECT v FROM {P}t) AS VARCHAR(5)) + ',' + CAST((SELECT v FROM {P}u) AS VARCHAR(5));";

    private const string TwoArgumentProcedure =
        "CREATE PROCEDURE {P}p @a INT, @b INT AS SELECT CAST(@a AS VARCHAR(5)) + ',' + CAST(@b AS VARCHAR(5));";

    private const string SchemaSetup =
        "CREATE SCHEMA {P}sa;\nGO\nCREATE SCHEMA {P}sb;\nGO\n"
        + "CREATE TABLE dbo.{P}t (v INT); INSERT dbo.{P}t VALUES (0);\n"
        + "CREATE TABLE {P}sa.{P}t (v INT); INSERT {P}sa.{P}t VALUES (1);\n"
        + "CREATE TABLE {P}sb.{P}t (v INT); INSERT {P}sb.{P}t VALUES (2);\n"
        + "CREATE USER {P}u WITHOUT LOGIN WITH DEFAULT_SCHEMA = {P}sb;\n"
        + "GRANT EXECUTE ON SCHEMA::{P}sa TO {P}u;\nGRANT SELECT ON SCHEMA::{P}sa TO {P}u;\nGRANT SELECT ON SCHEMA::{P}sb TO {P}u;\nGRANT SELECT ON SCHEMA::dbo TO {P}u;\n"
        + "GO\nCREATE PROCEDURE {P}sa.{P}m AS SELECT v FROM {P}t;\nGO\n";

    public static readonly IReadOnlyList<ScopeScenario> All =
    [
        new("where-cannot-see-select-alias", "query-order", "A1", ScopeObservableKind.Witness, false, OneColumnTable, "SELECT id AS x FROM {P}t WHERE x = 1"),
        new("orderby-sees-select-alias", "query-order", "A1", ScopeObservableKind.Witness, false, OneColumnTable, "SELECT TOP 1 id AS x FROM {P}t ORDER BY x"),
        new("orderby-select-name-beats-input-column", "query-order", "A2", ScopeObservableKind.Witness, false, TwoColumnTable, "SELECT TOP 1 b AS a, a AS c FROM {P}t ORDER BY a"),
        new("orderby-qualified-name-skips-select-alias", "query-order", "A2", ScopeObservableKind.Witness, false, TwoColumnTable, "SELECT TOP 1 b AS a, a AS c FROM {P}t ORDER BY {P}t.a"),
        new("inner-scope-shadows-outer", "query-order", "A3", ScopeObservableKind.Witness, false, TwoTablesSameColumn, "SELECT (SELECT TOP 1 v FROM {P}u) FROM {P}t"),
        new("alias-case-in-ci-database", "alias", "B3", ScopeObservableKind.Witness, false, OneColumnTable, "SELECT A.id FROM {P}t AS a"),
        new("alias-case-in-cs-database", "alias", "B3", ScopeObservableKind.Witness, true, OneColumnTable, "SELECT A.id FROM {P}t AS a"),
        new("schema-qualified-name-matches-unaliased-table", "alias", "B4", ScopeObservableKind.Witness, false, OneColumnTable, "SELECT dbo.{P}t.id FROM {P}t"),
        new("aliased-table-hides-qualified-name", "alias", "B4", ScopeObservableKind.Witness, false, OneColumnTable, "SELECT dbo.{P}t.id FROM dbo.{P}t AS a"),
        new("same-table-under-two-aliases", "alias", "B5", ScopeObservableKind.Witness, false, OneColumnTable, "SELECT COUNT(*) FROM {P}t a CROSS JOIN {P}t b"),
        new("unaliased-duplicate-by-last-name-part", "alias", "B5", ScopeObservableKind.Witness, false, OneColumnTable, "SELECT COUNT(*) FROM {P}t CROSS JOIN dbo.{P}t"),
        new("cte-own-name-hides-table-in-its-body", "cte", "B9", ScopeObservableKind.Witness, false, "CREATE TABLE {P}t (id INT); INSERT {P}t VALUES (100);",
            "WITH {P}t AS (SELECT 1 AS id UNION ALL SELECT id + 1 FROM {P}t WHERE id < 3) SELECT MAX(id) FROM {P}t"),
        new("cte-hides-table-after-its-definition", "cte", "B9", ScopeObservableKind.Witness, false, "CREATE TABLE {P}t (id INT); INSERT {P}t VALUES (100);",
            "WITH {P}t AS (SELECT 5 AS id) SELECT MAX(id) FROM {P}t"),
        new("update-target-resolves-to-from-alias", "dml-target", "C1", ScopeObservableKind.Witness, false, UpdateTables, "UPDATE {P}t SET v = 9 FROM {P}u AS {P}t; " + ReportBoth),
        new("update-target-without-alias-is-the-table", "dml-target", "C1", ScopeObservableKind.Witness, false, UpdateTables, "UPDATE {P}t SET v = 9 FROM {P}t AS a; " + ReportBoth),
        new("declare-in-taken-branch-visible-after", "variables", "E1", ScopeObservableKind.Witness, false, string.Empty,
            "IF OBJECT_ID('{P}none') IS NULL BEGIN DECLARE @x INT = 5; END SELECT @x"),
        new("declare-in-untaken-branch-still-declared", "variables", "E1", ScopeObservableKind.Witness, false, string.Empty,
            "IF OBJECT_ID('{P}none') IS NOT NULL BEGIN DECLARE @x INT = 5; END SELECT @x"),
        new("declare-in-loop-keeps-value-across-iterations", "variables", "E1", ScopeObservableKind.Witness, false, string.Empty,
            "DECLARE @i INT = 0, @r VARCHAR(20) = ''; WHILE @i < 2 BEGIN DECLARE @y INT; SET @y = ISNULL(@y, 0) + 1; SET @r += CAST(@y AS VARCHAR(3)); SET @i += 1; END SELECT @r"),
        new("table-variable-name-case-in-cs-database", "variables", "E2", ScopeObservableKind.Witness, true, string.Empty,
            "DECLARE @T TABLE (v INT); INSERT @T VALUES (1); SELECT COUNT(*) FROM @t"),
        new("exec-string-cannot-see-caller-variable", "exec", "G1", ScopeObservableKind.Witness, false, string.Empty, "DECLARE @x INT = 1; EXEC('SELECT @x')"),
        new("exec-string-expression-uses-caller-variable", "exec", "G1", ScopeObservableKind.Witness, false, string.Empty,
            "DECLARE @x VARCHAR(5) = '7'; EXEC('SELECT ' + @x)"),
        new("caller-temp-table-visible-in-callee", "temp-tables", "F1", ScopeObservableKind.Witness, false, "CREATE PROCEDURE {P}inner AS SELECT v FROM #tt;",
            "CREATE TABLE #tt (v INT); INSERT #tt VALUES (3); EXEC {P}inner"),
        new("callee-temp-table-invisible-to-caller", "temp-tables", "F1", ScopeObservableKind.Witness, false, "CREATE PROCEDURE {P}maker AS CREATE TABLE #cc (v INT);",
            "EXEC {P}maker; SELECT COUNT(*) FROM #cc"),
        new("positional-then-named-arguments", "exec", "G2", ScopeObservableKind.Witness, false, TwoArgumentProcedure, "EXEC {P}p 1, @b = 2"),
        new("named-argument-name-case-in-cs-database", "exec", "G2", ScopeObservableKind.Witness, true, TwoArgumentProcedure, "EXEC {P}p @A = 1, @B = 2"),
        new("unqualified-name-in-module-uses-module-or-caller-schema", "object-names", "D4", ScopeObservableKind.Witness, false, SchemaSetup,
            "EXECUTE AS USER = '{P}u'; EXEC {P}sa.{P}m; REVERT;"),
        new("bind-unqualified-table", "dependencies", "D4", ScopeObservableKind.Binding, false, "CREATE TABLE {P}t (id INT);",
            "CREATE PROCEDURE {P}m AS SELECT id FROM {P}t"),
        new("bind-qualified-table-and-column", "dependencies", "D6", ScopeObservableKind.Binding, false, "CREATE TABLE {P}t (id INT);",
            "CREATE PROCEDURE {P}m AS SELECT id FROM dbo.{P}t"),
        new("bind-synonym-registers-synonym-and-target", "dependencies", "D5", ScopeObservableKind.Binding, false,
            "CREATE TABLE {P}t (id INT); CREATE SYNONYM {P}syn FOR dbo.{P}t;", "CREATE PROCEDURE {P}m AS SELECT id FROM dbo.{P}syn"),
        new("bind-temp-table-records-no-dependency", "dependencies", "D6", ScopeObservableKind.Binding, false, string.Empty,
            "CREATE PROCEDURE {P}m AS BEGIN CREATE TABLE #z (id INT); SELECT id FROM #z; END"),
        new("bind-orderby-alias-records-base-column", "dependencies", "A2", ScopeObservableKind.Binding, false, "CREATE TABLE {P}t (id INT);",
            "CREATE PROCEDURE {P}m AS SELECT id AS x FROM dbo.{P}t ORDER BY x"),
        new("bind-missing-table-is-recorded-by-name", "dependencies", "D6", ScopeObservableKind.Binding, false, string.Empty,
            "CREATE PROCEDURE {P}m AS SELECT id FROM dbo.{P}missing"),
    ];
}
