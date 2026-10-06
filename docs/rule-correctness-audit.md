# Rule correctness audit

Ongoing pass auditing each shipped rule scanner's own logic against real SQL
Server engine behaviour (oracle-verified against a live instance where
possible, `vendor/sql2025` reference source otherwise) — not missing
detections (`detection-tasklist.md` covers those), but places where a
scanner's own reported verdict diverges from what the real engine actually
does: a false positive, a false negative, or a materially wrong claim in a
finding's own message. Confirmed items are recorded here for a later fix
pass and are not fixed inline as part of this audit.

An item only lands here once it is confirmed against the real engine or an
authoritative source, not merely suspected — this project treats a
false-positive bug report the same way it treats a false-positive finding:
worse than not reporting it at all.

First full pass: all 78 rule scanner families audited, 35 confirmed
correctness bugs found; 0 remain open below.

---

## Confirmed bugs (open)

---

## Fixed, tagged by root cause

Entries here are closed fixes, kept only so a later bug can be checked
against the same root-cause category before it's treated as new.

- **Category: rationale promised a plan operator the engine adds only when the
  chosen plan lacks a blocking one.** `dml/self-referencing` claimed every
  flagged statement carries an Eager Spool or Sort. Plan probes on heap and
  clustered targets showed UPDATE, DELETE and MERGE always protected in the
  probed shapes, but an INSERT whose plan reads the target through a hash
  join build input or a hash aggregate carries neither, and an INSERT whose
  source can only produce one row (no FROM clause, or an aggregate-only SELECT
  without GROUP BY) never gets one on a heap or a clustered table. The scanner
  now skips the single-row INSERT source (the shared single-row query helper
  also stopped counting a windowed aggregate as a scalar aggregate), and the
  rationale and doc state that the protection is a cost-based plan decision.
  No other rule's rationale claims a protective spool or sort operator.

- **Category: finding emitted although the claimed plan loss cannot occur.**
  Four shapes, each oracle-confirmed against plan shape or actual rows:
  (1) `index/key-lookup-prone` fired on equality over the full key of a unique
  index, which reads at most one row in the Key Lookup; (2)
  `query/table-variable-low-compat-estimate` fired on single-source
  statements whose plan is identical with or without the 1-row estimate, and
  now fires only for joins, GROUP BY, DISTINCT and IN/EXISTS semi-joins; (3)
  the sargability kinds fired on a column that is a key of no index, where the
  wrap changes nothing, and now report those at Medium confidence; (4) the
  sargability kinds fired on a wrapped predicate whose column also has a bare
  range/equality conjunct (in WHERE or an inner-join ON), where the wrap is
  only a residual filter. A bare predicate under OR does not suppress.
- **Category: typed-predicate verdict with no rule id reported as a finding.**
  `Unknown` and `OperandClash` typed-predicate verdicts carry no rule id and
  were serialized into the findings list; they are now excluded from the list
  and still counted in the typed-predicate summary.
- **Category: seek-preserving conversion claim didn't cover a truncating
  target category.** `NonSargablePredicateScanner.IsSeekableThroughConvert`
  treated any datetime-family-to-datetime-family `CAST`/`CONVERT` as
  seek-preserving; truncating to `DATE`/`TIME` is a many-to-one mapping the
  engine can't range-seek through, unlike numeric narrowing or decimal
  scale-narrowing (both still one-to-one/range-expressible). Oracle-confirmed
  and fixed.
- **Category: predicate-acceptance criterion looser than the rule's own
  claimed scope.** `IndexCoverageScanner` (key-lookup-prone) accepted any
  comparison operator as a "constraining" predicate for candidate-index
  selection, contradicting its own doc's equality-only claim; an unselective
  range predicate matching ~100% of rows made the optimizer prefer a scan
  over the seek+lookup the finding claims. Fixed by adding an equality-only
  parallel field to the shared `ConstrainedColumnStatementVisitor`, scoped to
  this one scanner (the visitor's other two consumers don't make an
  equality-specific claim).
- **Category: computed-column suppression check present on some wrap paths,
  missing on sibling paths in the same scanner.** The case-fold and
  date-function paths in `NonSargablePredicateScanner` already declined to
  fire when an indexed computed column's definition structurally matched the
  wrapped predicate expression. The generic function fallthrough, the
  `COALESCE`/`NULLIF`/`IIF`/`CASE` path, and `InspectArithmetic` skipped this
  same check, so a predicate matching an indexed computed column (e.g.
  `REPLACE(Col, '-', '')` or `Price + Tax`) still fired even though the real
  engine can seek through that column's index. Fixed by adding the same
  `ComputedColumnMatcher.HasIndexedMatchingComputedColumn` check to all three
  remaining call sites, and extending the matcher's structural comparison to
  `BinaryExpression` so it recognizes arithmetic definitions too. Both
  oracle-confirmed.
- **Category: relocation across a dynamic-SQL call site overwrote a
  finding's own confidence with the script's confidence instead of combining
  them.** `DynamicSqlPipeline`'s generic `Remap<TFinding>` helper, its
  harness-rule relocation loop, and its cross-boundary temp-table-usage
  relocation all substituted `script.Confidence` for whatever confidence the
  underlying scanner had already assigned, so a rule's own Medium- or
  Low-confidence finding (e.g. an intrinsic `NOLOCK` dirty-read hint at Low)
  came out at High whenever it was folded into a literal (High-confidence)
  `EXEC('...')`/`sp_executesql` script - the opposite of the one-level-deeper
  nested-relocation path, which already combined via `Worse(finding.Confidence,
  outerScript.Confidence)`. This let genuinely lower-confidence findings pass
  the default `--confidence high` filter. Fixed by routing all three call
  sites through the same `Worse` combinator used by the nested path; no other
  caller of `.Relocated`/`RelocatedAny` exists outside `DynamicSqlPipeline.cs`,
  and no other report/ranking/SARIF-level-mapping code overwrites a finding's
  confidence rather than combining it (SARIF only ever floors a level from a
  confidence, never raises one).
- **Category: same computed-column-suppression gap, reached through
  view-layer lineage instead of a direct predicate.** The rule's doc for
  `silentscan/lineage/expression-derived-column` claims a computed expression
  can never be seeked through regardless of type - true in general, but not
  when a base table has an indexed computed column whose definition matches
  a `CAST` baked into an upstream view's `SELECT` list; the engine inlines
  the view and seeks through that index. `ColumnProvenance.Cast` doesn't
  carry the original expression tree, so the fix is scoped to the reachable,
  common single-hop case (`CAST` directly over a base column, not a
  multi-step or cross-view chain): added
  `ComputedColumnMatcher.HasIndexedMatchingCastComputedColumn`, which
  reconstructs the comparison from the base column name and the cast's
  target type rather than needing the original AST. Oracle-confirmed; this
  rule has the most real-code findings of any rule in the catalog, so this
  was worth prioritizing over further step-2 sampling of smaller rules.
- **Category: join-shape check treated as a syntax rule when the real cutoff
  is predicate-shape.** `UnindexedTempTableUsageScanner`'s `JoinOperand`
  detection keyed purely off `CROSS JOIN` vs. other join syntax, but the
  engine's actual seek availability turns on whether a `WHERE`-clause
  equality predicate correlates the two sides, not on which join keyword was
  written. A bare `CROSS JOIN` with no correlating predicate anywhere is a
  true cartesian product — no seek is possible even when the temp table is
  indexed, forced-plan oracle-confirmed (`Index Scan` + `Row Count Spool`,
  no seek under `OPTION (LOOP JOIN, FORCE ORDER)`). `CROSS JOIN` combined
  with a `WHERE` predicate equating a column on each side is logically an
  inner join and does get a real seek path once indexed, identically to
  `INNER JOIN ... ON`, also forced-plan oracle-confirmed. Fixed by gating
  `CROSS JOIN` detection on the presence of a correlating `WHERE` equality
  predicate (matching aliases, not raw table names) instead of dropping
  `CROSS JOIN` detection outright.
- **Category: flow-tracking scanner only inspected the outermost query
  specification, missing predicates nested one level deeper.**
  `ParameterReassignmentPredicateScanner` tracks reassignment state per
  top-level statement (needed for its control-flow analysis), so unlike its
  sibling predicate scanners it doesn't go through `ModuleWalker`'s generic
  per-`QuerySpecification` dispatch, which is what makes those siblings
  transparently reach predicates nested inside a derived table. A predicate
  sitting inside a derived table in the `FROM` clause of a `SELECT`,
  `UPDATE ... FROM`, or `DELETE ... FROM` was silently skipped even though it
  compares a reassigned parameter against a base column exactly like the
  top-level case. Fixed by recursively descending into `QueryDerivedTable`
  nodes reachable through the statement's `FROM` clause (including through
  joins), inspecting each nested query specification's own predicate
  locations with a freshly resolved scope chain.
- **Category: run-once catalog fact re-run per batch.** When the full rule
  harness began running on dynamic SQL, `RuleRunner.RunOnBatch` also called
  every rule's `ScanCatalogOnce`, so each whole-catalog finding (the
  statement-shape "table has no primary key" check) was emitted again for
  every analysed dynamic-SQL batch and then relocated onto that batch's call
  site. One table produced one correct finding plus one duplicate per
  dynamic-SQL-bearing module, each wrongly attributed to an unrelated
  `EXEC`/`sp_executesql` call. Fixed by keeping `ScanCatalogOnce` solely in
  the once-per-run path; per-batch execution now runs only the batch's own
  module and legacy scans. Sibling sweep: `ScanCatalogOnce` has one
  implementer and one remaining call site, `RuleRunner.Run` and the
  catalog-only index-design scan each have a single caller, and the only
  `Prepare` override builds a lookup rather than findings.
- **Category: seek-loss verdict emitted on a column that leads no index.**
  `verdict/scan-forced` and `verdict/range-seek` claim that an implicit
  conversion on the column side removes a seek (or degrades it to a dynamic
  range seek), but the verdict was derived from the type pair alone. About
  two thirds of the High `scan-forced` findings in a production-shaped sample
  sat on columns that lead no index. Oracle-confirmed on plan shape: a
  varchar column of no index cannot seek whether the literal is `'123'` or
  `123`, and a column that is only a trailing key of an index behaves the
  same, while the leading column of an index seeks with the matching literal
  and cannot with the int literal. Fixed by reporting the verdict at High
  only when the converted column resolves to a base column that leads an
  active, non-filtered index (the catalog's existing `Indexed` fact), and at
  Medium otherwise, mirroring the sargability kinds. Sibling sweep, rule
  rationales read: the sargability kinds already gate on key-of-any-index;
  `under-length-parameter`, the ANSI-padding mismatch, local-variable
  predicate and filtered-index parameter findings make no claim about a seek
  lost to a conversion.
- **Category: assignment form missed by the shared write-site helper.**
  `control-flow/unassigned-output-parameter` treated `EXEC @p = proc` as no
  write to `@p`. Oracle-confirmed: the return status is assigned to the
  caller's variable, so an OUTPUT parameter set that way is assigned. Fixed in
  `VariableWriteSites`, which every scanner built on it shares; the other
  consumers only gain a recognised write.
- **Category: CATCH path assumed reachable from any TRY statement.**
  The flow walker entered CATCH from the state before the TRY body, so an
  assignment inside TRY was ignored on the CATCH path even though CATCH runs
  only when an error is raised. Under the no-hard-error scope the only
  statically decidable entries are `THROW` and `RAISERROR` with a literal
  severity of 11 or higher. Oracle-confirmed for assignment before the raise,
  after it, and a swallowing CATCH. The behaviour is opt-in through
  `CatchEntersOnlyThroughExplicitRaise`, so other walker policies are
  unchanged.
- **Category: constraint-enforcement claim made for a disabled foreign key.**
  `index-design/unindexed-foreign-key` (a parent-side DELETE/UPDATE forces a
  referential-integrity scan of the child) and `catalog/cascading-foreign-key`
  (a parent DML silently cascades) both read foreign keys from the catalog
  without looking at `is_disabled`. Oracle-confirmed: after `NOCHECK` a parent
  DELETE plan no longer touches the child table at all while an enabled,
  equally unindexed sibling is still scanned, and a disabled `ON DELETE
  CASCADE` leaves the child rows in place while the enabled sibling cascades.
  Fixed by skipping disabled foreign keys in both scanners. The sampled
  `unindexed-foreign-key` findings were otherwise all genuine: none of the
  sampled keys had any index leading on its column set, an index leading on
  only a prefix of a composite key was oracle-confirmed to still be scanned by
  the referential-integrity check, and untrusted-but-enabled keys are still
  enforced. Sibling sweep: `PartialCompositeForeignKeyJoinScanner` reads
  foreign keys too but its claim rests on the parent key's uniqueness, which a
  disabled constraint does not change, so it is unchanged.

---

## Audited, no bug found

- `WriteLossClassifier` (`Rules/WriteLossClassifier.cs`) — variable-target-only
  scoping for `LengthTruncation` is intentional (table-column narrowing is a
  hard compile error, not a silent loss); re-verified live.
- `AnsiNullDfltFlowResolver` + `CatalogBuilder`'s ANSI_NULL_DFLT fallback —
  re-verified the ON/OFF and OFF/OFF no-op asymmetry against a live instance;
  matches the already-shipped logic from the prior fix.
- `SqlTypeCategory` enum ordering used by `ExpressionTypeInferencer.Combine`
  for CASE/IIF branch-type merging — matches Microsoft's documented data-type
  precedence table exactly, member-by-member.
- A handful of hardcoded builtin-function return lengths (`SUSER_SNAME`,
  `USER_NAME`, `APP_NAME`, `DB_NAME`, `HOST_NAME` = nvarchar(128);
  `ORIGINAL_LOGIN` = nvarchar(4000)) — confirmed via
  `sys.dm_exec_describe_first_result_set`.
- `CartesianJoinScanner` — oracle-tested for the no-predicate row-count claim;
  traced the connectivity/union-find logic
  through third-table transitivity, self-references, parenthesized/negated
  predicates, `CROSS APPLY` exclusion, and the conservative bail-out on any
  unqualified column reference — all consistent with the existing test
  suite and the project's deliberately false-negative-favoring design here.
- `CascadingForeignKeyScanner` — fires on any FK action other than
  `NO ACTION` (CASCADE/SET NULL/SET DEFAULT); message and rule doc already
  hedge with "or nulls, or resets" rather than overclaiming "cascade" for
  the non-CASCADE actions. Purely catalog-derived, no session/DB-setting
  dependency to diverge on.
- `CompositeIndexLeadingColumnScanner` — the "cannot be seek-used at all
  without a bound leading column" claim holds on SQL Server 2025, including
  with an explicit `WITH (INDEX(...))` hint forcing the index; verified no
  newer "index skip scan" feature invalidates it (still a full `Index
  Scan`, never a seek, when only the non-leading column is bound).
- `ScalarUdfScanner` family (`in-select-or-expression`,
  `nested-under-view-or-tvf`, `in-predicate`, `in-computed-column-or-
  constraint`) — `ScalarUdfMap.Build` correctly excludes multi-statement
  TVFs from the "transparent, nested-under" claim (an MSTVF is genuinely not
  optimizer-transparent, unlike a view or inline TVF); `ScalarUdfContext
  Regions.Resolve`'s smallest-enclosing-region matching and `IsPredicate()`'s
  WHERE/JOIN-ON/HAVING/MERGE-ON classification both check out against the
  rule docs' own scoping claims.
- `IndexDesignScanner.ScanUnindexedForeignKeys` — checks the referencing
  (child) table's own leading index against its FK columns, not the
  referenced table's; matches the rule's own claim (an FK lookup scans the
  child side without a supporting index).
- `silentscan/query/unqualified-table-reference` — real-code precision
  sample (25 random findings against the local test database) reviewed
  end-to-end; all 25 confirmed true positives. One case initially looked
  ambiguous (a flagged column pointing mid-identifier inside what appeared
  to be a bracket-qualified `[schema].[table]` reference) but traced back to
  a different, genuinely unqualified reference to the same table name
  earlier in the same object — the finding's line/column was correct
  throughout; the ambiguity was in how the source text was sampled for
  review, not in the scanner.
- `ScalarUdfInlineabilityClassifier` / `silentscan/scalar-udf/in-select-or-
  expression` — real-code sampling showed 100% of findings against the
  local test database classified `NotInlineable`, which first looked like a
  possible over-broad heuristic; oracle-confirmed instead that the database
  itself runs at compatibility level 140, below the classifier's correct
  150 FROID floor (`MinInliningCompatibilityLevel`), so every scalar UDF is
  genuinely non-inlineable regardless of body shape — matches
  `sys.sql_modules.is_inlineable` reporting the functions as
  inlineable-in-principle at a higher compat level. Compat-level cutoff
  already covered by `ScalarUdfInlineabilityClassifierTests` plus the
  real-engine `is_inlineable` cases in `LiveCatalogReaderScalarUdfTests`.
- `silentscan/predicates/local-variable-predicate` — `IsFormalParameter`
  gating correctly excludes formal parameters (including ones later
  reassigned, which `silentscan/predicates/reassigned-parameter` covers
  instead), so the two rules partition DECLARE'd-local vs.
  reassigned-parameter cases without overlap or gap.
- `silentscan/forced-serial/table-variable-modification` — fires only on
  the write target (INSERT/UPDATE/DELETE/MERGE/OUTPUT INTO), not a
  read-only reference; already backed by a real executed-plan
  `NonParallelPlanReason` oracle test.
- `DeadCodeScanner` — the `ReachabilityWalker` control-flow model correctly
  treats RETURN/THROW as terminal, requires every IF/TRY-CATCH branch to be
  terminal (a `THROW` inside `TRY` alone doesn't make the block terminal —
  it's ANDed with `CATCH`'s own terminality), and never treats `WHILE` as
  terminal; unused-variable/parameter logic correctly separates reads from
  writes. Pure static-AST rule, no real-engine fact to diverge from beyond
  the definitional "RETURN/THROW end the routine."
- `DefaultNullableConstraintScanner` — only fires when a `DEFAULT`-bearing
  column is still nullable, matching the uncontested fact that a `DEFAULT`
  only applies when a column is omitted from an INSERT's column list, and
  an explicit `NULL` always overrides it.
- `FloatOrderDependentAggregateScanner` — aggregate-name gate (SUM/AVG/VAR/
  VARP/STDEV/STDEVP only, MIN/MAX/COUNT excluded) matches the rule doc's
  explicit claim; `OverClause is null` deliberately excludes windowed
  aggregates, a documented scope boundary rather than a divergence;
  `BaseColumnResolver` only resolving direct column-reference arguments
  (not an expression like `Value * 2`) is a precision-first scope limit,
  not a false claim.
- `ForcedSerialScanner` — all three `NonParallelPlanReason` claims
  oracle-verified via real plan XML (table-variable INSERT target →
  no-parallel-nested-transaction; `OBJECT_ID`/`@@TRANCOUNT`/
  `IDENT_CURRENT`/`ERROR_MESSAGE` inside a query with FROM →
  nonparallelizable intrinsic; FAST_FORWARD/bare FORWARD_ONLY READ_ONLY
  cursors → no-parallel cursor), including the negative cases
  (`@@ROWCOUNT`, `SCOPE_IDENTITY()`, `LOCAL STATIC FORWARD_ONLY READ_ONLY`,
  a no-option cursor, a `DYNAMIC` cursor) correctly never firing.
- `IndexHintScanner` — existing oracle tests already confirm the seek→scan
  degradation for an unbound hinted-index leading column; independently
  confirmed the "referenced anywhere" column collector correctly resolves
  correlated references inside a subquery, so a leading-key column bound
  only in a correlated subquery doesn't cause a false positive. The sibling
  nonexistent-hinted-index check was removed as a pure hard-error rule.
- `MaxTypedColumnScanner` — live-verified the two differentiated claims:
  `VARCHAR(MAX)` is allowed as an INCLUDE column but rejected as a key
  column (Msg 1919), while legacy `TEXT`/`NTEXT`/`IMAGE` is rejected even
  as an INCLUDE column (Msg 1999) — matches the rule's two separate
  messages exactly.
- `MissingStatisticsScanner` — auto-create-stats gate and
  leading-vs-non-leading statistic-column coverage logic both already
  oracle-tested end-to-end against a live catalog; the underlying catalog
  facts (`is_auto_create_stats_on`, `sys.stats`/`sys.stats_columns`) are
  read live, engine-authoritative by construction.
- `CatchAllPredicateScanner` — `(Column = @p OR @p IS NULL)` detection scope
  (equality-only, formal-parameter-only), the `WITH RECOMPILE`/
  `OPTION(RECOMPILE)` guard's exhaustiveness (confirmed all three
  CREATE/ALTER procedure statement forms share the same base type, and that
  triggers/functions cannot syntactically carry `WITH RECOMPILE` at all),
  and the dead-comparison absorption path are all correct or already
  oracle-tested.
- `ModuleCompileFlagScanner` — `RecompilesEveryCall` confirmed tied only to
  `WITH RECOMPILE` at create/alter time, unaffected by `sp_recompile`;
  `TableValuedFunctionReturnUsesDatabaseCollation` confirmed scoped exactly
  as documented across procedures/views/scalar functions/triggers/inline
  and multi-statement TVFs, including the schema-binding-sets-it-
  unconditionally case.
- `MultiReferencedCteScanner` — direct multi-reference already oracle-
  tested; newly verified transitive multi-reference (CTE B referencing CTE
  A twice, main body referencing only B once) via `STATISTICS IO` showing
  the base table scanned exactly the predicted number of times; recursive
  self-reference exclusion is structural; confirmed no CTE-shadowing path
  exists since T-SQL doesn't allow a `WITH` clause nested inside a
  subquery.

Skipped as members of the style families exempt from the oracle-test gate
(`RuleOracleCoverageTests`), whose findings make no result or plan claim:
`CodeMetricScanner` (every finding kind's own text says "no query result or
plan is affected") and `FormattingScanner` (same framing; the one
underlying T-SQL fact — an unbraced `IF`/`WHILE` body is exactly one
statement — is uncontroversial syntax, not a claim needing verification).
- `NestedViewDepthScanner` — depth-accumulation arithmetic (`deepestChild +
  1`) matches the doc's own "2+ layers deep before reaching a base table"
  definition exactly; inline TVFs correctly folded into the same view map.
- `NonUniqueUpdateSourceScanner` — the uniqueness proof correctly requires
  the *entire* key-column set of a non-filtered, non-disabled unique index
  to be covered by the join's equality columns (a subset is correctly
  rejected); the `MERGE` Msg 8672 framing is already oracle-verified.
- `NotInNullableSubqueryScanner` — requires base-column provenance with no
  view indirection for the subquery's selected column, and only recognizes
  a top-level AND-flattened `IS NOT NULL` guard (an OR-nested one correctly
  does not suppress); the core three-valued-logic claim is already
  oracle-verified.
- `ParameterReassignmentPredicateScanner` — WITH RECOMPILE/OPTION(RECOMPILE)
  suppression at both proc and statement level, intersect-on-merge "every
  path" semantics, and the `Depth: 0` correlated-subquery guard are all
  correct. This scanner shares `VariableWriteSites`
  (`Predicates/VariableWriteSites.cs`) with `OutputParameterScanner`;
  `VariableWriteSites.InStatement` now reports whether each write is
  guaranteed to execute (no `FROM` clause, or a `GROUP BY`-free aggregate
  assignment) versus merely conditional (a non-aggregate `SELECT @p = col
  FROM t WHERE ...` that may match zero rows), and both scanners only treat
  guaranteed writes as reassignment/assignment - closing the false-positive
  risk previously flagged here.
- `PartialCompositeForeignKeyJoinScanner` — composite-only grouping,
  local-vs-statement-wide equality coverage split, comma-join dedup, and
  the unique-index suppression direction (`i.KeyColumns.All(usedColumns.Contains)`,
  confirmed as the mathematically correct direction) all check out against
  the existing test suite's JOIN/comma-join/UPDATE-FROM/CTE-shadowing
  coverage.
- `PostExpansionJoinWidthScanner` — the `MinimumGap = 3` threshold and
  unresolved/derived-table undercounting exactly match the doc's own
  "lower bound, never exhaustive" framing.
- `ProcCallArgumentMismatchScanner` — verified both directions:
  `WriteLossClassifier.Classify(target, source, ...)` called correctly as
  `(formal, caller)` for the in-direction and `(caller, formal)` for the
  writeback direction; oracle-confirmed an OUTPUT parameter receives the
  caller's pre-call value regardless of whether `OUTPUT` appears at the
  call site, so the unconditional in-direction check is correct, and the
  writeback direction is correctly gated on both the formal parameter being
  OUTPUT and the call site supplying the `OUTPUT` keyword.
- `QueryAntiPatternScanner` (all other finding kinds) — `ALTER TABLE SWITCH`
  index/constraint/filegroup/temporal/CDC/rule/full-text checks all
  internally consistent with their claimed error codes; grouping-sets
  cardinality limits oracle-verified at all three exact boundaries (CUBE
  12/13, ROLLUP 32/33, GROUPING SETS 4096/4097); `RecursiveCteMissingMaxRecursion`'s
  "default 100" claim is standard documented behavior.
- `ScalarUdfInlineabilityScanner` — `MinInliningCompatibilityLevel = 150`
  correct; `MaxInlineableTableReferenceCount = 49` independently
  oracle-verified via a plan-inlining sweep (49 scalar-subquery table
  references inlines, 50 doesn't) — exact match to the scanner's `> 49`
  gate.
- `SecurityScanner` — credential/IP
  heuristics carry no falsifiable engine-behavior claim.
- `SecurityPredicateIndexScanner` — leading-key-column-only match is
  explicitly documented as an intentional design choice (already
  oracle-tested scan-vs-seek claim, deliberately declines an unproven
  "forces serial execution" claim).
- `SelectStarViewScanner` — star-consumer exclusion, full-explicit-selection
  exclusion, multi-source unqualified-column decline, and CTE/derived-table
  non-attribution all match existing test coverage; a pure code-structure
  claim (frozen metadata), no engine-timing dependency to diverge on.
- `SelfReferencingDmlScanner` — `HasLiteralTopOne`'s PERCENT/variable/
  non-literal exclusions and the target-alias-skip logic across self-join
  aliases match already-oracle-confirmed Eager Spool/Distinct Sort presence
  and absence across INSERT/UPDATE/DELETE/MERGE, direct and through-view.
- `SessionDateSettingScanner` — deliberately coarse (any `SET
  DATEFORMAT`/`DATEFIRST` presence, no literal-pattern matching, `Low`
  confidence by design); both underlying claims (DATEFORMAT mdy/dmy
  resolving a literal differently, DATEFIRST 1/7 shifting `DATEPART(weekday,
  ...)`) verified live.
- `SetOptionScanner` — ARITHABORT's exclusion from `SyntaxOnlyTriggers` was
  already oracle-tested for filtered indexes; newly verified it also holds
  for indexed views (ARITHABORT made no difference to the NOEXPAND path,
  while ANSI_NULLS/QUOTED_IDENTIFIER OFF correctly triggered the documented
  silent view-expansion fallback).
- `StaleSelectStarViewScanner` — `FindSingleBaseTable`'s join/CTE-shadowing
  exclusion, real-table-only resolution, and the order-sensitive
  `SequenceEqual` column comparison (correct, since `SELECT *` is
  positional) all check out; the rule's motivating "not merely a
  missing/extra column" phrasing is contextual illustration, not
  contradicted by the scanner's own intentionally-broader trigger
  condition.
- `TemporalTableHistoryIndexGapScanner` — index-kind filtering and
  `SameKeyColumns`/`IsComparableIndex` logic match the existing test suite
  and the doc's stated criteria exactly; confirmed live that the engine
  itself refuses a plain `CREATE UNIQUE NONCLUSTERED INDEX` (not just a
  constraint) against a temporal history table, consistent with the
  scanner's uniqueness-agnostic comparison.
- `TransactionHygieneScanner` — the reachability walk's conditional-open/
  unconditional-close merge behavior was traced and doesn't produce a false
  claim under the rule's own stated scope (leaked transactions); Msg 266
  and `@@TRANCOUNT` behavior already oracle-tested. Three known blind spots
  are already tracked in `detection-tasklist.md` and excluded from this
  audit's scope, not re-verified here.
- `TriggerOrderScanner` — `is_first`/`is_last` grouping matches the real
  `sp_settriggerorder` invariant (at most one trigger can hold First, one
  Last, per table/event); the "≥2 unordered" threshold correctly treats a
  single remaining unpinned trigger as fully determined by elimination.
- `TriggerRecursionCycleScanner` — live-verified the
  `RECURSIVE_TRIGGERS`-vs-`nested triggers` distinction: `RECURSIVE_TRIGGERS
  OFF` does not stop an indirect cross-table trigger cascade (confirmed
  running unbounded), while the server-level `nested triggers` option gates
  cross-table cascading from the very first hop — the scanner correctly
  gates on `nested triggers` only and excludes the same-table 1-hop
  self-loop that `RECURSIVE_TRIGGERS` actually governs.
- `TvfFenceScanner` — the APPLY-only correlation gate and inline-TVF
  resolution through the fence map check out; existing oracle tests verify
  `CorrelatedApply` outcomes against a real deployed engine.
- `UntrustedConstraintScanner` — `IsNotTrusted && !IsDisabled` filter
  matches live catalog semantics; confirmed disabling a trusted FK sets
  both `is_disabled` and `is_not_trusted`, so excluding disabled
  constraints is correct (the optimizer ignores them regardless of trust);
  the FK `DistinctBy(ConstraintName)` (absent for check constraints) is
  correct given `sys.foreign_key_columns`' per-column-pair row shape versus
  check constraints' non-duplicated one.
- `VerdictClassifier` (`silentscan/verdict/scan-forced`, `range-seek`) —
  reviewed all branches (SqlVariant handling, out-of-model gating, collation
  mismatch, same-category and cross-category paths). The one asymmetry that
  looked suspicious — same-category `IsMax` mismatch classified as
  `RangeSeek` regardless of which side is the MAX type — is not reachable
  the way it first appeared: a `VARCHAR(MAX)`/`NVARCHAR(MAX)` column can't be
  an index key column at all (hard DDL error), so the only reachable
  direction is column-non-MAX vs other-MAX, oracle-confirmed via plan XML to
  produce exactly the `GetRangeWithMismatchedTypes` seek-with-filter shape
  `RangeSeek` claims. The cross-category matrix itself is continuously
  oracle-verified by `TypePairMatrixLiveRegenerationTests` against every
  probed cell, so this rule's remaining risk surface is narrow.
- `QueryAntiPatternScanner` — `table-variable-low-compat-estimate` (the
  dominant volume driver in this family) gates on `catalog.CompatibilityLevel
  < 150`, null-safe when the level is unresolved, with the `DBCC
  TRACEON(11034)` false-positive risk already hedged in the rule's own
  rationale text; matches the documented compat-150 deferred-compilation
  change and has its own dedicated oracle test.
- `UnindexedTempTableUsageScanner` — an uncorrelated `CROSS JOIN` onto a
  `#temp` table that still carries its own WHERE-clause filter on that temp
  table (no equality predicate correlating the two join sides) was falling
  through both the join-operand and WHERE-filter branches and produced no
  finding at all, a false negative introduced by the earlier CROSS-JOIN
  correlating-predicate fix in this same family. Oracle-confirmed
  (`WITH (FORCESEEK)`) that the seek is genuinely available on the indexed
  temp table in this shape; the optimizer's own default plan choice (a
  table scan, cost-driven, for a trivially small table) is not evidence the
  seek is unavailable. Fixed by classifying this shape as
  `unindexed-where-filter` instead, matching the same-root-cause WHERE-filter
  case; the fix generalizes to unqualified column references in the WHERE
  clause too, consistent with the scope boundary that ambiguous unqualified
  references are assumed unreachable (hard-error out of scope).
- `ScalarUdfScanner` — root cause: claim attached to a context that cannot
  trigger the effect. A projection-position scalar UDF call was reported as
  per-row execution even in shapes with no row source (FROM-less SELECT,
  VALUES, variable assignment) or when the call only appears in the argument
  list of an uncorrelated table-valued function. Oracle-confirmed through the
  actual plan: the operator holding the function reference reports as many
  actual rows as the row source yields, and exactly one for the no-source
  shapes. Fixed by firing only inside a FROM-bearing query (or UPDATE/DELETE/
  MERGE) and not inside an uncorrelated TVF argument. A sampled set of
  findings after the fix was all true positives. Sibling of the same root
  cause: the predicate-position finding fired for a WHERE with no row source
  (`SELECT 1 WHERE dbo.f(1) = 2`); the row-source requirement now applies to
  every context.
- Sibling hunt for the three row-source/attribution fixes above — checked and
  clean: `ForcedSerialScanner` intrinsic kind (already gated on a FROM-bearing
  query; engine reports the serial reason for OBJECT_ID with a variable
  argument), its cursor kind (engine reports the reason even for a FROM-less
  cursor query, so no row-source gate is warranted), its OUTPUT INTO
  table-variable path (engine reports the reason even with no row source);
  the other `_currentPredicateFragment` consumers in `TypedPredicateExtractor`
  (only reached right after the fragment is set) and `NonSargablePredicateScanner`
  (renders its own node). Post-fix sample of table-variable, intrinsic and
  cursor forced-serial findings across distinct modules: all true positives,
  doubtful shapes (DML on a table variable as its own only source, OBJECT_ID
  over a variable in an EXISTS, OUTPUT INTO a table variable) confirmed through
  the plan's non-parallel reason.
- `ForcedSerialScanner` (table-variable modification) — root cause: claim
  attached to a context that cannot trigger the effect. An INSERT into a
  table variable with no row source (VALUES, EXEC, SELECT without a table
  reference) was reported as forcing a serial plan although there is nothing
  to parallelize. Oracle-confirmed that the plans for these shapes contain
  only constant/compute/insert operators, while an INSERT from a real table
  keeps the serial-reason marker and never gets a Parallelism operator even
  when a parallel plan is preferred (a `#temp` target does). Fixed by
  skipping row-source-less inserts into a table variable.
- `TypedPredicateExtractor` (expression-derived-column) — root cause: shared
  resolver reused outside the context its finding assumes. The operand
  resolver recorded a derived-column finding for any column reached while
  resolving a predicate operand, including columns buried inside a wrapping
  function or arithmetic whose text the finding's predicate did not show.
  Fixed by recording the finding only when the column reference is itself the
  direct (parenthesis-unwrapped) operand of the predicate.
- Test suite (plan-cache and forced-parameterization oracle tests) — root
  cause: server-wide side effect run concurrently with tests that assume an
  isolated plan cache. The only test changing server-wide state
  (`sp_configure` + `RECONFIGURE`) sat in a named xunit collection, which
  serializes only its own members while every other collection keeps running
  in parallel, so a reconfigure could clear the shared plan cache under a
  plan-cache reader and a different pair failed on each run. Fixed by
  declaring that collection with parallelization disabled. All other
  cache-reading tests filter to their own database, and no test issues
  `DBCC FREEPROCCACHE`.

- `QueryAntiPatternScanner` (table-variable-low-compat-estimate) — root
  cause: claim attached to a context that cannot trigger the effect. A table
  variable read in a statement carrying `OPTION (RECOMPILE)` was reported as
  estimated at one row. Oracle-confirmed that below compatibility level 150 a
  plain statement estimates 1 row while the same statement with
  `OPTION (RECOMPILE)` estimates the real row count, and that a procedure-level
  `WITH RECOMPILE` does not change the plain statement's estimate. Fixed by
  skipping table-variable sources inside a statement with the statement-level
  hint. The existing oracle read `COUNT(*)`, whose estimate is one row at any
  compatibility level, so it could not fail; it now reads the rows directly
  and has the recompile sibling.
- `IndexCoverageScanner` (key-lookup-prone) — root cause: competing access
  path ignored. An equality on a nonclustered index's leading key was
  reported even when the same predicate also constrained the leading key of
  the clustered index, where the optimizer seeks the clustered index and no
  lookup is produced; the single-candidate guard only counted nonclustered
  indexes. Fixed by skipping the table when the clustered leading key is
  equality-constrained too.
- `ConstrainedColumnStatementVisitor` (key-lookup-prone) — root cause: shared
  predicate extraction reused outside the context its finding assumes. The
  equality-constrained set counted both sides of every equality, so a
  column-to-column join condition counted as a WHERE equality. Oracle-confirmed
  that an unfiltered join on a nonclustered key plans as a hash join over
  scans with no lookup, while an equality against a constant in the ON clause
  keeps the lookup. Fixed by counting a column only when the opposite side
  holds no column reference. Siblings checked: the leading-column and
  missing-statistics scanners use the any-comparison set for different claims,
  and the leading-column scanner already treats the clustered index as an
  alternative seek path.
- `PostExpansionJoinWidthScanner` (post-expansion-join-width) — sampled 20
  findings across 20 modules: all true positives (each flagged view joins the
  listed tables). Not covered by the sample: a view read with `NOEXPAND`.
- `IndexCoverageScanner` (key-lookup-prone) — root cause: claim attached to a
  context that cannot trigger the effect. An equality on a bit-typed leading
  key was reported although the column has only two values, so the predicate
  matches most of the table unless the value asked for is the rare one.
  Oracle-confirmed that an equality on the common value of a bit flag plans
  as a scan with no Key Lookup. Fixed by skipping indexes whose leading key is
  a bit column; the rare-value case is deliberately given up because the
  scanner has no value distribution to tell the two apart.
- `NonSargablePredicateScanner` (function-wrapped-column, date-function-on-column)
  — root cause: claim attached to a context that cannot trigger the effect. A
  wrapped column whose base table could not be resolved (reached through a
  UNION view, a system catalog view, or an undeclared source) was reported at
  High, although the claimed seek loss exists only when the column leads an
  index key. Oracle-confirmed that wrapping a column no index keys leaves the
  plan unchanged, including through a UNION ALL view. Fixed by reporting
  unresolved columns at Medium, as columns known to key no index already are.
- `NonSargablePredicateScanner` (function-wrapped-column, date-function-on-column)
  — root cause: competing access path ignored. A wrapped column was reported
  although the same statement equality-binds every key column of a unique
  index on its table, so the plan seeks that index for at most one row and the
  wrap is a residual filter. Oracle-confirmed that a primary-key equality plus a
  wrapped indexed column still plans as a seek, while the same equality under
  OR does not. Fixed by skipping wraps beside a full-key equality on a unique
  index of the same table, in WHERE or an inner-join ON.
- `IndexCoverageScanner` (key-lookup-prone), `CompositeIndexLeadingColumnScanner`
  — root cause: outer-join semantics ignored. A constant compared in the ON
  clause of an outer join against a column of the preserved side was counted as
  a row filter. Oracle-confirmed that such a predicate filters nothing: the
  preserved side is scanned in full with no Key Lookup and no pushed predicate,
  while the same constant on the null-supplied side, or in WHERE, still plans as
  a seek plus lookup. Fixed by excluding those comparisons from the row-filter
  sets both scanners consume. The statistics scanner sharing the visitor keeps
  the unfiltered sets.
- `CompositeIndexLeadingColumnScanner` — root cause: competing access path
  ignored. A composite index was reported although another index on the table
  leads with a column the same statement compares to a constant, which the
  engine seeks instead. Oracle-confirmed, with a sibling table lacking that
  index staying a scan. Only constant comparisons count; a join-key equality
  does not, because the engine still scans there.
- `ExpressionTypeInferencer` string concatenation — root cause: category
  mismatch fell back to the wider-of-two rule instead of summing lengths.
  `varchar(11) + ', ' + char(8)` inferred length 13, the engine reports 21.
  Fixed by deriving the result category (unicode if either side is, variable
  if either side is) and summing lengths for every char/varchar/nchar/nvarchar
  pairing, oracle-checked against the engine's described result type.

- `WriteLossClassifier` unicode-to-non-unicode — root cause: expression
  provenance ignored. `QUOTENAME` returns nvarchar even for varchar input, so
  copying `QUOTENAME(varchar_col)` or a concatenation of ASCII literals and
  such calls into a varchar column was reported although every character
  already came from a non-unicode column of the same code page. Fixed by
  proving representability through concatenation, `QUOTENAME` and
  parentheses down to ASCII literals and non-unicode leaves sharing the
  target code page. Oracle-confirmed: the varchar round-trip is exact while
  `QUOTENAME` over an nvarchar column is replaced with question marks. Leaves
  of another code page or of nvarchar type still fire. Siblings:
  `IsNullReplacementValueTruncationScanner` shares the fix; the call-argument
  scanners (`ProcCallArgumentMismatchScanner`, `TvfCallArgumentMismatchScanner`,
  `SpExecuteSqlParameterMismatchScanner`,
  `ProcCallTableValuedArgumentMismatchScanner`) classify already-typed
  caller arguments without scope and stay conservative.

- `TransactionHygieneScanner` — root cause: path-insensitive guard handling.
  `IF @@TRANCOUNT > 0 ROLLBACK` (and `>= 1`, `<> 0`, `XACT_STATE() <> 0`) was
  merged with an implicit else that still carried the open transaction,
  although the false branch of such a guard means no transaction is open.
  Fixed by clearing the open site on the else path of those guards only.
  Oracle-confirmed: a catch rolling back under `XACT_STATE() <> 0` leaves
  the transaction count at zero, while a guard of `@@TRANCOUNT > 1` leaves it
  elevated and raises the count-mismatch error. `XACT_STATE() = -1` and
  `= 1` guards are not treated as closing, since the other state is still an
  open transaction. Siblings checked clean: the implicit-transaction finding
  kind shares this flow and gains the same behavior.

- `ScalarUdfScanner` (in-predicate) — root cause: column-free conjuncts
  treated as per-row. A UDF call whose arguments reference no column of the
  row source is evaluated once per statement; only conjuncts that depend on
  row data repeat. Fixed by recording column-free conjunct regions and
  excluding them. Oracle-confirmed through function-stats deltas with a
  column-dependent sibling. Known remaining gap: a UDF used as an index seek
  bound also runs once and is still reported.

- `ForcedSerialScanner` (non-parallelizable intrinsic, fast-forward cursor) —
  root cause: a query over a system catalog view is already serial. The
  catalog views force the non-parallelizable-intrinsic reason on their own,
  and a cursor over them reports only that reason, never the fast-forward
  one. Fixed by suppressing both findings when the outermost query reads a
  system catalog view. Oracle-confirmed with a user-table sibling that still
  reports the fast-forward reason. Known gap: a user view wrapping a system
  view is not recognised; bare read-only cursors are not reported.

- `TypedPredicateExtractor` (under-length parameter) — two root causes.
  Derived expressions (function results, scalar subqueries, concatenations)
  take their type from their inputs and never truncate, so only declared
  parameters and variables are considered. A variable whose only writes are
  literals no longer than its declared length cannot be truncated and is
  skipped. Oracle-confirmed with sibling cases that do truncate.

- `WriteLossClassifier` (temporal precision loss) — root cause: a datetime
  source that is provably midnight was reported as losing a time. Provable
  forms: date-only strings, integers rendered to a string, date-typed
  operands converted to datetime, and day-or-coarser DATEADD over a midnight
  base. Oracle-confirmed by a round-trip comparison, with time-carrying
  siblings (HOUR unit, time-bearing strings) still reported. The procedure-call,
  table-function-call, sp_executesql and table-valued-argument scanners call
  the same classifier and share the fix, without operand typing for
  variable-derived forms.

- `tier1/column-arithmetic` — sampled, no change. One sampled finding depends
  on runtime data and is not statically decidable; documented, not fixed.

---

## Not yet audited

None of the remaining rule scanner families are unaudited as of this pass.
`WindowFunctionArgumentScanner`, `AlwaysEncryptedComparisonMismatchScanner`,
`AlwaysEncryptedAssignmentMismatchScanner`, and `GroupByValidityScanner` were
audited here and then removed entirely as pure hard-error rules with no
silent-consequence branch (out of scope per CLAUDE.md's hard-error scope
boundary) - their audit entries are removed along with them. Re-auditing
after a shipped fix, or auditing a newly added rule family, restarts this
list.
