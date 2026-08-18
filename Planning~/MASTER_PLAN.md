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

The Phase 1 semantic slice and local platform evidence are complete. The remote Unity workflow dependency remains unresolved and `P0-005`, `P0-006`, and `P1-019` retain their honest states. By explicit owner direction on 2026-08-14, Phase 2 implementation proceeded from the accepted `P1-018` semantic source without treating that infrastructure gate as passed. Phase 2 is complete: `P2-001` through `P2-025` are done, including Windows Player conformance (`P2-022`) and the Phase 2 integration gate (`P2-025`), accepted 2026-08-18 against commit `a78d10a0fb2f964d64e253b284ad1cf19730f936` — see `Planning~/Evidence/P2-GATE/`. Phase 3 (editor/layout) is the next assignable frontier; per `Planning~/WORK_PACKAGES.md` its entry gate requires a dedicated decomposition task and the `OQ-005` graph-framework spike before implementation.
