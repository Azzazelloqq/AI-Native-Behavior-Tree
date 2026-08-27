# Phase 5 claims inventory

Prepared 2026-08-27 for the `P5-010` review, against candidate commit
`42a32eab7953944823401eccb40b8b60a5c94bfd`. Every supported claim below
already has committed evidence.

## Supported claims

- `OQ-007` (what "reload" means for a semantically changed tree with a live
  instance mid-execution) is resolved with evidence, not assumption: reload
  is always construct-fresh-and-selectively-copy by stable authoring node ID,
  never in-place array mutation or copy by compiled index (`P5-001`,
  `ADR-P5-001`, `AIBT-023`).
- A stable authoring `NodeId`, not the DFS-recomputed compiled index, is the
  correct migration key: a plain child reorder shifts both children's
  compiled indices, and a stable-ID-keyed classifier still correctly marks
  them migratable, proven against a real spike and again by
  `HotReloadCompatibilityClassifierTests.Reordering_ChildrenStillMigrate_OnlyParentFlaggedStructuralChange`
  (`P5-001`, `P5-003`).
- The compatibility classifier correctly localizes an incompatible change to
  the smallest necessary restart region, and conservatively escalates a
  Shared-scope blackboard write inside a migrating region to a full-tree
  restart rather than risk an unsound partial migration (`P5-003`, 8 tests).
- Full restart works correctly from any old-instance state, including a
  genuinely active one mid-update, and 50 repeated restart cycles leave no
  growing managed state (`P5-004`, 5 tests).
- Compatible migration actually preserves per-node instance state (memory,
  activation generation, cooldown flags) across a parameter edit -- proven by
  a direct before/after state-snapshot comparison, not merely asserted
  (`P5-005`/`P5-006`, `HotReloadStateMigrationTests.Migrate_PreservesPerNodeInstanceStateAcrossAParameterEdit`).
  It correctly refuses to run against a live (non-idle) old instance and
  falls back to full restart instead of attempting an unsafe mid-flight copy.
- The scheduler's work-estimator is reset, never carried over, across a hot
  reload, by construction (caller-owned, compiled-program-identity-keyed)
  rather than special-cased reload logic -- tested directly (`P5-007`).
- The Editor hot-reload workflow shows the user the actual classification and
  actual strategy chosen for every reload it triggers, and never presents an
  incompatible reload as a silently successful migration -- verified by live
  interactive driving of the real window in the open Unity Editor through all
  three reload strategies in one session, not only headless assertions
  (`P5-008`).
- Compatible migration is measurably, not just theoretically, cheaper than
  full restart: full restart costs ~1.9-2x a compatible migration at the same
  tree size, consistently across three tree sizes (1/6/63 nodes) and on both
  Editor batchmode and a real, non-development Windows x64 Standalone Player
  (`P5-009`).
- At least one hot-reload cost measurement runs on a real, non-Editor Player
  build, per `P4-008`'s own precedent that Editor batchmode numbers can
  differ from real Player numbers and must not be silently treated as
  representative (`P5-009`).
- Unity `6000.5.8f1` compiles `AIBT.Runtime` + `AIBT.Authoring` + `AIBT.Editor`
  as a detached UPM installation and passes 1089 EditMode tests with 0 failed
  and 0 skipped, including every Phase 5 test fixture re-run individually
  against this exact committed snapshot (this gate).
- `Editor` depends on `Authoring`/`Runtime` only, never the reverse; neither
  `Runtime` nor `Authoring` references `UnityEditor`, an MCP assembly, an
  LLM-provider assembly, or `Unity.Entities` (`assembly-dependencies.json`).

## Claims intentionally not made

- Native-backend hot reload. No mechanism exists to reload a live
  native-executor instance; `P5-004` through `P5-006` built the
  reference-executor backend only, disclosed as a real, load-bearing gap
  (`known-limitations.md`), not approximated against the wrong backend.
- Full mid-flight active-frame-stack migration. Migration only runs when the
  old instance is idle; a genuinely active old instance always falls back to
  full restart. Disclosed in `ADR-P5-001`'s implementation addendum, not
  silently narrowed.
- `P5-007`'s golden-equivalence re-run, batch isolation, and `Auto`
  determinism criteria for a hot-reloaded instance. These describe the native
  backend specifically, which has no reload mechanism to test -- disclosed as
  unmet, not faked against the reference-executor backend.
- Debug-instrumentation overhead (trace capture during a reload) has any
  known cost. It was not measured; `HotReloadPreviewDriver` has no trace-sink
  injection point within this phase's allowed changes.
- That reload cost amortizes across a population of live instances. Measured
  evidence shows the opposite: cost is linear in population size, since no
  batched-reload API exists.
- Any performance default, regression threshold, or "acceptable reload cost"
  claim. Every P5 card's own "Forbidden changes" repeats
  `Planning~/USER_ACTIONS.md`'s requirement that such a claim needs the
  owner's explicit approval -- none has been sought or granted.
- That the Editor hot-reload workflow is wired into `Editor/Graph/`'s live
  window, or that it is a second, automatic (e.g. file-watching) reload
  trigger. It is a single explicit "Reload From..." button in its own
  private window, per `P5-008`'s own scope.
- Anything about Phase 1/2/3/4's own runtime, editor, scheduling, or platform
  claims beyond what `P2-GATE`/`P3-GATE`/`P4-GATE` already recorded -- this
  gate does not re-litigate any earlier accepted gate.
- Stable public API compatibility beyond the recorded experimental `0.1.0`
  baseline. Phase 5 legitimately added new public types (see `README.md`'s
  Verdict section); none of them are claimed stable pre-`1.0.0`.
