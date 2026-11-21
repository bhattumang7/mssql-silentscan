# Test shape

Read this whole file before touching a rule's tests. It states where the tests
must end up, what does not count, and the order of work to get there.
`test-check.md` covers how to write each individual test well; this file
covers which tests a rule must have.

## Vision

Every claim SilentScan publishes is checked against SQL Server itself, on the
same statement the scanner judged, by code that cannot borrow the scanner's
own reasoning. If the scanner and the engine ever disagree, a test fails. If
the engine cannot be made to answer, the test fails too. No rule is trusted
because its tests agree with its implementation.

## The three parties

A rule's tests keep three things apart:

- **The scanner** reads SQL and reports findings. It is the thing under test.
- **The verifier** takes one finding and one statement, and asks SQL Server
  whether the finding's claim is true of that statement. It is the judge.
- **The case** is a setup script and a statement. It is the shared evidence
  both of them look at.

The scanner never sees the verifier's answer. The verifier never uses the
scanner's model. The case is the only thing they have in common.

## What a verifier must do

1. **Run the judged statement.** The statement the
   finding points at is executed, by the shared base, not a stand-in built
   from the finding's fields. A case holds exactly one judged statement.
2. **Observe, never infer.** A verdict comes from something the engine did: a
   result that changed, a plan that changed shape, a value that came back
   altered, an operation the engine refused. Catalog metadata may locate the
   object, but it is never the verdict.
3. **Observe the consequence the rule publishes.** The rationale names a
   consequence: a wrong result, lost data, or a plan or performance effect.
   That consequence is what the verifier shows, not the precondition the
   scanner detected.
4. **Perturb one thing.** The verifier changes exactly the condition the rule
   names and compares the statement's behaviour with and without it. The
   difference is the evidence.
5. **Prove the claim applies.** Before judging, the verifier shows the
   statement really touches what the finding names. A claim about something
   the statement never reads is undecided, not refuted.
6. **Give one of three answers.**
   - *Confirmed*: the engine showed the effect.
   - *Refuted*: the engine showed the effect is absent.
   - *Not decided*: anything else, with the reason. Not decided is never
     rounded to either side.
7. **Leave no trace.** Every change is undone, and a test proves the database
   is the same afterwards. A verifier that writes runs only against a
   database the harness created, and refuses any other.
8. **Stand alone.** A verifier may use its finding type, the rule ids, the
   verdict types, the database client and the base library. It may not use
   the scanner, the parser, the type model, or any helper that reaches them.

## What does not count

These pass the pairing checks and prove nothing. Do not write them, and
replace any that exist.

- **A lookup as the verdict.** Asking a catalog view, a property function or a
  dynamic management view whether the flagged condition is present only
  repeats what the scanner read. It confirms the precondition, never the
  consequence. A base class whose whole job is "run one query, 1 means
  confirmed" is this mistake made reusable.
- **The scanner's logic rewritten in SQL.** A verifier that re-derives the
  rule's detection over the catalog is a second copy of the scanner. Both
  copies can be wrong the same way.
- **An empty or unused statement.** If the case's statement is blank, or the
  verifier ignores it, nothing was judged.
- **A probe built only from the finding's fields.** A synthetic query that
  never runs the real statement confirms an engine fact in general, not the
  finding.
- **A silent claim about something else.** The hand-built claim on a silent
  case must be the claim the scanner would have made on that exact statement.
- **A helper that hides the dependency.** Calling into shared verification
  code that itself uses the scanner's model is the same as using it directly.

## What a rule's cases must cover

A rule declares a list of firing cases and a list of silent cases.

- **Firing cases**: the scanner reports the rule, at least one finding
  results, and the engine confirms every finding.
- **Silent cases**: the scanner reports nothing for the rule, and the engine
  refutes the claim the scanner would have made on that same statement.

The lists are complete when:

- every condition the rule's rationale says makes it fire has a firing case;
- every exclusion the rationale promises ("never fires when…") has a silent
  case;
- every kind or variant the rule publishes under its own id has both;
- each silent case differs from a firing case by the one thing that matters,
  so the pair isolates the condition.

The test class holds only cases. It has no assertions of its own; the shared
base makes them, so every rule is held to the same standard.

## What the verifier's own tests must cover

The verifier is judged separately, with hand-built findings:

- each path to Confirmed, Refuted and Not decided is reached by a test;
- a claim about an object the statement never touches is Not decided;
- a failure unrelated to the claim is Not decided, not Refuted;
- the database is unchanged after verification.

These tests are written red first, against the engine, before the verifier
exists or changes.

## Guarantees and what enforces each

Nothing in this document is left to the care of whoever writes the next
verifier. Each requirement below is held by a mechanism that fails the build
or a test. A requirement with no mechanism is not yet met, however carefully
the rule was written.

### The verifier really ran the statement and observed the engine

- **The base owns the execution.** A verifier does not run the statement or
  build a verdict itself. It declares three things: how to apply the one
  perturbation, how to undo it, and which observation to compare (the result
  set, the stored value, or the plan of the judged statement). The shared
  base runs the judged statement before and after the perturbation, takes the
  observation both times, and derives the verdict from the difference.
- **Verdicts cannot be made by hand.** Only the base can construct a
  Confirmed or Refuted verdict. A verifier that tries breaks the build.
- **The statement-swap control.** For every firing case the base hands the
  same finding to the verifier with a different statement: the paired silent
  statement, and a statement that never touches the named object. Neither may
  come back Confirmed. A verifier that ignores the statement, or answers from
  a lookup, confirms both and fails.
- **The perturbation control.** For every firing case the base also runs the
  observation twice with no perturbation applied. The two must match. This
  proves the difference seen comes from the perturbation and not from the
  statement varying on its own.
- **The harness is itself tested.** A deliberately bad verifier of each kind
  under "What does not count" exists in the tests, and the base must reject
  each one.

### The cases cover what the rule claims

- **Conditions are data, not prose.** Each rule lists its firing conditions
  and its exclusions as named entries in the rule catalog. The published
  rationale is generated from those entries, so the text and the list cannot
  disagree.
- **Every entry has a case.** Each case names the entry it proves. A test
  fails for any firing condition with no firing case, any exclusion with no
  silent case, and any case naming an entry that does not exist.
- **Every silent case names its firing partner.** The two share a setup and
  differ in the statement, so the pair isolates one condition and the
  statement-swap control has something to swap.
- **Agreement is checked in both directions.** On every case, the scanner
  reporting and the engine confirming must coincide. A scanner that reports
  where the engine refutes fails, and a scanner that stays silent where the
  engine confirms fails.
- **Every case is also run in other shapes.** The base rewrites each judged
  statement through a shared set of meaning-preserving forms (inside a stored
  procedure, behind a view, in a common table expression, in a derived table,
  with aliases and bracketed names, as dynamic SQL) and requires the same
  agreement on each. A form a rule genuinely cannot support is declared as an
  exclusion in the catalog, with its own silent case; it is never skipped.
- **A miss found in the wild becomes a case first.** Any disagreement found
  outside the tests is added as a failing case, and if its shape is general,
  as a new shared form, before the scanner is changed.

### The answer holds on every engine

- **Every case runs on every engine.** The base runs each case against every
  local instance and every compatibility level the product supports. All must
  agree with the scanner.
- **A version difference is declared.** If the engine's behaviour differs by
  version or compatibility level, the rule's catalog entry says so, the
  scanner honours it, and the cases prove both sides.
- **No engine, no pass.** An unreachable instance fails the run. Oracle tests
  are never skipped, filtered or marked inconclusive.

### Pairing and isolation

- Every published rule has a paired oracle, or is named in the baseline of
  rules not yet migrated.
- The baseline only shrinks. A new rule is never added to it.
- Every oracle has a verifier, and every verifier has an oracle and a
  published rule.
- A rule trait names a rule-id constant, never a literal.
- A verifier that references anything outside its allowed set breaks the
  build.
- The number of oracle cases that actually executed is checked against the
  number declared, so a case that silently did not run fails the run.

Each mechanism has a test that proves it fires and one that proves it stays
quiet.

### What no test can guarantee

Two limits remain and must not be described as covered:

- A statement shape that is neither written as a case nor produced by a
  shared form is unchecked until someone meets it.
- An engine version that is not installed locally is unchecked.

Everything else in this document is enforced. If a requirement here turns out
to have no mechanism behind it, building that mechanism comes before
migrating another rule.

## Work order

Only one session works on this at a time. The baseline, its recorded size and
the shared test base are single files; two sessions editing them overwrite
each other. If the tree has uncommitted verifier or oracle work you did not
write, stop and ask before changing anything.

### Step one: bring the base up to this document

Check each item against the code. Do the ones that are not yet true, each
with its own red test first, before migrating any rule. Skip the ones already
true.

1. **Several cases per side.** `RuleOracle` takes a list of firing cases and a
   list of silent cases, each silent case carrying its own claimed finding
   and naming its firing partner, and reports which case failed.
2. **Every mechanism under "Guarantees and what enforces each".** Go through
   that section line by line. For each mechanism not present in the code,
   write the test that proves it fires and the test that proves it stays
   quiet, then build it. This includes the base owning execution and
   verdicts, the statement-swap and perturbation controls, the rejected bad
   verifiers, conditions as catalog data with the coverage test, agreement in
   both directions, the shared statement forms, every engine and
   compatibility level, and the executed-case count.
3. **A narrow allowlist.** The analyzer check on verifiers allows named types
   from the verification project (the verdict types, the verifier base, the
   connection options), not the whole namespace. Any shared helper a verifier
   may call is itself held to the same allowlist.
4. **A write guard.** A verifier that changes data or settings refuses to run
   unless the target database was created by the harness, and a test proves
   the refusal.
5. **The judged statement, not the whole script.** When a case could hold more
   than one statement, the verifier runs the one at the finding's location.
6. **Oracle tests run where the build is trusted.** Confirm the oracle
   category is not filtered out of the run that gates a change.

### Step two: undo the pairings that do not count

Find every verifier that falls under "What does not count". For each rule it
covers, either write a real verifier now, or delete the verifier and its
oracle class and put the rule back in the baseline. Remove a lookup-only base
class once nothing uses it. A rule sitting honestly in the baseline is better
than a rule falsely marked as paired.

### Step three: migrate rule by rule

Work one rule family at a time, in this order:

1. Rules that produce a wrong result or lose data. Their consequence is a
   value or a row set, which is the easiest thing to observe.
2. Rules whose consequence is a plan shape. The observation is the plan XML
   of the judged statement, with and without the condition.
3. Rules about engine decisions over time or across sessions (locking,
   caching, ordering, estimates). These need the most care to observe
   reliably.
4. Rules about configuration or schema design. Each must show its consequence
   on a statement that suffers it; if it cannot, see "When no observation
   exists".

For each rule:

1. Read the rule's rationale and list every firing condition and every
   promised exclusion.
2. Read the existing oracle tests for the rule. They already hold probes that
   show the engine effect; that observation is the starting point for the
   verifier.
3. Read how the engine really behaves, from the reference code first and the
   live instance second, as `CLAUDE.md` requires. Decide what observable
   difference proves the claim and what single perturbation produces it.
4. Write the verifier's tests red, then the verifier.
5. Write the oracle class: one case per item on the list from step 1.
6. Remove the rule from the baseline and lower the recorded size to match.
7. Once the paired oracle covers everything an older oracle test for the rule
   proved, delete the older test. Keep any older test that proves something
   the paired cases do not, and say what in its name.
8. If the engine refutes a firing case or confirms a silent one, the scanner
   or the rationale is wrong. Fix that; never bend the case or the verifier
   to agree.

Building and testing is slow. Finish a whole family before building, run only
that family's tests while working, and run the full suite and the Sonar scan
before each commit. Commit one family at a time.

### When no observation exists

If, after reading the engine's behaviour, no difference can be produced on a
real engine for a rule's claim, the rule has no provable claim. Do not pair
it with a lookup. Record what was tried, then raise it for deletion as
`CLAUDE.md` requires. Leave it in the baseline until that is decided.

## Done means

For a rule:

- it is out of the baseline;
- every firing condition and every exclusion in the rationale has a case;
- no case passes on Not decided;
- the verifier observes the published consequence on the judged statement;
- the verifier's verdict paths are each covered and it leaves no trace;
- the build's pairing and isolation checks pass without exemptions.

For the whole effort:

- the baseline is empty and the baseline mechanism is removed;
- nothing under "What does not count" exists in the tree;
- every remaining oracle test is either a paired oracle or names the extra
  fact it proves.
