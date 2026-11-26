namespace SilentScan.Verify.Oracle;

public enum ScopeObservableKind
{
    Witness,

    Binding,

    OutputColumns,
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
    private const string OutputColumnsKind = "output-columns";
    private const string QueryOrderKind = "query-order";
    private const string AliasKind = "alias";
    private const string VariablesKind = "variables";
    private const string DependenciesKind = "dependencies";

    private const string TwoTables = "CREATE TABLE {P}t (id INT, a INT, b INT); CREATE TABLE {P}u (id INT, c INT, b INT);";

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
        new("where-cannot-see-select-alias", QueryOrderKind, "A1", ScopeObservableKind.Witness, false, OneColumnTable, "SELECT id AS x FROM {P}t WHERE x = 1"),
        new("orderby-sees-select-alias", QueryOrderKind, "A1", ScopeObservableKind.Witness, false, OneColumnTable, "SELECT TOP 1 id AS x FROM {P}t ORDER BY x"),
        new("orderby-select-name-beats-input-column", QueryOrderKind, "A2", ScopeObservableKind.Witness, false, TwoColumnTable, "SELECT TOP 1 b AS a, a AS c FROM {P}t ORDER BY a"),
        new("orderby-qualified-name-skips-select-alias", QueryOrderKind, "A2", ScopeObservableKind.Witness, false, TwoColumnTable, "SELECT TOP 1 b AS a, a AS c FROM {P}t ORDER BY {P}t.a"),
        new("inner-scope-shadows-outer", QueryOrderKind, "A3", ScopeObservableKind.Witness, false, TwoTablesSameColumn, "SELECT (SELECT TOP 1 v FROM {P}u) FROM {P}t"),
        new("alias-case-in-ci-database", AliasKind, "B3", ScopeObservableKind.Witness, false, OneColumnTable, "SELECT A.id FROM {P}t AS a"),
        new("alias-case-in-cs-database", AliasKind, "B3", ScopeObservableKind.Witness, true, OneColumnTable, "SELECT A.id FROM {P}t AS a"),
        new("schema-qualified-name-matches-unaliased-table", AliasKind, "B4", ScopeObservableKind.Witness, false, OneColumnTable, "SELECT dbo.{P}t.id FROM {P}t"),
        new("aliased-table-hides-qualified-name", AliasKind, "B4", ScopeObservableKind.Witness, false, OneColumnTable, "SELECT dbo.{P}t.id FROM dbo.{P}t AS a"),
        new("same-table-under-two-aliases", AliasKind, "B5", ScopeObservableKind.Witness, false, OneColumnTable, "SELECT COUNT(*) FROM {P}t a CROSS JOIN {P}t b"),
        new("unaliased-duplicate-by-last-name-part", AliasKind, "B5", ScopeObservableKind.Witness, false, OneColumnTable, "SELECT COUNT(*) FROM {P}t CROSS JOIN dbo.{P}t"),
        new("cte-own-name-hides-table-in-its-body", "cte", "B9", ScopeObservableKind.Witness, false, "CREATE TABLE {P}t (id INT); INSERT {P}t VALUES (100);",
            "WITH {P}t AS (SELECT 1 AS id UNION ALL SELECT id + 1 FROM {P}t WHERE id < 3) SELECT MAX(id) FROM {P}t"),
        new("cte-hides-table-after-its-definition", "cte", "B9", ScopeObservableKind.Witness, false, "CREATE TABLE {P}t (id INT); INSERT {P}t VALUES (100);",
            "WITH {P}t AS (SELECT 5 AS id) SELECT MAX(id) FROM {P}t"),
        new("update-target-resolves-to-from-alias", "dml-target", "C1", ScopeObservableKind.Witness, false, UpdateTables, "UPDATE {P}t SET v = 9 FROM {P}u AS {P}t; " + ReportBoth),
        new("update-target-without-alias-is-the-table", "dml-target", "C1", ScopeObservableKind.Witness, false, UpdateTables, "UPDATE {P}t SET v = 9 FROM {P}t AS a; " + ReportBoth),
        new("declare-in-taken-branch-visible-after", VariablesKind, "E1", ScopeObservableKind.Witness, false, string.Empty,
            "IF OBJECT_ID('{P}none') IS NULL BEGIN DECLARE @x INT = 5; END SELECT @x"),
        new("declare-in-untaken-branch-still-declared", VariablesKind, "E1", ScopeObservableKind.Witness, false, string.Empty,
            "IF OBJECT_ID('{P}none') IS NOT NULL BEGIN DECLARE @x INT = 5; END SELECT @x"),
        new("declare-in-loop-keeps-value-across-iterations", VariablesKind, "E1", ScopeObservableKind.Witness, false, string.Empty,
            "DECLARE @i INT = 0, @r VARCHAR(20) = ''; WHILE @i < 2 BEGIN DECLARE @y INT; SET @y = ISNULL(@y, 0) + 1; SET @r += CAST(@y AS VARCHAR(3)); SET @i += 1; END SELECT @r"),
        new("table-variable-name-case-in-cs-database", VariablesKind, "E2", ScopeObservableKind.Witness, true, string.Empty,
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
        new("bind-unqualified-table", DependenciesKind, "D4", ScopeObservableKind.Binding, false, "CREATE TABLE {P}t (id INT);",
            "CREATE PROCEDURE {P}m AS SELECT id FROM {P}t"),
        new("bind-qualified-table-and-column", DependenciesKind, "D6", ScopeObservableKind.Binding, false, "CREATE TABLE {P}t (id INT);",
            "CREATE PROCEDURE {P}m AS SELECT id FROM dbo.{P}t"),
        new("bind-synonym-registers-synonym-and-target", DependenciesKind, "D5", ScopeObservableKind.Binding, false,
            "CREATE TABLE {P}t (id INT); CREATE SYNONYM {P}syn FOR dbo.{P}t;", "CREATE PROCEDURE {P}m AS SELECT id FROM dbo.{P}syn"),
        new("bind-temp-table-records-no-dependency", DependenciesKind, "D6", ScopeObservableKind.Binding, false, string.Empty,
            "CREATE PROCEDURE {P}m AS BEGIN CREATE TABLE #z (id INT); SELECT id FROM #z; END"),
        new("bind-orderby-alias-records-base-column", DependenciesKind, "A2", ScopeObservableKind.Binding, false, "CREATE TABLE {P}t (id INT);",
            "CREATE PROCEDURE {P}m AS SELECT id AS x FROM dbo.{P}t ORDER BY x"),
        new("bind-missing-table-is-recorded-by-name", DependenciesKind, "D6", ScopeObservableKind.Binding, false, string.Empty,
            "CREATE PROCEDURE {P}m AS SELECT id FROM dbo.{P}missing"),
        new("columns-alias-swaps-names", OutputColumnsKind, "A2", ScopeObservableKind.OutputColumns, false, TwoTables, "SELECT b AS a, a AS x FROM {P}t"),
        new("columns-derived-table-column", OutputColumnsKind, "A3", ScopeObservableKind.OutputColumns, false, TwoTables, "SELECT d.v FROM (SELECT c AS v FROM {P}u) AS d CROSS JOIN {P}t"),
        new("columns-cte-named-like-table-hides-it", OutputColumnsKind, "B9", ScopeObservableKind.OutputColumns, false, TwoTables, "WITH {P}t AS (SELECT c AS id FROM {P}u) SELECT id FROM {P}t"),
        new("columns-unqualified-picks-the-only-owner", OutputColumnsKind, "B2", ScopeObservableKind.OutputColumns, false, TwoTables, "SELECT a, c FROM {P}t CROSS JOIN {P}u"),
        new("columns-qualified-star-then-column", OutputColumnsKind, "B6", ScopeObservableKind.OutputColumns, false, TwoTables, "SELECT {P}t.*, {P}u.c FROM {P}t CROSS JOIN {P}u"),
        new("columns-derived-table-column-list", OutputColumnsKind, "A3", ScopeObservableKind.OutputColumns, false, TwoTables, "SELECT d.k FROM (SELECT a FROM {P}t) AS d (k)"),
        new("columns-alias-named-like-another-table", OutputColumnsKind, "B1", ScopeObservableKind.OutputColumns, false, TwoTables, "SELECT {P}u.a FROM {P}t AS {P}u"),
        new("columns-two-aliases-same-column-name", OutputColumnsKind, "B2", ScopeObservableKind.OutputColumns, false, TwoTables,
            "SELECT t1.id, u1.id AS uid FROM {P}t AS t1 JOIN {P}u AS u1 ON t1.id = u1.id"),
        new("columns-cross-apply-derived", OutputColumnsKind, "A3", ScopeObservableKind.OutputColumns, false, TwoTables,
            "SELECT o.c FROM {P}t AS t1 CROSS APPLY (SELECT TOP 1 c FROM {P}u WHERE {P}u.id = t1.id ORDER BY c) AS o"),
        new("columns-through-synonym", OutputColumnsKind, "D5", ScopeObservableKind.OutputColumns, false, TwoTables + " CREATE SYNONYM {P}syn FOR dbo.{P}t;", "SELECT a FROM {P}syn"),
        new("columns-through-view", OutputColumnsKind, "B7", ScopeObservableKind.OutputColumns, false, TwoTables + "\nGO\nCREATE VIEW {P}vw AS SELECT a AS va FROM {P}t;\nGO\n", "SELECT va FROM {P}vw"),
        new("columns-union-takes-first-branch-name", OutputColumnsKind, "B7", ScopeObservableKind.OutputColumns, false, TwoTables, "SELECT a AS z FROM {P}t UNION ALL SELECT c FROM {P}u"),
        new("columns-order-by-expression-does-not-change-sources", OutputColumnsKind, "A2", ScopeObservableKind.OutputColumns, false, TwoTables, "SELECT b AS a FROM {P}t ORDER BY a"),
        new("columns-nested-ctes-chain", OutputColumnsKind, "B7", ScopeObservableKind.OutputColumns, false, TwoTables, "WITH c1 AS (SELECT a AS x FROM {P}t), c2 AS (SELECT x AS y FROM c1) SELECT y FROM c2"),
        new("columns-sibling-subqueries-reuse-alias", OutputColumnsKind, "B7", ScopeObservableKind.OutputColumns, false, TwoTables, "SELECT d.a FROM (SELECT a FROM {P}t) AS d WHERE EXISTS (SELECT 1 FROM (SELECT c FROM {P}u) AS d WHERE d.c = 1)"),
        new("columns-inner-alias-shadows-outer-alias", OutputColumnsKind, "B7", ScopeObservableKind.OutputColumns, false, TwoTables, "SELECT t1.a FROM {P}t AS t1 WHERE EXISTS (SELECT 1 FROM {P}u AS t1 WHERE t1.c = 1)"),
        new("columns-two-derived-tables-same-column-name", OutputColumnsKind, "B7", ScopeObservableKind.OutputColumns, false, TwoTables, "SELECT x.v, y.v AS v2 FROM (SELECT a AS v FROM {P}t) AS x CROSS JOIN (SELECT c AS v FROM {P}u) AS y"),
        new("columns-values-constructor", OutputColumnsKind, "B7", ScopeObservableKind.OutputColumns, false, TwoTables, "SELECT v.k FROM (VALUES (1)) AS v (k)"),
        new("columns-cte-named-like-other-table-hides-it", OutputColumnsKind, "B7", ScopeObservableKind.OutputColumns, false, TwoTables, "WITH {P}u AS (SELECT a AS c FROM {P}t) SELECT c FROM {P}u"),
        new("columns-alias-collides-with-other-tables-column", OutputColumnsKind, "B7", ScopeObservableKind.OutputColumns, false, TwoTables, "SELECT {P}t.a AS c, {P}u.c AS a FROM {P}t CROSS JOIN {P}u"),
        new("columns-star-inside-derived-table", OutputColumnsKind, "B7", ScopeObservableKind.OutputColumns, false, TwoTables, "SELECT d.a FROM (SELECT * FROM {P}t) AS d"),
        new("columns-aliased-star", OutputColumnsKind, "B7", ScopeObservableKind.OutputColumns, false, TwoTables, "SELECT x.* FROM {P}t AS x"),
        new("columns-expression-and-plain-column", OutputColumnsKind, "B7", ScopeObservableKind.OutputColumns, false, TwoTables, "SELECT ISNULL(a, 0) AS z, a FROM {P}t"),
        new("columns-cast-column", OutputColumnsKind, "B7", ScopeObservableKind.OutputColumns, false, TwoTables, "SELECT CAST(a AS BIGINT) AS z, a FROM {P}t"),
        new("columns-pivot-output", OutputColumnsKind, "B7", ScopeObservableKind.OutputColumns, false, TwoTables, "SELECT p.[1] FROM (SELECT id, a FROM {P}t) AS s PIVOT (MAX(a) FOR id IN ([1])) AS p"),
        new("columns-window-function-beside-column", OutputColumnsKind, "B7", ScopeObservableKind.OutputColumns, false, TwoTables, "SELECT a, ROW_NUMBER() OVER (ORDER BY a) AS rn FROM {P}t"),
        new("columns-nested-derived-tables", OutputColumnsKind, "B7", ScopeObservableKind.OutputColumns, false, TwoTables, "SELECT e.q FROM (SELECT d.v AS q FROM (SELECT a AS v FROM {P}t) AS d) AS e"),
        new("columns-cte-column-list-reorders", OutputColumnsKind, "B7", ScopeObservableKind.OutputColumns, false, TwoTables, "WITH c (p, q) AS (SELECT a, b FROM {P}t) SELECT q, p FROM c"),
        new("columns-view-over-view-chain", OutputColumnsKind, "B7", ScopeObservableKind.OutputColumns, false, TwoTables + "\nGO\nCREATE VIEW {P}vw1 AS SELECT a AS va FROM {P}t;\nGO\nCREATE VIEW {P}vw2 AS SELECT va AS vb FROM {P}vw1;\nGO\n", "SELECT vb FROM {P}vw2"),
        new("columns-left-join-null-side-column", OutputColumnsKind, "B7", ScopeObservableKind.OutputColumns, false, TwoTables, "SELECT {P}u.c FROM {P}t LEFT JOIN {P}u ON {P}t.id = {P}u.id"),
        new("columns-qualified-by-schema-and-table", OutputColumnsKind, "B7", ScopeObservableKind.OutputColumns, false, TwoTables, "SELECT dbo.{P}t.a FROM dbo.{P}t"),
        new("columns-same-cte-referenced-twice", OutputColumnsKind, "B7", ScopeObservableKind.OutputColumns, false, TwoTables, "WITH c AS (SELECT a AS x FROM {P}t) SELECT c1.x, c2.x AS x2 FROM c AS c1 CROSS JOIN c AS c2"),
        new("columns-top-with-order-by-alias", OutputColumnsKind, "B7", ScopeObservableKind.OutputColumns, false, TwoTables, "SELECT TOP 1 b AS a FROM {P}t ORDER BY a"),
    ];
}
