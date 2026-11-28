# SilentScan code review — findings (non-test code)

Severity: H = likely wrong finding / false positive / crash; M = missed case, fragile logic, maintenance risk; L = nit/cleanup.

## Progress
- [x] Core/TypeInference
- [x] Core/Common
- [x] Core/Parsing
- [x] Core/Rules
- [x] Core/Diagnostics (skim only — progress UI)
- [x] Core/Lineage
- [x] Core/Catalog
- [~] Core/Predicates — core analyzers + ~20 scanners reviewed. Not reviewed in depth: DynamicSqlScannerV2/Transfer/Cfg internals, DuplicationScanner, DeadCode, ScalarUdfInlineability blocker list (needs verification), the small single-construct scanners (Regexp*, Json*, StringAgg/ForXml order, Unistr, WaitFor, Naming, Formatting, CodeMetric).
- [x] Core/Reporting — pipeline, SARIF writer, rule-id wiring; readable writer and RuleDocs only diffed for coverage, not read line by line.
- [x] Live — runner, row-value fetcher; parity checker / plan-cache reader / Sweep skimmed only.
- [x] Verify — Catalog (module reader, read-only guard). Oracle/ and Deployment/ not reviewed (internal tooling).
- [x] Cli / Analyzer. Bench not reviewed (internal tooling).

## Fix-first list (H, ordered by blast radius)
1. F45 — proc-level WITH RECOMPILE treated as OPTION (RECOMPILE) in 3 rules; published claim + fix advice false.
2. F42 — filtered-index-parameter-mismatch: "OPTION (RECOMPILE) doesn't help" is false.
3. F11 — SET-option flow drops branch SETs → FPs in StringConcatNull and `= NULL` rules.
4. F20 — variables carry no collation → false Unicode-loss on every `DECLARE @v varchar = N'…'`.
5. F25 — unqualified column falls through to outer scope (repro pending) — feeds many rules.
6. F1 — CS_AS distinct literals "contradiction" false (trailing spaces, width).
7. F51 — leading `[…]` LIKE reported as non-sargable; oracle shows Index Seek.
8. F48 — StringConcatNull ignores null-rejecting filters (a test pins the FP).
9. F64 — concatenated-value dynamic SQL fires on single-constant splices (a test pins the FP).
10. F46 — NOT IN nullable ignores ANSI_NULLS OFF.
11. F49 — unbounded write fires on join-bounded UPDATE/DELETE.
12. F32, F33 — catalog nullability after ALTER COLUMN; table-variable collation (scan-db too).
13. F19 — 'Oct'/'August' treated as time literals → false temporal precision loss.
14. F2, F3 — REPLACE/STUFF typing; CASE with unknown length.
15. F52, F50, F35 (now M, see F58).
Scope removals to decide: F54 (key-length Msg 1946), F63 (Msg 266), F50 (PK-violation framing).

## Findings
Numbering is review order, not file order. F49–F65 sit in the Predicates section and also cover Reporting (F57), Live (F56, F59), Verify (F62) and Cli (F60, F61).

### TypeInference

**F1 [H, oracle-confirmed] `Collation.GuaranteesDistinctLiteralsAreUnequal` is too strong → dead-predicate false positive**
`src/SilentScan.Core/TypeInference/Collation.cs:204`, consumed by `Predicates/Normalization/PredicateSurvivalAnalyzer.cs:467` (two distinct `=` literals on one CS_AS column ⇒ "contradiction").
- Trailing-space padding: under `Latin1_General_CS_AS`, `c = 'a' AND c = 'a '` returns the row (oracle: 1 row).
- Width/kana insensitivity: CS_AS without `_WS`/`_KS` — `c = N'A' AND c = N'Ａ'` returns the row (oracle: 1 row).
- Only `_BIN`/`_BIN2` really guarantees it, and even then trailing spaces still pad. Fix: require BIN/BIN2 (or CS_AS_KS_WS) AND compare literals after `TrimEnd(' ')`; or drop the 2-required-literals branch.
- Related: LIKE-without-wildcard leaves (line 591) are merged into the same required/excluded sets as `=`/`<>`, but LIKE does not pad trailing spaces, so `required.Overlaps(excluded)` mixing LIKE and `=` on a `char(n)` column can disagree with the engine. Needs a probe.

**F2 [M→H, oracle-confirmed] REPLACE/STUFF/TRANSLATE take arg-0's category only**
`BuiltinFunctionTypeResolver.cs:486-508`. `REPLACE(varchar(5), 'a', N'b')` → engine `nvarchar(4000)`; `STUFF(varchar(5),1,1,N'b')` → `nvarchar(5)`. SilentScan says varchar. Any Unicode/implicit-conversion rule consuming the inferred type sees the wrong family. Fix: if any string arg is Unicode, promote.

**F3 [M→H, oracle-confirmed] Combine with one unknown length picks the known one**
`ExpressionTypeInferencer.cs:343,374,385` (`left.Length ?? right.Length`) and `ConcatenateSameCategory:244-246` (drops to `Length:null` but `LengthKnown` stays `true`). `CASE WHEN .. THEN @v5 ELSE SPACE(@n) END` → engine `varchar(8000)`, SilentScan `varchar(5)`. Understated result length feeds truncation-style rules → FP risk. Fix: if either side's length is unknown, the result is unknown (`LengthKnown=false`). Also `SPACE` → engine `varchar(8000)`, and non-max `REPLACE` → `varchar(8000)`/`nvarchar(4000)` — both could be fixed lengths instead of unknown.

**F4 [M, oracle-confirmed] datetime2 fractional precision stored in two different fields**
`SqlTypeReferenceResolver.ResolveFractionalSeconds` puts it in `Scale`; `BuiltinFunctionTypeResolver` `SYSDATETIME`/`SYSUTCDATETIME`/`SYSDATETIMEOFFSET` put 7 in `Precision`. `MergeTemporalScale`/`ImpliedFractionalScale` read `Scale` → built-ins are invisible to temporal-scale logic (silent recall loss; any `Scale`-reading rule gets null).

**F5 [M, oracle-confirmed] Unparameterised / partially parameterised decimal not defaulted**
`SqlTypeReferenceResolver.ResolveDecimal`: `decimal` → engine `decimal(18,0)`, `decimal(10)` → `decimal(10,0)`; SilentScan leaves Precision/Scale null, so `ExactNumericPrecisionScale` returns null and all decimal arithmetic/union math is skipped (recall loss on the classic CAST(x AS decimal) rounding case unless a separate rule covers it).

**F6 [M, oracle-confirmed] `float(1..24)` is `real`, kept as Float**
`SqlTypeReferenceResolver.ResolveFloat`: `CAST(1 AS float(24))` → engine `real`. Category stays `Float` with Precision 24; any category-based float/real precision-loss logic (TypePairMatrix) treats it as 8-byte float. Check how many consumers look at `Precision` for Float.

**F7 [L, oracle-confirmed] Unary minus on tinyint is smallint** — `ExpressionTypeInferencer.cs:24` passes the operand type through; `-CAST(1 AS tinyint)` → engine `smallint`.

**F8 [M] Schema-qualified UDF calls resolved as built-ins**
`ExpressionTypeInferencer.cs:46` matches `FunctionCall` by `FunctionName` only, ignoring `CallTarget`. `dbo.Trim(x)` / `dbo.Replace(...)` / `dbo.Left(...)` (common legacy UDF names) are typed as the built-in. Fix: skip built-in lookup when `CallTarget` is non-null (except `sys.`?).

**F9 [L] `IsMax` set while `Length` kept** — `CombineDifferentCategory:327-329` (`winner with { IsMax = true }` keeps winner.Length). Anything reading `Length` before `IsMax` sees a bogus length.

**F10 [L] Unicode string literal >4000 / non-Unicode >8000 chars** — `LiteralTypeResolver` gives `nvarchar(5000)`; engine types it as `nvarchar(max)`. Needs a probe before relying on it.

### Common

**F11 [M] SET-option flow resolvers drop SETs made inside IF/WHILE/TRY branches**
`Common/SetOptionFlowPolicyBase.cs:394-398`, `NumericRoundAbortFlowResolver.cs:313-317`, `AnsiNullDfltFlowResolver.cs:241-245`: `Merge` returns the *pre-branch* value (`RestoreIsOff`). So after `IF @x=1 SET CONCAT_NULL_YIELDS_NULL OFF ELSE SET CONCAT_NULL_YIELDS_NULL OFF;` the state is "ON", although the option is OFF on every path (SET persists past the IF in a batch). `StringConcatNullScanner` fires when the state is ON → FP on code after such an IF; same shape for NUMERIC_ROUNDABORT consumers. Same merge in `DeprecatedSyntaxScanner.AnsiNullsFlowPolicy.Merge` (`DeprecatedSyntaxScanner.cs:186-190`) → `= NULL` / `<> NULL` reported as "never matches" after an IF whose branches all `SET ANSI_NULLS OFF`. Precision-safe fix: track a tri-state (On/Off/Unknown); when branches disagree → Unknown → consumers suppress; when they agree → that value.

**F12 [L] ANSI_NULL_DFLT_ON OFF modelled as "NOT NULL"** — `AnsiNullDfltFlowResolver.cs:219-227`. Engine: with both DFLT_ON and DFLT_OFF off, the database's `ANSI_NULL_DEFAULT` option decides. Code hard-codes NOT NULL (correct only for the default-OFF database setting). `SET ANSI_NULL_DFLT_OFF OFF` after `..._OFF ON` keeps `false` rather than reverting. Low impact; could read the db option on scan-db.

**F13 [M] Unsized `varchar`/`nvarchar`/`char`/`binary` in DECLARE/params/columns resolves to unknown length, engine says 1**
`Common/VariableTypeTracker.cs:155` and every `SqlTypeReferenceResolver.Resolve` call without `unsizedStringOrBinaryDefaultLength`. `DECLARE @v varchar; SET @v='abc'` silently keeps 'a'. Recall loss for truncation rules (unless a dedicated rule covers it — check Predicates).

**F14 [L] `FragmentTextRenderer` uses `Sql160ScriptGenerator` while parsing is TSql180** — SQL 2025-only syntax (VECTOR, JSON type/aggregates, REGEXP_*) may render wrongly in finding text. Also allocates a generator per call (35 call sites).

**F15 [L] `SchemaObjectNameHelper.Qualify` drops the server part** — `Srv.Db.dbo.T` qualifies as `Db.dbo.T`; if Db is the scanned database it may resolve to the local table. Check `DatabaseCatalog.Find` behaviour for 3-part names.

### Parsing

**F16 [M] Non-UTF-8 files decoded as Latin-1, not Windows-1252**
`Parsing/SqlScriptParser.cs:110-111`. SSMS-saved ANSI files are cp1252 (or the OS code page); bytes 0x80–0x9F (’ “ ” – € …) become C1 control chars U+0080–U+009F. Literal-content rules (code-page write-loss classifier, UNISTR, LIKE/regex) then reason about the wrong characters — e.g. N'’' becomes U+0092, which cp1252 cannot store → possible false "silently replaced with ?" claim. Fix: `CodePagesEncodingProvider` + `Encoding.GetEncoding(1252)` (or configurable). File-mode only; scan-db reads from sys.sql_modules so unaffected.

**F17 [L] QUOTED_IDENTIFIER OFF retry picks the parse with fewer errors** — `SqlScriptParser.cs:21-25`. If the OFF parse wins, every `"x"` in the file becomes a string literal, changing semantics of all batches, not just the failing one. Safer: per-batch, or only accept the OFF parse when it has zero errors.

**F18 [L] Quadratic work in `FindUnanalyzedBatches`** — `survivingStarts.Any(...)` per batch and `CountLines` from offset 0 per batch. Only matters for very large single files.

### Rules

**F19 [H] `IsDateOnlyLiteral` treats any 't'/'T' as a time separator**
`Rules/WriteLossClassifier.cs:291` uses `Contains('T', OrdinalIgnoreCase)`. Month names `Oct`, `August`, `Sept`/`September` contain 't', so `'1 Oct 2024'` / `'August 5, 2024'` written to a `date` target is reported as `TemporalPrecisionLoss` (line 85) though there is no time part. Same helper also gates `IsMidnightConversionSource`. Fix: detect ISO `T` only as digit-`T`-digit, and `:` for time.

**F20 [H, code path + oracle confirmed] Unknown target collation ⇒ non-ASCII literal is reported unrepresentable**
Confirmed path: `TypedPredicateExtractor.OnEnterDeclareVariableStatement` resolves the variable type with `columnCollation: null` → `EmitWriteLossFinding` → `Classify` → `UnicodeToNonUnicodeReplacement`. No downstream filter. Oracle (db `SQL_Latin1_General_CP1_CI_AS`): `DECLARE @v varchar(10) = N'café'` keeps `café`. Same for SET, FETCH INTO, proc/TVF/sp_executesql parameters. Also the message text "replaced with '?'" (`SarifReportWriter.cs:1534`, `ReadableScanReportWriter.cs:415`) is inaccurate: the engine best-fits many characters (`N'āŁ'` → `aL`; only unmappable ones like `✓` become `?`).
Original note:
`WriteLossClassifier.cs:165-173`: `TryGetCodePage(null)` → `false` → `UnicodeToNonUnicodeReplacement`. Proc/function/sp_executesql formal parameter types and DECLAREd variables are resolved with `columnCollation: null` (they actually use the database default collation). `N'café'` → `varchar` parameter in a Latin1 database stores `é` fine. Precision fix: fall back to `catalog.DefaultCollation` for variables/parameters; when still unknown, return "not a risk" rather than "risk".

**F21 [L] Exact→approximate precision loss not modelled** — `NumericFamilyNarrowing.cs:159-162` returns null when families differ unless target is exact. `bigint`/`decimal(38,x)` → `float` (>2^53) and `int` → `real` (>2^24) silently lose digits. Recall only.

**F22 [L] smalldatetime target not modelled** — `IsDateTimeGridRoundingRisk` covers `datetime` only; `datetime`/`datetime2` → `smalldatetime` rounds to the minute. Recall only.

**F23 [L] `TemporalBoundaryClassifier.CountFractionalDigits` uses `LastIndexOf('.')`** — dotted date formats (`'31.01.2024 23:59:59'`) count the year as fractional digits. Mostly harmless, but wrong when column scale is 5-7.

### Common (cont.)

**F24 [note] `StringConcatNullScanner` is the consumer that makes F11 a false-positive (it fires when the resolver reports ON).**

### Lineage

**F25 [H, repro pending] Unqualified column falls through to an outer scope when the inner scope has an unresolved relation**
`Lineage/ScalarExpressionResolver.cs:234-252`. The scope-chain loop moves to the next (outer) scope whenever no inner relation *we resolved* has the column. If an inner relation is unresolved (`ResolvedRelation(name, [])` for a table with no DDL, `ResolvedRelation.Empty` for OPENJSON/OPENROWSET/VALUES/STRING_SPLIT/linked-server/unsupported TVF/recursive-CTE fallback), the column most likely belongs to *that* relation, but it gets bound to a same-named outer column. Example: `... FROM A WHERE A.k IN (SELECT code FROM OPENJSON(@j) WITH (code int))` → `code` bound to `A.code`. Wrong provenance/type feeds implicit-conversion, nullability and dead-predicate rules. Same in `TryResolveImmediateRelation:288-307`. Fix: if any relation in the current scope is unresolved/partial, stop and return Unknown instead of searching outer scopes.

**F26 [M] Unsized CAST target length differs between the two type paths**
`ScalarExpressionResolver.ResolveCastOrConvert:143` calls `SqlTypeReferenceResolver.Resolve` *without* `unsizedStringOrBinaryDefaultLength: 30`; `ExpressionTypeInferencer` passes 30. `CAST(x AS varchar)` is `varchar(30)` in one path and length-unknown in the other. Unify (ideally through one helper).

**F27 [M] `WidenDecimalFacets` (UNION type) clamps precision without reducing scale**
`Lineage/ColumnProvenanceAnalysis.cs:206-218`. `decimal(38,10) UNION decimal(38,30)` → engine reduces scale to keep 28 integral digits; code yields `decimal(38,30)` (8 integral digits). `ExpressionTypeInferencer.MergeExactNumericPrecisionScale` does it right — two implementations of the same rule have diverged. Reuse one.

**F28 [L, dead code] `RecursiveCteTypeMismatch` / `CompareRecursiveBranchTypes` never consumed**
`Lineage/CteResolver.cs:8-9, 96-175`: nobody passes `typeMismatches`. It would be out of scope anyway (Msg 240 is a hard error). Delete.

**F29 [L] View/CTE/derived-table column-count mismatch truncates the column list**
`LineageResolver.cs:389-391`, `CteResolver.cs:47-49`, `FromScopeResolver.cs:294-296`: when the query resolves *fewer* columns than the explicit list (e.g. `SELECT *` from an unknown table), only the first N names survive; the rest disappear, so later references are "not found" and can then fall through to outer scopes (F25). Emit one Unknown column per declared name.

**F30 [L, recall] `FROM (VALUES ...) v(a,b)`, `OPENJSON ... WITH`, `STRING_SPLIT`, `GENERATE_SERIES` resolve to zero columns** — `FromScopeResolver.ResolveUnsupportedTableReference`. Common constructs whose column types are statically known.

**F31 [L, recall] `UPDATE T SET ... FROM X WHERE T.id = X.id` (target not repeated in FROM)** — `FromScopeResolver.ResolveForDataModification:53-56` uses only the FROM clause, so `T.*` references are unresolved.

SELECT INTO nullability (`SelectIntoNullability`) oracle-checked against 12 expression shapes — all match.

### Catalog

**F32 [H, oracle-confirmed] `ALTER TABLE … ALTER COLUMN c <type>` without NULL/NOT NULL makes the column nullable; catalog keeps the old nullability**
`Catalog/CatalogBuilder.cs:1121-1131` (`c with { Type = newType, ... }` keeps `IsNullable`). Oracle: `a int NOT NULL` → `ALTER COLUMN a bigint` → `is_nullable = 1`. The `AlterTableAlterColumnOption` enum carries `Null`/`NotNull`; when a DataType is given and neither is specified, set `IsNullable = true` (subject to ANSI_NULL_DFLT); `NotNull` → false. Downstream: every NOT-NULL-based suppression or claim (dead `IS NULL` predicate, redundant ISNULL, NOT IN null-safety, SELECT INTO nullability) treats a nullable column as NOT NULL → FP risk. File mode only (scan-db reads sys.columns).

**F33 [H, oracle-confirmed] Table variables take the *database* collation, not tempdb's**
`CatalogBuilder.VisitDeclareTableVariable:1270` builds columns with `catalog.EffectiveTempdbCollation`. Oracle (db `Latin1_General_CS_AS`, server/tempdb `SQL_Latin1_General_CP1_CI_AS`): `DECLARE @t TABLE (c varchar(10))` → `Latin1_General_CS_AS`; `#x` → tempdb collation. With differing collations, collation-mismatch/seek/case-sensitivity rules mis-classify every table-variable string column. Use `catalog.DefaultCollation`.

**F34 [M] ALTER TABLE ADD / ALTER COLUMN on a #temp table uses the database collation**
`CatalogBuilder.cs:1068` (ADD) and `:1121-1126` (ALTER COLUMN) use `catalog.DefaultCollation` even when the target is a temp table (which follows tempdb). Mirror image of F33.

**F35 [H] Inline `INDEX ix CLUSTERED (col)` in CREATE TABLE is recorded as non-clustered**
`CatalogBuilder.BuildInlineIndex:1530` sets `IsClustered` only for `ClusteredColumnStore`; `IndexTypeKind.Clustered` (rowstore) is ignored. `CREATE TABLE T (a int, b int, INDEX cx CLUSTERED (a), INDEX ix NONCLUSTERED (b))` → `IndexDesignScanner.ScanHeapFindings` (`IndexDesignScanner.cs:72`) reports "heap with nonclustered indexes" → FP. Also affects `IndexCoverageScanner.cs:67`, `ScalarUdfScanner.cs:140`. Fix: `IsClustered: kind is Clustered or ClusteredColumnStore`.

**F36 [L] PK assumed clustered even when another constraint/index is declared CLUSTERED** — `CatalogBuilder.cs:1320, 1501` (`Clustered ?? IsPrimaryKey`). `PRIMARY KEY` + `UNIQUE CLUSTERED` on the same table → engine makes the PK nonclustered; catalog shows two clustered indexes.

**F37 [M, recall] Cross-file DROP TABLE removes a table that a later file re-creates**
`CatalogBuilder.Build:50-64`: unresolved drops are re-applied after *all* CREATEs, so `DROP TABLE T` in an earlier-sorted file removes the `T` created in a later file. Recall loss (all of T's columns become unknown), not FP.

**F38 [L] sp_rename target parsing ignores brackets/quotes** — `SplitTableTarget`/`SplitLastSegment` split on `.`; `'[dbo].[Old]'` or `'dbo.[a.b]'` never match. Also column/index name comparisons use `OrdinalIgnoreCase` instead of `catalog.IdentifierComparer` (CS databases) — same in `VisitAlterColumn`, `VisitDropTableElements`, `ApplyPrimaryKeyNotNull`, `VisitAlterIndex`, `VisitDropIndex`.

**F39 [L] `DatabaseCatalog.SyncIdentifierComparer` skips two dictionaries** — `_viewDefinitionTextByQualifiedName` and `_partitionFunctionSignatureBySchemeName` keep `OrdinalIgnoreCase` after the comparer switches (CS databases). `_partitionFilegroupsBySchemeAndNumber` uses `ToUpperInvariant`.

**F40 [L] `DatabaseCatalog.Remove(name, scope)` also removes the unscoped entry** — a `DROP TABLE #t` inside a procedure deletes a same-named script-level `#t`.

**F41 [M] Dynamic-SQL-created #temp tables are registered in the enclosing procedure's scope and override a same-named static #temp**
`Catalog/DynamicSqlTempTableDiscovery.cs` wraps `EXEC('CREATE TABLE #t ...')` text as the enclosing procedure; `LiveScanRunner.cs:70-72` merges it via `MergeFileModeExtras`, which overwrites `_tablesByQualifiedName[key]` for temp tables. A #temp created inside dynamic SQL is dropped when that EXEC ends, so the caller's later static statements never see it; if the procedure also creates a static `#t` with a different shape, the dynamic shape wins in the catalog.

**Scope note (scan-db):** `LiveScanRunner` uses the live catalog for permanent tables and only merges temp tables / table variables / table types / function and proc signatures from module bodies. So F32, F35, F36 affect file mode (and #temp in module bodies); **F33 affects scan-db directly** (every table variable in every module).

Determinism checker oracle-checked (PARSENAME, COMPRESS, DATENAME, ISDATE, SQL_VARIANT_PROPERTY, COLLATIONPROPERTY, TYPE_NAME, FORMATMESSAGE non-det; RAND(seed), DECOMPRESS, ISNUMERIC, CONVERT style 120, DATEDIFF(week) det) — all agree.

Engine claims still to verify (deferred to the rule-claim pass): `ScalarUdfInlineabilityScanner` blocker list (ORDER BY without TOP, multiple RETURN, CTE, STRING_AGG, sys.* access, @@ list).

### Predicates

**F42 [H, oracle-confirmed — rule claim false] `filtered-index-parameter-mismatch` says OPTION (RECOMPILE) doesn't help; it does**
Rationale in `Reporting/RuleCatalog.cs:81` ("Not suppressed by OPTION (RECOMPILE) - a compile-time limitation"); code `TypedPredicateExtractor.TryAddFilteredIndexParameterMismatchFinding` ignores `HasActiveRecompileGuard`. Oracle (SQL 2025): filtered index `WHERE st = 'A'`; `DECLARE @s char(1)='A'; SELECT … WITH (INDEX(fx)) WHERE st = @s OPTION (RECOMPILE)` compiles and runs (and the unhinted plan picks `[fx]`); the same query without RECOMPILE → Msg 8622. Parameter embedding under statement-level `OPTION (RECOMPILE)` turns the variable into a literal. Fix: suppress when `_statementHasOptionRecompile` (not proc-level WITH RECOMPILE, which does not embed), fix the rationale, and add the RECOMPILE sibling to the oracle test. Also fires for any operator (`>`, `LIKE`), not only `=` — check that's intended.

**F45 [H, oracle-confirmed — published claim + fix advice false] Procedure-level `WITH RECOMPILE` treated as equivalent to `OPTION (RECOMPILE)` in three rules**
Rules: local-variable-predicate (`RuleCatalog.cs:80`), catch-all predicate, parameter-reassignment. Each says WITH RECOMPILE "lets the optimizer see the real value", suppresses on it (`TypedPredicateExtractor.cs:929` `HasActiveRecompileGuard`, `CatchAllPredicateScanner.cs:53`, `ParameterReassignmentPredicateScanner.cs:85`) and recommends it as a fix. Oracle (SQL 2025, skewed 20k-row table, actual value matches 1 row):
- local variable in `WITH RECOMPILE` proc → Index Seek EstimateRows **1818.18** (density); with `OPTION (RECOMPILE)` → **1**.
- catch-all `(@p IS NULL OR k = @p)` in `WITH RECOMPILE` proc → **Index Scan** (compiled value sniffed, still no seek).
- parameter reassigned before use in `WITH RECOMPILE` proc → ParameterCompiledValue `(1)`, EstimateRows **19990**.
WITH RECOMPILE compiles the whole procedure up front with the *passed* parameter values and no parameter embedding. Impact: false negatives plus wrong published fix advice. Fix: guard on statement-level OPTION (RECOMPILE) only; rewrite the three rationales/fix texts; add WITH RECOMPILE sibling cases to their oracle tests.

**F46 [H, oracle-confirmed] NOT IN nullable-subquery rule ignores ANSI_NULLS OFF**
`Predicates/NotInNullableSubqueryScanner.cs` never consults the module's `uses_ansi_nulls` (`DatabaseCatalog.TryGetModuleUsesAnsiNulls` exists) or an in-script `SET ANSI_NULLS OFF`. Oracle: `SET ANSI_NULLS OFF; … WHERE x NOT IN (SELECT y … containing NULL)` returns 1 row; with ON returns 0. Modules created under ANSI_NULLS OFF are common in legacy databases, so in scan-db this is a real FP source. Fix: suppress when the enclosing module (or flow state) has ANSI_NULLS OFF. Also exposed to F25 (unqualified inner column binds to an outer column when the subquery's FROM is unresolved).

**F47 [L] Same-column numeric contradiction ignores the column's own type** — `PredicateSurvivalAnalyzer.DetectGroupContradiction`: `realCol = 0.1 AND realCol = 0.1000000001` → two distinct decimal points → "contradiction", but both literals convert to the same `real`. Only float/real (and datetime-from-number) columns are affected; skip numeric-range folding when the column is approximate.

**F48 [H] String-concat-NULL rule ignores WHERE / JOIN / CTE filters that reject NULL — and a test locks the FP in**
`Predicates/StringConcatNullScanner.cs` takes nullability from `CatalogColumn.IsNullable` only; "proven non-null" comes only from CASE WHEN implications. `SELECT a + b FROM t WHERE b IS NOT NULL`, `… INNER JOIN x ON x.k = t.b`, or a CTE/derived table with `WHERE b IS NOT NULL` still fire, although the concatenation can never meet a NULL. `tests/SilentScan.Tests/Predicates/StringConcatNullScannerTests.cs:114-124` (`CteSharesNameWithRealTable_ResolvesThroughCteToRealColumn_Fires`) asserts exactly this case fires. Fix: treat a top-level AND-conjunct null-rejecting predicate on the column (in the same query or the defining CTE/derived/view) as proof of non-null — `NotInNullableSubqueryScanner.HasDefensiveNotNullFilter` already has the shape. Conversely, LEFT-JOIN nullable side (`IsNullableSide`) is ignored, so NOT NULL columns on the outer side are missed (recall).

**F49 [H] "Unbounded table write" fires on join-bounded UPDATE/DELETE**
`QueryAntiPatternScanner.InspectUnboundedWrite` checks only WHERE and TOP. `UPDATE t SET … FROM t INNER JOIN x ON x.id = t.id`, `DELETE t FROM t JOIN inserted i ON …` (every trigger) and `MERGE`-free semi-join deletes are bounded by the join, yet the message says "a whole-table write with no row-limiting mechanism at all". Also fires for #temp / table-variable targets. Fix: require no FROM clause with an inner/semi join (or treat any FROM with a join predicate as bounded), and skip temp targets.

**F50 [M→H] MERGE-missing-HOLDLOCK: out-of-scope claim and two FP shapes**
`QueryAntiPatternScanner.InspectMergeMissingHoldlock`:
- The stated consequence is "race a primary-key violation" — a hard error, out of scope per CLAUDE.md. The in-scope consequence is silent duplicate inserts when no unique constraint exists; the rationale should say that or the rule should go.
- Fires when the MERGE has no `WHEN NOT MATCHED … INSERT` action (UPDATE/DELETE-only MERGE has no insert race).
- Fires for `#temp` targets (session-private, no concurrent writer). Also ignores session `SET TRANSACTION ISOLATION LEVEL SERIALIZABLE`.

**F51 [H, oracle-confirmed] Leading-wildcard LIKE rule treats a leading `[…]` character class as a wildcard**
`NonSargablePredicateScanner.IsLikeWildcardChar` (`:260`) includes `'['`. Oracle (SQL 2025, indexed varchar): `WHERE c LIKE '[AB]%'` and `WHERE c LIKE '[A]1%'` both produce **Index Seek**. Only leading `%` and `_` force a scan. Fix: drop `'['` from the leading-char check (or require `[^…]`, which needs its own probe).

**F52 [M→H] Duplicate/subsumed index rules ignore clustered-vs-nonclustered**
`IndexDesignScanner.CompareIndexPairForDuplicateOrSubsumed`: both kinds are `CatalogIndexKind.Index`, so (a) a non-unique *clustered* index on `(A)` plus a nonclustered `(A, B)` reports the **clustered** index as "redundant … safe" (it holds the table — it can't be dropped without turning the table into a heap); (b) a nonclustered index on exactly the clustered key is reported as an "exact duplicate … zero query benefit", but it is far narrower (count/scan/FK checks). Fix: never report a clustered index as the redundant one; don't pair clustered with nonclustered as duplicates.

**F53 [L] Unindexed-FK message overclaims** — `IndexDesignScanner.cs` "every parent-side DELETE/UPDATE forces a referential-integrity scan … and every join along this relationship has no seek path": only DELETE / key-UPDATE on the referenced table check the referencing side, and joins can still seek the referenced side's key.

**F54 [M, scope] `VariableLengthKeyColumnExceedsKeyLimit` is a hard-error rule**
`RuleCatalog.cs:239`: the only consequence is that "the first INSERT/UPDATE that actually stores values long enough … fails" (Msg 1946). Per CLAUDE.md ("If the only oracle-confirmable fact about a rule is 'this throws Msg NNNN,' it's out of scope"), this rule should be removed, like the MERGE-HOLDLOCK PK-violation framing (F50).

**F55 [M] Trigger multi-row-unsafe assignment ignores single-row guards**
`TriggerCorrectnessScanner.InspectMultiRowUnsafeAssignments`: `IF (SELECT COUNT(*) FROM inserted) > 1 BEGIN RAISERROR…; ROLLBACK; RETURN END` (or `IF @@ROWCOUNT > 1 … RETURN`) before `SELECT @v = col FROM inserted` makes the multi-row path unreachable, yet both kinds fire with High confidence ("silently wrong for every multi-row INSERT/UPDATE/MERGE"). Also `UpdateFunctionWithoutValueComparison` fires in INSERT-only triggers, where the "no-op save" story doesn't apply.

**F56 [M] Live row-value fetch truncates silently and is treated as the complete value set**
`LiveTableRowValueFetcher.FetchAsync` runs `SELECT DISTINCT TOP (cap)` and `DynamicSqlTransfer.TryFetchLiveScalar` (`:484`) joins the rows as the variable's full set of possible values. If the column has more than `cap` distinct values, the set is cut short with no signal, so downstream dynamic-SQL verdicts (e.g. "every reconstructed variant is safe/parameterized") are computed over a partial domain. Fix: fetch `cap + 1` and return null when it overflows. Also: NULL rows are dropped (a NULL value makes the concatenated SQL NULL, a real outcome); `GetValue(0).ToString()` is culture-dependent for date/decimal columns; non-equality WHERE conjuncts are dropped (fine as a superset, but combined with TOP it isn't). Scope note: this is a row-data oracle, outside "decidable from code and catalog" — opt-in only (`fetchSqlFromTables`), worth stating in the rule docs.

**F57 [L] Readable vs SARIF writers use different keys for the same four families**
`ReadableScanReportWriter` looks up DatabaseConfiguration / DynamicSql / ExecResultSetsShape / TempTableExecShape findings via `*RuleId` constants, while `SarifReportWriter` and `LiveScanRunner.WithFindings` use the literal `"…Scanner"` strings. Both currently resolve, but the report is keyed by three separately maintained string sets (registry `Id`, SARIF `Find<>` list, readable `Find<>` list). No completeness check exists that a newly registered rule appears in both writers — a missed line silently drops a rule from one output format. Suggest one table mapping finding type → id → writer, or a test that diffs `RuleRegistry.All` + live-only ids against both writers.

**F58 [L, correction to F35]** `IndexDesignScanner` runs only in scan-db (`LiveScanRunner.cs:174`) against the live catalog, so F35's false-heap report applies to file-mode consumers of `IsClustered` only (`IndexCoverageScanner.cs:67,92`, `ScalarUdfScanner.cs:140`). Severity of F35 drops to M.

**F59 [M, oracle-confirmed] scan-db never sees database-scoped DDL triggers, and doesn't say so**
`LiveModuleReader` (all four queries) inner-joins `sys.sql_modules` to `sys.objects`. Oracle: in a fresh database with one `CREATE TRIGGER … ON DATABASE FOR CREATE_TABLE`, `sys.sql_modules` has 1 row, the join has 0. These triggers (they live in `sys.triggers` with `parent_class = 0`) are neither scanned nor listed as unanalyzable, so coverage reporting claims completeness it doesn't have. Fix: `LEFT JOIN sys.triggers t ON t.object_id = m.object_id AND t.parent_class = 0` (no schema) and scan them, or at minimum report them as unanalyzed.

**F60 [M] scan-db SARIF / `--strict` miss coverage gaps that only `LiveScanResult` knows about**
`ScanDbCommand` renders SARIF from `result.Report` only (`SarifReportWriter.Write(result.Report)`), and `HasCoverageGaps` takes only the `ScanReport`. So encrypted, CLR, non-standard-type and numbered-procedure modules (`result.UnanalyzableModules`), lineage-parity mismatches, uncompilable objects and stale cached metadata never appear in SARIF. `invocation.executionSuccessful` can be `true` for a database whose modules are all `WITH ENCRYPTION`, and `--strict` returns 0 for it. The readable writer does show them. Fix: pass the unanalyzable/parity sets into the SARIF notifications and into `HasCoverageGaps`.

**F61 [L] CLI exit codes**
- Exit 1 for any lineage-parity mismatch, even without `--strict`, so a self-check of SilentScan's own lineage fails the user's pipeline. This is the same code used for real errors; worth a distinct code or gating it behind `--strict`.
- There is no exit-on-findings option, so CI gating relies on SARIF consumers.
- Only `SqlException` / `InvalidOperationException` are caught. Any other exception, such as `IOException` from `--output` or a rule-harness bug outside `RuleRunner`, ends in an unhandled stack trace.

**F62 [L] `LiveReadOnlyGuard.AssertSelectOnly` accepts `SELECT … INTO`**
`s is not SelectStatement` lets `SelectStatement { Into: not null }` through, which creates a table. Every current caller passes constant or describe-probe text, so this can't be hit today; it's a gap in the guard's stated contract. Fix: also reject `Into` and `OnFileGroup`.

**F63 [M, scope question] `transaction-hygiene/unresolved-on-some-path` rests on a loud error**
`RuleCatalog.cs:124` grounds the rule in "SQL Server itself raises Msg 266 … the instant such a procedure returns". Msg 266 is raised to the caller every time that path runs, whether the caller is a module or an ad-hoc batch. Under CLAUDE.md's hard-error rule, the in-scope part is only what the error doesn't reveal: the transaction and its locks stay open after a caller that swallows the error (e.g. a client ignoring info/errors, or a TRY/CATCH that logs and moves on). Either rewrite the rationale around that silent consequence and test it with an oracle, or remove the rule. The implicit-transactions sibling (`:125`) is different and stays in scope: at top level no Msg 266 fires, and the open transaction is genuinely silent.

**F64 [M→H] Concatenated-value dynamic SQL rules fire where the plan-cache claim can't hold**
`DynamicSqlPipeline.DetectUnparameterizedConcatenation` (`:615`) fires whenever any **literal** segment after the first (`DynamicSqlSegmentMap.ConcatenationBoundaryOffsets` = every non-placeholder segment but the first) starts in a value position. The rationale (`RuleCatalog.cs:111-112`) says "every distinct concatenated value compiles its own cached plan". Two problems:
- When the spliced slot has exactly one possible value, there is exactly one SQL text and one plan. Examples: `'… WHERE c = ' + '5'` split for line length, or `SET @t = 'X'; EXEC('… = ''' + @t + '''')`. The claim is vacuous there but it still fires, at the script's confidence.
- Unknown values (placeholders) never count, so the rule fires only on the cases where the value set is finite and known. The case where the claim is true (≥2 distinct values reaching the slot) is never checked.

Evidence: `DynamicSqlUnparameterizedTests.ExecStringConcatenatesLiteralValue_FiresBothKinds` asserts both kinds fire for `DECLARE @Code VARCHAR(20) = 'ABC'; EXEC('… = ''' + @Code + '''')`, which has one possible text and therefore one plan. The test pins the false positive.
Fix: fire only when the slot's `SqlTextValue` has ≥2 distinct alternatives (a guarded join), and merge adjacent literal segments before computing boundaries. Separately, simple/forced parameterization can make even the multi-value case share one plan for trivial statements; the oracle test should include a sibling with one constant.

**F65 [L] QueryAntiPattern table-variable / TVP name sets are batch-scoped, not procedure-scoped**
`QueryAntiPatternScanner` clears `_tableVariableNames` / `_tableValuedParameterNames` per batch. CREATE PROCEDURE must be alone in its batch, so this only matters where one batch holds several scopes: dynamic-SQL inner batches and ad-hoc script batches next to a module. There, a table-variable name from one scope can mark a same-named TVP in another. Low reach; reset on module/scope entry instead.

**F43 [L] `NOT (x NOT BETWEEN a AND b)` treated as NOT BETWEEN** — `TypedPredicateExtractor.cs:638` uses `NotBetween || _negated`, should be XOR. Recall only.

**F44 [L] INSERT…SELECT pending-target handoff can be consumed by the wrong QuerySpecification** — `_pendingInsertTargetColumns` is taken by the *next* QuerySpecification entered; `INSERT TOP ((SELECT …)) INTO T SELECT …` visits the TOP subquery first.

### TypeInference — verified correct (no action)
decimal +,-,*,/,% precision/scale incl. >38 reduction; SUM/AVG widening; LEN/DATALENGTH bigint on max; QUOTENAME/SOUNDEX/ORIGINAL_LOGIN/USER_NAME/APP_NAME/HOST_NAME/SUSER_SNAME lengths; empty literal `varchar(1)`; >int integer literals (ScriptDom gives NumericLiteral → numeric(10,0)); CASE char/varchar/nchar mixes; concat lengths.
