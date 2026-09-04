# AIBT master plan

This plan is the coordination entry point for human and AI contributors.

## Required reading order

1. Repository `AGENTS.md`.
2. `Documentation~/specifications/conventions.md`.
3. Normative specifications relevant to the assignment.
4. `Planning~/AGENT_WORKFLOW.md`.
5. `Planning~/DECISION_BOUNDARIES.md`.
6. `Planning~/DEFINITION_OF_DONE.md`.
7. The assigned work-item card under `Planning~/Tasks/`.

Do not begin from the roadmap alone. Roadmap items are not implementation authorization.

## Source priority

When instructions conflict, use this order and report the conflict:

1. explicit current user instruction;
2. accepted decisions and normative specifications;
3. assigned work-item card;
4. architecture and scope;
5. roadmap and explanatory documentation.

An implementation task cannot silently amend a higher-priority source.

## Delivery strategy

```text
P0 toolchain + verification entrypoints
        |
        v
P1 semantic vertical slice
        |
        +--> platform/CI evidence and P0 evidence gate
        |
        v
P2 data-oriented Burst runtime
        |
        +------> P3 editor/layout
        |
        +------> P4 benchmark scheduler research
        |
        v
P5 hot reload -> P6 MCP/AI -> P7 production hardening
```

Phase 1 intentionally produces a correct reference executor before performance specialization. Phase 2 must preserve the Phase 1 behavior cases. Editor and MCP consume the same authoring/compiler contracts rather than defining alternatives.

## Work-item states

- `Draft`: insufficiently specified or blocked by an unaccepted decision.
- `Ready`: dependencies are complete and the card is assignable.
- `In Progress`: one owner is actively implementing it.
- `Review`: implementation and handoff exist; a self-verification pass against the task card and `DEFINITION_OF_DONE.md` is pending before `Done`.
- `Blocked`: a concrete unresolved dependency prevents progress.
- `Done`: acceptance criteria and Definition of Done were satisfied and verified.

`Planning~/work-items.json` is updated directly as each task completes.

## Phase gates

### Phase 0 gate

- Exact Unity editor and modules are available.
- Package imports and empty assemblies compile.
- Repeatable verification commands exist.
- Windows CI design is functional.
- Android build smoke is proven.
- Unity Web/Burst WASM spike has an accepted backend decision.

### Phase 1 gate

- Canonical JSON round-trips deterministically.
- Invalid documents return stable structured diagnostics.
- Reference executor obeys lifecycle, memory/reactive composite, and budget contracts.
- Behavior cases execute the same observable semantics across reference modes.
- End-to-end sample passes from JSON through validation, compilation, execution, and assertions.
- No normative contract was weakened to satisfy implementation.

The Phase 2 gate is decomposed in `Planning~/Tasks/P2/` and `work-items.json`. Later phase gates remain work-package summaries until their prerequisite implementation evidence exists.

## Current assignable frontier

The Phase 1 semantic slice and local platform evidence are complete. The remote Unity workflow dependency remains unresolved and `P0-005`, `P0-006`, and `P1-019` retain their honest states. By explicit owner direction on 2026-08-14, Phase 2 implementation proceeded from the accepted `P1-018` semantic source without treating that infrastructure gate as passed. Phase 2 is complete: `P2-001` through `P2-025` are done, including Windows Player conformance (`P2-022`) and the Phase 2 integration gate (`P2-025`), accepted 2026-08-18 against commit `a78d10a0fb2f964d64e253b284ad1cf19730f936` — see `Planning~/Evidence/P2-GATE/`. Phase 3 (editor/layout) was decomposed into `P3-001` through `P3-013` on 2026-08-18. `P3-001` (the `OQ-005` graph-framework spike) is done and **rejects Unity Graph Toolkit** on measured evidence (no in-memory/transient graph support — every graph forces a real Unity YAML asset the moment a node is added; zero support anywhere in its public API for groups, comments, sticky notes, or reroutes, all required by `Documentation~/editor-and-layout.md`) — see `Planning~/Evidence/P3-001/`. A second spike, `P3-014`, was decomposed and run the same day and **recommends adopting `UnityEditor.Experimental.GraphView`**: serialization control and testability pass (standalone construction, no asset backing forced, headless `EditorWindow` hosting succeeded), extensibility mostly passes (native `Group`/`StickyNote` types; reroutes need a custom element on `Edge`, following Shader Graph/VFX Graph precedent), large-graph construction performance shows no red flag, and a support-risk reflection check found zero `[Obsolete]`-attributed members in the installed `6000.5.8f1` Editor — see `Planning~/Evidence/P3-014/`. `AIBT-012` was accepted by explicit owner direction on 2026-08-18: adopt `UnityEditor.Experimental.GraphView`, rejecting Unity Graph Toolkit — see `Documentation~/decisions/ADR-P3-001-editor-graph-framework.md` and `Documentation~/decisions/ADR-P3-014-editor-graph-framework.md`, both linked from `Documentation~/decisions.md`. `P3-002` (layout model v1 contract) is done: `Documentation~/specifications/editor-layout-v1.md` defines `*.aibt.layout.json` — see `Planning~/Evidence/P3-002/`. `P3-003` (graph adapter foundation, read-only) is done: a `GraphView`-backed read-only adapter over `TreeDocument` — see `Planning~/Evidence/P3-003/`. `P3-004` (deterministic auto-layout service) is done: a post-order tidy-tree algorithm producing canonical `.aibt.layout.json` bytes — see `Planning~/Evidence/P3-004/`. `P3-005` (manual organization and layout persistence) is done: groups/notes/reroutes extend `LayoutDocument`, a strict `Newtonsoft.Json`-based reader/writer round-trips them, and `Editor/Organization/` adds pin/group/note/reroute operations, undo/redo, and load/save persistence (which now also resolves `P3-004`'s auto-layout fallback at the persistence layer) — see `Planning~/Evidence/P3-005/`. `P3-006` (semantic graph editing) is done: `Editor/Editing/` adds add/remove/connect/disconnect/set-parameter operations gated by the real `ReferenceCompiler`/`TreeValidator` pipeline (no separate weaker validation), plus undo/redo — see `Planning~/Evidence/P3-006/`. None of `P3-004`/`P3-005`/`P3-006` are wired into `Editor/Graph/`'s live UI (out of their allowed scope; flagged as a follow-up each time). `P3-007` (layout/semantic isolation proof) is done: an automated test proves every manual-organization action and auto-layout leave the compiled program's content hash unchanged, and that a genuine semantic edit does change it — see `Planning~/Evidence/P3-007/`. `P3-008` (validation UX) is done: `Editor/Validation/` classifies any diagnostic to a Document/Node/Field graph location and builds a per-severity summary, always recomputed fresh from current diagnostics — see `Planning~/Evidence/P3-008/`. `P3-009` (editor preview via reference oracle) is done: `Authoring/Execution/ReferencePreviewDriver.cs` (new public facade in `AIBT.Authoring`, an explicit owner-decided deviation from this card's allowed-changes scope to cross the `AIBT.Editor`/`AIBT.Runtime` internals-visibility boundary — see the card's Outcome section) drives the accepted Phase 1 `ReferenceExecutionMachine` as-is, and `Editor/Preview/BehaviorTreePreviewWindow.cs` provides step/play/pause/breakpoint controls and live active-node highlighting over a private `BehaviorTreeGraphView` instance; verified both by an automated step-sequence parity proof against a raw oracle machine and by live interactive driving of the open `6000.5.8f1` Editor via Unity MCP — see `Planning~/Evidence/P3-009/`. Preview is fixed to the same Phase 1 fixture/built-in node-behavior set the headless behavior-case runner already exercises (no production per-project leaf-registration mechanism exists yet in AIBT) and is not wired into `Editor/Graph/`'s live window, matching the same disclosed scope boundary as `P3-004` through `P3-008`. `P3-010` (native execution debugger attachment) is done: research before implementation found no production Play-mode host component anywhere in AIBT (nothing drives a native lifecycle machine during Play mode, and no production code wires a native trace channel to a live pass at all), so the card's "attach to a running native executor in Play mode" premise had nothing to attach to; by explicit owner direction on 2026-08-19 (via `AskUserQuestion`, `Planning~/Evidence/P3-010/README.md`'s Decision section) scope was narrowed to proving the attach/detach/read protocol against a self-driven native pass, mirroring `P3-009`'s own-instance pattern, with the missing Play-mode host disclosed as a known limitation rather than silently built or silently assumed. `Editor/Debugger/NativeExecutionDebuggerSession.cs` attaches read-only to a caller-owned `NativeTraceChannelOwnerV1` (no new assembly-boundary facade needed here, unlike `P3-009` — every native trace type is already public); verified by 5 automated tests (including a real Burst job, an allocation-neutral proof, and a byte-for-byte detach-is-unaffected proof) and live interactive driving of the open `6000.5.8f1` Editor via Unity MCP — see `Planning~/Evidence/P3-010/`. `P3-012` (large-graph interaction and performance tests) is done: `Tests/Editor/Performance/` proves rendering (P3-003), auto-layout (P3-004), reposition (P3-005), a real compiled add-node round trip (P3-006), and pan/zoom all still work at 240 (matching `P3-001`'s measured spike scale), 500, 1000, and 2000 nodes, with wall-clock/memory numbers recorded in `Benchmarks~/Platform/Editor/`; live interactive driving of the open `6000.5.8f1` Editor via Unity MCP closed the exact live-GUI-interaction gap `P3-001`'s own spike evidence had flagged as unmeasured, showing a real windowed load (2020.4ms at 2000 nodes) markedly costlier than the headless render figure alone (755.6ms) — see `Planning~/Evidence/P3-012/`. Individual organize/edit operations pass a stated (evidence-only, not shipped) usability read at every measured scale; full-view render/re-render/load is explicitly reported as degraded at 1000 and 2000 nodes rather than silently passed, per the card's own record-only scope (regression thresholds remain Phase 4's ownership). `P3-011` (trace views) is done: `Editor/Trace/TraceTimelineModel.cs` replays a `NativeDebuggerTraceView` snapshot (P3-010's own read-only view, never touched or re-read directly) into a per-step active-node history and step/node-correlated diagnostics, and `Editor/Trace/TraceTimelineWindow.cs` provides a scrub slider, live highlighting on a private `BehaviorTreeGraphView` instance reflecting the scrubbed step's actual state, and an explicit degraded banner on channel drops/fault; verified by a scrub-parity proof against an independently hand-replayed oracle, a real bounded-channel overflow test (the channel's own unmodified eviction logic, not a synthetic hook), and live interactive driving of the open `6000.5.8f1` Editor via Unity MCP — see `Planning~/Evidence/P3-011/`. Like `P3-010`, it is scoped to self-driven channels (no production Play-mode host exists yet); `AttachSession` accepts any caller-owned session so it will work unchanged once one does. `P3-013` (the Phase 3 integration gate) is **done: accepted 2026-08-19 against commit `4700b22e4a17de5d8c118c5d22dfb271a04177fc`** — see `Planning~/Evidence/P3-GATE/`. A fresh, otherwise-empty Unity project referencing `com.azzazello.aibt` as a local `file:` UPM package (no other host-project packages present) compiled cleanly and passed the full detached EditMode regression at **953/953**, 0 failed, 0 skipped; the 3 failures repeatedly seen inside the host `Modules` project across every `P3-009`-`P3-012` evidence file did not reproduce, confirming they were host-project noise, not AIBT defects. `P3-007`'s isolation proof re-ran and passed individually against this exact snapshot. Public API surface recorded across all three production assemblies for the first time (`AIBT.Runtime` + `AIBT.Authoring` + `AIBT.Editor`: 382 types, 1994 members) and the assembly dependency audit reconfirmed `Editor` depends on `Authoring`/`Runtime` only, never the reverse. No defect was found while running this gate. **Phase 3 is complete**: `P3-001` through `P3-014` are all `Done`. `phase4-inputs.md` and `phase5-inputs.md` in the gate evidence hand off raw editor-benchmark input and the editor/compiler revision-stability guarantee Phase 5's hot reload will depend on, respectively.

Phase 4 (benchmark scheduler research) was decomposed into `P4-001` through `P4-009` on 2026-08-19, grounded directly in `Documentation~/benchmarks.md`'s six-step "Scheduler research" process, `Documentation~/execution-and-scheduling.md`'s policy/work-estimation/batching/explainability contract, and `OQ-006` (`Planning~/OPEN_QUESTIONS.md`). `P4-001` (benchmark scenario catalog and harness) is **done**: `Tests/Runtime/Benchmarking/SchedulingPolicyDriver.cs` drives N native tree-instance agents under all three accepted Phase 2 fixed policies (Immediate, Budgeted, BatchedJobsSameFrame) from an already-compiled `CompiledProgram`, shared unchanged between an in-project EditMode correctness suite and the isolated Player-benchmark project under `Benchmarks~/Phase4/Scheduling/` (mirroring `Benchmarks~/Phase2/Dispatch/`'s isolated-project technique, including reusing the `AIBT.Runtime.Tests` friend-assembly name rather than widening production `InternalsVisibleTo`). Six of the fourteen catalog scenarios are implemented and measured end-to-end at two agent-count points each; the remaining eight, plus the `PipelinedJobs`/`Auto` policies, are documented placeholders in the result JSON, never silently substituted — see `Planning~/Evidence/P4-001/`. `P4-003` (`PipelinedJobs` executor) is **done**: `Runtime/Scheduling/Native/NativePipelinedPhaseControllerV1.cs` wraps the same unmodified `NativeBatchedLifecycleOwnerV1` P2-019 built, differing only in structurally refusing to complete a round within the same stage it was scheduled (`TryAdvanceStage`-gated), with the resulting delay explicit and caller-queryable via `NativePipelineMetricsV1.StagesElapsed`. Two boundary/interpretation questions were escalated and resolved by explicit owner decision before implementation: this card's Allowed changes were expanded to include `Tests/Integration/NativeRuntime/` (the golden-case equivalence matrix its own acceptance criteria requires lives there, not in the originally-listed area), and single-tree-instance equivalence under `PipelinedJobs` was confirmed identical to `Immediate` in that adapter (nothing to pipeline within one instance's strictly-sequential steps), with genuine cross-stage latency proven separately on real multi-round scenarios — see `Planning~/Evidence/P4-003/`. `P4-002` (fixed-policy cost curves) is **done**: reviewed and promoted `Draft` → `Ready` (no scope gap found, unlike `P4-001`/`P4-003`), then ran `P4-001`'s harness unmodified at a wider agent-count sweep (16/64/256/1024) across all six implemented scenarios and every fixed-policy/parameter combination (192 measured cases). Immediate/Budgeted are flat, population-independent per-agent cost as expected; fixed-batch `BatchedJobsSameFrame` is **not** flat — per-agent cost roughly doubles to quadruples from 16 to 1024 agents because the number of Job-scheduling chunks grows with population at a fixed batch size, concretely demonstrating why `P4-004`'s batch-size calibration is necessary rather than optional. No default, threshold, or recommendation was derived — see `Planning~/Evidence/P4-002/` and `Benchmarks~/Phase4/CostCurves/README.md`. `P4-004` (work-estimation and batching calibration) is **done**: reviewed and promoted `Draft` → `Ready` (Allowed changes already matched its acceptance criteria, no gap), then implemented `NativeWorkEstimatorV1` (smoothed/bounded work estimation — a single spike moves the estimate by at most 12.5% regardless of magnitude, proven with a 100x synthetic spike) and `NativeBatchSizeCalibrationV1` (target/estimate batch-size formula clamped by policy and memory limits, memory always winning conflicts, plus a load-balancing floor). The calibrated coefficient (678.75 ns/atomic-step) is the pooled median of `P4-002`'s 360 Immediate-policy samples; validated directly against all 24 real (scenario, agent count) points from that same data, every one landing within a 10% tolerance set from the real worst case (8.71%), not assumed in advance — see `Planning~/Evidence/P4-004/`. **Addendum (2026-08-26)**: `P4-008`'s platform probes measured real per-step cost directly on Windows and Android Players (61.82 / 58.75 ns/step, within ~5% of each other) and found it ~11x lower than the Editor-batchmode figure the coefficient was originally calibrated from — an Editor-calibrated coefficient shipped into a release build would size native batches roughly 11x too small, reproducing the exact fixed-batch-size scheduling overhead the batching formula exists to prevent. The coefficient was recalibrated to `60.275` (pooled median of all 42 real Player Immediate-policy samples) and `CalibrationTolerance` re-derived to `0.25` from a fresh 42-point correlation check (worst case 20.98%, up from the original 8.71%) — see `Planning~/Evidence/P4-004/README.md`'s addendum. `P4-005` (`Auto` heuristic selection) is **done**: `NativeAutoSelectionV1.TrySelect` deterministically chooses among the four accepted policies from `P4-004`'s work estimate, a caller-supplied policy-capability set (no Web backend exists anywhere in this package to detect real platform capability from), and the full override surface (forced policy, minimum job workload, batch/memory bounds, worker count, update budget, latency mode, update cadence). Before implementation, the explainability surface was escalated and narrowed to fields with a genuine, verifiable data source — `Documentation~/execution-and-scheduling.md`'s full list includes commands/wakeups/deferred-agents and a real per-batch scheduling cost that no existing type in `Runtime/Scheduling/Native/` tracks, a documented gap rather than a faked field. A forced `PipelinedJobs` selection that contradicts the caller's own `LatencyMode` is rejected with a structured diagnostic, same as an unsupported-backend force — the "never does so silently" guarantee is treated as applying regardless of how a policy was chosen. 24 tests cover every selection branch and determinism against all 6 real `P4-001` catalog scenarios — see `Planning~/Evidence/P4-005/`. `P4-006` (`Auto`-vs-fixed comparison) is **done**: measured `Auto` against the three same-frame-capable fixed policies across all 6 implemented `P4-001` scenarios at 4 agent-count points (24 cases, scoped to `LatencyMode=SameFrame` since `P4-001`'s harness was never wired to measure `PipelinedJobs` — an infrastructure gap escalated and resolved by explicit user decision before implementation). **`Auto` underperforms the best fixed policy in 23 of 24 cases**, by +188% to +1,774% in ns/agent, reported honestly rather than tuned away (forbidden by this card) — root cause traced to `Auto`'s decision tree unconditionally preferring `BatchedJobsSameFrame` for same-frame-required large workloads without accounting for `P4-002`'s own finding that fixed-batch-size `BatchedJobsSameFrame` does not amortize at these scales on this workstation. This is real evidence for `P4-007`'s `OQ-006` judgment, though not by itself proof that runtime autotuning (rather than recalibrating `P4-005`'s own fixed thresholds) is the right fix — see `Planning~/Evidence/P4-006/`. `P4-007` (`OQ-006`'s autotuning resolution) is **done**: **`OQ-006` is resolved — runtime autotuning rejected** (`AIBT-013`, `Documentation~/decisions/ADR-P4-007-runtime-autotuning-resolution.md`). A lightweight-adaptation prototype (`NativeAutoPolicyCostTrackerV1` + `NativeAutoSelectionV1.TrySelectAdaptive`, per-policy bounded-EWMA cost tracking) was built per the step-5 gate `P4-006`'s gap cleared, then tested against a realistic single-observer feedback model using real `P4-002` numbers from one of `P4-006`'s worst-gap cases: across 50 simulated rounds the tracker for the never-chosen policy never receives an observation, so the adaptive comparison never activates — the prototype stays stuck on its cold-start mistake indefinitely, and adding real exploration would introduce exactly the overhead/instability/unpredictability step 6 disqualifies. `P4-006`'s gap is a specific, nameable defect in `P4-005`'s own decision rule (no real cost comparison before preferring `BatchedJobsSameFrame`), fixable by deterministic recalibration as legitimate follow-up work, not runtime adaptation. `TrySelect` (P4-005's shipped entry point) is behaviorally unchanged; `TrySelectAdaptive` is retained as the tested, disclosed experiment, called from nowhere production-facing — see `Planning~/Evidence/P4-007/`. `Planning~/OPEN_QUESTIONS.md`'s `OQ-006` row now reads Resolved with no blocking. `P4-008` (platform benchmark evidence) is **done**: built and ran a real, non-development, IL2CPP, Burst-enabled Windows x64 Standalone Player containing `P4-001`'s exact scenario/policy sweep — the first Phase 4 benchmark to run outside Editor batchmode. **Major finding: the release Player runs ~13-14x faster than Editor batchmode**, consistently across every scenario and agent count — every Editor-measured number in `P4-001`/`P4-002`/`P4-005`/`P4-006`/`P4-007`'s evidence understates real release performance by roughly an order of magnitude on this workstation; `BatchedJobsSameFrame`'s traced fixed-batch-size overhead reproduces in the Player too, confirming it is not an Editor artifact. After that pass, checking properly (rather than assuming) found both Android and WebGL Unity modules installed and a Browser pane able to run a real WebGL build — **Web was measured** too (`Immediate`/`Budgeted` only, per this backend's accepted scope), after fixing a real gzip-hosting mismatch and disclosing a genuine browser-timer-resolution limitation. Only an x86_64 system image/AVD was available locally for Android, not satisfying `USER_ACTIONS.md`'s ARM64-device-class requirement, so the user connected their own physical Google Pixel 10 Pro over `adb` (confirmed genuine `arm64-v8a`) instead — **Android ARM64 was measured too**, all three fixed policies, via a real IL2CPP/Burst Android Player with results read through `adb logcat`. Two notable findings there: this Windows workstation is only ~1.1x-1.3x faster than the phone for `Immediate` (far closer than the Editor-vs-Player gap suggested), and `BatchedJobsSameFrame`'s fixed-batch-size overhead reproduces at roughly the same ~18x-23x magnitude on ARM64 mobile silicon as Windows's own ~21x-29x — confirming the mechanism is tied to the scheduling code's interaction with Unity's Job system, not one CPU architecture or OS. **All three mandatory pre-1.0 targets are now measured.** No threshold or default introduced — see `Planning~/Evidence/P4-008/`. `P4-009` (the Phase 4 integration gate) is **done: accepted 2026-08-27 against commit `9b9744443d9bbcaa3d4b3341343aeda818a26770`** — see `Planning~/Evidence/P4-GATE/`. A clean detached UPM harness (fresh project referencing `com.azzazello.aibt` as a local `file:` package, nothing else from the host `Modules` project) compiled cleanly and passed the full detached EditMode regression at **1060/1060**, 0 failed, 0 skipped; the 3 failures repeatedly seen inside the host `Modules` project across every P3/P4 evidence file did not reproduce, confirming host-project noise, not AIBT defects — the same pattern `P3-013` found. `P4-003`'s `PipelinedJobs` equivalence proof and `P4-005`'s `Auto` determinism-on-rerun proof both re-ran and passed individually against this committed snapshot, not merely cited. `OQ-006` confirmed `Resolved: rejected` with `ADR-P4-007` linked and accepted. Public API surface (382 types, 1994 members across `AIBT.Runtime` + `AIBT.Authoring` + `AIBT.Editor`) is byte-identical to `P3-GATE`'s own dump — Phase 4 added zero new public API surface, consistent with its work being entirely internal scheduling/native-execution machinery. `README.md` and `CHANGELOG.md` were found stale (still describing the `P2-025` gate as in-progress and omitting Phase 3/4 entirely) and updated to reflect actual completion, with every updated claim checked against a fresh claims inventory to confirm nothing stronger than evidence was introduced. No performance default, regression threshold, or supported-hardware-class claim is authorized by this gate itself — every P4 card's own `Forbidden changes` repeats `Planning~/USER_ACTIONS.md`'s requirement that thresholds and hardware-class approval come from the owner after the research exists, not from an implementation agent. **Phase 4 is complete**: `P4-001` through `P4-009` are all `Done`. `Planning~/Evidence/P4-GATE/phase5-inputs.md` hands off scheduler-contract stability confirmation Phase 5's hot reload will depend on.

Phase 5 (hot reload) was decomposed into `P5-001` through `P5-010` on 2026-08-27, grounded in a newly-authored `Documentation~/hot-reload.md` (no dedicated hot-reload contract document existed before this decomposition, unlike every other phase, which had its own foundational-commit spec; `hot-reload.md` consolidates the scattered decisions already present in `architecture.md`, `scope.md`, `roadmap.md`, `decisions.md`'s `AIBT-008`, `testing.md`, `benchmarks.md`, and the relevant `specifications/` documents, rather than inventing new ones) plus `OQ-007` (`Planning~/OPEN_QUESTIONS.md`), the one genuinely undecided question both `P3-013` and `P4-009` deliberately left open: what "reload" means for a semantically changed tree with a live instance mid-execution. `P5-001` is a dedicated spike/decision card resolving `OQ-007` via an ADR, mirroring `P3-001`/`P4-007`'s pattern — every later card is blocked on its acceptance, not merely on `P5-001` being `Done`. `P5-002` (node identity/program-version/state-layout hashing) and `P5-003` (compatibility classifier) build the inspectable data model and decision logic the three reload strategies consume. `P5-004` (safe full restart), `P5-005` (affected-subtree restart), and `P5-006` (compatible active-state migration) implement the strategies themselves in increasing order of complexity, each falling back to the previous when its own preconditions cannot be proven. `P5-007` closes the seam against Phase 4's accepted scheduler contract (policy semantic equivalence across a reload, estimator reset-vs-carry-over) without reopening any accepted Phase 4 decision. `P5-008` surfaces reload as an explicit, explained Editor workflow per `architecture.md`'s assignment of hot-reload orchestration to the Editor, inheriting the same "own private view, not wired into `Editor/Graph/`'s live window" disclosed limitation every Phase 3 editor card carried. `P5-009` measures reload/migration/compilation cost with the same no-default-no-threshold discipline every Phase 4 benchmark card used. `P5-010` is the Phase 5 integration gate, mirroring `P2-025`/`P3-013`/`P4-009`'s shape and handing off to Phase 6 (AI and MCP).

`P5-001` is **done**: `OQ-007` is **resolved** — `Documentation~/decisions/ADR-P5-001-hot-reload-compatibility-model.md` (`AIBT-023`, accepted 2026-08-27). Reading the real code first (not just spec prose) found `ReferenceCompiler.OrderNodes`/`IndexNodes` assign compiled node index by a fresh pre-order DFS traversal on every compile — zero stability across recompiles — while every live-state array in both execution backends is flatly indexed by that same unstable index, and the native layer already hard-rejects cross-generation execution by design (`AIBT4311`). **Decision: reload is never an in-place array mutation; it is always construct-fresh-and-selectively-copy, keyed by stable authoring node ID, never by compiled index.** Full restart, subtree restart, and compatible migration are the same mechanism with a different exclusion set (whole tree / localized subtree / empty) — a correction this ADR made to the original `P5-004`/`P5-005`/`P5-006` card split, applied to all three before implementation starts. A disposable spike (`Spikes~/HotReloadCompatibilityModel/`, run live via Unity MCP against real `CompiledProgram` pairs from the real `ReferenceCompiler`, never committed to `Tests/`) proved the classifier against all five `testing.md` categories: **5/5 passed**, including the load-bearing proof that a plain child reorder shifts both children's compiled indices while a stable-ID-keyed classifier still correctly calls them migratable — see `Planning~/Evidence/P5-001/`. `P5-002` (node identity/layout signature, `HotReloadProgramIdentityMap`) and `P5-003` (compatibility classifier, `HotReloadCompatibilityClassifier`, including subtree localization and a conservative shared-blackboard-write safety escalation) are **done**, both proven against real `CompiledProgram` pairs (6 and 8 tests respectively, all passing). `P5-004` (safe full restart) is **done** for the reference-executor backend (`HotReloadFullRestart`, 5 tests including a 50-cycle stress test) — building it surfaced a real bug (a hardcoded abort update ID collided with `ReferenceExecutionMachine`'s strictly-increasing update-ID contract, fixed by making it caller-supplied) and a disclosed gap (the native backend's own fresh-instance construction, a separate capacity-plan/lease subsystem, remains open follow-up work). `P5-005`/`P5-006` (subtree restart and compatible migration) are **done together**, per `ADR-P5-001`'s own finding that they are one mechanism (`HotReloadStateMigration`), not two: state (memory, activation generation, cooldown flags, blackboard values) migrates by stable node ID between old and fresh instances. Building this surfaced a genuinely deeper architectural finding than the ADR anticipated — `ReferenceFrame`'s read-only `NodeIndex` and extensive per-decorator-type fields make full active-frame-stack migration much larger than copying memory/generation arrays — escalated to and decided by the owner: **migration runs only when the old instance is idle**, falling back to full restart otherwise; full mid-flight migration is disclosed follow-up work. This required adding two new `internal` methods (`CaptureNodeState`/`SeedNodeState`) to the already gate-accepted `ReferenceExecutionMachine.cs` — the first Phase 5 change to an existing Phase 1/2 file rather than only new ones, done with explicit owner approval and verified purely additive (zero regressions across the full existing suite, checked immediately after the change). `P5-007` (scheduler and backend interaction) is **partially done**: the estimator reset-vs-carry-over decision is made and tested (reset, never carried over — `NativeWorkEstimatorV1` has no reload-awareness of its own, so a compiled-program-identity-keyed caller gets a fresh one automatically after any reload; reasoned against `P4-006`/`P4-007`'s own finding that a wrong policy choice, once made, is never re-evaluated). **Everything else this card asks for is blocked**: its remaining acceptance criteria (golden-equivalence re-run, batch isolation, `Auto` determinism, all for a hot-reloaded instance) describe native-backend hot-reload specifically — the scheduler and its four policies have no reference-executor equivalent, and `P5-004`/`P5-005`/`P5-006` built the reference-executor backend only. This is disclosed as a real, load-bearing gap, not faked or approximated against the wrong backend — see `Planning~/Evidence/P5-007/README.md`. `P5-008` (editor hot-reload workflow) is **done**: `HotReloadPreviewDriver` (public `AIBT.Authoring`, mirroring `P3-009`'s boundary-crossing pattern) wraps `HotReloadCompatibilityClassifier`/`HotReloadStateMigration` for `HotReloadWorkflowWindow` (public `AIBT.Editor`, `AIBT/Hot Reload Workflow` menu item), presenting the actual classification and strategy chosen rather than a generic success message; verified by 5 headless tests plus live interactive driving of the open `6000.5.8f1` Editor via Unity MCP through all three reload strategies in one session — see `Planning~/Evidence/P5-008/`. Reference-executor backend only, per the user's decision after `P5-007`'s native-backend gap. `P5-009` (hot-reload benchmark evidence) is **done**: `Benchmarks~/Phase5/HotReload/` isolates compile-only, full-restart, compatible-migration, and subtree-restart cost at three tree sizes, measured both in Editor batchmode and in a real, non-development Windows x64 Standalone Player built and run through an isolated project (satisfying the card's explicit real-Player requirement, per `P4-008`'s own precedent). **Key finding: full restart consistently costs ~1.9-2x a compatible migration at the same tree size, on both Editor and Player** — migration is measurably, not just theoretically, cheaper when it applies. A supplementary measurement found reload cost does not amortize across a population of live instances sharing one new document (no batched-reload API exists) — a disclosed real architecture characteristic. Debug-instrumentation overhead was **not measured**, disclosed as a genuine, structurally-grounded gap: `HotReloadPreviewDriver` hardcodes `traceSink: null` with no injection point, and both ways to fix that (an internals-visible test assembly, or a public API change) fell outside this card's own allowed/forbidden-changes fence — see `Planning~/Evidence/P5-009/`. `P5-010` (the Phase 5 integration gate) is **done: accepted 2026-08-27 against commit `42a32eab7953944823401eccb40b8b60a5c94bfd`** — see `Planning~/Evidence/P5-GATE/`. A clean detached UPM harness (fresh project referencing `com.azzazello.aibt` as a local `file:` package, nothing else from the host `Modules` project) compiled cleanly and passed the full detached EditMode regression at **1089/1089**, 0 failed, 0 skipped; every Phase 5 test fixture and `P3-007`'s inherited isolation proof re-ran and passed individually against this exact snapshot, not merely cited. `OQ-007` confirmed `Resolved` via `ADR-P5-001` (`AIBT-023`, Accepted). Public API surface grew from `P4-GATE`'s 382 types/1994 members to **391 types/2024 members, confirmed purely additive by diff** (`HotReloadPreviewDriver`, `HotReloadCompatibilityClassifier`, `HotReloadProgramIdentityMap`, `HotReloadWorkflowWindow`, and supporting types) — unlike Phase 4, which added zero, Phase 5 legitimately needed a public, inspectable classification/identity model and cross-assembly-boundary facades. `README.md` and `CHANGELOG.md` were found stale (still describing Phases 1-4 as complete) and updated, checked against a fresh claims inventory to confirm nothing stronger than evidence was introduced. Two real, disclosed scope reductions are recorded rather than smoothed over: native-backend hot reload does not exist (`P5-004`/`P5-007`'s gap), and compatible/subtree migration only runs against an idle old instance, falling back to full restart for a genuinely active one (`ADR-P5-001`'s implementation addendum). **Phase 5 is complete**: `P5-001` through `P5-010` are all `Done`. `Planning~/Evidence/P5-GATE/phase6-inputs.md` hands off the resolved hot-reload contract and its disclosed native-backend gap that Phase 6 (AI and MCP) will build tooling against.

Phase 6 (AI and MCP) was decomposed into `P6-001` through `P6-012` on 2026-08-27, grounded directly in `Documentation~/ai-and-mcp.md` (the phase's existing normative contract; unlike Phase 5, no new foundational document was needed before decomposition) and `Documentation~/roadmap.md`'s Phase 6 scope. Before decomposing, a scope check against the actual codebase (not the roadmap summary alone) found no MCP scaffolding exists anywhere yet -- Phase 6 is a genuinely new architecture layer, not a wrapper over existing code, unlike every phase since Phase 2. It also found `.aibt/policy.json` and `Schemas~/policy.schema.json` already exist and are already fully enforced by `Authoring/Validation/TreeValidator.cs` since Phase 1 (`ValidatePolicyContract`/`ValidateProjectPolicy`) -- `ai-and-mcp.md`'s "Project policy" requirement needed no new engine, only exposure through the manifest/discovery tools, so no dedicated policy-engine card was created. Two genuinely undecided architectural questions are resolved by dedicated spike/ADR cards before any implementation, mirroring `P3-001`/`P5-001`'s pattern: `P6-001` (MCP transport, hosting-process model, new-assembly dependency shape, and the permission-model taxonomy every later tool declares against) and `P6-002` (the domain-patch/revision/dry-run/diff transaction model `ai-and-mcp.md` requires but no spec file defines yet). `P6-003` (node catalog and project manifest query layer, over the already-accepted `P1-004` registry) depends only on the Phase 5 gate and can proceed independently of both spikes. `P6-004` (domain-patch transaction engine, built on `P3-006`'s existing operations) depends on `P6-002`. `P6-005` (MCP server host, discovery tools, and permission enforcement) depends on `P6-001` and `P6-003` and is the foundation every remaining tool card (`P6-006` authoring, `P6-007` validate/compile/simulate/explain, `P6-008` trace/test/benchmark, `P6-009` node development, `P6-010` custom tool providers) registers against, so none of them stand up a second server or a second enforcement path. `P6-011` (generated agent documentation) depends on `P6-003`, `P6-006`, and `P6-009` as its only data sources, per `ai-and-mcp.md`'s "duplicated hand-maintained catalogs are forbidden" rule. `P6-012` is the Phase 6 integration gate, mirroring `P2-025`/`P3-013`/`P4-009`/`P5-010`'s shape, requiring a real end-to-end MCP-client session proof of the roadmap's own exit criterion before accepting. Every card inherits `Planning~/Evidence/P5-GATE/phase6-inputs.md`'s constraint that no MCP tool may claim native-backend hot reload or Play-mode debugger/trace attach capability that does not exist.

`P6-001` is **done**: `Documentation~/decisions/ADR-P6-001-mcp-transport-and-permission-model.md` (`AIBT-024`, Accepted 2026-08-27) selects an external `dotnet` process on the official C# MCP SDK (stdio transport), bridged to the Unity Editor by a thin, no-SDK-dependency Editor-side listener over a discovery file (`Library/`-based, mirroring Unity's own `Library/EditorInstance.json` shape) -- mirroring this project's own already-installed `com.coplaydev.unity-mcp`'s real architecture (external process + thin Editor listener, confirmed from its own docs) in C# instead of Python, and explicitly rejecting the alternative of vendoring the SDK's DLLs into Unity's Mono domain (a real assembly-conflict risk against the already-vendored `Assets/Plugins/Roslyn/*.dll`). Distribution requires the .NET SDK installed on the user's machine (`dotnet run`, no vendored binary) and setup UX is a to-be-built one-button Editor command that writes the AI client's MCP config and detects (never silently installs) the SDK -- both owner decisions made directly in conversation before the spike ran. All four spike checks passed against real tooling on this workstation: the external server (`Spikes~/McpTransportModel/Server/`) compiled clean first try; the official Anthropic `@modelcontextprotocol/inspector` CLI round-tripped `tools/list`/`tools/call`/`resources/list`/`resources/read` against it; a live-Editor `TcpListener` + discovery file was read and connected to by a genuinely external process (with the discovered `process_id` matching the real `Library/EditorInstance.json`'s own value for the same session); and `dotnet --version` detection plus valid-JSON config writing were proven live from inside the Editor process via Unity MCP `execute_code`. One verification path failed on its first attempt and was disclosed honestly rather than hidden: adding the spike server to this repo's own `.mcp.json` mid-session did not let this Claude Code session's own MCP-client tooling see it, because Claude Code does not dynamically reconnect servers from a mid-session `.mcp.json` edit. The owner was told plainly and, after being asked directly whether MCP had actually been tested, agreed to restart their Claude Code session with the server pre-registered -- the real target client then listed the spike tool, called it, and listed/read the spike resource, matching the Inspector CLI's own earlier results exactly. A permission-category taxonomy (`Read`/`SemanticEdit`/`LayoutEdit`/`CodeGeneration`/`Compilation`/`TestExecution`/`BenchmarkExecution`/`ArbitraryProjectIntegration`, verbatim from `ai-and-mcp.md`) is decided in shape only; `P6-005` implements enforcement. See `Planning~/Evidence/P6-001/`. `P6-003` (node catalog and project manifest query layer) is **done**: `Authoring/Discovery/` wraps the already-built `P1-004` registry and `NodeManifestCanonicalJson` directly (no second catalog formatter) and adds the first production reader for `.aibt/policy.json` (nothing read that file before this card, confirmed by grep). Research before implementation found this card significantly thinner than its own prose stated, and surfaced a new finding outside its own scope: `Editor/Editing/SemanticEditTransaction.cs` already implements a speculative-apply/compile-validate/accept-or-reject-unchanged primitive covering most of `P6-002`'s "safe mutation protocol" shape, and `Runtime/Core/Identity/Revision.cs` is already a working, incremented-by-`SemanticEditOperations` monotonic revision -- `P6-002`'s task card was corrected in the same commit to reflect a narrower real scope (expected-revision precondition and a semantic/layout diff format, not inventing a transaction mechanism from scratch). 13 new tests pass live against the actual Unity `6000.5.8f1` Editor via Unity MCP, plus a 39/39 regression check of the existing `NodeRegistry`/`Editing` suites, with one genuine wrong assumption caught and fixed during test-writing (a freshly built `TreeDocument`'s revision starts at `1`, not `0` -- `TreeDocument`'s own constructor normalizes an unset revision to `1`). See `Planning~/Evidence/P6-003/`.

`P6-002` (domain-patch/revision/dry-run/diff model decision) is **done**: `Documentation~/decisions/ADR-P6-002-domain-patch-revision-and-diff-model.md` (`AIBT-025`, Accepted 2026-08-27) confirms `SemanticEditTransaction.Apply` already provides atomicity and free dry-run for semantic patches (spiked live via Unity MCP `execute_code` against the real Editor: a real `ChildPolicy` violation left the document completely unchanged with real diagnostics; a valid two-operation composition was accepted and correctly diffed), decides an expected-revision precondition wrapper (spiked, correct), and decides purpose-built semantic/layout diff formats. A new structural finding beyond `P6-003`'s own correction: semantic (`TreeDocument`) and layout (`LayoutDocument`) operations are separated at the type level (confirmed by reading every public method signature in `SemanticEditOperations` and `LayoutOrganizationOperations`), so domain patches are decided as two kinds, never unified -- and since `LayoutDocument` carries no revision field at all, its precondition is a computed canonical-JSON content hash (`StableHash.Sha256Hex` over `CanonicalLayoutJsonWriter.Write`) instead of a new persisted field. A real caller-facing fact was found and recorded rather than assumed: a multi-operation semantic patch increments `Revision` once per individual operation, not once per patch (1 -> 3 for a two-operation patch in the spike) -- callers must always use the actual returned revision, never assume a fixed increment. See `Planning~/Evidence/P6-002/`.

`P6-004` (domain-patch transaction engine) is **done**: `Editor/Patching/SemanticPatchTransaction.cs`/`LayoutPatchTransaction.cs` implement `ADR-P6-002` as real production code. A location correction was found and applied before any code was written: the card's original text placed the engine in `Authoring/Patching/`, but `SemanticEditTransaction`/`SemanticEditOperations`/`LayoutOrganizationOperations` are all part of the `AIBT.Editor` assembly (confirmed directly from `Editor/AIBT.Editor.asmdef`'s own reference list), and `architecture.md`'s dependency direction forbids `Authoring/` from referencing `Editor/` at all -- the engine was built in `Editor/Patching/` instead, matching where its own dependencies actually live. 8 new tests pass live against the real Unity `6000.5.8f1` Editor via Unity MCP (atomicity for both semantic and layout patches, revision/hash preconditions rejecting before any operation runs, structured `SemanticDiff`/`LayoutDiff`, dry-run-is-free by construction), plus a 9/9 regression re-run of `P3-007`'s isolation suite and the existing `SemanticEditOperations`/`SemanticEditTransaction` tests -- no regressions. See `Planning~/Evidence/P6-004/`.

`P6-005` (MCP server host, discovery tools, and permission enforcement) is **done**: the first real, running piece of the MCP layer. `MCP~/Server/` (external `dotnet` process, promoted from `P6-001`'s disposable spike) and `MCP/` (`AIBT.Mcp`, the new Unity Editor-only bridge assembly) implement `ADR-P6-001` for real -- three discovery tools (`aibt_get_project_manifest`, `aibt_search_nodes`, `aibt_get_node_contract`) and seven static resources, all backed directly by `P6-003`'s query layer, gated by a real, fail-closed `McpPermissionEnforcer` covering all 8 `ADR-P6-001` categories. The server-bridge split is this card's own design (not part of any prior ADR): a minimal newline-delimited JSON relay protocol, with every real decision -- query computation and permission enforcement alike -- living in the bridge so it stays testable via Unity EditMode the same way every other P6 card has been, rather than needing a second dotnet-project test pipeline. 30 new tests pass, plus a 21/21 regression re-run of `P6-003`/`P6-004`'s suites. Live end-to-end verification against the real permanent server via the official `@modelcontextprotocol/inspector` CLI found and fixed two real bugs invisible to unit tests alone -- a wrong package-root path assumption for static resources (used the parent of `Assets/`, correct for the per-project `.aibt/policy.json`, instead of `Assets/AIBT` itself, where `Schemas~/`/`Documentation~/` actually live), and an MCP resource template that compiled fine but never appeared in `resources/list` (fixed by exposing each of the seven resources as its own concrete method) -- plus confirmed a real environment-variable-passthrough gotcha in the Inspector CLI itself (shell-exported env vars did not reach the spawned subprocess; a `.mcp.json`-shaped `--config`/`--server` file with an explicit `env` map, the same mechanism a real AI client uses, worked reliably). See `Planning~/Evidence/P6-005/`.

`P6-006` (MCP authoring tools) is **done**: `MCP/Authoring/` implements all 11 tools `ai-and-mcp.md`'s Authoring section lists (create tree; add/remove/move/replace/configure nodes; declare/change blackboard keys; extract/inline subtrees; apply a domain-patch transaction; request layout of the affected region), every mutation routed through `P6-004`'s `SemanticPatchTransaction`/`LayoutPatchTransaction`, wired through `McpToolDispatcher.cs` (11 new permission-tagged cases) and relayed by 11 new thin server methods in `MCP~/Server/AuthoringTools.cs`. Building it surfaced no pure `Move`/`Replace`/blackboard-declare/extract-inline operation anywhere (resolved without escalation: new pure functions inside this card's own module, since `SemanticEditTransaction.Apply` accepts any `Func<TreeDocument,TreeDocument>`, not only `SemanticEditOperations`'s own -- `Editor/Editing/` was never touched) and a real atomicity trap in `TreeDocument`'s legacy mutating instance methods (`SetBlackboard`, instance `AddNode`/`ReplaceNodeAt`/`RemoveNodeAt`), avoided by never calling them. **A pre-existing, load-bearing bug was found and escalated to the owner**: `TreeDocument.Revision` is never persisted to `*.aibt.json` (`CanonicalTreeJsonWriter` never writes it; `CanonicalTreeJson.ReadDocument` hard-codes `default` on every parse) -- harmless for `P6-003`'s `get_project_manifest` (never checked there) but load-bearing here, since every MCP call reloads a tree fresh from disk with no live session, so `SemanticPatchTransaction.Apply`'s own revision precondition could never actually detect a concurrent edit between two separate calls. Owner decision: a content-hash precondition (`expectedHash`/`contentHash`), the same fix `ADR-P6-002` already made for `LayoutDocument` -- `ai-and-mcp.md`'s "checked against its Revision" line is now inaccurate for the MCP surface specifically, a documentation correction recommended as follow-up. Two interpretive judgment calls were made and disclosed: "replace" keeps `NodeId`/`Children`, swapping only type/parameters (a full subtree swap composes remove+add in one `apply_domain_patch` call); "extract/inline" is payload-based, not a live subtree reference (no such concept exists anywhere in AIBT yet, and the acceptance criterion's compiled-content-hash round-trip proof could not be satisfied by a live-reference design regardless). Two further real bugs were found live: `ReferenceCompilerOptions.SourceId` needed a relative logical path, not the absolute file path (`AIBT3010`, caught by the first EditMode run); the official Inspector CLI's `--tool-arg key=value` parser mishandles JSON-text argument values (worked around with its own `--tool-args-json` flag). 17 new EditMode tests pass against the real `McpToolDispatcher.Dispatch` entry point, including an extract-then-inline compiled-content-hash round-trip proof (`CompiledContentHasher`, the same mechanism `P3-007`'s isolation proof relies on); a 45/45 regression re-run (Discovery+Patching+Editing) and a 62/62 full re-run after live verification's own domain reload both passed with no regressions. Live end-to-end verification against the real permanent server via the official Inspector CLI and the real Unity bridge exercised a full authoring session (create/add/dry-run-remove/extract/inline/request_layout) and the complete permission-negative matrix (`SemanticEdit`-only and `LayoutEdit`-only rejections, both `AIBT9012`), with all live-created fixture files cleaned up afterward. See `Planning~/Evidence/P6-006/`.

`P6-007` (MCP verification tools: validate/compile/simulate/explain) is **done**: `MCP/Verification/` implements all 4 tools this card owns, each wrapping exactly one already-accepted entry point (`TreeValidator`, `ReferenceCompiler`, `ReferencePreviewDriver`), wired through `McpToolDispatcher.cs` (4 new permission-tagged cases: `validate`/`compile` -> `Compilation`, `simulate` -> `TestExecution`, `explain_diagnostic` -> `Read`) and relayed by 4 new thin server methods in `MCP~/Server/VerificationTools.cs`. Five real gaps surfaced: only 2 of ~12 `DiagnosticCatalog`s in the whole codebase are reachable from a new `AIBT.Mcp` module (no `InternalsVisibleTo` grant exists for it anywhere -- confirmed by inspecting every catalog holder class) -- `explain-diagnostic` honestly reports `catalogReachable: false` for everything outside `TreeValidationDiagnosticCatalog` (`AIBT2010`-`2041`) and `BlackboardDiagnosticCatalog` (`AIBT2001`-`2008`) rather than fabricating or silently widening assembly visibility; `ReferencePreviewDriver` (the only simulation entry point this card may call) exposes no event/completion/resume/abort/step-budget injection API and no caller control over `updateId`/`snapshotRevision`/`treeInstanceId`/`rootSeed` -- `simulate` is scoped to plain `update` steps validated against the driver's own sequential assignment, inheriting the same limitation `P3-009`'s editor preview already has; no existing real `*.aibtcase.json` fixture is actually drivable through `ReferencePreviewDriver` (one names a tree file that does not exist anywhere in the repo, the other uses node types outside the Phase 1 fixture set) -- substituted with `P3-009`'s own proven `success-then-running.aibt.json` fixture, disclosed as a deliberate substitution rather than assumed away; `TreeValidator.Validate` and `.aibt/policy.json`'s own `ProjectPolicySnapshot` (`P6-003`/`P6-005`) are two unrelated types with no existing conversion -- mapped field-by-field, confirmed against `policy.schema.json`'s own declared enums, with `validate` reporting `policyApplied: false` honestly when the file is absent; `P6-006`'s existing diagnostic JSON is a non-canonical, field-dropping hand-rolled writer -- this card's tools use the real `AIBT.Authoring.DiagnosticJson.Serialize` instead (confirmed byte-for-byte identical to a direct `TreeValidator` call by a dedicated test), `P6-006`'s own files left untouched, the inconsistency disclosed as follow-up work. 14 new EditMode tests pass against the real `McpToolDispatcher.Dispatch` entry point (including a byte-for-byte diagnostic parity proof, a compiled-content-hash parity proof, and a simulate trace proof matching `P3-009`'s own established oracle behavior); a 62/62 regression re-run passed with no regressions. Live end-to-end verification against the real permanent server via the official Inspector CLI and the real Unity bridge exercised validate/compile on real trees, simulate against the real project's own `tree.test.preview-success-then-running` fixture, explain-diagnostic, and a permission-negative check, with all live-created fixture files cleaned up afterward. See `Planning~/Evidence/P6-007/`.

`P6-008` (trace/test/benchmark tools) and `P6-009` (node development tools) each depend only on `P6-005` (done); `P6-010` (custom tool providers) depends only on the accepted `P6-001` ADR. All three were assignable in parallel.

`P6-008` was narrowed 2026-08-29, before implementation began, when research found its `trace`/
`compare-trace` half's own premise false: nothing in production or tests anywhere wires a *real*
running native tree's lifecycle steps into a `NativeTraceChannelOwnerV1` -- the only two things
that ever write trace records are synthetic test fixtures never derived from an actual compiled
tree's execution, exactly the gap `Planning~/Evidence/P3-010/README.md` already disclosed and
deliberately left open. That half was spun off into `P6-015` (`Draft`, its own spike/decision
card, mirroring `P3-010`/`P6-013`/`P6-014`'s pattern) rather than built silently inside a
tool-wrapping card. `P6-008` (**now titled** "MCP verification tools: test, benchmark", narrowed
to `run-tests`/`run-benchmark`) is **done**: its own two entry points, `BehaviorCaseRunner`
(`P1-017`) and `SchedulingPolicyDriver`/`SchedulingScenarios` (`P4-001`), turned out to live in
Editor-only Tests assemblies `AIBT.Mcp` cannot reference without violating `architecture.md`'s
dependency direction (`MCP -> Runtime/Authoring/Editor`, never `MCP -> Tests`) -- resolved by
**promoting** the genuinely reusable, test-framework-free logic into the production layers
`AIBT.Mcp` already sits on: the whole `Tests/BehaviorCases/Framework/` tree moved unchanged into
`Authoring/BehaviorCases/`, and `SchedulingPolicyDriver`/`SchedulingScenarios` moved unchanged into
`Runtime/Scheduling/`/`Authoring/Benchmarking/` (the latter promoted out of the previously-uncompiled
`Benchmarks~/Phase4/Scheduling/Unity/` template folder into the main project for the first time).
`Tests/Integration/SemanticSlice/ReferenceBehaviorCaseAdapter.cs` was deliberately **not** promoted
-- it hardcodes test-only `SemanticSliceNodeContracts` registries explicitly documented "do not
ship in production registries" -- a fresh `Authoring/BehaviorCases/AuthoringBehaviorCaseExecutorFactory.cs`
was written instead, reusing `ReferencePreviewFixtureEnvironment`'s own already-accepted (`P3-009`)
production registries, the same Phase 1 fixture/built-in node set `simulate` (`P6-007`) already
uses. Every promotion kept its moved types `internal`, relying on `AIBT.Authoring`'s/`AIBT.Runtime`'s
existing `InternalsVisibleTo("AIBT.Mcp")` grants -- no new public API surface. The isolated
Phase 4 Player-benchmark harnesses (`Benchmarks~/Phase4/{Scheduling,AutoComparison,Platform/*}/`)
had their own per-file special-case copy steps for the two promoted files removed (now redundant
and a duplicate-type risk, since their own existing wholesale `Runtime/`/`Authoring/` copy steps
pick the promoted files up automatically) -- a mechanical fix verified by inspection only, not by
re-running the full Player harness (out of proportion for this card, disclosed as `not run`).
`Authoring/AssemblyInfo.cs` gained one more `InternalsVisibleTo("AIBT.Runtime.Tests")` grant so
those isolated harnesses' own same-named local assembly (an established trick already used for
`AIBT.Runtime`'s own grant) can still see the promoted `SchedulingScenarios`. A real bug was found
live: `UnityEngine.Application.unityVersion`/`platform`/`isBatchMode` are main-thread-only and
throw when called from the MCP bridge's background TCP-handling thread (`AIBT9013`) -- fixed by
using only thread-safe `System.Environment` fields for `run-benchmark`'s environment metadata. 997
regression tests (`AIBT.BehaviorCases.Tests`, `AIBT.Integration.Tests`, `AIBT.Runtime.Tests`,
`AIBT.Editor.Tests`) plus 8 new `Tests/Editor/Mcp/Testing/` tests all pass; live end-to-end
verification against the real permanent `MCP~/Server/` via the official Inspector CLI exercised
`run_tests`/`run_benchmark` (including the placeholder-scenario refusal and the permission-negative
matrix) against real project fixtures. See `Planning~/Evidence/P6-008/`.

A follow-up tech-debt survey the same session (2026-08-29, prompted directly by `P6-008`'s own
work) found seven disclosed limitations across Phase 3-5 evidence that had never been turned into
their own tracked work items -- each already recorded in some card's own "known limitations"/
addendum prose, but none assignable. Six new `Draft` cards were added, split by size per explicit
owner direction ("big ones get their own card, small ones share one"): `P6-016` (no Phase 3/5
Editor tool is wired into `Editor/Graph/`'s live `GraphView` window -- eight tools each disclosed
this independently), `P6-017` (no production per-project leaf-registration mechanism exists --
`P3-009`/`P6-007`/`P6-008` each independently hardcode the same fixed Phase 1 fixture set),
`P6-018` (hot-reload state migration only runs against an idle instance, per `ADR-P5-001`'s own
implementation addendum), `P6-019` (`P4-005`'s `Auto` selection rule has a specific, named defect
`P4-007` diagnosed but did not fix -- recalibration, not runtime adaptation, which `ADR-P4-007`
already rejected), and `P6-020` (`HotReloadPreviewDriver` hardcodes `traceSink: null`, blocking the
debug-instrumentation-overhead measurement `P5-009` disclosed it could not take). The two small
items -- widening 4 more `private` diagnostic-catalog `Catalog` fields to `internal` for
`explain_diagnostic`, and live-verifying (not just inspecting) the 5 isolated Phase 4
benchmark-harness scripts `P6-008` edited -- were bundled into one card, `P6-021`, rather than each
getting a separate spike/decision cycle. None of the six is required for the Phase 6 integration
gate (`P6-012`); all mirror `P6-013`/`P6-014`/`P6-015`'s own pattern of deciding cross-phase debt
on paper before any production change.

`P6-009` (node development tools) is **done**: `MCP/NodeDevelopment/` implements all 6 tools this
card owns (`generate-node`, `preview-node-diff`, `generate-node-tests-and-manifest`,
`analyze-and-compile-node`, `test-node`, `apply-node`) -- the first time in the project's history a
genuinely new custom node is generated, compiled through the real packaged Roslyn analyzer, and
registered end-to-end. `test-node`'s own literal wording assumed a capability that does not exist
(a generic translator from a compiled node's descriptor metadata into the native dispatch-workspace
shape real execution requires -- the only existing example,
`Tools~/Verification/P2/CodeGen/SampleGolden/PublicBurstNodeSampleGoldenTests.cs.txt`, hand-computes
every field offset for one specific known node), spun off into its own `P6-022` decision card
(owner-confirmed, mirroring `P6-008`'s `P6-015` split) rather than built ad hoc; `test-node` is
narrowed to compile-clean + registry-materialization-valid, both real, already-`public` production
checks. Building this card found and fixed a second real gap in shared, `P6-005`-owned bridge
infrastructure (owner-confirmed before touching it): `McpBridgeListener` does not survive a Unity
domain reload by default -- confirmed empirically live (a script write's resulting reload silently
killed the listening TCP port) -- which never surfaced before because every earlier P6 tool only
ever wrote data files that never trigger compilation. Fixed with a new
`MCP/McpBridgeAutoRestart.cs` (`[InitializeOnLoad]` + `SessionState`, verified surviving two real
domain reloads), and `analyze-and-compile-node` was designed as a two-call, instantaneous,
non-blocking start/check pair so no single request can ever be caught mid-reload. Live verification
against the real running Editor drove the full generate-preview-analyze-compile-test-apply gate for
both maintained templates (Condition and Action, the owner's chosen scope over the acceptance
criteria's narrower Condition-only bar) to a real, compiled, registry-searchable applied node each,
catching and fixing two further real bugs along the way (a generated-source namespace access gap,
and an `AIBT5011` one-shard-per-assembly collision when two applied nodes shared no destination
asmdef) -- both confirmed clean on re-verification. 18 new EditMode tests plus a 1015/1015
regression (run twice) all pass; `P6-011`'s only remaining dependency besides `P6-003`/`P6-006`
(both already done) is now satisfied. See `Planning~/Evidence/P6-009/`.

`P6-010` (custom MCP tool provider registration and permission model) is **done**: `MCP/CustomTools/` implements `ai-and-mcp.md`'s "Custom MCP tools" contract via a public `ICustomMcpToolProvider` interface, discovered purely through `UnityEditor.TypeCache` (the only assembly-scanning call in the feature, confined to the Editor-only `AIBT.Mcp` assembly, never player/runtime code) -- no AIBT assembly ever references a consuming project's provider assembly. Research before implementation found a real architectural fork: every existing MCP tool is a compile-time `[McpServerToolType]` method in `MCP~/Server/`, which cannot know a consuming project's custom tool name at its own build time, so `ai-and-mcp.md`'s "a tool declares stable name... JSON input/output schemas" cannot be satisfied by that static pattern alone. A throwaway reflection probe against the installed `ModelContextProtocol 2.2.0` SDK (not assumed from memory) confirmed the SDK's own supported dynamic-tool extension point (`McpServerBuilderExtensions.WithTools`, and `McpServerTool` itself -- public abstract, protected constructor, three abstract members) exists for exactly this case; put to the owner via `AskUserQuestion` (generic passthrough tool vs. real dynamic per-tool registration), who chose **dynamic per-tool registration** -- `MCP~/Server/CustomTools.cs` exposes each discovered custom tool as its own first-class MCP tool with its own real JSON schema, proven live against the official `@modelcontextprotocol/inspector` CLI (`tools/list` showed both sample tools alongside all 28 built-ins). Three further decisions are disclosed rather than silently assumed: custom-tool registration on the external server is a startup-time snapshot, not a live per-`tools/list` refresh (mirrors `ADR-P6-001`'s own "client may need a restart" precedent); `SupportsCancellation` is declaration-only, since the entire bridge wire protocol has no cancellation transport for any tool, built-in or custom; and the "declared `read`, rejected when it attempts a semantic-edit-shaped operation" acceptance criterion is satisfied at the MCP-protocol call boundary (the same boundary every built-in tool's permission enforcement already operates at), proven by a real sample tool that writes a file and a negative test showing the file never comes to exist when the session lacks the tool's declared category -- not by retrofitting a capability check into the public, already gate-accepted `SemanticPatchTransaction`/`LayoutPatchTransaction` APIs, which would be a cross-assembly change outside this card's scope. A real live bug was found and fixed: the SDK requires genuine `CallToolResult.StructuredContent`, not just text content, whenever a tool declares an `OutputSchema` -- caught by the first real Inspector CLI call, not assumed from docs. 11 new EditMode tests pass (4 pure discovery/validation tests, 7 real end-to-end `McpToolDispatcher.Dispatch` tests pulling in a genuinely separate `AIBT.SampleCustomTool` fixture assembly) plus a full host-project EditMode regression (1571/1571 executed, only the same 3 pre-existing failures every recent P6 card's evidence already disclosed as host-project noise, re-confirmed identical before and after a live provider-assembly-removal check proved the MCP server keeps functioning normally with the sample assembly entirely absent). See `Planning~/Evidence/P6-010/`.

`P6-011` (generated agent documentation) is **done**: `MCP/Documentation/` (`AIBT.Mcp` assembly) generates the node catalog, a short workflow guide, recipes, anti-patterns, and a versioned migrations stub into `Documentation~/generated/*.md`, via one explicit `AIBT/MCP/Regenerate Documentation` Editor menu command (never automatic, mirroring `McpBridgeWindow`'s own pattern). A location correction was found before writing code: the card's own text suggests `Authoring/Documentation/`, but the workflow guide's own deliverable ("reflecting the actual registered MCP tools, not an idealized set") needs the real bridge tool-name list, which only exists inside `AIBT.Mcp` -- `architecture.md`'s dependency direction forbids `Authoring/` from referencing `MCP/`, the same reason `P6-004` already relocated its own engine from `Authoring/Patching/` to `Editor/Patching/`. `P6-010`'s own private reserved-tool-name list was promoted into a new shared `MCP/McpBuiltInTools.cs` so there is exactly one real list, not two (re-verified: `P6-010`'s own tests still pass unchanged). A second real finding: the per-tool JSON schemas (`[Description]` text, parameter names) exist only inside the external `MCP~/Server/*.cs` process, genuinely unreachable from Unity-compiled code, and extending that project with a schema-dump mode is `P6-005`-owned and outside this card's allowed paths -- so recipes are generator-emitted static content with tool-call JSON transcribed from the real source, proven correct by live execution against a real MCP client (the same bar the card's own acceptance criterion actually states), which caught and fixed one real discrepancy: the `run_tests` recipe's guessed response shape did not match what the live tool actually returns. A scope correction mirrors `P6-008`/`P6-009`'s own precedent: the card's fourth recipe, "inspect a trace," is not buildable (`P6-008` found no production code wires a real trace channel to a live pass; that work is `P6-015`, still `Draft`) -- substituted with "run a behavior-case test" (`aibt_run_tests` against the real, already-committed `success-then-running.aibtcase.json` fixture) rather than claiming a capability that does not exist. The node catalog's own "matches P6-003 field for field" acceptance criterion is satisfied by construction, not parallel maintenance: each node's section embeds `NodeCatalogQuery.TryGetContract`'s own `JObject` verbatim. 10 new EditMode tests pass (determinism per generator, diff-locality via a real `NodeRegistryBuilder.AddUserExtension` fixture, field-for-field parity against all 11 real built-in nodes, a no-machine-path/no-date scan, and a drift check proving the committed generated files match a fresh regeneration byte-for-byte) plus a full host-project EditMode regression (1581/1581 executed, the same 3 pre-existing unrelated failures every recent P6 card's evidence already discloses, zero new ones). Live end-to-end verification against the real bridge, the real `MCP~/Server/`, and the official Inspector CLI exercised the create-tree/add-node/validate recipe, the run-benchmark recipe, the run-tests recipe (catching the discrepancy above), and a narrower live check of the custom-node recipe's first two steps (the remaining steps are the identical sequence `P6-009`'s own evidence already proved live end-to-end; re-running the full compile/apply here would only create another real generated node for no new information). See `Planning~/Evidence/P6-011/`.

`P6-012` (the Phase 6 integration gate) is **done: accepted 2026-08-31 against commit `97e3501e71534f8de2e063cf74cdf52a36a43d04`** — see `Planning~/Evidence/P6-GATE/`. Before its own official run, the gate's first detached-UPM-harness attempt found two real bugs in `P6-011`'s own test/generator code (a test-only package-root assumption that broke under real UPM `file:` consumption, and a generator that embedded platform-dependent line endings) — both fixed as addendum commits (`c766d50`, `97e3501`) and confirmed passing in both the host project's embedded layout and a detached harness before treating `97e3501` as the real candidate. A clean detached UPM harness (fresh project referencing `com.azzazello.aibt` as a local `file:` package, nothing else from the host `Modules` project) compiled cleanly and passed the full detached EditMode regression at **1224/1224**, 0 failed, 0 skipped (+135 over `P5-GATE`'s 1089 baseline, covering every test Phase 6 added); a genuine cold-start GC-allocation flake in a pre-existing, Phase-6-untouched `P3-010` test was found and confirmed non-reproducing on an immediate warm re-run. Public API surface for the three previously-audited assemblies grew from `P5-GATE`'s 391/2024 to **405 types (+14), 2067 members (+43), confirmed purely additive by diff** (`AIBT.Authoring.NodeCatalogQuery`/`ProjectManifestQuery`/`ProjectPolicySnapshot` from `P6-003`, `AIBT.Editor.Patching.*` from `P6-004`); the genuinely new `AIBT.Mcp` assembly's own public surface (7 types, 29 members) was recorded for the first time as a separate baseline, never audited by any prior gate. A real, live, end-to-end MCP client session against the real permanent `MCP~/Server/` and the real open Editor demonstrated every operation the roadmap's own Phase 6 exit criterion names — discover, create, atomic add/connect, configure (with the resulting `aibt_simulate` trace proving the configured value actually took effect through compilation and execution), validate, compile, the complete generate/preview/test-scaffold/compile/test/apply gate for a genuinely new custom node, and run a real benchmark — **except one: "inspects a trace,"** which does not exist in production per `P6-008`'s own finding (`P6-015`, still `Draft`). This gate's own live session additionally found, for the first time, a second real exit-criterion gap: a custom node the same session had just generated, compiled, tested, and applied via `aibt_apply_node` returned not-found from `aibt_search_nodes`/`aibt_get_node_contract`, because both discovery tools query `NodeRegistryBuilder.CreateWithBuiltIns()`'s hardcoded built-in list, which nothing wires an applied shard into — a concrete, now-proven manifestation of the already-tracked `P6-017` (still `Draft`). Both gaps were disclosed directly to the owner (the trace gap via `AskUserQuestion`, before verification began) rather than smoothed over; the owner's decision was to accept Phase 6 with both gaps explicitly disclosed, mirroring `P5-010`'s own acceptance of Phase 5 with two disclosed scope reductions. A third, smaller finding — `generate_node`'s condition template does not compile for a `Bool`-typed blackboard read — surfaced live and is recorded as a disclosed `P6-009` defect, not fixed by this gate. `README.md` and `CHANGELOG.md` were found stale (still describing Phases 1-5 as complete, and the repository map still naming a nonexistent planned `Tools~/McpServer/`) and updated to reflect actual Phase 6 completion, including both disclosed gaps, checked against a fresh claims inventory afterward. **Phase 6 is complete**: `P6-001` through `P6-012` are all `Done`, with the trace-inspection and node-discoverability gaps explicitly disclosed as scoped-out rather than silently missing. `Planning~/Evidence/P6-GATE/phase7-inputs.md` hands off to Phase 7 (production hardening), restating that native-backend hot reload and a production Play-mode host both remain open and adding what Phase 6 itself contributes.

`P6-013` (`ReferencePreviewDriver` simulation-capability decision) was added 2026-08-28 during a dedicated fix session on 6 owner-confirmed findings from `P6-006`/`P6-007`'s own evidence (see `Planning~/Evidence/P6-006/` and `Planning~/Evidence/P6-007/`'s several 2026-08-28 addenda for the other five, already applied). It was **not** implemented directly: `P6-007`'s `simulate` tool cannot inject events/completions or drive resume/abort/step-budget, and confirming whether `P6-008` would close that gap found it does not (`P6-008` uses entirely different entry points). A deeper look found `ReferenceExecutionMachine` -- the already-accepted engine `ReferencePreviewDriver` wraps -- already implements completions injection, `Resume`, `Abort`, and a caller-supplied `TreeInstanceId` internally; only the facade never surfaces them. Widening a P3-009-owned public API is still a "must escalate" change per `DECISION_BOUNDARIES.md`, so rather than deciding unilaterally, the owner chose to spin this off as its own `Draft` spike/decision card (dependent on `P6-007` and `P3-009`, both done; not required for the `P6-012` gate) instead of deciding the widening question mid-session. See `Planning~/Tasks/P6/P6-013-reference-preview-driver-simulation-capability-decision.md`.

`P6-014` (MCP blackboard Agent/Shared scope decision) was added 2026-08-29, during the same fix
session, as the sixth and last finding (`P6-006`'s blackboard tool rejects non-Tree scope
explicitly). Two investigation passes are recorded in the card itself rather than repeated here:
the first found the "obvious" blockers (`BlackboardScopeContract`, the registered-type-default
catalog) smaller than expected -- a built-in-scalar-only scope needs no catalog access at all, since
Enum32/Registered types are already excluded. The owner approved narrowing to that scope and
proceeding, but a second pass then found a real, deeper blocker the first pass missed:
`TreeValidator.ValidateBlackboardScope` rejects Agent/Shared keys outright unless
`ReferenceCompilationPolicy.SupportsAgentScope`/`SupportsSharedScope` are `true`, and
`ReferenceCompilationPolicy.Phase1` -- the exact policy constant hardcoded by every MCP tool -- has
both `false`. A codebase-wide grep found `supportsAgentScope`/`supportsSharedScope: true` used only
in three test files, never in any production path. Supporting Agent/Shared through MCP would mean
becoming the first production consumer of a capability flag left off everywhere else, under a
policy constant deliberately named `Phase1` -- a materially bigger decision than "widen JSON
parsing," per `DECISION_BOUNDARIES.md`'s escalation rule. The owner deferred this to its own
`Draft` spike/decision card (dependent on `P6-006`, done; not required for the `P6-012` gate)
rather than deciding mid-session. See
`Planning~/Tasks/P6/P6-014-mcp-blackboard-agent-shared-scope-decision.md`.

`P6-014` is **done: not implemented, deferred**. `ADR-P6-014` (`AIBT-029`) found the true picture one
layer harder than either investigation pass placed it: a disposable spike confirmed `TreeValidator`
does respect `SupportsAgentScope`/`SupportsSharedScope` (a custom, non-`Phase1` policy instance makes
it accept an Agent-scope document `Phase1`'s own defaults reject), but `ReferenceCompiler.cs`'s own
separate Tree-scope-only check (`AIBT3012`) does not consult those flags at all -- it is
unconditional. The exact same opt-in policy that satisfies `TreeValidator` still fails compilation,
so a validated Agent/Shared document can never become a `CompiledProgram`, and the runtime-storage
question (whether `ReferenceExecutionMachine` actually executes such a slot) never arises --
`ReferenceBlackboardStorage`'s own matching rejection is unreachable, not merely unexercised. Both
`ReferenceCompiler.cs` and `ReferenceBlackboardStorage.cs` independently say "Phase 1 ... supports
only Tree-scope" in their own diagnostic text, confirming `Phase1`'s naming is a deliberate,
engine-level boundary. MCP's current explicit rejection needs no change; supporting this for real
would require a future engine capability card making the compiler's own check policy-aware, not an
MCP authoring change. See `Planning~/Evidence/P6-014/`.

`P6-021` (MCP diagnostic-catalog accessibility and benchmark-harness housekeeping) is **done**: the
only card in the `P6-013`-`P6-022` tech-debt batch that is mechanical rather than a spike/decision
cycle. Widened `ReferenceCompilerDiagnostics`/`ReferenceExecutionDiagnostics`/
`CommandAsyncDiagnostics`/`BlackboardStorageDiagnostics`'s own `Catalog` fields from `private` to
`internal` (matching `P6-007`'s own 2026-08-28 addendum pattern exactly) so `explain_diagnostic` can
reach all four, proven by 4 new parametrized tests through the real `McpToolDispatcher.Dispatch`
entry point -- no diagnostic code, severity, or field contract changed, only reachability. Also
closed `P6-008`'s own disclosed "not run" gap on the 5 isolated Phase 4 benchmark harness scripts it
mechanically edited: ran 3 of the 5 end-to-end (`Run-SchedulingBenchmark.ps1`,
`Run-AutoComparisonBenchmark.ps1`, both Editor batchmode, plus **`Run-WindowsPlatformBenchmark.ps1`,
which built and ran a real Windows x64 IL2CPP/Burst non-Development Standalone Player** -- the one
that actually satisfies "real Player build produced," since the two Editor-batchmode scripts do not
build a Player at all); `Build-AndroidPlatformBenchmark.ps1`/`Build-WebPlatformBenchmark.ps1` were
not run this session (both need real device/browser access `P4-008` already used once and separately
acquired), disclosed honestly rather than assumed identical from the 3 passing scripts. Full
regression re-run (1585/1585 executed, same 3 pre-existing unrelated failures, zero new). See
`Planning~/Evidence/P6-021/`.

`P6-013` (`ReferencePreviewDriver` simulation-capability decision) is **done, accepted**:
`ADR-P6-013` (`AIBT-026`) decides `ReferencePreviewDriver`'s facade should be widened for
completions injection, resume-with-step-budget, abort, and a caller-supplied `TreeInstanceId` --
all four already implemented internally by the wrapped `ReferenceExecutionMachine`, never exposed.
A disposable spike (`Spikes~/ReferencePreviewSimulationCapability/`, run live via Unity MCP against
the real, unmodified engine) proved all four, and found a real, non-obvious fact along the way:
`ReferenceExecutionMachine.RequestAbort` cannot cancel a *waiting* operation at all (it requires an
already-open update and is rejected once a tick reaches `Waiting`) -- the ADR instead specifies the
`Abort(update, reason, index)` overload, which opens its own fresh update and actually works for
exactly the case a preview caller needs. `rootSeed` and behavior-case-style external "events" are
both rejected as out of scope, confirmed by direct evidence (a literal, already-shipped
`NotSupportedException("Phase 1 reference execution does not consume external events.")`) to be
genuine missing engine capability, not facade gaps this card could resolve. No production file was
touched, per this card's own Forbidden-changes clause; a future, not-yet-numbered implementation
card applies the ADR. See `Planning~/Evidence/P6-013/`.

`P6-015` (native trace production-wiring decision) is **done, accepted**: `ADR-P6-015` (`AIBT-027`)
decides the real-lifecycle-step-to-trace-record translation lives as an external recorder
co-located with whatever already drives `NativeLifecycleMachineV1` (e.g. `SchedulingPolicyDriver`),
hooking its existing `TryAdvance`/`TryCompleteDispatch` call sites additively -- never a change
inside the machine itself -- with a fixed mapping from `NativeLifecycleStepKindV1` (plus dispatch
completion phase) to `NativeTraceEventKindV1`. A disposable spike (`Spikes~/NativeTraceProductionWiring/`,
run live via Unity MCP against the real, unmodified machine and a real `NativeTraceChannelOwnerV1`)
drove a real compiled 3-node tree across two updates and proved the resulting trace reads back
correctly, unmodified, through `NativeExecutionDebuggerSession.TryReadTrace` and
`TraceTimelineModel.Build` (both of their own existing test suites re-run unmodified, still
passing). A real, disclosed finding surfaced along the way: `NativeLifecycleStepResultV1.CompositeExited`
carries no exit status at all (unlike `Completed`'s own `HasRootStatus`/`RootStatus`) -- the root's
own exit is recoverable by deferring it into the following `Completed` step, but a *nested*
composite's own exit is not, confirmed by a second spike test rather than assumed. Left open for a
future implementation card, per this card's own Forbidden-changes clause. See
`Planning~/Evidence/P6-015/`.

`P6-020` (hot-reload debug-instrumentation trace injection decision) is **done, accepted**:
`ADR-P6-020` (`AIBT-028`) decides `HotReloadPreviewDriver.TryReload` gains a purely additive
`internal` overload accepting an optional `IReferenceTraceSink`, resolving `P5-009`'s own two-
candidate question directly -- since the sink type is itself `internal` to `AIBT.Runtime`, a public-
parameter option was never actually available. A future benchmark-owning assembly needs
`InternalsVisibleTo` grants from both `AIBT.Runtime` and `AIBT.Authoring`, mirroring `P4-001`'s own
technique. A disposable spike (`Spikes~/HotReloadTraceInjection/`, run live via Unity MCP against the
real, unmodified `HotReloadStateMigration.Migrate`) proved both branches (compatible migration, and
the internal fallback to `HotReloadFullRestart` for an active instance) correctly forward a caller-
supplied sink, and found a real, disclosed nuance along the way: the sink attaches to the resulting
fresh machine's own future ticks, not the reload procedure's own internal state-capture bookkeeping,
which never calls it at all -- a future benchmark card must measure post-reload ticking cost, not
the reload procedure's own cost, since those are genuinely different things. `HotReloadPreviewDriverTests`
(`P5-008`'s own suite) re-run unmodified, still passing. See `Planning~/Evidence/P6-020/`.

`P6-018` (active-instance hot-reload migration decision) is **done, accepted: build it**.
`ADR-P6-018` (`AIBT-030`) found `ADR-P5-001`'s own implementation addendum's blocking analysis
inaccurate: `ReferenceFrame`'s only read-only property is `NodeIndex` (set once at construction,
which migration needs anyway), not the "extensive per-decorator-type fields" the addendum named --
all 30+ other fields already have normal settable properties. The real gap is structural:
`ReferenceExecutionMachine`'s own frame stack (`_frames`) is `private`, with no accessor at all,
unlike per-node state's existing `CaptureNodeState`/`SeedNodeState`. A disposable spike
(`Spikes~/ActiveInstanceHotReloadMigration/`, run live via Unity MCP against a real, actively-
executing instance -- a `Repeater(count: 3)` wrapping a perpetually-`Running` leaf, 2 real nested
active frames with genuine decorator state) migrated the entire frame stack field-for-field across a
real `Migrate`-category parameter edit (reflection standing in only for the not-yet-built
`CaptureFrameStack`/`SeedFrameStack` accessor pair this ADR specifies, per this card's own
Forbidden-changes clause) and proved the migrated instance kept running correctly under a real
subsequent `Update`, without fault. Scope: only when every node on the active path classifies
`Migrate`; any `IncompatibleRestart`/`Dropped` node on that path still falls back to full restart,
since a coherent traversal path cannot be partially migrated the way idle per-node state can.
`HotReloadStateMigrationTests` (the existing idle-instance suite) re-run unmodified, still passing.
See `Planning~/Evidence/P6-018/`.

`P6-017` (per-project leaf-node registration mechanism decision) is **done, accepted: a real,
buildable capability, deferred to a dedicated future engineering card**. `ADR-P6-017` (`AIBT-031`)
found `ReferenceLeafRegistry`/`ReferenceLeafBinding` already fully general -- no engine change
needed there, `CreatePhase1Fixtures()` is one convenience factory among possible others, mirroring
`P6-014`'s own `Phase1` finding. A disposable spike (`Spikes~/PerProjectLeafRegistration/`, run live
via Unity MCP) ticked a genuinely new leaf type through a real, unmodified `ReferenceExecutionMachine`
alongside the built-ins, then confirmed the real blocker is a deliberately-enforced three-layer wall,
not an unbuilt discovery mechanism: `NodeRegistryBuilder.AddUserExtension` (the only public
registration path) never attaches a `NodeHandlerBindingContract`; that type is itself `internal`;
and `NodeRegistryBuilder`'s own `ValidateBinding` explicitly rejects a `UserExtension` registration
that carries one -- reproduced by direct compile failure (`AIBT3012`), not inferred. `P6-010`'s own
attribute/`TypeCache` discovery pattern is the right template for finding a project's registered
leaves once a public contract exists, but does not solve today's actual blocker, which is that no
such public contract exists yet. `ReferencePreviewParityTests` (`P3-009`'s own suite) re-run
unmodified, still passing. See `Planning~/Evidence/P6-017/`.

`P6-019` (Auto scheduler heuristic recalibration) is **done, implemented, owner-approved**. Per
`P4-006`'s/`P4-007`'s own already-accepted finding (`Auto` underperformed the best fixed policy in
23 of 24 measured cases because `NativeAutoSelectionV1.TrySelect` unconditionally preferred
`BatchedJobsSameFrame` for same-frame-required throughput with no real cost comparison), the owner
approved a specific, presented-before-implementation fix: reorder `TrySelect`'s branches so
`Immediate`/`Budgeted` are tried before `BatchedJobsSameFrame` (demoted, not removed -- it remains
reachable when it is the only same-frame-capable policy supported), grounded entirely in `P4-002`'s/
`P4-006`'s own already-measured cost curves (`Immediate`/`Budgeted` cheaper in 24 of 24 measured
points). No new numeric threshold or coefficient was introduced -- the existing data does not
support deriving a reliable break-even formula, and this card's own text forbids fabricating one.
`P4-007`'s own rejected runtime-autotuning experiment (`TrySelectAdaptive`) was deliberately left
untouched. Re-running `P4-006`'s own 24-case comparison methodology unchanged against the
recalibrated rule: **`Auto` now matches the best fixed policy in 24 of 24 cases, up from 1 of 24**
-- reported honestly, with `P4-005`'s/`P4-006`'s own prior evidence left unedited. Full host-project
EditMode regression: 1586/1586 executed, same 3 pre-existing unrelated failures, zero new ones. See
`Planning~/Evidence/P6-019/`.

`P6-016` (Editor graph-view unification decision) is **done, accepted, tool-by-tool**. `ADR-P6-016`
(`AIBT-032`) found the "eight tools" are not architecturally uniform, contrary to the card's own
framing risk: reading every one of their own directories found only two
(`BehaviorTreePreviewWindow`/`TraceTimelineWindow`) actually own a duplicated `GraphView` worth
unifying; five (`P3-004`/`P3-005`/`P3-006`/`P3-008`/`P3-010`) own no window or view at all -- pure
library code operating on documents directly -- and integrate instead as UI commands calling their
existing APIs, never a view-sharing mechanism; one (`HotReloadWorkflowWindow`) stays standalone by
reasoned exception (its own focused, sequential reload flow genuinely benefits from a dedicated
window). A disposable spike (`Spikes~/EditorGraphViewUnification/`, run live via Unity MCP against
the real, unmodified `BehaviorTreeGraphView`) proved two independent consumers of one shared view
instance see the identical live node objects by reference (not copies), and that a document-operating
tool (`SemanticEditOperations.AddNode`) needs no new mechanism at all -- just calling its existing
API and re-populating the shared view. `BehaviorTreeGraphAdapterTests`/`SemanticEditOperationsTests`
re-run unmodified, still passing. See `Planning~/Evidence/P6-016/`.

`P6-022` (generic native-dispatch test-harness decision) is **done**, closed in a follow-up session
after an earlier attempt left its own required live spike outstanding. `ADR-P6-022` (`AIBT-033`)
decides the translator reads `CanonicalDescriptorJson` (via the already-accepted
`GeneratedShardMetadataMaterializer`) for per-node case/field/binding shape, plus the generated
catalog's own reflected fingerprints for the handshake, and a real, load-bearing finding from the
original session carried through unchanged: `GeneratedFieldEncoding` and
`NativeBurstDispatchFieldEncodingV2` are not numerically aligned (`GeneratedHandle` differs,
`FixedBytes`/`Registered` have no dispatch equivalent at all) -- a naive cast would silently corrupt
dispatch behavior. The isolated-project spike (`Spikes~/GenericNativeDispatchTestHarness/`, a
scoped-down copy of `Build-And-Verify.ps1`'s own "SampleUnityProject" stage) was then run to
completion and passed 1/1, driving a real, unmodified `ExecuteImmediate` through a workspace shape
built entirely from compiled metadata, matching `PublicBurstNodeSampleGoldenTests`'s own
independently hardcoded `ThresholdCondition` result. Getting there surfaced a second real,
load-bearing finding beyond the original session's own two: `NativeBurstDispatchWorkspaceOwnerV2.
TryCreate`'s validation requires a workspace's `Cases` array to be positionally self-consistent
starting at index 0, and that same index is forwarded verbatim into the real generated
`ExecuteImmediate`'s own dispatch switch -- so the real, two-node `Samples~/BurstNodes` sample's
own `ThresholdConditionNode` (real dispatch index 1, after `AsyncWriteActionNode`) cannot be
isolated into a single-case shape at all without also translating `AsyncWriteActionNode`'s
explicitly-out-of-scope async bindings. The spike proved the translator instead against a dedicated,
disposable single-node shard built for exactly this purpose (necessarily assigned dispatch index 0),
a legitimate proof of the translator itself but not of extracting an arbitrary node from the middle
of a pre-existing multi-node catalog -- disclosed as a real, load-bearing scope boundary for whichever
future card widens `P6-009`'s `test-node` into production, rather than smoothed over. See
`Planning~/Evidence/P6-022/`. **Phase 6 is now complete**: `P6-001` through `P6-022` are all `Done`.

`P0-005`, `P0-006`, `P1-019`, and `P5-007` were re-checked on 2026-08-31 against a real, live
GitHub Actions API query (not assumption) rather than assumed stale or superseded by later gates.
**All four remain genuinely, correctly open, not stale bookkeeping.** `P0-005`/`P0-006`/`P1-019`
cascade from one single unresolved item: the `Validation` workflow's own `unity` job
(`runs-on: [self-hosted, Windows, X64, unity-6000.5.8f1]`) has never been picked up by any runner
-- confirmed live via `GET /repos/.../actions/runs/33381826491/jobs`, showing `status: queued`,
`runner_id: 0` for that exact job, queued since the most recent push to `main`. This is a distinct
concern from `P3-013`/`P4-009`/`P5-010`/`P6-012`'s own local detached-UPM-harness EditMode
regressions (which all passed and needed no runner) -- a real GitHub Actions run on PRs is what
`P0-005` itself requires, and nothing else has ever substituted for it. `P5-007` remains blocked for
the same reason recorded in its own evidence: native-backend hot reload does not exist, restated
unchanged through `P5-GATE` and `P6-GATE`'s own phase-handoff notes. No file was changed by this
check; the owner chose to proceed to Phase 7 decomposition rather than stand up a self-hosted runner
this session.

Phase 7 (production hardening) was decomposed into `P7-001` through `P7-016` on 2026-08-31,
grounded in `Documentation~/roadmap.md`'s Phase 7 scope and `Documentation~/scope.md`'s "Release
criteria for 1.0" list, plus `Planning~/Evidence/P6-GATE/phase7-inputs.md`'s own handoff (native
hot reload and a production Play-mode host both still open; the applied-node-discoverability and
trace-inspection gaps tied to `P6-017`/`P6-015`, both already decided but not yet built; a real
`generate_node` template defect). Two genuinely undecided architectural questions get a dedicated
spike/decision card before implementation, mirroring `P3-001`/`P5-001`/`P6-001`'s own pattern:
`P7-010` (a production Play-mode host -- the single most-repeated disclosed gap across the whole
project, independently found by `P3-009`, `P3-010`, `P3-011`, `P6-008`, and `P6-012`'s own gate
session) and `P7-011` (native-backend hot reload, `P5-004`'s own disclosed "separate
capacity-plan/lease subsystem" gap, restated unchanged since). `P7-001` (public-API/persisted-format
stability inventory) and `P7-002` (supported-platform matrix/regression-threshold proposal) are also
decision-shaped cards, but for a different reason: `Planning~/USER_ACTIONS.md` already requires the
owner's explicit approval for exactly what they produce ("Approve final public API and
persisted-format stability review"; "Approve the exact supported browser/version policy" and
"acceptable regression thresholds after research results exist") -- these cards assemble the real
evidence so that approval is a decision, not a guess. Three cards apply already-accepted-but-not-yet-built
decisions from the `P6-013`-`P6-022` tech-debt batch: `P7-007` (`ADR-P6-015`, native trace
production-wiring), `P7-008` (`ADR-P6-017`, per-project leaf registration -- this card's own
acceptance criteria fold in fixing the applied-node-discoverability gap the `P6-012` gate reproduced
live, since it is a direct consequence of the same registry-wiring gap, not a separate problem), and
`P7-009` (`ADR-P6-022`, the generic native-dispatch translator -- bundled with the small, disclosed
`generate_node` `Bool`-typed template defect, mirroring `P6-021`'s own precedent for grouping small
mechanical fixes rather than giving each its own decision cycle). `P7-005`/`P7-006` split a decision
from its implementation for migration tooling, since no mechanism spec exists yet for
`Documentation~/scope.md`'s "versioned node contracts and data migrations" beyond `P6-011`'s own
disclosed documentation stub. `P7-003` (profiler integration), `P7-004` (long-running/stress
tests), `P7-013` (samples expansion), and `P7-014` (generated API reference documentation) are
straightforward implementation cards against an already-clear roadmap line. `P7-015` (release
automation) is scoped explicitly around `P0-005`'s own still-open runner gap rather than silently
assuming it will be resolved first. `P7-016` is the Phase 7 integration gate, mirroring
`P2-025`/`P3-013`/`P4-009`/`P5-010`/`P6-012`'s own shape, and is the first gate whose own acceptance
criteria explicitly require a recorded owner decision (from `P7-001`/`P7-002`) rather than only
internal verification -- it does not, itself, declare `1.0.0`; that remains the owner's release
decision per `Planning~/USER_ACTIONS.md`.

`P7-010` (production Play-mode host decision) is **done, accepted**: `ADR-P7-010` (`AIBT-034`)
decides a future host is one `MonoBehaviour` per tree instance living in `Runtime/Integration/`
(never `AIBT.Editor` or any `*.Tests` assembly), driving `Immediate`/`Budgeted` only in its initial
scope (`BatchedJobsSameFrame`/`PipelinedJobs` need a separate, not-yet-designed population-level
coordinator, since `SchedulingPolicyDriver.TryRunBatchedJobsSameFrame` takes a whole agent population
in one call, not one instance at a time), ticking via plain `Update()` (no documented tie between
tree progression and the physics timestep anywhere in `execution-and-scheduling.md`), and owning its
own `NativeTraceChannelOwnerV1` per instance -- exactly the "caller-owned session" shape `P3-010`'s
debugger and `ADR-P6-015`'s external recorder already expect. A disposable spike
(`Spikes~/ProductionPlayModeHost/`, live via Unity MCP against the real, unmodified `6000.5.8f1`
Editor) found a real, load-bearing platform fact before any of that: Unity flatly refuses
`AddComponent` for a script in an Editor-only-platform asmdef, and independently also refuses one
carrying `"optionalUnityReferences": ["TestAssemblies"]` (the flag `AIBT.Editor.Tests` itself uses) --
reproduced live, confirming the host cannot live in `AIBT.Editor` or a `*.Tests` assembly regardless
of any other reasoning. Once attached from a plain, non-restricted asmdef, the spike's `MonoBehaviour`
ticked a real compiled tree (via the public `ReferencePreviewDriver`, a deliberate substitution for
the native `SchedulingPolicyDriver` path a future in-`Runtime`-assembly host will drive directly, made
only so this disposable, non-privileged spike needed no `AIBT.Runtime`-internal access) across over
32,000 real `Update()` calls in one continuous live Play-mode session, and its own
`NativeTraceChannelOwnerV1` was attached to and read by `P3-010`'s completely unmodified
`NativeExecutionDebuggerSession.Attach`/`TryReadTrace` mid-session -- including a real,
capacity-driven channel fault after sustained ticking, read back correctly via the existing
`IsFaulted` flag with zero special-casing needed. `OnDestroy` disposed the channel's
`Allocator.Persistent` arrays cleanly on Play-mode exit, with no leak diagnostic. No production file
under `Runtime/`, `Authoring/`, or `Editor/` was touched, per this card's own Forbidden-changes
clause -- a future, not-yet-numbered implementation card builds the real host per this ADR. A full
detached EditMode regression was additionally attempted as a not-card-required sanity check after the
spike's own required verification had already passed; the Unity Editor became unresponsive to the MCP
bridge for an extended period during that run, the owner confirmed not to pursue it further, and the
stuck job was cleared -- disclosed as not completed rather than silently omitted, since it was never
part of this card's own Required verification. See `Planning~/Evidence/P7-010/`.

`P7-011` (native-backend hot reload decision) is **done, accepted**: `ADR-P7-011` (`AIBT-035`)
decides `ADR-P5-001`'s construct-fresh-and-selectively-copy model applies to the native backend by
reusing `NativeProgramImageOwnerV1.TryCreate`/`NativeInstanceArenaOwnerV1.TryCreate` unchanged for
fresh-instance construction (both already derive their own capacity), and finds state capture/seeding
needs **zero new internal engine methods** -- a real, positive difference from the reference-executor
backend, which needed two new methods (`CaptureNodeState`/`SeedNodeState`) before migration was
buildable at all; native composes entirely from `NativeInstanceArenaOwnerV1`'s already-public
execution-lease/View API. A disposable spike (`Spikes~/NativeHotReloadModel/`, 2 tests, live via
Unity MCP `run_tests` against the real, unmodified `6000.5.8f1` Editor) proved both: full restart
(abort an active old instance, construct a fresh one bound to a new program) and migration of a
genuinely **active** (non-idle) instance's per-node `Frame`/`Generation` state across a real
compiled-index-shifting reorder, keyed by stable `NodeId` through only public Owner API. **Native
migration is not restricted to an idle old instance the way the reference executor's own
implementation is** -- confirmed by reading real code, not assumed identical: `NativeFrameStateV1` is
one uniform blittable struct for every node kind, unlike the reference executor's polymorphic
`ReferenceFrame`, which is what forced that restriction there. A second real, disclosed finding:
native's `TryRequestAbort` requires an *open* update, the opposite precondition from the reference
executor's own `Abort` (which requires *no* open update) -- confirmed live by a real
`NativeLifetimeStateInvalid` failure on first attempt (aborting a `Waiting`, between-ticks instance
directly is rejected; a caller must reopen a fresh update first, safely resuming rather than
re-entering the already-active instance). A third finding, caught by reasoning through the spike's
own captured values rather than a failing assertion: the spike's own naive whole-`Frame` copy does
not implement `ADR-P5-001`'s already-decided composite-cursor-reset rule (item 2) -- a verbatim
`ChildCursor` copy would silently point at the wrong child after a reorder if the migrated instance
were driven further; the rule transfers unchanged to native's own `ChildCursor` field and is
disclosed as real follow-up scope for `P7-012`, not smoothed over. No production file under
`Runtime/Execution/Native/`, `Runtime/Compiled/Native/`, `Runtime/State/Native/`, or
`Runtime/Scheduling/Native/` was touched, per this card's own Forbidden-changes clause. The Unity
Editor became genuinely unresponsive to the MCP bridge twice during this card's work, disclosed in
its own evidence rather than smoothed over; both recovered (the first after the owner manually
restarted the Editor) and did not affect the correctness of the spike results. See
`Planning~/Evidence/P7-011/`.

`P7-007` (native trace production-wiring implementation) is **done**: `ADR-P6-015` (`AIBT-027`) is
applied to production. `Runtime/Scheduling/NativeTraceRecorderV1.cs` (new) mirrors
`Spikes~/NativeTraceProductionWiring/`'s already-proven recorder shape, wired additively into a new
`SchedulingPolicyDriver.TryRunImmediate` overload (`NativeTraceRecorderV1[] recorders`, parallel to
`agents`) that calls the exact same `TryHandleStep` with the exact same inputs the original body
already did -- a recorder can observe but never influence a scheduling decision. The existing
5-argument `TryRunImmediate` is now a one-line delegation to the new overload with `recorders: null`,
proven bit-identical by the full, completely untouched `AIBT.Runtime.Tests` regression (588/588,
including the pre-existing `SchedulingPolicyDriverTests`). Two new tests
(`Tests/Runtime/NativeExecution/Scheduling/TraceWiring/`) prove a real two-update run through the
wired driver produces a correctly ordered, correctly bracketed trace (root's own completion folded
into a `NodeExited(0)` record with `ExitReason`, leaf `NodeEntered` recorded exactly once despite
spanning two updates, zero dropped/faulted records) on a real `NativeTraceChannelOwnerV1`, read via
the channel's own public snapshot API (`AIBT.Runtime.Tests` has no reference to `AIBT.Editor`, so it
cannot itself exercise `NativeExecutionDebuggerSession`/`TraceTimelineModel`). The specific "reads
back through `NativeExecutionDebuggerSession`/`TraceTimelineModel`, completely unmodified" proof the
card's own acceptance criteria require was produced as a temporary, disposable live test (deleted
after the run, matching this project's own established spike-then-delete pattern, since a permanent
file exercising `AIBT.Editor` types falls outside this card's own `Tests/Runtime/NativeExecution/`
Allowed-changes fence) -- 1/1 passing, live via Unity MCP. A real, deliberate scope narrowing:
`TryRunBudgeted`/`TryRunBatchedJobsSameFrame` remain completely unwired, since `NativeLifecycleBudgetDriverV1`'s
own resume-across-calls contract (whether a budget-suspended tick's `UpdateOpen` state permits a
bracketing `RecordUpdateStarted`/`RecordUpdateEnded` pair the same way `Immediate`'s always-one-call
tick does) was not going to be guessed at, and `ADR-P6-015` itself already disclosed exactly this
same "not separately spiked" boundary for both policies. No production file under
`Runtime/Execution/Native/` was touched, and no new trace event kind or field was introduced beyond
the ADR's own accepted mapping table. Mid-session, the live Unity Editor's own Test Runner became
unable to initialize any test job for roughly 15 minutes (four consecutive dispatch failures,
including one targeting an already-deleted class, ruling out this card's own new code as the cause)
-- a distinct instability from `P7-010`/`P7-011`'s own earlier MCP-bridge unresponsiveness this same
session; bringing the Editor window into focus resolved it, disclosed rather than smoothed over. See
`Planning~/Evidence/P7-007/`.

`P7-008` (per-project leaf-registration mechanism implementation) is **done**: `ADR-P6-017`
(`AIBT-031`) is applied to production. `IReferenceLeafBehavior`/`ReferenceLeafContext` (new, public,
`Runtime/Execution/Reference/Leaves/Public/`) are the public equivalents of the internal
`IReferenceLeafHandler`/`ReferenceNodeContext` -- `ReferenceLeafContext` is a public `readonly ref
struct` holding a private by-value copy of the internal context, safe because `Configuration`/
`Memory` are span views over arrays already held by reference and blackboard I/O is forwarded
through the internal context's own captured service interface, never mutable struct state.
`IReferenceLeafBehaviorProvider` (new, public, `Authoring/Registry/`) pairs a project's
`NodeManifest` with a behavior factory, mirroring `ICustomMcpToolProvider`'s own `P6-010` shape.
`NodeRegistryBuilder.AddProjectExtension` attaches a real handler binding under the existing
`NodeManifestSource.UserExtension` source; `ValidateBinding`'s `UserExtension` case changes from
"any binding is an error" to "binding is optional, validated like a built-in/fixture binding when
present" -- the pre-existing `AddUserExtension` (no-binding) path is untouched, so the ADR's own
"unchanged negative test" passes unmodified. `MCP/Authoring/ProjectLeafExtensionDiscovery.cs` (new,
Editor-only) mirrors `P6-010`'s own `TypeCache`-based discovery split exactly and wires discovered
project registrations into `aibt_search_nodes`/`aibt_get_node_contract`/`get_project_manifest`
(the three `NodeRegistryBuilder.CreateWithBuiltIns()` call sites in `MCP/McpToolDispatcher.cs`),
degrading to a built-ins-only build if a malformed project registration fails validation, rather
than surfacing a null registry to every discovery tool. Proven with a real project-style leaf
(defined using only the new public contract) ticking correctly through a real, unmodified
`ReferenceExecutionMachine`, and live against the real open `6000.5.8f1` Editor via Unity MCP
`execute_code` calling the real `AIBT.Mcp.McpToolDispatcher.Dispatch` entry point directly against a
temporary script added outside `AIBT/` (deleted afterward). This card's own live verification caught
a real regression before it shipped: `UnityEditor.TypeCache` scans every loaded assembly, test
assemblies included, so the first version of this card's own proof test's public-parameterless-
constructor fixture got itself live-discovered, breaking the pre-existing
`McpToolDispatcherTests.ZeroCustomNodesReturnsExactlyThePhase1BuiltInCatalog` (12 entries instead of
11 in a supposedly clean environment) -- fixed by giving the test fixture a non-public constructor,
since `Type.GetConstructor(Type.EmptyTypes)`'s default binding only returns public ones; full
regression re-ran clean after the fix (1589/1592, the 3 remaining failures pre-existing and
unrelated -- a `CodeGen` package-path environment assumption and an unrelated `LocalSaveSystem`
package test). Reference-executor backend only, per the ADR's own scope; `P3-009`/`P6-007`/`P6-008`
still build their own registries from the fixed Phase 1 fixture/built-in set unchanged, and no
async-operation support exists on the public leaf context in this v1 -- both disclosed, not silently
assumed solved. See `Planning~/Evidence/P7-008/`.

`P7-009` (generic native-dispatch translator production implementation) is **done**, with one
owner-approved re-scoping. Investigation before implementation found the card's own "test-node run
against a real, freshly generated, non-index-0 custom node" acceptance criterion structurally
unreachable through today's staging architecture: `StagingSlot.WriteNode` always clears and stages
exactly one node file in its own isolated one-node assembly, and `GeneratedMetadataEmitter` assigns
dispatch index by ordering nodes within one compiled shard -- a staged node is therefore always
dispatch index 0, and `test-node` only ever reflects the staging assembly, never a real applied
catalog. Surfaced directly to the owner rather than guessed at; approved: build
`GenericNativeDispatchTranslatorV1`'s full `0..targetIndex` case-prefix support anyway (matching
what `ADR-P6-022` actually decided), proving that part against a dedicated permanent fixture instead
of through the live tool, which can only ever exercise the index-0 case.
`GenericNativeDispatchTranslatorV1` (new, `Authoring/Registry/Generated/` -- a real
dependency-direction correction from the card's own tentative `Runtime/Execution/Burst/Dispatch/`
location, since `AIBT.Runtime` cannot reference `AIBT.Authoring`'s materializer while `AIBT.Authoring`
already can reach `AIBT.Runtime`'s internal dispatch contracts) ports the spike's proven single-case
mapping, extended to flatten a real prefix's cases into the shared arrays
`NativeBurstDispatchCaseV2`'s own range fields index into -- confirmed against
`NativeBurstDispatchBindingValidationV2`'s own real consuming code (not assumed) that field/binding
ordinals are local to their own case while a binding's own value-field position is global across the
flattened array. `MCP/NodeDevelopment/GenericNodeDispatchRunner.cs` (new) drives the translator's
output through a real `NativeBurstDispatchWorkspaceOwnerV2` with a generic, zero-initialized request
(no per-field-name knowledge, so it works for any in-scope node shape), invoking the dynamically
compiled catalog's `ExecuteImmediate` via reflection. A real, empirically-found compile-time
constraint mid-implementation: `[AibtCatalogSet]` cannot reference a shard declared in the *same*
compiled assembly (a real `AIBT5011` failure on first attempt, then confirmed against
`Samples~/BurstNodes/`'s own existing shard-assembly/catalog-assembly split) -- fixed by staging the
companion catalog-set file into its own `Pending/Catalog/` sub-assembly, referencing the node's own
staging assembly by name. The disclosed `generate_node` `Bool`-typed condition-template bug
(`current >= config.Minimum` does not compile for `bool`) is fixed (`==` for `Bool`, unchanged `>=`
for every numeric type). All three -- real dispatch execution, honest out-of-scope reporting for an
async-bound node, and the `Bool` fix -- were proven live against the real open `6000.5.8f1` Editor via
Unity MCP `execute_code` calling the real `AIBT.Mcp.McpToolDispatcher.Dispatch` entry point directly
through the real `generate_node` -> `analyze_and_compile_node` -> `test_node` sequence; the
prefix-translation path itself is proven by a new permanent 3-node real-compiled fixture
(`Tests/Editor/CodeGen/Dispatch/`, mirroring `Tests/Editor/CodeGen/Generation/
GeneratedArtifactContractTests.cs`'s own already-established in-assembly-analyzer pattern, no
isolated project needed). `Registered`-encoded fields and `AsyncOperation`/`Completion` bindings
remain explicitly unproven, per the ADR's own scope. See `Planning~/Evidence/P7-009/`.

`P7-012` (native-backend hot reload implementation) is **done**: `ADR-P7-011` (`AIBT-035`) is
applied to production, and `P5-007`'s own long-blocked deliverables (golden-equivalence re-run,
batch isolation, `Auto` determinism for a hot-reloaded native instance) are finally closed alongside
it -- `P5-007` moves to `Done` in the same pass. `NativeHotReloadInstance`/
`NativeHotReloadFullRestart`/`NativeHotReloadStateMigration` (new, `Runtime/Execution/Native/HotReload/`)
reuse `NativeProgramImageOwnerV1.TryCreate`/`NativeInstanceArenaOwnerV1.TryCreate` unchanged for
fresh-instance construction and the existing backend-agnostic `HotReloadProgramIdentityMap`/
`HotReloadCompatibilityClassifier` unchanged for classification, exactly as `ADR-P7-011` decided.
Test-driven verification found a real bug the ADR's own spike had not: its "apply the
composite-cursor-reset rule to native's `ChildCursor`" framing named the right symptom but the wrong
mechanism. Investigated directly against `NativeLifecycleMachineV1`'s own dispatch code while a
migration test was genuinely, reproducibly failing (not assumed, not guessed) -- `_frames` is not
indexed by compiled node index at all, it is a call stack indexed by DEPTH, reused across sibling
nodes over an instance's lifetime; a frame's own `NodeIndex` field, not its array position, says
which node it represents. The first implementation (mirroring the ADR spike's own simplified copy)
copied Frame state by node index, which silently swapped two leaves' live/inactive state the moment
more than one node had ever been active in the same tree -- confirmed via `Debug.Log` diagnostics
through several live Unity MCP `run_tests` round-trips, isolating the bug to exactly this mechanism
after first ruling out the identity-map index resolution itself (independently verified correct).
Fixed by copying the active call stack position-for-position by depth and remapping only each
frame's own `NodeIndex` field, the same technique `Spikes~/ActiveInstanceHotReloadMigration/` had
already proven for the reference executor (`P6-018`), generalized here to native's own
depth-indexed `_frames`; an active path running through a node that cannot migrate now fails
`TryMigrate` cleanly rather than silently truncating the stack. 12 new tests
(`Tests/Runtime/NativeExecution/HotReload/`, `Tests/Integration/NativeRuntime/`) prove: full restart
of a genuinely active instance; migration correctly resuming a reordered composite from its reset
cursor (the exact gap `P7-011`'s own evidence disclosed as unverified); golden-equivalence for all 4
accepted policies against a full-restarted instance (extending `NativeExecutionEquivalenceTests`'s
own established trace-equality technique, since `NativeBehaviorCaseExecutor`'s simpler direct-array
construction does not share a construction path with the new owner/lease-based hot-reload
machinery); batch isolation (one reloaded lane among three leaves its untouched siblings
bit-identical to an all-untouched control batch); and `Auto` determinism-on-reload, including a
direct confirmation that a reseeded post-reload `NativeWorkEstimatorV1` matches a never-reloaded one
for the same observations. Full EditMode regression (1609 tests) shows no new failures beyond the 3
already-pre-existing, unrelated ones. Tree-blackboard content, the `V2` construction/lease path,
deeper multi-frame-deep concurrent active state, and a dedicated subtree-restart test remain
explicitly out of scope, disclosed rather than smoothed over, matching `P7-011`'s own "Explicitly
unverified" framing. See `Planning~/Evidence/P7-012/`.

`P7-004` (long-running and stress test suite) is **done**: `Tests/Runtime/Stress/` (new, test-only)
adds the layer `roadmap.md` names for Phase 7 -- a 20,000-tick-cycle soak test (zero managed GC
allocation and no native-array resizing after warmup, extending `P2-021`'s own established
technique by two-plus orders of magnitude); a 10,240-agent stress test (10x `P4-002`'s largest
measured population) via `SchedulingPolicyDriver.TryRunBatchedJobsSameFrame` at `batchSize=128`
(`P4-002`'s own largest measured, deliberately-costly configuration), every agent asserted against
a 16-agent control for determinism drift; and a repeated-reload-under-load test for both backends,
comparing a never-reloaded group against an all-untouched control while a repeatedly-reloaded group
survives 10 full-restart waves. Two real findings surfaced during test-driven development, both
genuine test failures traced to their root cause rather than worked around: a normally-completed
native instance is terminal (`NativeLifecycleMachineV1.TryBeginUpdate` requires
`control.HasRootStatus == 0`, and reading `PopFrame`/`PopAbortedFrame` directly shows that flag is
cleared only on the abort path, never after a normal completion) -- the soak tests' first draft
wrongly assumed a "tick to completion, then begin again" model and failed reproducibly with
`NativeLifetimeStateInvalid` on the second cycle, fixed by redesigning around the engine's actual
established usage pattern (a perpetually-active agent, `Waiting` between ticks, matching
`NativeExecutionEquivalenceTests.Scenario`'s own `BeginNextUpdate` convention); and
`NativeCommandAsyncOwnerV1`'s own operation-record table is a monotonic lifetime log, not a
reclaimable ring buffer (`TryCancel` marks an operation's own state in place but never frees its
slot, confirmed by reading `TryStart`/`TryCancel` directly) -- the soak test's third assertion was
redesigned around this real contract (proving the capacity boundary is safe, not asserting a
"never exhausted" property that would be false by design). No stress test surfaced a real
production defect this pass; full EditMode regression (1615 tests) shows no new failures beyond the
3 already-pre-existing, unrelated ones. See `Planning~/Evidence/P7-004/`.

`P7-001` (public API and persisted-format stability inventory) is **done**: produces the concrete
material `USER_ACTIONS.md`'s "Approve final public API and persisted-format stability review"
requires -- a proposal, not a freeze. `Tools~/Verification/P7/Audit/Get-FullPublicApi.ps1` (new,
permanent) widens the stale, 2-assembly `Tools~/Verification/P2/Audit/` dump script to all four
public assemblies -- every gate since `P3-GATE` had improvised and thrown away this same extension
each time rather than committing it. Run live against current `main`: 4 assemblies, 417 types, 2111
members. A mechanical diff against all five prior gates' own `public-api.txt` dumps confirms every
one of their "purely additive" claims empirically for the first time across the *entire*
P2-through-current history in one pass (each gate previously only checked its own diff against its
immediate predecessor): **zero removals or breaking renames anywhere in the project's history.** The
25 lines new since `P6-GATE` trace entirely to `P7-008`'s own public surface, a consistency check
confirming the diff pipeline is accurate (`P7-007`/`P7-009`/`P7-012`/`P7-004`'s own new code is
`internal` or test-only, correctly contributing nothing). Also closed a real gap found by reading
`Verify-Schemas.ps1` directly: it only ever validates 2 of the project's 6 JSON schemas against a
real document; the other 4 are now validated live too (all pass), surfacing one genuine finding along
the way -- `node-manifest.schema.json` governs a single node's own manifest shape, not the aggregate
`get_project_manifest` response, which has no schema of its own (first validation attempt against the
wrong document shape failed with 19 errors before this was understood; fixed by generating a real
single-node example live via `McpToolDispatcher.Dispatch("get_node_contract", ...)`, mirroring
`P7-008`/`P7-009`'s own temporary-script live-verification pattern). Git-log/timestamp comparison
also confirmed no schema has ever changed shape after the gate that accepted it. The actual
deliverable, `Planning~/Evidence/P7-001/stability-review-proposal.md`, recommends
`AIBT.Runtime`/`AIBT.Authoring` stable-for-1.0 and `AIBT.Editor`/`AIBT.Mcp` still-experimental, and
raises 5 explicit open questions for the owner. No production file was touched; no freeze was
decided -- that remains the owner's own decision per `DECISION_BOUNDARIES.md`. See
`Planning~/Evidence/P7-001/`.

`P7-002` (supported-platform matrix and regression-threshold proposal) is **done and accepted**:
`Documentation~/compatibility-matrix.md` (new) consolidates every platform benchmark gathered since
Phase 2 (`P2-022`/`P2-023`/`P2-024`/`P0-003`/`P4-002`/`P4-006`/`P4-008`/`P4-004`'s own
2026-08-26 addendum/`P6-021`) into one matrix, every measured cell cited to its exact evidence file
re-opened live during planning, not recalled -- catching a real attribution error along the way
(`CalibratedNanosecondsPerNodeStep`/its tolerance figure live in `P4-004`'s own addendum, not
`P4-008` as prior-session memory had it). The regression-threshold proposal is grounded in real
spread pulled directly from the raw Windows Player JSON: `Immediate`/`Budgeted` show single-digit-%
max-over-median spread even on real hardware, while `BatchedJobsSameFrame` shows **+313.7%
max-over-median at the smallest tested population** -- reproducing `P4-002`'s own Editor-side
"batch overhead doesn't amortize with population" finding at the Player level too, not an
Editor-only artifact. This directly grounded the proposal's central structural call: a per-policy
threshold (20% for `Immediate`/`Budgeted`, multi-run median comparison proposed instead of a
single-run percentage for the batched policies), not one uniform number that would be meaningless
either way. Unlike `P7-001`, this card's own acceptance criteria required the owner's actual
decision recorded, not just a proposal left pending -- put directly via `AskUserQuestion` (approve
as-is / approve with changes / reject pending more data), and **the owner approved as-is**
(2026-09-02). `compatibility-matrix.md`'s own status banner moved from "DRAFT PROPOSAL" to
"ACCEPTED" accordingly; it is now the reference every later platform claim should point at. No
production file, test, or benchmark script was touched. See `Planning~/Evidence/P7-002/`.

`P7-014` (generated C# API reference documentation) is **done**: decides and builds, in one pass
(mirroring `P6-021`'s own precedent for a mechanical, non-architectural decision made and applied
together), whether AIBT's public C# surface needs a generated reference or can rely on XML-doc
comments plus `architecture.md`. Investigated directly first: real current XML-doc coverage is
**~2.4-4.5% of public members** (a source-parse across all four assemblies, cross-checked two ways
-- the regex sweep's own type count matched the reflection dump's exactly, and independently
counting every `<summary>` tag confirmed the low number is real, not a parsing artifact), and
`architecture.md` is confirmed narrative-only with no per-type/per-member entries at all -- neither
supports "rely on XML comments" as a defensible choice. New `MCP/Documentation/
McpApiReferenceGenerator.cs` mirrors `P6-011`'s own established generator pattern exactly, reflecting
all four public assemblies live (placed inside `MCP/Documentation/` rather than an isolated-project
script, since `AIBT.Mcp`'s own assembly already transitively references the other three, so its
AppDomain naturally has all four loaded when the existing regenerate command runs) and writing
`Documentation~/generated/api-reference-{runtime,authoring,editor,mcp}.md` -- every one of the 2,528
reflected public declarations gets its own signature line, satisfying the card's own "100% of public
members have a generated reference entry" bar exactly, without inventing prose for the ~95% of
members that have none today. A type's own XML-doc `<summary>` is inlined where one exists (60 of
417 types, 14.4%) via a best-effort source-parse keyed on exact `FullName`; member-level correlation
was investigated and found too fragile to attempt this pass, disclosed rather than silently
attempted. `Tests/Editor/Documentation/McpDocumentationGeneratorsTests.cs`'s drift-check was
extended for the four new files, and a new coverage-check test proves the 100% bar mechanically
(fresh reflection vs. committed file, not sampled) -- both passed live on the first run; full
EditMode regression (1616 tests) shows no new failures beyond the 3 already-pre-existing, unrelated
ones. See `Planning~/Evidence/P7-014/`.

`P7-013` (samples expansion) is **done**, with one owner-approved re-scoping found mid-
implementation. `Samples~/CustomMcpToolProvider/` (new) demonstrates `ICustomMcpToolProvider`
(`aibt_sample_greeting`), proven live against the real, permanent `MCP~/Server/` via the official
`@modelcontextprotocol/inspector --cli` -- `tools/list` shows it among 33 real tools, `tools/call`
returns a real response. Investigation before building the second sample found a real structural
blocker: no public production API exists anywhere to drive a compiled tree with a chosen scheduling
policy (every native-backend execution type is `internal`, confirmed against this session's own
`P7-001` public-API dump), and the one public preview entry point
(`ReferencePreviewDriver`/`HotReloadPreviewDriver`) compiles exclusively against a fixed
built-in/fixture registry -- neither was updated for `P7-008`'s own new project-extension mechanism,
though both drivers' own doc comments still claim no such mechanism exists at all. Surfaced to the
owner via `AskUserQuestion` mid-implementation; approved: trim `Samples~/FullExample/` to
demonstrate the hot-reload pass alone, on the fixed node set both preview drivers actually support,
disclosed explicitly in the sample's own README rather than faked or silently dropped. Live-verified
via Unity MCP `execute_code` against the real open Editor and `HotReloadWorkflowWindow` (`P5-008`) --
also found live, not assumed, that the README's own first-draft claim ("live state survives the
reload") was only true for an *idle* old instance; reloading an *active* one instead falls back to a
full restart (the reference executor's own idle-only migration scope, a real pre-existing limitation
distinct from `P7-012`'s native backend, which explicitly supports migrating an active instance).
Corrected before shipping. No production file was touched; neither existing sample
(`BurstNodes`/`SemanticSlice`) changed. See `Planning~/Evidence/P7-013/`.

`P7-003` (profiler integration and validation) is **done**: `Unity.Profiling.ProfilerMarker`
instrumentation was added to `NativeLifecycleMachineV1` (the Burst-compiled per-instance lifecycle
tick, `TryAdvance`, and each of its five per-node-kind dispatch methods), the two same-frame-class
scheduling controllers' `TryScheduleExecuteRound`, `NativeSharedContextOwnerV1.TryReduceUpdate` (the
real production blackboard-bridge call), and `ReferenceExecutionMachine.AdvanceOneStep` (the
reference-backend mirror) -- every change a single `using var _ = marker.Auto();` line, confirmed
purely additive by `git diff --stat` (40 insertions, 0 deletions across all five touched files). Two
deviations from the card's own text were put to the owner before proceeding, both approved:
command-bridge markers were dropped from scope after a repo-wide grep found the native command-merge
subsystem (`NativeCommandMergeOwnerV1`/`NativeCommandAsyncOwnerV1`, built by `P2-010`) has zero
production callers anywhere, only its own test file -- instrumenting it would have misrepresented
dead code as a hot path, the same class of built-but-unwired gap `P5-007`/`P6-015` already disclosed
elsewhere; and the real Profiler capture required a Development Build with
`BuildOptions.ConnectWithProfiler`, not the "non-Development Player" the card's own text names,
since Unity's Profiler transport is compiled out of true Release builds entirely -- `P4-008`'s own
non-Development precedent is right for wall-clock measurement but cannot be connected to. A third
finding needed no escalation: `Unity.Profiling.ProfilerRecorder`-based marker-presence unit tests
(the card's own suggested example technique) do not work in this Unity version's EditMode
environment -- confirmed with a plain, independent, non-AIBT, non-Burst control marker fired
directly against the live Editor, ruling out anything Burst- or AIBT-specific before dropping the
approach. Burst compatibility was verified for real rather than assumed: live reflection into the
open Editor's own Burst Inspector data model (`Unity.Burst.Editor.BurstInspectorGUI`) forced a fresh
compile of the real `AdvanceJob` Burst target and read back `IsBurstError=False` plus 286,573
characters of real x64 disassembly, confirming all six new marker fields are initialized via genuine
`ProfilerUnsafeUtility.CreateMarker` calls with zero managed-fallback indicators anywhere in the
compiled code. A real Windows x64 Development Player (`Benchmarks~/Phase7/Profiler/`, new) ran
`P4-001`'s own `deep-sequence-selector-traversal` scenario continuously for 150 seconds (33,036
frames, no failure); the open Editor's Profiler connected to it live and saved a real 31-frame `.data`
capture showing the new markers' hierarchy and per-call cost, attached as evidence rather than
described in prose. `P4-001`'s own harness (`SchedulingPolicyDriver`) was re-run with and without the
markers via a disposable, uncommitted EditMode spike: zero GC allocation delta in every sample, and a
real, disclosed +5.6% median wall-clock delta on a worst-case single-node-per-call Editor-Mono
micro-benchmark (not the actual Burst-compiled production path, which `P4-008`'s own finding says
runs roughly an order of magnitude faster and less noisily than anything Editor-measured) -- reported
honestly, not smoothed away, and explicitly not turned into a threshold or default, per every Phase 4
card's own precedent. Full regression across every AIBT test assembly: 601/601
(`AIBT.Runtime.Tests`, re-confirmed identical with and without markers via `git stash`), 470/470,
167/167, and 19/21 (2 pre-existing, host-embedded-layout failures unrelated to any file this card
touched). The owner flagged the committed capture's own size (22 MB, larger than any prior binary
evidence in this repository — the previous largest was `P4-008`'s own 8.3 MB WebGL build) during
review and approved committing it as-is while asking for a dedicated look at evidence-artifact size
discipline generally, rather than deciding unilaterally in the moment -- spun off as `P7-017`
(`Draft`, depends only on `P7-003`, not required for the `P7-016` gate, mirroring `P6-013`-`P6-021`'s
own cross-phase-debt pattern). See `Planning~/Evidence/P7-003/`.

`P7-005` (migration-tooling design decision) is also **done: `ADR-P7-005` (`AIBT-036`) accepted
2026-09-02**. Investigation before deciding anything found the real gap: `TreeValidator.cs:487`
today does a hard equality check on a node's `TypeVersion` with zero compatibility path — any
version bump instantly invalidates every existing authored document referencing that node type, no
automated or documented recovery exists; zero migration execution machinery exists anywhere for
node types (the closest precedent, `BlackboardTypeDescriptor`'s own `MigrationSourceVersion`/
`MigrationContractId`, is declared metadata for a different kind of type with zero consumers
anywhere); `burst-node-abi-v1.md` forbids custom migration callbacks inside a Burst-compiled node;
and no node type has ever actually been version-bumped in this project's history — a genuinely
greenfield decision with zero real precedent to validate scope against. Discussed directly with the
owner rather than decided unilaterally, grounded in this being an AI-first library (the primary
consumer of a diagnostic is often an MCP-driving agent, not only a human): the decision covers only
field-added-with-default and field-renamed (removal/type-change stay disclosed hard failures); the
mechanism is a declarative, ordered `(NodeTypeId, sourceVersion)` rule registry at the
authoring-tooling layer only, never inside the node itself, so ABI v1's ban does not apply;
`validate`/`compile` apply it to an in-memory copy only, the on-disk document is never mutated as a
side effect; every applied migration emits a structured, non-blocking `Info`-severity diagnostic
reachable through `explain_diagnostic`; persisting to disk is a separate explicit action (an Editor
notification that must never block the MCP/AI-agent path, and an MCP tool), both deferred to
`P7-006`; diff preview reuses the existing `CanonicalTreeJsonWriter`. A disposable spike
(`Spikes~/MigrationToolingDecision/`, run live via Unity MCP, 2/2 passed) proved the mechanism
against a real fixture node type bumped v1→v2 (rename + add-with-default), compiled through the
real `ReferenceCompiler`, and a negative case (an unregistered v2→v3 gap) still hard-failing through
the existing `UnsupportedNodeVersion` diagnostic, unchanged — the recorded diff shows exactly
`"moveSpeed": 10` → `"speed": 10, "acceleration": 5`. No production code shipped; `P7-006` applies
this ADR to production. See `Planning~/Evidence/P7-005/`.

`P7-006` (migration tooling implementation) is also **done**, applying `ADR-P7-005` as real
production code in one pass — its own Allowed-changes list, written before the ADR existed, was
corrected before implementation (owner-confirmed) to match the ADR's full Consequences section
rather than silently narrowing or expanding scope. `Authoring/Migration/` (new) implements the
declarative rename/add-with-default rule engine (`NodeMigrationRule`/`NodeMigrationRegistry`/
`DocumentMigrator`); a new `AIBT2042 MigrationApplied` diagnostic (`Info` severity — the diagnostic
catalog's per-code default was widened from a single hardcoded `Error` for every code to a real
per-code default, so `explain_diagnostic` reports it correctly) is hooked into
`McpVerificationToolDispatcher.Validate`/`Compile` immediately after document load, migrating in
memory before validation/compilation ever run — the on-disk file is never touched by this path. A
new `aibt_migrate_document` MCP tool (`MCP/Migration/`, tagged `SemanticEdit` mirroring `add_node`'s
own tag, `MCP~/Server/MigrationTools.cs` relay, a real `dotnet build` of the external server project
confirmed 0 errors) persists that same migration to disk on explicit request, dry-run by default,
mirroring `McpAuthoringToolDispatcher`'s own accept-then-persist shape. A new non-blocking
`Editor/Migration/MigrationNotificationWindow.cs` lists every project document with a migratable
node and a per-row persist button — verified live against the real, currently-open Editor scanning
the real project's actual 72 `.aibt.json` documents, correctly reporting "Nothing to migrate" since
no node type has ever had its contract version bumped in this project. `McpMigrationsDocumentGenerator.cs`
gained a real "Node-contract migrations" section alongside its pre-existing, genuinely unrelated "MCP
surface migrations" one (tool renames, not node-contract versioning — the two concepts share one
generated file by this card's own deliberate choice, not conflated). 11 new tests
(`Tests/Editor/Migration/`) pass live, each proving a real production entry point — the standalone
engine against a real `NodeManifest`/`TreeDocument` compiled through the real `ReferenceCompiler`
(including a chained two-hop migration proving rule hops never skip ahead, and the unhandled-category
negative case still hard-failing through the existing `UnsupportedNodeVersion` diagnostic unchanged),
the exact `ApplyMigrations` hook inside the verification dispatcher, the `aibt_migrate_document`
dispatcher (dry-run/persist/permission-negative), and the Editor window's scan/list logic — never a
synthetic in-memory reimplementation. Full regression: `AIBT.Runtime.Tests` 601/601;
`AIBT.Editor.Tests`+`AIBT.Integration.Tests`+`AIBT.BehaviorCases.Tests` 481/481 (470 pre-existing + 11
new), identical baseline pass count, zero new failures. See `Planning~/Evidence/P7-006/`.

`P7-015` (release automation) is also **done**, the last directly-assignable Phase 7 card before the
`P7-016` gate. `P0-005`'s self-hosted `unity-6000.5.8f1` runner was reconfirmed genuinely blocked
live (GitHub REST API, 2026-09-03, before any code was written): the most recent `Validation` run's
Unity job sat `queued, runner_id: 0` since dispatch, matching every prior run. Built local-first,
mirroring `Verify-Static.ps1`/`validation.yml`'s own existing relationship rather than trusting an
untested workflow: `Tools~/Verification/P7/Release/Verify-ReleaseReadiness.ps1` (new) validates
semver parsing, that the target version is strictly greater than `package.json`'s current version,
that `CHANGELOG.md` has no duplicate release heading and a non-empty `[Unreleased]` section, and
that no matching git tag already exists — throwing on the first failure, never mutating either file
itself. `.github/workflows/release.yml` (new) is `workflow_dispatch`-only with a required
`confirm_local_editmode_passed` input carrying no default, so the workflow fails loudly rather than
silently skipping the Unity EditMode gate it cannot yet run; its three `windows-2022` jobs
(`readiness` → `static` → `publish`, the last scoped to `contents: write` alone) reuse
`validation.yml`'s own pinned action SHAs verbatim and publish via the runner-preinstalled `gh` CLI
with `secrets.GITHUB_TOKEN` — no credential embedded anywhere. All three required-verification
commands ran live: `Verify-Static.ps1` passed; the positive case (`0.1.0 -> 0.2.0`) printed the
correct dry-run summary; the version-consistency negative case (`-TargetVersion 0.0.1`, not greater
than the real current version) and an invalid-semver negative case both failed loudly as required.
A PowerShell-specific bug was found and fixed during this work: a local variable differing from a
mandatory parameter only by case (`$targetVersion` vs. `$TargetVersion`) silently collided in
PowerShell's case-insensitive variable resolution, producing an empty value — renamed throughout to
`$targetSemVer`/`$currentSemVer` and re-verified live. `release.yml` itself was never dispatched for
real this session — doing so would push a real tag and create a real public GitHub Release, a
separate explicit ask distinct from building the automation — matching the card's own acceptance
criteria that a local-equivalent script satisfies the dry-run requirement. `package.json` remains
`0.1.0`; no real release has been cut. See `Planning~/Evidence/P7-015/`.

`P7-016` (the Phase 7 integration gate) is **done: Accepted, with disclosed gaps — 2026-09-03,
against commit `eedeb3c8408714ed5e5b3ee773a7a76c258e9864`**, explicitly not declaring `1.0.0` (a
separate owner decision per `USER_ACTIONS.md`). A clean detached-UPM-harness compile (exit 0) and
full EditMode regression **1269/1270**, 0 skipped (up from `P6-GATE`'s 1224/1224) — one real,
pre-existing failure, not caused by this gate and not fixed inside it per its own Forbidden-changes
clause: `McpApiReferenceGenerator`'s type-`<summary>` correlator hardcodes
`Application.dataPath + "/AIBT"`, silently returning nothing for any real `file:`/registry UPM
consumer — the first time this generator was ever exercised outside the host project, spun off as
`P7-021`. Public API surface: **425 types/2130 members, +13 types/+34 members versus `P6-GATE`'s own
combined baseline, confirmed purely additive by direct type-set comparison** (a separate, apparent
"5 removed member" signal from a naive flat-sorted diff was investigated and traced to the dump
tool's own file format — types listed in one block, unique member *signatures* in a second,
type-agnostic block — not a real change; the earlier P6/P7 split-file baseline versus this gate's
single combined 4-assembly dump explained the artifact fully). Assembly-dependency audit: all 4
production `.asmdef` files byte-identical to `P6-GATE`'s own recorded references — zero drift across
all of Phase 7. `Documentation~/scope.md`'s 7-item "Release criteria for 1.0" checked item-by-item
against real evidence: 5 fully met, 2 partially met (stable contracts, blocked on tree-format `v2`
promotion; production-ready editor/debugger, blocked on a still-undecided-but-unbuilt production
Play-mode host). This gate's own review, before any mechanical verification ran, found and fixed
real task-card bookkeeping drift on 4 cards (`P6-012`, `P7-007`, `P7-010`, `P7-011` all had real,
accepted evidence but a task-card file still reading `Status: Draft` with no `## Outcome` — a "mark
the card done" step silently skipped at least 4 times across two gates) and closed a literal,
unmet acceptance criterion of its own: `P7-001`'s public-API/persisted-format stability proposal had
never received a recorded owner decision. One was gathered live during this gate's own session —
`AIBT.Runtime`/`AIBT.Authoring` stable for `1.0` (uncontested, additive-only since
`P2-GATE`/`P3-GATE`); `AIBT.Editor`/`AIBT.Mcp` stay explicitly experimental, the latter partly
*because* diffing `MCP/` against the `P6-GATE` candidate commit found a real, previously-undocumented
breaking change (`test-node`'s `scopeNote` output field silently removed by `P7-009`, never logged in
`Documentation~/generated/migrations.md`'s own "MCP surface migrations" section despite existing
specifically to record exactly this) — retroactively logged as part of this gate's own
documentation-consistency pass, live-verified (`McpDocumentationGeneratorsTests`, 11/11) before being
folded into the gate's own commit. Three of `P7-001`'s five open questions produced new,
required-before-`1.0` follow-up cards, not left as prose: `P7-018` (promote tree-format `v2` to the
production default, unblocking `ReferenceCompilationPolicy.Phase1`'s Agent/Shared capability flags),
`P7-019` (a JSON Schema for the aggregate `get_project_manifest` response), `P7-020` (a CI-enforced
public-API diff check) — plus `P7-021` from the regression failure above. None of these four are
required for `P7-016`'s own verdict; all are required before a clean `1.0.0` release. `README.md`/
`CHANGELOG.md` had no Phase 7 section at all (both stopped at the Phase 6 paragraph/bullet) and were
updated, checked against a fresh claims inventory. **Phase 7 is complete**: `P7-001` through `P7-016`
are all `Done`. See `Planning~/Evidence/P7-GATE/README.md`.

Same day, `P7-021` (API-reference generator package-root resolution fix) is also **done**.
`MCP/Documentation/McpApiReferenceGenerator.cs`'s `CollectTypeSummaries()` no longer hardcodes
`Application.dataPath + "/AIBT"` — it now mirrors `McpDocumentationGeneratorsTests
.FindGeneratedDocumentationDirectory()`'s own already-correct `UnityEditor.PackageManager
.PackageInfo.FindForAssembly` resolution, falling back to the old assumption only when Package
Manager genuinely doesn't know about the assembly (this repo's own host-embedded dev layout) — the
summary-matching logic itself is unchanged. A new pinning test proves the fallback branch locally;
the real-UPM-consumer branch (unfakeable in a plain EditMode test) was proven live via a fresh
detached harness whose `file:` package pointed directly at the host project's own `Assets/AIBT` —
full regression there: **1271/1271 passed, 0 failed**, including the exact test `P7-016`'s gate
found failing, now passing. A related, out-of-scope finding was disclosed rather than fixed:
`Tests/Editor/CodeGen/Generation/GeneratedArtifactContractTests.cs` has its own, independent,
pre-existing `PackageInfo.FindForAssembly`-non-null assertion that fails in this same host-embedded
layout for the identical underlying reason — a different, untouched file. No committed generated doc
changed (the fix reproduces byte-identical host-project output, as designed). See
`Planning~/Evidence/P7-021/README.md`.

Same day, `P7-017` (evidence-artifact size discipline) is also **done**. Its own cited baseline
("`P4-008`'s WebGL build, 8.3 MB committed") turned out not to exist — a fresh `git ls-files` sweep
found `P4-008`'s real build binaries (a 21 MB Android APK, a 6.1 MB WebGL `.wasm.unityweb`) were
never committed at all, excluded by pre-existing, targeted `.gitignore` files already living in
their own `Results/` folders (`Benchmarks~/Phase4/Platform/{Android,Web}/Results/.gitignore`) — a
real, already-established precedent nobody invented for this card. The same sweep found `P7-003`'s
22 MB Profiler capture is the single largest tracked file in the whole repository by roughly 18x
(next largest: 1.2 MB), a one-off outlier with no growth trend, and that its real cost inside git's
own pack compression is only 3.25 MB (6.8x ratio) — a materially smaller practical concern than the
raw file size suggests. Recommendation, put to the owner via the plan itself rather than adopted
unilaterally: no hard size threshold, CI gate, or Git LFS — instead a short addition to `AGENTS.md`'s
"Quality gates" section states the principle this project already followed implicitly (regeneratable
build intermediates are `.gitignore`d; genuine evidence artifacts are committed even when large;
anything approaching double-digit MB gets flagged to the owner at commit time, exactly what `P7-003`
already did). `P7-003`'s own file was not touched. See `Planning~/Evidence/P7-017/README.md`.

Same day, `P7-019` (aggregate project-manifest JSON Schema) is also **done**. `Schemas~/
project-manifest.schema.json` (new) is a `oneOf` over the two real response shapes
`MCP/McpToolDispatcher.GetProjectManifest` actually produces — discovered by reading the real
wrapper, not assumed from `ProjectManifestQuery.Build()` alone (which never emits the
`skippedTreeFiles` field the wrapper always injects, nor the completely different minimal shape
returned when `.aibt/policy.json` can't be read). `Verify-Schemas.ps1` gained two new real-document
validation pairs — a fresh finding corrected the card's own claim that the script "currently
validates 6 schemas": only 2 were actually wired before this card (`work-item-index`, `policy`); the
other 4 pre-existing schemas, including `node-manifest.schema.json`, were never permanently wired
despite `P7-001`'s own one-time manual check. **A real, previously-undiscovered production bug was
found live while generating the schema's own example, not fixed inside this card (out of its own
Allowed-changes fence), and spun off as `P7-022`**: `get_project_manifest`, called with the real
production `projectRoot` value every real caller actually uses, has never successfully found
`.aibt/policy.json` in this repository's real host-embedded layout — confirmed reproducibly, twice,
live — always returning the degraded error shape instead of real capabilities/policy/tree data. The
real success example was produced by calling `ProjectManifestQuery.Build()` directly against real
project data, bypassing only the buggy path-derivation step; the real error example is the genuine
captured bug evidence, committed as one of the schema's own two validated documents. A deliberately
malformed third copy (uncommitted) correctly fails validation, naming its exact injected defects. See
`Planning~/Evidence/P7-019/README.md`.

Same day, `P7-022` (`get_project_manifest` policy-path fix) is also **done** — but not as a code fix.
Live re-verification of the card's own claim found it wrong: `MCP/McpToolDispatcher.cs`'s
`ProjectRootParent(Application.dataPath)` resolution always finds the true Unity project root
regardless of AIBT's install topology (unlike `P7-021`'s bug, this one is not topology-dependent,
since `Application.dataPath` is always `<ProjectRoot>/Assets`), and that resolution is the
documented, intentional convention — independently confirmed in `Planning~/Evidence/P6-005/
README.md` and `Planning~/Evidence/P6-007/README.md`, both predating this card, neither checked
before it was written. The real defect was that `C:\UnityProjects\Modules\.aibt\policy.json` (this
host project's own real project root) never existed; `Assets/AIBT/.aibt/policy.json`, the file the
card pointed to, is AIBT's own internal self-hosting policy, not a policy belonging to the `Modules`
host project. Put to the owner rather than assumed: a real `.aibt/policy.json` was added at the
`Modules` project root (in the parent repository, outside the AIBT submodule — no line of
`MCP/McpToolDispatcher.cs` changed). `get_project_manifest`, called live with the real production
`projectRoot` against the real, open project, now returns the real success shape. See
`Planning~/Evidence/P7-022/README.md`.

Same day, `P7-020` (CI-enforced public API diff check) is also **done**. One part of the card's own
text didn't hold up: its claim that the headless dump technique "already uses" `windows-2022` is
wrong — `Get-FullPublicApi.ps1` only works via a real, licensed Unity Editor in batch mode, and
`validation.yml`'s `windows-2022` `static` job has no Unity installed anywhere (no `game-ci`/
`unity-builder` step exists in this repo's CI); only the `unity` job (self-hosted, still blocked by
`P0-005`) has Unity access. Resolved by adding the new check as a step *inside* that existing job
rather than a second one — no new runner dependency, and like the rest of that job, unproven in real
GitHub Actions until `P0-005` closes (disclosed, matching `P7-015`'s own precedent). `Get-
FullPublicApi.ps1` gained a `-BaselinePath` parameter using a content-based set difference
(`Compare-Object`), deliberately avoiding the positional-diff trap that produced `P7-016`'s own false
"5 removed members" signal (`Planning~/Evidence/P7-GATE/README.md`) — only a baseline line genuinely
absent from the fresh dump ever fails the check; additions never do. New stable baseline
`Tools~/Verification/P7/Audit/Baseline/public-api-baseline.txt`, seeded from `Planning~/Evidence/
P7-GATE/public-api.txt` and confirmed live to still match today's real compiled surface (425
types/2130 members, unchanged since `P7-016`). Both acceptance criteria proven through the real
isolated-Unity-harness mechanism: a real positive run (passed cleanly) and a real negative run
against a temporary, uncommitted synthetic baseline line (failed loudly, exact line named, exit code
1) — no production symbol was touched to prove the negative case. See `Planning~/Evidence/P7-020/
README.md`.

Same day, two new `Draft` cards were added to `1.0` scope on direct owner request, not spun off from
a gate finding: `P7-023` (showcase example behavior trees — live-demoed this session that the
existing `AIBT Graph` editor already renders a real tree, `tree.golden.parallel-decorator`, cleanly
with zero diagnostics; the owner had never actually looked at one, and no illustrative example
exists anywhere in the repo, only tiny test/golden fixtures) and `P7-024` (a showcase report/chart
built from already-existing, already-validated Job-system-vs-non-Job scheduling benchmark data —
`Benchmarks~/Phase4/Scheduling`/`CostCurves`/`AutoComparison` plus `P7-002`'s own Windows Player run
already measure exactly this, just never packaged as something readable at a glance). Both
explicitly scoped as reusing/repackaging real existing capability and data, not new research.

Same day, a live usability review of `P7-023`'s own demo (the owner watching a real tree render in
`AIBT Graph` for the first time) surfaced four more real, verified gaps, each spun off as its own new
`Draft` card, all confirmed in `1.0` scope: `P7-025` (the graph editor has no pan/zoom/selection
anywhere — confirmed by grep, no `GraphView` in the whole codebase ever calls `AddManipulator` with
the standard `ContentZoomer`/`ContentDragger`/etc.; node titles render the raw dotted `TypeId` since
`NodeManifest` has no display-title field; and `.aibt.layout.json` is never actually read despite
`P3-005`'s own persistence subsystem, `LayoutPersistenceController.Load`, already existing and simply
never being called), `P7-026` (validate, with a real Player build-size diff, the architectural claim
that trees are cheap data and build size scales with node *types* not tree *count* — never actually
measured; explicitly not the same concern as `P7-017`, confirmed with the owner), `P7-027` (a real
production Play-mode debugger with live animated visual state — this is exactly `ADR-P7-010`'s own
already-accepted "future, not-yet-numbered implementation card," the single most-repeated disclosed
gap in the whole project's history, now finally numbered), and `P7-028` (the built-in node catalog is
only 11 structural composites/decorators, zero condition/utility/action leaves — confirmed by reading
`BuiltInNodeManifests.cs` directly).

Same day (2026-09-04), starting `P7-018` per the owner's own priority order ("functionality before
polish"), re-reading its required dependency `P6-014` before planning found the card's own premise
contradicted an already-accepted decision: `ADR-P6-014` (2026-08-31) found flipping Agent/Shared
capability flags unlocks nothing, since `ReferenceCompiler.cs`'s `AIBT3012` check and
`ReferenceBlackboardStorage.cs`'s matching check both reject Agent/Shared scope *unconditionally*,
never consulting the policy flags at all — and decided **not implemented, deferred**. Since
`TreeDocument.CreateVersion2`'s only real difference from v1 is the Agent/Shared scope contracts,
promoting v2-to-default without real Agent/Shared support would have been pure format churn. Put to
the owner rather than silently worked around: the owner chose to reopen `ADR-P6-014`'s "deferred"
conclusion and commission the real implementation — recorded as an Addendum on the ADR itself, and
`P7-018`'s own card fully rewritten to the real scope (make `ReferenceCompiler`/
`ReferenceBlackboardStorage` policy-aware, mirroring `TreeValidator`'s already-correct pattern,
without touching `ReferenceCompilationPolicy.Phase1`'s own shared defaults — already proven safe by
`ADR-P6-014`'s own spike). Also fixed, found the same way: `P6-014`'s own task-card file was still
marked `Status: Draft` despite its evidence being accepted and `work-items.json` already correctly
recording it `done` since 2026-08-31 — the same class of drift `P7-016`'s gate fixed on four other
cards, now fixed on this one too.

Same day, `P7-018` (real Agent/Shared blackboard scope) is also **done** — one more real re-scoping
happened mid-planning, before any code was written: `Runtime/Blackboard/Storage/
ReferenceBlackboardStorage.cs`, read in full, turned out to be architecturally a flat
single-tree-instance byte arena with no shared/cross-instance concept at all — real reference-side
runtime support would mean new storage architecture from scratch. Meanwhile `Authoring/Compilation/
Generated/GeneratedScopeCompiler.cs` and `Runtime/Blackboard/Native/Shared/
NativeSharedContextOwnerV1.cs` were found, by direct reading not assumption, to **already fully
implement** Agent/Shared blackboard scope for the native backend — unconditional compilation with
real reduction semantics, and a complete contribution/reduction/multi-instance runtime, both already
covered by passing test suites. Put to the owner again: build the missing reference-side storage
from scratch, or wire up only the already-working native path. **The owner chose native-only.** The
real remaining gap was narrower than either framing: MCP's authoring/verification surface is
deliberately, exclusively reference-executor-bound, and `ReferenceCompiler.cs`'s own scope check was
*unconditional* — never consulted the policy flags at all, unlike `TreeValidator`'s own matching
check. Fixed to be policy-aware, mirroring `TreeValidator` exactly; live-discovered while testing
that `ReferenceCompiler.Compile` already runs `TreeValidator.Validate` first with the same
policy-derived options, so this fix is the *second* gate, reached only once validation already
passes — exactly `ADR-P6-014`'s own two-layer finding. MCP's `create_tree`/`compile`/`validate` now
read `.aibt/policy.json`'s opt-in (mirroring `Validate`'s own already-established pattern), writing
real `formatVersion: 2` specifically for documents that declare Agent/Shared entries; `simulate`
needed no code change at all — it already, automatically produces a clear diagnostic since it
hardcodes `Phase1` and never reads project policy. `ReferenceCompilationPolicy.Phase1`'s own shared
default stays untouched. Full regression: 392/392 (`AIBT.Editor.Tests`), and only 3 pre-existing,
unrelated failures across the entire 1645-test host-project run. Native capability reconfirmed live,
not rebuilt: `GeneratedScopeCompilerTests` 11/11, `AIBT.NativeSharedBlackboard.Tests` 36/36. Also
fixed, found while reading `P7-018`'s own required dependency: `P6-014`'s task-card file was still
marked `Status: Draft` despite its evidence being accepted and `work-items.json` already correctly
recording it `done` since 2026-08-31 — the same class of drift `P7-016`'s gate fixed on four other
cards, now fixed on this one too. See `Planning~/Evidence/P7-018/README.md`.

Same day, the owner live-reproduced three real bugs in already-shipped Phase 7 functionality
immediately after `P7-018` landed, filed together as new `Draft` card `P7-029`: `DocumentMigrator
.TryMigrate` (`P7-006`) silently drops a migrated document's `blackboard`/`description`/Agent-Shared
scope contracts and a migrated node's `bindings` (confirmed by reading the exact constructor calls —
both default the omitted fields to `null`); native hot reload (`P7-012`) can corrupt an actively
running `Sequence` after a child reorder, since the active call-stack frames are migrated
independently of the freshly-reset structural cursor with no reconciliation (the reference-executor
equivalent has no analogous bug only because `ADR-P7-011`'s own decision 3 uniquely widened the
*native* backend to migrate active instances at all — there is no existing correct pattern to
mirror); and native hot reload never copies `NativeHotReloadInstance.CooldownInitialized` at all
(confirmed by grep — zero references in the migration file), silently resetting cooldown state.
None were introduced by `P7-018`, though the first directly affects `P7-018`'s own new v2/Agent-Shared
documents once a migrated node is involved.

Same day, `P7-027` (production Play-mode host and live visual debugger) is also **done** —
applying `ADR-P7-010` to production, the single most-repeated disclosed gap across the whole
project. The ADR's own hedge ("driving `SchedulingPolicyDriver`'s (or a promoted equivalent's)
methods") turned out well-founded: `SchedulingPolicyDriver` is a benchmark harness whose every leaf
status is supplied by the caller in advance via a plain array, unable to drive a tree whose leaves
compute their own real outcome. Put to the owner rather than deferred; the owner asked to resolve
real dispatch now. Resolved by reading the engine's own primitives directly:
`SchedulingPolicyDriver.TryRunImmediate`'s own loop is a thin wrapper over already-`internal`
`NativeLifecycleMachineV1.TryAdvance`/`TryCompleteDispatch` — the new `Runtime/Integration/
ProductionTreeHost.cs` drives these directly, resolving each real Tick through a delegate injected
at construction (keeping the host itself free of any `AIBT.Authoring` dependency, matching the ADR's
own reasoning). A real `CS0051` compile error, not predicted at planning time, was found and fixed
during implementation: `NativeLifecycleNodeKindV1` is itself `internal` and cannot appear in a public
method's signature, resolved by deriving it internally via the existing `NativeHotReloadInstance
.ClassifyKind` helper — a cleaner API as a side effect. `TraceTimelineWindow` gained a live
`EditorApplication.update`-driven auto-refresh (while attached and in real Play mode) plus a
~200ms border-alpha fade animation on node highlighting, the owner's own explicit ask. Live proof
via Unity MCP in real Play mode: the load-bearing check was reading the trace model's step count
grow from 17 to 47 across two calls **without an explicit `Refresh()` in between**, proving the live
auto-refresh subscription genuinely works. Full regression 396/396 (`AIBT.Editor.Tests`), only the
same 3 pre-existing unrelated failures across the whole 1649-test host-project run; no leak
diagnostic on real Play-mode teardown. See `Planning~/Evidence/P7-027/README.md`.

Remaining Phase 7 work, per the owner's own priority ("functionality before polish"): `P7-028`
(node library) next, then `P7-026` (build-size validation); `P7-029` (three data-loss/state bug
fixes, filed after `P7-018`) alongside this queue, not yet sequenced ahead of it; `P7-023`/`P7-024`/
`P7-025` (editor/showcase cards) last. `1.0.0` itself remains the owner's own separate release
decision.

### Review follow-up scopes (2026-09-04)

The owner requested a fresh validation of eleven review findings and scoped cards, not code fixes.
Current source was rechecked against `66fa058`; fresh Unity probes reconfirmed document data loss,
the active Sequence reorder failure, terminal-instance rejection and the host's zero clock, plus
the actual Editor log path mismatch. The cards distinguish these from earlier probes and static
control-flow findings. Concurrent P7-028 work remains outside this review's changes.

| Scope | Card | Findings | State |
| --- | --- | --- | --- |
| Document/native state migration | [P7-029](Tasks/P7/P7-029-migration-and-hot-reload-data-loss-fixes.md) | Data preservation, active stack/cursor reconciliation, cooldown state | Done 2026-09-04; owner-approved active-child lifecycle |
| Production host execution | [P7-030](Tasks/P7/P7-030-production-host-execution-contract.md) | Terminal handling, clock, Action lifecycle, Budgeted integration | Done 2026-09-04; owner-approved implementation |
| MCP node development | [P7-031](Tasks/P7/P7-031-mcp-node-development-boundaries.md) | Assets containment, attempt-bound compilation, complete TCP recipe | Done 2026-09-04; 46/46 focused and live post-apply compile |
| Native scheduler recovery | [P7-032](Tasks/P7/P7-032-native-scheduler-error-recovery.md) | Rejected completion buffers in SameFrame and Pipelined | Done 2026-09-04; 126/126 scheduling tests |

Formulation corrections: host terminal handling is P2 (the terminal result is not itself lost);
automatic restart is not an assumed requirement. The Tick-only delegate's documented contract
is accurately described, but it cannot connect the general Action lifecycle. Migration preserves
semantic fields rather than raw JSON formatting; a legitimate composite reset may repeat a child,
so P7-029 must resolve the precise active-child lifecycle before implementation. P7-027's historical
completion evidence does not close these newly identified host gaps. The new cards do not reorder
the owner's queue or authorize implementation. At that review stage no runtime fix or new release
readiness was claimed.

**P7-029 is done** after owner agreement on cancellation before new-order traversal. Document
fields and compatible cooldown state are preserved; reordered active descendants receive their
required Abort/Exit before the reset composite proceeds. Focused tests: 90/90; full EditMode:
1685/1688 with the same three baseline failures. Separate live probes confirm document v1/v2
preservation, exact Sequence callbacks and cooldown boundaries. Parallel/suspended-state migration
remains a separate disclosed gap. See [P7-029 evidence](Evidence/P7-029/README.md).

Following explicit owner approval, **P7-030 is done**. The host now stops after terminal results,
supplies scaled Unity time or a controlled clock, invokes the complete Action lifecycle, and
resumes budgeted execution across actual frames. Disable/enable and scene teardown were exercised
in Play mode through the Unity player loop. Focused tests: 55/55; full host EditMode: 1666/1669,
with the same three unrelated baseline failures. Warmed host measurement: 0 bytes across 1000
calls in Immediate and budget 1; no Player/platform build claim. See
[P7-030 evidence](Evidence/P7-030/README.md) and the [host guide](../Documentation~/production-host.md).

`P7-028` (production-ready built-in node library) is **done**. Investigation before planning found
three parallel node-authoring mechanisms, not one — the manual `NodeManifest` builder the existing
11 composites use, `IReferenceLeafBehaviorProvider` (managed, reference-only, `P7-008`), and
`[AibtBurstNode]` attribute codegen (real native Burst execution, leaf-only since `BurstNodeKind` has
no Decorator/Composite value). Owner chose both mechanisms at once, scoping the card to leaves.
Two further architectural walls surfaced mid-implementation, each put to the owner rather than
guessed past: a native `[AibtBurstNode]`'s only blackboard-access mechanism (`GeneratedHandle` config
fields) is unsupported by the reference compiler at all, so the planned `aibt.core.blackboard-bool-
condition` node was dropped from scope rather than shipped non-functional on one backend; and
`AIBT.CodeGen`'s `BurstNodeGenerator` (`AIBT5012`) permanently freezes the `aibt.core.` namespace
against any live `[AibtCatalogSet]` shard — its authority-merge treats a shard's own declared
identity as either unauthorized (authority unchanged) or a duplicate (authority amended to match),
with no way through — so the owner chose a new always-on namespace, `aibt.stdlib.*`, for built-in
leaves that carry a real native declaration. Delivered: `aibt.stdlib.wait` (Action, ticks-based delay)
and `aibt.stdlib.random-condition` (Condition, percentage gate off the native side's real per-instance
Burst random stream, a disclosed non-bit-identical `System.Random` on the reference side). Each
ships both a real native Burst execution path and a real reference-executor path; where the compile-
time ABI enforcement requires exact canonical-JSON parity between the two, a new regression test
(`BuiltInLeafManifestsTests`) reads the shard's own generator-emitted metadata back via reflection so
future drift fails at the unit-test level, not only a full Editor domain reload. Full regression
396/396 (`AIBT.Editor.Tests`, 392 baseline + 4 new), only the same 3 pre-existing unrelated failures
across the whole ~1653-test host-project run; public API diff purely additive (10 new members, zero
removals). Live proof: a real tree using `aibt.stdlib.wait`, validated and compiled through the real
`McpVerificationToolDispatcher` against the project's own adopted `.aibt/policy.json`, came back
`valid: true` / `success: true` with zero diagnostics. See `Planning~/Evidence/P7-028/README.md`.
