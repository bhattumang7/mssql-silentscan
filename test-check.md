Rules for each test:

1. CLAIM: one sentence stating the specific engine behavior this test proves (from the class/method doc + the rule it backs — e.g. RuleCatalog/detection-reference.md — not guessed from the test name).

2. MUTATION CHECK: if the behavior were broken/deleted, would this test actually fail? Assert.NotNull, Assert.True(count > 0), and broad Assert.Contains against a large blob are red flags — state what a broken implementation would still make the test print.

3. SELF-MATCH RISK: could the test's own query match itself (its own compiled plan, its own DDL, a leftover row from another test in the same shared instance)? Plans are cached at compile time — a query can see itself.

4. RIGHT ROW, NOT A ROW: if a query can return multiple matching rows (ad-hoc + prepared cache entries, multiple compat-level plans, multiple name-matched objects), does the test pin down which row demonstrates the claim, or just read once and trust it?

5. CONTROL/NEGATIVE CASE: is there a sibling case that should NOT show the effect, checked in the same run/statement (per ForcedParameterizationScanner's same-statement isolation pattern)? Positive-only tests can't rule out "this just always happens here."

6. RIGHT ARTIFACT: for plan-SHAPE claims (operator, seek vs scan, CONVERT_IMPLICIT), does the test read SHOWPLAN_XML/actual plan attributes — not cached SQL text or a catalog view that's merely correlated? Conversion findings: oracle is plan-XML based, never plan-shape based.

7. TEXT-FORMAT BRITTLENESS: for normalized/cached-SQL-text assertions, could exact spacing/casing/punctuation assumptions (e.g. "dbo.T" vs "dbo . T") cause silent over- or under-matching? Verify by actually running one, don't assume existing tests got it right.

New scanners with type/narrowing logic must route through Rules/WriteLossClassifier,
Rules/NumericFamilyNarrowing, TypeInference/ExpressionTypeInferencer (via
Lineage/ScalarExpressionResolver, not directly), or ScopedSqlVisitorBase's own
ResolveColumnFacts/CurrentResolutionContext - never hand-roll a resolveLeaf closure or a
parallel scope-resolution helper. See tests/SilentScan.Tests/Architecture/TypeInferenceConventionTests.cs.

## Fixing any bug behind an engine-behavior claim: two tests, not one

This applies to a fix anywhere in the codebase whose correctness rests on a claim about what
SQL Server itself does - a resolver, a classifier, a scanner, a narrowing/widening rule, a
plan-shape check, anything under `src/SilentScan.Core`. A unit test with a hand-computed
expected value, alone, is not enough - see "Adding red test first" and "Every rule needs an
oracle test" in CLAUDE.md, which this expands on. Add both:

1. A fixture-level extraction/scan test (parse -> catalog -> lineage -> the real extractor or
   scanner for that rule family, e.g. the `Extract(...)`-style helper already used for that
   family) proving the actual static pipeline - not just the one function in isolation - now
   flags (or stops flagging) what it should, with the specific fields (kind/name/type/etc.)
   asserted, not just "a finding exists."
2. A live-database integration test against a real, disposable local SQL Server instance
   (extend that family's existing oracle-test base fixture; tag `Category=Oracle` and
   `Rule=<rule-id>` per CLAUDE.md) that runs the actual SQL and asserts on the real, observed
   result - never a hardcoded/assumed expectation standing in for one. This is what proves the
   engine truly behaves the way the fix assumes, and it must fail if that assumption is wrong.
   Run it against the live instance yourself and read what comes back before writing the
   assertion - do not write the expected value first and hope it matches. Avoid
   literal-foldable predicates (`WHEN 1 = 1`/`WHEN 1 = 0`, `IF 1 = 1`, etc.) when the claim
   depends on runtime branching or type merging - the optimizer can constant-fold to a single
   branch and silently validate a wrong formula; use a predicate the optimizer can't evaluate
   at compile time (e.g. `OBJECT_ID('x') IS NULL`, a real column, a variable).

Do this before considering the bug fixed, not just when asked, and for every fix of this kind -
not only the family of rule currently being worked on.
