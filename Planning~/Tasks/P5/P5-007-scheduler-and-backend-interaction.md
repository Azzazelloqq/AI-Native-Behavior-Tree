# P5-007 — Scheduler and backend interaction

Status: `Done`

## Objective

Verify and, where a real gap exists, fix how the three reload strategies
(`P5-004`, `P5-005`, `P5-006`) interact with Phase 4's scheduler
(`NativeWorkEstimatorV1`, `NativeAutoSelectionV1`, the four accepted
policies) and with both execution backends. This card does not add a new
reload strategy; it closes the seam between reload and everything Phase 4
already accepted.

## Depends on

- `P5-004`, `P5-005`, `P5-006` (all three reload strategies must exist to
  verify the seam against each).

## Required reading

- `Documentation~/hot-reload.md`'s "Interaction with the scheduler" section.
- `Planning~/Evidence/P4-GATE/phase5-inputs.md`.
- `Documentation~/execution-and-scheduling.md`.

## Allowed changes

- `Runtime/HotReload/` (extending `P5-004`/`P5-005`/`P5-006`'s own areas
  only where the seam requires it -- not `Runtime/Scheduling/Native/`
  itself, per Forbidden changes below).
- `Tests/Runtime/HotReload/` and `Tests/Runtime/NativeExecution/Scheduling/`
  (new cross-cutting tests only).

## Forbidden changes

- Any change to an accepted Phase 4 policy's behavior, `NativeWorkEstimatorV1`'s
  calibration formula, or `NativeAutoSelectionV1`'s decision rule -- this
  card verifies the seam, it does not re-open Phase 4's own accepted
  contracts.
- Introducing any live-adapting scheduling state -- `OQ-006` already
  rejected runtime autotuning; a reload mechanism must not reintroduce it
  through the back door to solve a migration convenience problem.

## Deliverables

- A decision (with tests) for whether a per-instance `NativeWorkEstimatorV1`
  is reset or carried over across each reload strategy, and why -- `P5-001`'s
  ADR left this open per `hot-reload.md`'s own text.
- Proof that a policy's semantic equivalence to the reference oracle
  (inherited from Phase 2/4) still holds for an instance that has been
  hot-reloaded, for every accepted policy (`Immediate`, `Budgeted`,
  `BatchedJobsSameFrame`, `PipelinedJobs`).
- Proof that a full or subtree restart, or a migration, of one instance does
  not disturb another live instance sharing the same worker pool or batch.
- Proof that `Auto`'s selection for a reloaded instance is deterministic and
  explainable the same way `P4-005` proved for a freshly constructed one.

## Acceptance criteria

- Every accepted policy's golden-equivalence proof (inherited from
  `P4-003`/Phase 2) is re-run against a hot-reloaded instance and passes
  unchanged.
- The estimator reset-vs-carry-over decision is justified by a real test
  showing the chosen behavior does not reproduce `P4-006`'s already-known
  `Auto`-underperformance pattern any worse than an equivalent freshly
  constructed instance would.
- A batch containing one reloaded instance and several untouched instances
  produces bit-identical results for the untouched instances, proven
  directly, not assumed.
- No new performance default, threshold, or "acceptable reload overhead"
  claim is introduced -- this card verifies correctness, `P5-009` measures
  cost.

## Required verification

```text
Verify-Static.ps1
Run-UnityTests.ps1 -Mode EditMode -Scope Focused -TestFilter <scheduler-interaction fixture>
golden-equivalence re-run for every accepted policy against a reloaded instance
batch-isolation proof (reloaded instance does not disturb siblings)
```

## Handoff notes

- `P5-008` surfaces this card's estimator-reset decision to the user only if
  it is user-relevant (e.g., a visible warm-up cost after reload); otherwise
  it stays an internal detail.
- `P5-009` benchmarks the actual cost of whatever this card's decisions
  imply (reset vs. carry-over has a real cost difference).

## Outcome

Done. The estimator reset-vs-carry-over decision was made and tested early (reset, never carried
over, since `NativeWorkEstimatorV1` has no persistence of its own and a compiled-program-identity-
keyed caller gets a fresh one automatically after any reload, per `ADR-P5-001`: reload never mutates
in place). Everything else this card asked for was genuinely blocked on native-backend hot reload
not existing yet (`P5-004`/`P5-005`/`P5-006` built the reference-executor backend only) — that gap
is closed by `P7-012`, which finally built `Runtime/Execution/Native/HotReload/` and, alongside its
own mechanism, delivered this card's remaining acceptance criteria for the native backend directly:
golden-equivalence re-run for all 4 accepted policies against a full-restarted instance, a
batch-isolation proof, and an `Auto`-determinism-on-reload confirmation (including a direct
confirmation that a reseeded post-reload `NativeWorkEstimatorV1` matches a never-reloaded one for
the same observations, extending this card's own estimator-reset decision to the native backend
specifically). See `Planning~/Evidence/P7-012/README.md` for the full evidence — recorded there
rather than duplicated here, since the native-backend mechanism and its own verification are that
card's own deliverable.
