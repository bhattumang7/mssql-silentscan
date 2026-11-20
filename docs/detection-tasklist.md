# SilentScan detection checklist

Open work and the decisions that close it. The research behind it — anti-pattern
space, incumbent survey, measured engine facts, calibrated thresholds, killed
candidates — is in `detection-reference.md`. Every shipped rule is in
`rules.html`.

A shipped item's entry is deleted, not annotated. Only two things outlive an
item: a fact that can't be re-derived from the code, and a decision that
would otherwise be re-proposed — both move to `detection-reference.md`'s
Settled section.

Competitor tools are referred to generically; real identities are in
`vendor/tool-references.md` (gitignored).

---

## Open work

### Precision gate

`RulePrecisionLedger` records the labelled sample of every default-on rule. A rule is
default-on only at 95% true positives over enough decided samples; every other rule,
including every unmeasured one, is opt-in (`--confidence medium`). Re-measure a rule
after changing it and update its entry.

False-positive shapes to remove before a rule can return to default-on:
- function-wrapped-column, column-arithmetic, cast-or-convert-on-column, date-function-on-column,
  case-fold-on-column, expression-derived-column: the wrapped column is not indexed, or a
  sargable predicate on another column drives the access, or the wrapped side is the
  probe side of a join.
- max-typed-column: the MAX column is only stored or returned, never filtered, joined or sorted on.
- unicode-to-non-unicode: nvarchar from a split over numeric ids or identifier names.
- catch-all-parameter: the catch-all conjunct is residual behind a selective sargable predicate.
- case-expression-missing-else: the CASE is exhaustive over a bounded domain or already filtered.
- nested-under-view-or-tvf (tvf-fence): the TVF is the only row source, so no join is misled.
- fast-forward-cursor, count-star-variable-existence-check, multi-referenced-cte: the cursor
  query or CTE is trivially small.
- heap-with-nonclustered-indexes, filter-column-not-in-index, post-expansion-join-width,
  non-persisted-computed-column: the claim states no engine consequence by itself.
- plus-operator-null-propagation: delimiter prefix inside FOR XML PATH, or an operand
  already guarded.
- Unlabelled for lack of source or a decidable claim: unassigned-output-parameter,
  unresolved-transaction, unindexed-foreign-key, check-constraint-null-not-handled,
  scalar-udf/nested-under-view-or-tvf, write-loss/length-truncation,
  write-loss/temporal-precision-loss, verdict/range-seek.

Medium and low confidence findings have not been sampled.

Lost coverage to restore: `ScanReportBuilder` minimum-confidence filtering had one
test (`MinimumConfidence_FiltersEachFindingByItsOwnConfidence_NotAllOrNothing`)
built on a Low-confidence task-comment finding; rewrite it on a surviving
Low-confidence rule.

### Detections

- Write-loss: DATETIME/DATETIME2/string-literal assignment into SMALLDATETIME
  is unhandled — engine rounds to the nearest minute (seconds >= 30 round up),
  confirmed via CAST probe, but WriteLossClassifier only checks
  IsFractionalSecondsFamily targets (Time/DateTime2/DateTimeOffset) and
  target == Date, never SmallDateTime. Needs a new WriteLossKind plus an
  oracle test before shipping.
- Write-loss: DECIMAL-to-DECIMAL narrowing only compares scale
  (NumericFamilyNarrowing ranks Decimal by Scale alone), so a target with
  fewer integer digits than the source at equal scale — e.g. DECIMAL(4,2)
  vs. DECIMAL(5,2) — isn't flagged even though it risks an overflow on
  assignment. Two now-orphaned helpers for this comparison
  (IsDecimalPrecisionNarrowed, IsDecimalIntegerDigitCapacityNarrowed) were
  removed from NumericFamilyNarrowing.cs as dead code; the underlying gap
  is real and needs a proper Classify() rank that accounts for integer
  digit capacity, plus an oracle test, before shipping.
- CASE/COALESCE/IIF type-merge: nullability isn't modeled on SqlType at all
  (the real engine merges a not-nullable flag across CASE branches into the
  result type), but no current rule consumes SqlType nullability — revisit
  only if a rule needs it, don't add speculatively.

---

## Out of scope

Production-only signals — the one real exclusion under CLAUDE.md's scope rule.
Parameter sniffing depends on the runtime data distribution and on which value
first compiled the plan; runtime-only signals (spills, memory grants, execution
frequency, stale statistics, plan-cache duplication, row-estimate mismatch)
don't exist until a query runs. The static risk factors for sniffing ship as
their own findings.

---

## For every new stream

Thresholds are calibrated against the real measured distribution, never copied
from convention. Record the calibration in `detection-reference.md`
Appendix 10.

Before writing (or re-auditing) a rule whose rationale depends on a session
setting, a keyword list, a data-type set, or a fixed option enum: find the
engine's own closed enumeration for it - a ScriptDom enum, a live catalog/DMV
(`sys.dm_os_performance_counters`, `sys.configurations`,
`sys.database_scoped_configurations`), or an internal engine table via
`the reference engine code` (target the function that *consumes* a fixed-size table,
not a decompiled enum's member names - those rarely survive compilation) -
and diff every member against the rule's actual coverage. Free-text sources
(`sys.messages`) don't work for this: there's no reliable way to rank them by
relevance, so don't try. Oracle-verify every surviving candidate before
trusting it - a structurally-missing case is not yet a confirmed gap.
