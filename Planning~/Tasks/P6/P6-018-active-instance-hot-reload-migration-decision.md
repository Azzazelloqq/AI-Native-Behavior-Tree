# P6-018 — Active-instance hot-reload migration decision

Status: `Draft`

## Objective

Decide whether and how to extend `HotReloadStateMigration` (`P5-005`/`P5-006`) to migrate a live
instance's full active-frame-stack state when that instance is *not* idle, rather than always
falling back to a full restart in that case as it does today.

This card exists because `ADR-P5-001`'s own implementation addendum (see
`MASTER_PLAN.md`'s Phase 5 narrative) found migrating a genuinely active instance's state
substantially larger in scope than the ADR itself anticipated: `ReferenceFrame`'s read-only
`NodeIndex` and extensive per-decorator-type fields make full active-frame-stack migration much
bigger than copying memory/generation arrays, and the owner explicitly decided at the time to ship
**migration only when the old instance is idle**, falling back to full restart otherwise, with
"full mid-flight migration is disclosed follow-up work" recorded as the deferred scope. `P5-007`
separately confirms the scheduler-side half of hot reload has no reference-executor equivalent
gap, so this card is scoped to the reference-executor active-instance case specifically.

## Depends on

- `P5-006` (done -- owns `HotReloadStateMigration`, the mechanism this card would extend).
- `P5-010` (Phase 5 integration gate, done).

## Required reading

- `Runtime/State/Reference/ReferenceFrame.cs` -- the read-only `NodeIndex` and per-decorator-type
  fields the original implementation found blocking; confirm exactly what makes them read-only and
  whether that constraint is fundamental or just how the type happens to be shaped today.
- `Authoring/HotReload/HotReloadStateMigration.cs` (or wherever `P5-005`/`P5-006` landed it) --
  the existing idle-only migration path this card extends, not replaces.
- `Documentation~/decisions/ADR-P5-001-hot-reload-compatibility-model.md` -- the original decision
  and its implementation addendum recording the idle-only scope narrowing; this card's own
  decision must not contradict `ADR-P5-001`'s core model (construct-fresh-and-selectively-copy,
  keyed by stable authoring node ID), only extend what gets copied.
- `Planning~/Evidence/P5-005/` and `P5-006/` -- the original evidence recording exactly which
  state (memory, activation generation, cooldown flags, blackboard values) already migrates, so
  this card's own gap analysis is additive, not a re-derivation.

## Allowed changes

- `Spikes~/ActiveInstanceHotReloadMigration/` (new, disposable) -- proves the recommended design
  against a real, actively-executing (not idle) `ReferenceExecutionMachine` instance, mirroring
  `P5-001`'s own spike-before-ADR methodology.
- `Planning~/Evidence/P6-018/`.
- One proposed ADR (an addendum to `ADR-P5-001`, or a new linked ADR -- decide which during the
  card and record the reasoning).

## Forbidden changes

- Any production change to `Runtime/State/Reference/ReferenceFrame.cs` or
  `Authoring/HotReload/HotReloadStateMigration.cs` -- this card decides on paper; a separate future
  card implements it.
- Weakening the idle-instance migration path's own already-accepted correctness guarantees to make
  the active-instance case easier.
- Concluding "not worth it, keep the idle-only fallback forever" without first spiking a real
  attempt -- a reasoned rejection is an acceptable outcome, but only after the spike, not instead
  of it.

## Deliverables

- A decision on whether full active-frame-stack migration is worth building, and if so, the exact
  mechanism (a new capture/seed pair mirroring `CaptureNodeState`/`SeedNodeState`'s existing
  pattern but covering frame-stack fields, or something structurally different).
- A disposable spike proving the recommended mechanism against a real, actively-executing instance
  (mid-tick, not idle) -- or, if the decision is to reject active-instance migration, a spike
  demonstrating concretely why the attempt fails or costs more than it is worth.
- A proposed ADR (or `ADR-P5-001` addendum) recording the decision and rationale.

## Acceptance criteria

- The spike is run against a real, actively-executing `ReferenceExecutionMachine` instance (mid-
  tick), not an idle one -- the exact condition the original implementation could not cover.
- A regression check confirms nothing in this investigation weakens the existing idle-instance
  migration path's own accepted tests (re-run them unmodified).
- The ADR states plainly what remains out of scope regardless of outcome (the native backend still
  has no hot reload at all, per `P5-004`/`P5-007`'s own disclosed gap -- this card does not change
  that).

## Required verification

```text
Verify-Static.ps1
disposable spike: real ReferenceExecutionMachine mid-tick, live Unity MCP execute_code
regression: existing idle-instance HotReloadStateMigration tests, unmodified, still passing
```

## Handoff notes

- Not required for the Phase 6 integration gate (`P6-012`) -- discovered as cross-phase debt
  during a Phase 6 session, mirroring `P6-013`/`P6-014`/`P6-015`'s own pattern.
- If accepted, a future implementation card applies the ADR to production and updates
  `P5-008`'s `HotReloadWorkflowWindow` to surface the newly-possible active-instance migration
  path instead of always reporting a full restart in that case.
