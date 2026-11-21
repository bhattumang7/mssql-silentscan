# Test shape

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

1. **Run the judged statement.** The verifier executes the statement the
   finding points at, not a stand-in built from the finding's fields.
2. **Observe, never infer.** A verdict comes from something the engine did: a
   result that changed, a plan that changed shape, a value that came back
   altered, an operation the engine refused. Catalog metadata may locate the
   object, but it is never the verdict.
3. **Perturb one thing.** The verifier changes exactly the condition the rule
   names and compares the statement's behaviour with and without it. The
   difference is the evidence.
4. **Prove the claim applies.** Before judging, the verifier shows the
   statement really touches what the finding names. A claim about something
   the statement never reads is undecided, not refuted.
5. **Give one of three answers.**
   - *Confirmed*: the engine showed the effect.
   - *Refuted*: the engine showed the effect is absent.
   - *Not decided*: anything else, with the reason. Not decided is never
     rounded to either side.
6. **Leave no trace.** Every change is undone, and a test proves the database
   is the same afterwards. A verifier that writes runs only against a
   database the harness created.
7. **Stand alone.** A verifier may use its finding type, the rule ids, the
   verdict types, the database client and the base library. It may not use
   the scanner, the parser, the type model, or any helper that reaches them.

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

## What the build enforces

- Every published rule has a paired oracle, or is named in the baseline of
  rules not yet migrated.
- The baseline only shrinks. A new rule is never added to it.
- Every oracle has a verifier, and every verifier has an oracle and a
  published rule.
- A rule trait names a rule-id constant, never a literal.
- A verifier that references anything outside its allowed set breaks the
  build.

Each enforcement has a test that proves it fires and one that proves it stays
quiet.

## Applying this to a rule

1. Read the rule's rationale and list every firing condition and every
   promised exclusion.
2. Read how the engine really behaves for each, then decide what observable
   difference proves the claim and what single perturbation produces it.
3. Write the verifier's tests red, then the verifier.
4. Write the oracle class: one case per item on the list from step 1.
5. Remove the rule from the baseline.
6. If the engine refutes a firing case or confirms a silent one, the scanner
   or the rationale is wrong. Fix that; never bend the case or the verifier
   to agree.
7. If no observable difference can be produced on a real engine, the rule has
   no provable claim and is deleted.

## Done means

- The rule is out of the baseline.
- Every firing condition and every exclusion in the rationale has a case.
- No case passes on Not decided.
- The verifier's verdict paths are each covered and it leaves no trace.
- The build's pairing and isolation checks pass without exemptions.
