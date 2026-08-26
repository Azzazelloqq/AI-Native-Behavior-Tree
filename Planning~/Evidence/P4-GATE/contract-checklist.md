# Phase 4 contract checklist

Prepared 2026-08-27 for the `P4-009` review, against candidate commit
`9b9744443d9bbcaa3d4b3341343aeda818a26770`. This is the checklist the gate
verification pass checks, not a separate acceptance record from what each P4
card's own evidence already established.

## Phase 2 gate's six "required before any scheduling claim" items (`P2-GATE/phase4-inputs.md`)

| # | Requirement | Resolution |
| --- | --- | --- |
| 1 | Close `P2-022` first (a scheduler cannot be calibrated against a platform with no Player baseline) | `P2-022` accepted in `P2-025` before Phase 4 started |
| 2 | Build the scenario catalog before the policies | `P4-001`: 6 of 14 catalog scenarios implemented and measured end-to-end; the remaining 8, plus `PipelinedJobs`/`Auto` at catalog time, are documented placeholders in the result JSON, never silently substituted |
| 3 | Implement `PipelinedJobs` and prove semantic equivalence against the reference oracle before measuring it | `P4-003`: `NativePipelinedPhaseControllerV1`, golden-case equivalence matrix; re-run clean in this gate's detached harness (`NativePipelinedPhaseControllerTests`, all passing -- see `verification-results.json`) |
| 4 | Define work estimation as an explicit, inspectable model; `Auto` must explain a decision, not only make one | `P4-004`: `NativeWorkEstimatorV1`/`NativeBatchSizeCalibrationV1`, coefficients recorded with calibration source (recalibrated against real Player data, `P4-004`'s 2026-08-26 addendum); `P4-005`: `NativeAutoSelectionV1` explainability surface scoped to fields with a genuine, verifiable data source |
| 5 | Resolve `OQ-006` with evidence: runtime autotuning must beat calibrated fixed heuristics or is not adopted | `P4-007`: resolved, **rejected** -- `AIBT-013`, `ADR-P4-007-runtime-autotuning-resolution.md` (Accepted 2026-08-21); confirmed linked from `Documentation~/decisions.md` and `Planning~/OPEN_QUESTIONS.md`'s `OQ-006` row (`Resolved`) |
| 6 | Establish regression thresholds only after multiple hardware classes exist, with owner approval | **Deliberately not done.** `P4-008` measured three platforms (Windows x64, Android ARM64 physical device, single-thread Web) but this is one device per platform, not multiple hardware classes per platform; no threshold has been proposed to the owner and `Planning~/USER_ACTIONS.md`'s approval requirement is undischarged by design, not violated |

All six are closed or correctly left open per their own stated precondition (item 6).

## Constraints Phase 4 must not violate (`P2-GATE/phase4-inputs.md`)

| Constraint | Check |
| --- | --- |
| No default, threshold, or crossover point derives from a single workstation | Every P4 card's evidence README explicitly disclaims this; `P4-004`'s 2026-08-26 addendum uses two devices (one Windows workstation, one Android phone) and still explicitly scopes the result to "one device per platform, not a generalization claim" |
| Scheduling may change timing and latency, never tree semantics | `P4-003`'s golden-case equivalence matrix (`PipelinedJobs` vs. `Immediate`) re-run clean in this gate's harness; native/reference golden equivalence inherited unchanged from Phase 2 |
| Every published number records environment, build, warmup, and raw samples | Every `Benchmarks~/Phase4/**/Results/*.json` file has an `environment` block (Unity version, platform, CPU, Burst/64-bit flags) and warmup/measured sample counts |
| `GC.GetTotalMemory` is not a zero-allocation proof; the Unity recorder is | `NativePipelinedPhaseControllerTests.SteadyStatePipelineDrivingIntroducesNoManagedAllocation` uses `GcAllocIs.Not.AllocatingGCMemory()` (Unity's own recorder-based constraint), not `GC.GetTotalMemory` |
| Android device and Safari/mobile Web claims wait for `USER_ACTIONS.md` hardware access | Android: satisfied -- `P4-008` measured on the user's own physical Google Pixel 10 Pro (genuine `arm64-v8a`, confirmed via `adb`). Safari/mobile Web: still not measured, still open (`OQ-004`), correctly disclosed rather than silently skipped |

## Verified from existing Phase 4 evidence

| Gate | Evidence |
| --- | --- |
| Benchmark harness reuses proven native execution unmodified | `Evidence/P4-001/`: `SchedulingPolicyDriver.cs` drives already-compiled `CompiledProgram`s under all three accepted Phase 2 fixed policies |
| `PipelinedJobs` proven semantically equivalent, not merely implemented | `Evidence/P4-003/`: golden-case equivalence matrix; cross-stage latency proven on real multi-round scenarios |
| Fixed-batch-size scheduling overhead is a real, traced, non-flat effect, not assumed | `Evidence/P4-002/`; `Benchmarks~/Phase4/CostCurves/README.md`: `BatchedJobsSameFrame` per-agent cost roughly doubles-to-quadruples from 16 to 1024 agents at fixed batch size 32 |
| Work-estimation/batching model calibrated from real measured data, not assumed constants | `Evidence/P4-004/`: correlation check against real data, both the original 24-Editor-point derivation and the superseding 2026-08-26 real-Player-data addendum (42 points, tolerance re-derived to 0.25 from the real worst case 20.98%) |
| `Auto`'s decisions are deterministic and explainable from a genuine data source | `Evidence/P4-005/`: 24 tests covering every selection branch and determinism against all 6 real `P4-001` scenarios; re-run clean in this gate's harness (`NativeAutoSelectionTests`) |
| `Auto` vs. fixed-policy comparison is reported honestly, including where `Auto` loses | `Evidence/P4-006/`: `Auto` underperforms the best fixed policy in 23 of 24 measured cases, by +188% to +1,774% ns/agent -- not tuned away |
| `OQ-006` resolved on real evidence, not assumption | `Evidence/P4-007/`: adaptive-tracker prototype tested against a realistic single-observer feedback model and found to get permanently stuck on a cold-start mistake; `TrySelect` (shipped) behaviorally unchanged, `TrySelectAdaptive` retained as a disclosed, non-production experiment |
| Real, non-Editor Player evidence exists for all three mandatory pre-1.0 platforms | `Evidence/P4-008/`: Windows x64 IL2CPP/Burst, Android ARM64 IL2CPP/Burst (physical device), single-thread WebGL, each a real non-Development build, not Editor batchmode |
| The Editor-vs-Player calibration gap is a real, disclosed, actionable finding, not silently absorbed | `Evidence/P4-008/`; `Benchmarks~/Phase4/CostCurves/README.md`'s 2026-08-26 addendum: release Player runs ~11-14x faster than Editor batchmode, both overall and per-step; the coefficient was recalibrated in response, not left wrong |
| Full detached-package regression | 1060/1060 EditMode, 0 failed, 0 skipped, this gate's harness; XML SHA-256 `3a4e7e6c58c34b24665c07b5a6379d57feaf906864345bc5626866d6dfb416e5` |
| `P4-003`'s equivalence proof and `P4-005`'s determinism proof re-run against the committed snapshot, not merely cited | Both test fixtures listed individually as `Passed` in this gate's full-suite run -- see `verification-results.json` |
| Clean detached-UPM-harness compile | this gate: exit code 0, see `verification-results.json` |
| Static, schema, and diff hygiene | static 73 work items, 6 schemas, clean working tree at the candidate commit |
| Public API surface unchanged from `P3-GATE` | `public-api.txt`/`.sha256`: 3 assemblies, 382 types, 1994 members -- byte-identical to `P3-GATE`'s own dump; Phase 4 added zero new public API surface |
| Runtime dependency direction unchanged: `Editor` depends on `Authoring`/`Runtime` only, never the reverse | `assembly-dependencies.json` |

## Explicitly disclosed, not silently claimed

| Item | Where disclosed |
| --- | --- |
| Only 6 of 14 catalog scenarios (and no `PipelinedJobs`/`Auto` catalog entries) are measured end-to-end | `Evidence/P4-001/README.md` |
| `Auto` underperforms the best fixed policy in 23 of 24 measured cases | `Evidence/P4-006/README.md` |
| Runtime autotuning was prototyped, tested, and rejected -- not silently omitted | `Evidence/P4-007/README.md`; `ADR-P4-007` |
| Calibration is one workstation + one physical Android phone, not a hardware-class generalization | `Evidence/P4-004/README.md`'s addendum; `Evidence/P4-008/README.md` |
| No regression threshold, scheduling default, or supported-hardware-class claim exists anywhere in the package | Every P4 card's own "Forbidden changes"; confirmed again in `claims-inventory.md` |
| Web coverage is `Immediate`/`Budgeted` only, reduced parameter matrix, and does not exercise Burst-compiled-to-WASM code | `Evidence/P4-008/README.md`; `Benchmarks~/Phase4/Platform/Web/README.md` |
| A real defect (stale correlation-test fixtures) was found and fixed while re-deriving `P4-004`'s tolerance, not swept past | `Evidence/P4-004/README.md`'s addendum; `verification-results.json`'s `FINDING` entry |

No normative contract was relaxed to obtain the verified rows above.
