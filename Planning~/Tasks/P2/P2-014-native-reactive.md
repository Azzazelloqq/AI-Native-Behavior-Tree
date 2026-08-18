# P2-014 — Native reactive composites

Status: `Done`

## Objective

Implement ReactiveSequence and ReactiveSelector replacement semantics in packed native state.

## Depends on

- `P2-013`.

## Required reading

- `Documentation~/specifications/execution-semantics-v1.md`
- P1 reactive behavior and trace tests.

## Allowed changes

- `Runtime/Execution/Native/Composites/Reactive/`
- `Tests/Runtime/NativeExecution/Reactive/`

## Forbidden changes

- Speculative candidate Enter, worker-parallel children, observer behavior, or changes to P1 semantics.

## Deliverables

- Packed reactive cursor/replacement state and scoped abort integration.

## Acceptance criteria

- A new eligible update reevaluates from the correct ordinal exactly once.
- Old subtree aborts deepest-first and fully exits before any candidate Enter.
- Nested reactive owners choose the shallowest replacement and do not duplicate reset.
- Results, generations, step counts, and trace reason/source match P1.
- Suspended retained branch paths remain representable without managed frame lists.

## Required verification

```text
P1 reactive table and exact-trace parity
nested/reactivated/same-child generation tests
capacity and invalid descriptor negatives
allocation/Burst checks
```

## Handoff notes

- Observer-triggered reactive changes are integrated later by P2-016.
