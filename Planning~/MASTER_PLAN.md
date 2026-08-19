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

The Phase 1 semantic slice and local platform evidence are complete. The remote Unity workflow dependency remains unresolved and `P0-005`, `P0-006`, and `P1-019` retain their honest states. By explicit owner direction on 2026-08-14, Phase 2 implementation proceeded from the accepted `P1-018` semantic source without treating that infrastructure gate as passed. Phase 2 is complete: `P2-001` through `P2-025` are done, including Windows Player conformance (`P2-022`) and the Phase 2 integration gate (`P2-025`), accepted 2026-08-18 against commit `a78d10a0fb2f964d64e253b284ad1cf19730f936` — see `Planning~/Evidence/P2-GATE/`. Phase 3 (editor/layout) was decomposed into `P3-001` through `P3-013` on 2026-08-18. `P3-001` (the `OQ-005` graph-framework spike) is done and **rejects Unity Graph Toolkit** on measured evidence (no in-memory/transient graph support — every graph forces a real Unity YAML asset the moment a node is added; zero support anywhere in its public API for groups, comments, sticky notes, or reroutes, all required by `Documentation~/editor-and-layout.md`) — see `Planning~/Evidence/P3-001/`. A second spike, `P3-014`, was decomposed and run the same day and **recommends adopting `UnityEditor.Experimental.GraphView`**: serialization control and testability pass (standalone construction, no asset backing forced, headless `EditorWindow` hosting succeeded), extensibility mostly passes (native `Group`/`StickyNote` types; reroutes need a custom element on `Edge`, following Shader Graph/VFX Graph precedent), large-graph construction performance shows no red flag, and a support-risk reflection check found zero `[Obsolete]`-attributed members in the installed `6000.5.8f1` Editor — see `Planning~/Evidence/P3-014/`. `AIBT-012` was accepted by explicit owner direction on 2026-08-18: adopt `UnityEditor.Experimental.GraphView`, rejecting Unity Graph Toolkit — see `Documentation~/decisions/ADR-P3-001-editor-graph-framework.md` and `Documentation~/decisions/ADR-P3-014-editor-graph-framework.md`, both linked from `Documentation~/decisions.md`. `P3-002` (layout model v1 contract) is done: `Documentation~/specifications/editor-layout-v1.md` defines `*.aibt.layout.json` — see `Planning~/Evidence/P3-002/`. `P3-003` (graph adapter foundation, read-only) is done: a `GraphView`-backed read-only adapter over `TreeDocument` — see `Planning~/Evidence/P3-003/`. `P3-004` (deterministic auto-layout service) is done: a post-order tidy-tree algorithm producing canonical `.aibt.layout.json` bytes — see `Planning~/Evidence/P3-004/`. `P3-005` (manual organization and layout persistence) is done: groups/notes/reroutes extend `LayoutDocument`, a strict `Newtonsoft.Json`-based reader/writer round-trips them, and `Editor/Organization/` adds pin/group/note/reroute operations, undo/redo, and load/save persistence (which now also resolves `P3-004`'s auto-layout fallback at the persistence layer) — see `Planning~/Evidence/P3-005/`. `P3-006` (semantic graph editing) is done: `Editor/Editing/` adds add/remove/connect/disconnect/set-parameter operations gated by the real `ReferenceCompiler`/`TreeValidator` pipeline (no separate weaker validation), plus undo/redo — see `Planning~/Evidence/P3-006/`. None of `P3-004`/`P3-005`/`P3-006` are wired into `Editor/Graph/`'s live UI (out of their allowed scope; flagged as a follow-up each time). `P3-007`, `P3-008`, `P3-009`, `P3-010`, and `P3-012` are now `Ready`; `P3-011` and `P3-013` remain `Draft`, blocked on their own further dependencies.
