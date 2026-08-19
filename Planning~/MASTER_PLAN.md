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
