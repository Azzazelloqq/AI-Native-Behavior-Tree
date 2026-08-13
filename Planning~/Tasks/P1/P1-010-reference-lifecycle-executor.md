# P1-010 — Reference lifecycle and leaf executor

Status: `Done`

## Objective

Execute one compiled tree instance synchronously with exact Enter/Tick/Abort/Exit behavior for reference condition and action leaves.

## Depends on

- `P1-008`
- `P1-009`

## Required reading

- `specifications/execution-semantics-v1.md`
- `specifications/update-phases-v1.md`
- `specifications/trace-v1.md`
- `specifications/reference-executor-machine-v1.md`

## Allowed changes

- `Runtime/Execution/Reference/Core/`
- `Runtime/Execution/Reference/Leaves/`
- `Runtime/State/Reference/`
- `Tests/Runtime/ReferenceExecutor/Lifecycle/`

## Forbidden changes

- Jobs, Burst dispatch, production user-node API, composites, or per-frame host integration. Phase 1 handlers remain internal.

## Deliverables

- Explicit frame-machine lifecycle/state, stack/cursor, reference leaf dispatch, Runtime trace record contract, trace recorder test double, and explicit terminal-root restart API.

## Acceptance criteria

- Every lifecycle path and illegal retick/reentrancy case is tested.
- Abort is deepest-first and followed by `Exit(Aborted)` exactly once.
- Node memory is zeroed before enter and cleared after exit.
- Root never implicitly restarts in the same update.

## Required verification

- Focused lifecycle tests with exact trace assertions.
