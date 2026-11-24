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

### Precision

`RulePrecisionLedger` records the labelled sample of each measured rule. Every finding is
reported; its confidence label is information, not a filter. Re-measure a rule after
changing it and update its entry.

A finding is a false positive only when the code or schema rules the claimed effect out.
Table size and the values stored today never make a finding false: data grows, so a
consequence the code allows counts as true.

False-positive shapes still to remove:
- function-wrapped-column, column-arithmetic, cast-or-convert-on-column, date-function-on-column,
  expression-derived-column, scan-forced, composite-leading-column-unconstrained: a sargable
  predicate on the same or another column drives the access, the wrapped side is the outer
  side of a join, or the comparison is against another column of the same row.
- key-lookup-prone: the equality covers every key column of a unique or clustered index.
- max-typed-column: the MAX column is never filtered, joined or sorted on.
- unicode-to-non-unicode: varchar text round-trips through an nvarchar function back to varchar.
- float-equality: a stored float compared to a same-typed value with no arithmetic.
- case-expression-missing-else: the CASE is exhaustive over a domain bounded by DATEPART, an
  IN filter or the statement's own WHERE.
- rbar-single-row-loop-dml: the loop is a retry loop that resets its key variable, not one that
  advances it over rows.
- partial-composite-fk: the omitted foreign key column is equated to a constant or variable on the
  referenced table elsewhere in the same statement, so the matched columns identify one row.
- reassigned-parameter: the equality covers every key column of a unique index.
- unresolved-transaction: BEGIN and COMMIT guarded by the same trancount flag, or a reported
  exit that cannot leave the transaction open.
- argument-type-mismatch, length-truncation, under-length-parameter,
  isnull-replacement-value-truncation, approximate-to-exact-truncation, temporal-precision-loss:
  the value's declared source already fits the target (narrow temp-table column, short
  constant, same parameter written to the column, integer-only or date-typed source).
- scalar-udf/nested-under-view-or-tvf: the UDF sits in a view column the caller never projects.
- scalar-udf/in-computed-column-or-constraint: the UDF is only in a DEFAULT constraint.
- dml/self-referencing: the statement is bounded to one row by a key.
- equals-null-comparison: the `= NULL` is in a dead OR branch against NOT NULL columns.
- outer-join-predicate-collapse: the outer-joined side is reached through a non-nullable
  foreign key, so no unmatched rows exist.
- tvf-fence/nested-under-view-or-tvf: the TVF is the only row source.
- filter-column-not-in-index: the filtered index is unique and exists to enforce uniqueness.
- check-constraint-null-not-handled: NULL is handled through COALESCE or a validating function.
- post-expansion-join-width: the claim states no engine consequence by itself.

### Detections

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
