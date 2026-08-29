# P6-019 — Auto scheduler heuristic recalibration

Status: `Draft`

## Objective

Fix the specific, named defect `P4-006`/`P4-007` identified in `NativeAutoSelectionV1.TrySelect`'s
decision rule -- it unconditionally prefers `BatchedJobsSameFrame` for same-frame-required large
workloads without any real cost comparison against the other fixed policies -- via deterministic
recalibration of the rule's own thresholds, using `P4-002`'s real cost-curve data.

This card exists because `P4-007`'s own conclusion (`ADR-P4-007`, `OQ-006` resolved: runtime
autotuning rejected) drew a sharp, explicit line the master plan's own Phase 4 narrative records:
"`P4-006`'s gap is a specific, nameable defect in `P4-005`'s own decision rule (no real cost
comparison before preferring `BatchedJobsSameFrame`), fixable by deterministic recalibration as
legitimate follow-up work, not runtime adaptation." `P4-006` measured `Auto` underperforming the
best fixed policy in 23 of 24 cases, by +188% to +1,774% in ns/agent -- a large, real, reported gap
that `P4-007` diagnosed but did not fix, since fixing it was explicitly out of that card's own
scope.

**This card's own output is a performance threshold / Auto scheduler default** -- exactly the
category `DECISION_BOUNDARIES.md` names as "must escalate before implementation" and
`Planning~/USER_ACTIONS.md` requires explicit owner approval for. This card may derive and spike a
recalibrated rule; it may not ship a new default without that approval.

## Depends on

- `P4-005` (done -- owns `NativeAutoSelectionV1`, the file this card recalibrates).
- `P4-006` (done -- the measurement that found the gap).
- `P4-007` (done -- `ADR-P4-007`, which named this exact fix as legitimate follow-up work and
  rejected the alternative, runtime adaptation).

## Required reading

- `Runtime/Scheduling/Native/Auto/NativeAutoSelectionV1.cs` and `NativeAutoContracts.cs` -- the
  real decision rule this card recalibrates; read the exact branch that unconditionally prefers
  `BatchedJobsSameFrame`.
- `Benchmarks~/Phase4/CostCurves/` (`P4-002`'s real measured data, 192 cases) and
  `Planning~/Evidence/P4-006/` (the 24-case `Auto`-vs-fixed comparison this card must improve) --
  the real evidence this card's recalibration must be grounded in, not synthetic.
- `Documentation~/decisions/ADR-P4-007-runtime-autotuning-resolution.md` -- confirms this card's
  own approach (deterministic recalibration) is the one `P4-007` already endorsed, not a
  reopening of the runtime-adaptation question.
- `Planning~/USER_ACTIONS.md` and `DECISION_BOUNDARIES.md`'s "Must escalate" list -- the approval
  gate this card's own output must pass through before any new threshold ships.

## Allowed changes

- `Runtime/Scheduling/Native/Auto/NativeAutoSelectionV1.cs` -- the recalibrated decision rule
  itself, **only after explicit owner approval of the specific new thresholds**, per
  `USER_ACTIONS.md`.
- `Tests/` -- updated/new tests asserting the recalibrated rule's behavior against `P4-002`'s real
  data points.
- `Planning~/Evidence/P6-019/`.

## Forbidden changes

- Shipping any new threshold, coefficient, or default without a recorded, explicit owner approval
  -- this is not a private-implementation judgment call.
- Reopening `OQ-006`/`ADR-P4-007`'s own rejection of runtime autotuning -- this card is
  deterministic recalibration only.
- Deriving new thresholds from anything other than `P4-002`'s/`P4-006`'s own already-measured,
  already-accepted data -- no new benchmark methodology invented here; if the existing data proves
  insufficient to recalibrate confidently, say so and stop rather than fabricating additional
  measurements outside this card's own evidence base.

## Deliverables

- A root-cause-grounded recalibration of `NativeAutoSelectionV1`'s decision rule (real cost
  comparison before preferring `BatchedJobsSameFrame`, using `P4-002`'s real per-scenario/
  per-agent-count cost curves) -- proposed to the owner before implementation, per
  `USER_ACTIONS.md`.
- A re-run of `P4-006`'s own 24-case `Auto`-vs-fixed comparison methodology (same scenarios, same
  agent counts, same fixed-policy baselines) against the recalibrated rule, reporting the new
  win/loss count and magnitude honestly -- including if it is still not a clean win, per
  `P4-006`'s own "reported honestly rather than tuned away" discipline.
- `P4-005`'s existing 24-branch/determinism test suite re-run unmodified to confirm no other
  selection branch regressed.

## Acceptance criteria

- The recalibrated thresholds were explicitly approved by the owner before being committed to
  `NativeAutoSelectionV1.cs` -- recorded in this card's evidence, not assumed.
- The re-run comparison uses `P4-006`'s own real scenarios/agent-counts/fixed-policy baselines,
  not a new, easier benchmark set chosen to make the result look better.
- `P4-005`'s and `P4-006`'s own prior evidence files are not edited to retroactively match the new
  result -- this card's own evidence records the *before* (23/24 underperforming) and *after*
  numbers side by side.

## Required verification

```text
Unity EditMode: P4-005's existing 24-branch/determinism suite, unmodified, still passing
Re-run of P4-006's own 24-case comparison methodology against the recalibrated rule
Verify-Static.ps1
```

## Handoff notes

- Not required for the Phase 6 integration gate (`P6-012`) -- discovered as cross-phase debt
  during a Phase 6 session, mirroring `P6-013`/`P6-014`/`P6-015`'s own pattern.
- This is the one card in this batch most likely to need a synchronous conversation with the owner
  before any code is written, given the explicit threshold-approval requirement -- do not treat
  "Draft -> Ready" as sufficient authorization to pick numbers unilaterally.
