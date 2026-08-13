# P1-011 — Memory sequence and selector

Status: `Done`

## Objective

Implement `MemorySequence` and `MemorySelector` in the reference executor.

## Required reading

- `specifications/execution-semantics-v1.md`
- `specifications/reference-executor-machine-v1.md`

## Depends on

- `P1-010`

## Allowed changes

- `Runtime/Execution/Reference/Composites/Memory/`
- `Tests/Runtime/ReferenceExecutor/MemoryComposites/`

## Forbidden changes

- Reactive behavior, observers, scheduling, or generic configurable sequence semantics.

## Deliverables

- Explicit handlers with retained child cursor and empty-child behavior.

## Acceptance criteria

- Successful/failed prior children are not reticked while a later child runs.
- Multiple immediate terminal children can advance in one pass.
- Empty sequence succeeds and empty selector fails.
- Parent abort propagates exactly once to the active child subtree.

## Required verification

- Table-driven status and exact lifecycle-trace tests.
