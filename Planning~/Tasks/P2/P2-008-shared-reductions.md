# P2-008 — Native Shared contributions and deterministic reducers

Status: `Done`

## Objective

Implement append-only per-instance Shared contributions and the deterministic Reduce phase without direct Shared mutation during Execute.

## Depends on

- `P2-003`.
- `P2-007`.

## Required reading

- `Documentation~/specifications/agent-shared-blackboard-v1.md`
- `Documentation~/specifications/update-phases-v1.md`
- `Documentation~/specifications/determinism-v1.md`

## Allowed changes

- `Runtime/Blackboard/Native/Shared/`
- `Tests/Runtime/NativeExecution/Blackboard/Shared/`

## Forbidden changes

- Worker-completion-order reduction, direct Shared writes in Execute, implicit reducer selection, or unbounded buffers.

## Deliverables

- Bounded contribution streams and Min/Max/Sum/Any/All/First/Last reducers plus only the custom reducer hook accepted by P2-003.

## Acceptance criteria

- Results and versions are invariant under worker count, batch partition, contribution insertion, and job completion order.
- Overflow, unsupported type/policy, capacity exhaustion, and malformed contribution fail atomically with stable diagnostics.
- Shared changes become visible only after Reduce and queue observers according to the accepted scope contract.
- No managed allocation occurs after initialization.

## Required verification

```text
permutation/property matrix for every reducer
integer/float/NaN/overflow/capacity negatives
multi-instance deterministic version tests
allocation and native lifetime checks
```

## Handoff notes

- Do not infer First/Last from worker order; use the normative stable contribution key.
