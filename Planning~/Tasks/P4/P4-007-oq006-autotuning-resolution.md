# P4-007 — `OQ-006` resolution: runtime autotuning evaluation

Status: `Done`

## Objective

Resolve `OQ-006` ("decide whether runtime autotuning beats calibrated fixed heuristics") with evidence, per `Documentation~/benchmarks.md`'s "Scheduler research" steps 5-6. This card's scope is conditional on `P4-006`'s results and its outcome is not predetermined: closing `OQ-006` with "fixed heuristics are sufficient, no adaptation ships" is as valid an outcome as shipping a lightweight adaptive mechanism, provided either conclusion is evidence-based.

## Depends on

- `P4-006`.

## Required reading

- `Documentation~/benchmarks.md` ("Scheduler research" steps 5-6 specifically).
- `Planning~/OPEN_QUESTIONS.md` (`OQ-006`'s exact wording and blocking scope: "Auto scheduler finalization").
- `P4-006`'s comparison results.

## Allowed changes

- `Runtime/Scheduling/Native/Auto/` (only if `P4-006` shows meaningful performance is being left on the table; otherwise no runtime code changes at all).
- `Planning~/OPEN_QUESTIONS.md` (marking `OQ-006` resolved).
- `Documentation~/decisions.md` (a new ADR recording the resolution either way).
- `Planning~/Evidence/P4-007/`.

## Forbidden changes

- Prototyping or shipping any runtime adaptation *before* checking `P4-006`'s results against this card's own step-5 gate. If `P4-006` does not show meaningful performance left on the table, this card's only deliverable is the evidence-based closure -- no adaptation code is written.
- Shipping adaptation whose overhead, instability, or unpredictability outweighs its measured improvement (`benchmarks.md`'s own step-6 rejection rule) -- if a prototype is built and fails this bar, `OQ-006` closes as "rejected," not as an accepted feature.
- Any regression threshold or scheduling default derived from a single workstation's autotuning results.

## Deliverables

- A written verdict against `P4-006`'s evidence: either (a) fixed heuristics are sufficient and `OQ-006` closes with no adaptation, or (b) a lightweight adaptation prototype was built, measured, and either accepted (with its own measured overhead/instability bounds recorded) or rejected.
- `Planning~/OPEN_QUESTIONS.md`'s `OQ-006` row updated to `Resolved`, referencing the accepted ADR.
- A new ADR under `Documentation~/decisions/` recording the resolution and its evidence, linked from `Documentation~/decisions.md`.

## Acceptance criteria

- The decision cites specific `P4-006` numbers, not a general impression.
- If adaptation was rejected, the rejection reason (no meaningful gap, or overhead/instability/unpredictability outweighing improvement) is stated with the measurement that supports it.
- If adaptation was accepted, its measured overhead, instability, and unpredictability bounds are recorded, and it does not violate any fixed policy's own semantic guarantees (no reordering, no silently changed latency).
- `OQ-006` is not left open after this card completes; "inconclusive" is not an accepted terminal state given `P4-006`'s evidence already exists.

## Required verification

```text
Verify-Static.ps1
review of P4-006 evidence against this card's own decision criteria
if adaptation is built: the same equivalence/allocation/lifetime verification P4-003/P4-005 required, applied to the adaptive path
```

## Handoff notes

- This closes the last item `Planning~/OPEN_QUESTIONS.md` blocks on "Auto scheduler finalization" -- `P4-008`/`P4-009` should not proceed with platform evidence or the Phase 4 gate while `OQ-006` remains open.

## Outcome

**`OQ-006` resolved: runtime autotuning rejected** (`AIBT-013`, `Documentation~/decisions/ADR-P4-007-runtime-autotuning-resolution.md`).
`P4-006`'s gap cleared the step-5 gate to test lightweight adaptation, so a prototype was built:
`NativeAutoPolicyCostTrackerV1` (per-policy bounded-EWMA cost tracking, reusing `P4-004`'s proven
smoothing mechanism) plus `NativeAutoSelectionV1.TrySelectAdaptive`. Tested against a **realistic
single-observer feedback model** (a caller only learns the cost of the policy it actually ran)
using real `P4-002` numbers from one of `P4-006`'s worst-gap cases: across 50 simulated rounds, the
tracker for the never-chosen policy never receives an observation, so the adaptive comparison
(which needs at least two tracked candidates) never activates — the prototype stays stuck on its
cold-start mistake indefinitely. A control test confirmed the comparison logic itself is correct
once both policies' costs are externally known, isolating the flaw to the missing exploration
mechanism. Adding real exploration would introduce exactly the overhead/instability/unpredictability
step 6 disqualifies. Verdict: fixed heuristics remain the right tool; `P4-006`'s gap is a specific,
nameable defect in `P4-005`'s own decision rule (no real cost comparison before preferring
`BatchedJobsSameFrame`), fixable by deterministic recalibration — legitimate follow-up work, not
built by this card. `TrySelect` (P4-005's shipped entry point) is unchanged in behavior (its 24
tests re-run and pass after a non-behavioral refactor); `TrySelectAdaptive` is retained as the
tested, disclosed experiment, called from nowhere production-facing. Full reasoning in
`Planning~/Evidence/P4-007/README.md` and the ADR.
