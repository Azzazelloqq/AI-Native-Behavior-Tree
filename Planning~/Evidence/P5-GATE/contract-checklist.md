# Phase 5 contract checklist

Prepared 2026-08-27 for the `P5-010` review, against candidate commit
`42a32eab7953944823401eccb40b8b60a5c94bfd`. This is the checklist the gate
verification pass checks, not a separate acceptance record from what each P5
card's own evidence already established.

## Phase 4 gate's constraints Phase 5 must not violate (`P4-GATE/phase5-inputs.md`)

| Constraint | Check |
| --- | --- |
| Node coordinates, colors, groups, and comments never influence semantics or reload decisions | `HotReloadCompatibilityClassifier`/`HotReloadProgramIdentityMap` operate only on `CompiledProgram` (`Nodes`, `DebugMap`, `BlackboardSlots`) -- layout data (`LayoutDocument`) is never read by any Phase 5 type |
| A hot-reload path must not weaken `P3-006`'s "every semantic edit is gated by the real compiler/validator" contract | `HotReloadPreviewDriver.TryCreate`/`TryReload` both call `ReferenceCompiler.Compile` (the same production compiler, `ReferenceCompilationPolicy.Phase1`); an uncompilable document is rejected (`TryReload_RejectsAnUncompilableDocument`), never silently accepted |
| A hot-reload path must not weaken any accepted policy's proven semantic equivalence to the reference oracle | Not applicable to the reference-executor backend Phase 5 built (no scheduling policy is invoked by any hot-reload strategy); the native-backend policies (`Immediate`/`Budgeted`/`BatchedJobsSameFrame`/`PipelinedJobs`) are untouched by any Phase 5 change, confirmed by this gate's assembly-dependency audit finding no Phase 5 file under `Runtime/Scheduling/Native/` |
| A hot-reload path must not introduce a new execution path that bypasses the four accepted policies | Confirmed: hot reload only constructs/discards `ReferenceExecutionMachine` instances; it schedules nothing |
| Calibration state is separate from compiled-program identity, safe to leave untouched or reset across a reload | `P5-007`: **reset, never carried over** -- `NativeWorkEstimatorV1` has no reload-awareness; a compiled-program-identity-keyed caller gets a fresh estimator automatically after any reload, by construction rather than special-cased reload logic |

## Each P5 card's own contract, verified

| # | Requirement | Resolution |
| --- | --- | --- |
| `P5-001` | Resolve `OQ-007` (what "reload" means) with evidence, mirroring `P3-001`/`P4-007`'s ADR pattern | `ADR-P5-001` (`AIBT-023`), Accepted 2026-08-27; backed by real-code findings (`ReferenceCompiler.OrderNodes` index instability, flat live-state arrays, native `AIBT4311` cross-generation rejection) and a 5/5-passing live spike against real `CompiledProgram` pairs |
| `P5-002`/`P5-003` | Build an inspectable node-identity/layout model and a compatibility classifier the reload strategies can consume | `HotReloadProgramIdentityMap` (6 tests), `HotReloadCompatibilityClassifier` (8 tests, including subtree localization and a conservative shared-blackboard-write safety escalation) -- both re-run clean in this gate's detached harness |
| `P5-004` | Safe full restart for the reference-executor backend | `HotReloadFullRestart`, 5 tests including a 50-cycle stress test; native-backend full restart explicitly out of scope (capacity-plan/lease machinery not built) |
| `P5-005`/`P5-006` | Localized subtree restart and compatible active-state migration | Built together as one mechanism (`HotReloadStateMigration`) per `ADR-P5-001`'s own correction; migration scoped to an idle old instance only, a real architectural reduction from full mid-flight migration, disclosed and owner-approved |
| `P5-007` | Scheduler/backend interaction: policy equivalence and estimator behavior across a reload | Estimator reset-vs-carry-over decision made and tested (reset); remaining acceptance criteria (golden-equivalence re-run, batch isolation, `Auto` determinism for a hot-reloaded instance) require the native backend, which this phase does not hot-reload -- disclosed as a real, load-bearing gap, not approximated |
| `P5-008` | Explicit, explained Editor hot-reload workflow | `HotReloadPreviewDriver` + `HotReloadWorkflowWindow`; 5 headless tests plus live interactive driving of the open Editor via Unity MCP through all three reload strategies in one session |
| `P5-009` | Measure reload/migration/compilation cost, real-Player evidence included | `Benchmarks~/Phase5/HotReload/`: full restart costs ~1.9-2x a compatible migration at the same tree size, measured in both Editor batchmode and a real non-development Windows x64 Standalone Player; debug-instrumentation overhead disclosed as out of scope (no trace-sink injection point in the allowed public API) |

## Verified from existing Phase 5 evidence

| Gate | Evidence |
| --- | --- |
| Reload is never in-place array mutation; always construct-fresh-and-copy by stable node ID | `Evidence/P5-001/`; `ADR-P5-001` |
| A plain child reorder is correctly classified migratable despite shifting compiled indices | `Evidence/P5-001/`'s spike; `HotReloadCompatibilityClassifierTests.Reordering_ChildrenStillMigrate_OnlyParentFlaggedStructuralChange` |
| A Shared-scope blackboard write inside a migrating region forces a conservative full-tree restart | `HotReloadCompatibilityClassifierTests.SharedBlackboardWriteInsideCandidateRegion_EscalatesToFullTreeRestart` |
| Full restart works from any old-instance state, including a genuinely active one | `HotReloadFullRestartTests.Restart_AbortsAnActiveOldInstance_AndReturnsAFreshWorkingMachine` |
| Migration actually preserves per-node instance state across a parameter edit, not merely claims to | `HotReloadStateMigrationTests.Migrate_PreservesPerNodeInstanceStateAcrossAParameterEdit` (direct `CaptureNodeState` comparison) |
| Migration correctly refuses to run against a live (non-idle) old instance, falling back safely | `HotReloadStateMigrationTests.Migrate_FallsBackToFullRestart_WhenOldInstanceIsActive`; `HotReloadPreviewDriverTests.TryReload_WhileOldInstanceIsActive_FallsBackToFullRestart` |
| An incompatible reload is never silently presented as a successful migration | `HotReloadPreviewDriverTests.TryReload_IncompatibleTypeChange_ReportsIncompatibleRestart`; live-interactive proof in `Evidence/P5-008/README.md` |
| Migration is measurably, not just theoretically, cheaper than full restart | `Benchmarks~/Phase5/HotReload/README.md`: ~1.9-2x on both Editor and a real Windows Player |
| At least one hot-reload measurement runs on a real, non-Editor Player | `Benchmarks~/Phase5/HotReload/Results/hot-reload-benchmark-windows-player-20260827-074542.json` |
| Full detached-package regression | **1089/1089** EditMode, 0 failed, 0 skipped, this gate's harness; XML SHA-256 `537c92ec7c5408c917add8d375447f0144eca4adea3b552be4384c2c1a8b1507` |
| Every Phase 5 test class re-run individually against the committed snapshot, not merely cited | See `gate-runbook.md` step 5 and `verification-results.json` |
| Clean detached-UPM-harness compile | This gate: exit code 0, see `verification-results.json` |
| Static, schema, and diff hygiene | Static: 83 work items, 6 schemas, clean working tree at the candidate commit |
| Public API surface: legitimate new public types, not a smuggled claim | `public-api.txt`/`.sha256`: 3 assemblies, 391 types (+9), 2024 members (+30), additive-only diff against `P4-GATE`'s 382-type/1994-member dump -- see `README.md`'s Verdict section for the full new-type list |
| Runtime dependency direction unchanged: `Editor` depends on `Authoring`/`Runtime` only, never the reverse | `assembly-dependencies.json` |

## Explicitly disclosed, not silently claimed

| Item | Where disclosed |
| --- | --- |
| Native-backend hot reload is entirely out of scope for this phase | `Evidence/P5-004/README.md`, `Evidence/P5-007/README.md`; restated in `known-limitations.md` |
| Migration only runs when the old instance is idle; full mid-flight active-frame-stack migration is not built | `ADR-P5-001`'s implementation addendum; `Evidence/P5-005/`, `Evidence/P5-006/` |
| `P5-007`'s scheduler/batch-isolation/`Auto`-determinism acceptance criteria are unmet, blocked on the native-backend gap | `Evidence/P5-007/README.md` |
| Debug-instrumentation overhead (trace capture during reload) is not measured | `Evidence/P5-009/README.md`; `Benchmarks~/Phase5/HotReload/README.md`'s "Scope and limitations" |
| Reload cost does not amortize across a population of live instances (no batched-reload API exists) | `Evidence/P5-009/`; `Benchmarks~/Phase5/HotReload/README.md` |
| Every hot-reload UI/preview surface is its own private view, not wired into `Editor/Graph/`'s live window | `Evidence/P5-008/README.md`, inheriting the same disclosed limitation every Phase 3 editor card carried |
| No regression threshold, "acceptable reload cost," or supported-reload-scale claim exists anywhere in the package | Every P5 card's own "Forbidden changes"; confirmed again in `claims-inventory.md` |

No normative contract was relaxed to obtain the verified rows above.
