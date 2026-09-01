# P7-012 — Native-backend hot reload implementation

Status: `Done`

## Objective

Build the native-backend hot-reload mechanism `P7-011`'s ADR decides, and finish `P5-007`'s own
long-blocked acceptance criteria (golden-equivalence re-run, batch isolation, `Auto` determinism —
all for a hot-reloaded native instance) for the first time, since `P5-004`/`P5-005`/`P5-006` only
ever built the reference-executor backend.

## Depends on

- `P7-011` (the accepted design decision this card implements).

## Required reading

- The accepted `Documentation~/decisions/ADR-P7-011-*.md`.
- `Planning~/Evidence/P7-011/` and its own spike (the proven shape this production implementation
  must match).
- `Planning~/Tasks/P5/P5-007-scheduler-and-backend-interaction.md` (the card whose remaining,
  currently-blocked acceptance criteria this card finally makes assignable).

## Allowed changes

- `Runtime/Execution/Native/`, `Runtime/Compiled/Native/`, `Runtime/State/Native/`,
  `Runtime/Scheduling/Native/` — exactly the areas the ADR names, no wider.
- `Tests/Runtime/NativeExecution/`, `Tests/Integration/NativeRuntime/` (new).
- `Planning~/Evidence/P7-012/`.
- Reopening `P5-007`'s own card to complete its remaining acceptance criteria once this card's own
  mechanism exists (per that card's own Handoff notes) — coordinate rather than duplicate its
  acceptance criteria here.

## Forbidden changes

- Any change to an accepted Phase 4 policy's behavior, `NativeWorkEstimatorV1`'s calibration
  formula, or `NativeAutoSelectionV1`'s decision rule — `P5-007`'s own Forbidden-changes clause
  applies here unchanged.
- Reopening `ADR-P5-001`'s accepted reload model or `P7-011`'s own accepted design without a new
  escalation.

## Deliverables

- The native-backend hot-reload mechanism (full restart at minimum; subtree restart and compatible
  migration if `P7-011`'s ADR proved them), built into the areas the ADR names.
- `P5-007`'s own deliverables, finally completed: golden-equivalence re-run for every accepted
  policy against a hot-reloaded native instance; proof a reload of one instance does not disturb
  sibling instances sharing a batch/worker pool; proof `Auto`'s selection for a reloaded native
  instance is deterministic and explainable the same way `P4-005` proved for a freshly constructed
  one.

## Acceptance criteria

- Every accepted policy's golden-equivalence proof (inherited from `P4-003`/Phase 2) is re-run
  against a hot-reloaded native instance and passes unchanged.
- A batch containing one reloaded native instance and several untouched instances produces
  bit-identical results for the untouched instances, proven directly.
- No new performance default, threshold, or "acceptable reload overhead" claim is introduced — this
  card verifies correctness; a future benchmark card (mirroring `P5-009`) measures native-backend
  reload cost if one is assigned.
- Regression: the full existing native-execution/scheduling/hot-reload suites pass unchanged.

## Required verification

```text
Verify-Static.ps1
Run-UnityTests.ps1 -Mode EditMode -Scope Full
golden-equivalence re-run for every accepted policy against a hot-reloaded native instance
batch-isolation proof (reloaded instance does not disturb siblings)
Auto determinism-on-reload proof
```

## Handoff notes

- `Planning~/Tasks/P5/P5-007-scheduler-and-backend-interaction.md`'s own status can move to `Done`
  once this card's evidence satisfies its remaining acceptance criteria — update its own Outcome
  section and `work-items.json` entry accordingly rather than leaving it stranded in `Draft`.

## Outcome

Done. Built `NativeHotReloadInstance`/`NativeHotReloadFullRestart`/`NativeHotReloadStateMigration`
in `Runtime/Execution/Native/HotReload/`, reusing the existing backend-agnostic
`HotReloadProgramIdentityMap`/`HotReloadCompatibilityClassifier` unchanged.

Test-driven verification found a real bug this card's own required verification exists to catch:
`ADR-P7-011`'s "apply the composite-cursor-reset rule to native's `ChildCursor`" framing described
the right symptom but the wrong mechanism. Investigated directly against
`NativeLifecycleMachineV1`'s own dispatch code (not assumed) while a migration test was genuinely
failing: `_frames` is not indexed by compiled node index at all — it is a call stack indexed by
DEPTH, reused across sibling nodes over an instance's lifetime. The first implementation copied
`oldFrames[oldIndex] -> newFrames[newIndex]` by node index (mirroring the P7-011 spike's own
simplified copy), which silently swapped two leaves' live/inactive Frame state the moment more than
one node had ever been active in the same tree. Fixed by copying the active stack position-for-
position by depth and remapping only each frame's own `NodeIndex` field — the same technique
`Spikes~/ActiveInstanceHotReloadMigration/` had already proven for the reference executor (`P6-018`),
generalized here to native. See `Planning~/Evidence/P7-012/README.md` for the full diagnostic trail.

Also finished `P5-007`'s own long-blocked deliverables for the native backend: golden-equivalence
re-run for all 4 accepted policies against a full-restarted instance, a batch-isolation proof, and
an `Auto`-determinism-on-reload confirmation. `P5-007`'s own status moves to `Done` alongside this
card, per its Handoff notes above.

12 new tests, all passing; full EditMode regression (1609 tests) shows no new failures. Evidence:
`Planning~/Evidence/P7-012/README.md`.
