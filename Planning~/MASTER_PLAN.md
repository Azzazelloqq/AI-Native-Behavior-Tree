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
- `Review`: implementation and handoff exist; independent verification is pending.
- `Blocked`: a concrete unresolved dependency prevents progress.
- `Done`: acceptance criteria and Definition of Done were independently satisfied.

Only the coordinator updates the machine-readable index. Individual implementation agents report status in their handoff to avoid merge conflicts.

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

Future phase gates are defined at work-package level and become atomic only after their prerequisite implementation evidence exists.

## Parallelism rules

- Tasks may run concurrently only when the dependency graph permits it and their exclusive write paths do not overlap.
- Concurrent agents use separate Git worktrees or equivalent isolated branches.
- Shared files such as package metadata, changelog, task index, and assembly definitions are integration-owner files.
- Tests may be owned separately from implementation only when the expected behavior is already normative.
- A contract author cannot be the sole acceptance reviewer of the implementation using that contract.

See `PARALLELIZATION.md` for safe execution waves.

## Current assignable frontier

P1-001 becomes ready after P0-002 is merged. Android and CI evidence may proceed from the verified toolchain; the representative Web spike follows P1-018. P0-006 and P1-019 then close the platform/evidence and semantic gates without a dependency cycle. Agents use `work-items.json`, not phase numbers, to determine readiness.
