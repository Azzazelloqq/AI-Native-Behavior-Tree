# P2-013 — Native lifecycle core and memory composites

Status: `Done`

## Objective

Implement the packed nonrecursive Enter/Tick/Abort/Exit state machine plus MemorySequence and MemorySelector as the first native semantic slice.

## Depends on

- `P2-012`.

## Required reading

- `Documentation~/specifications/execution-semantics-v1.md`
- `Documentation~/specifications/reference-executor-machine-v1.md`
- `Documentation~/specifications/determinism-v1.md`

## Allowed changes

- `Runtime/Execution/Native/Core/`
- `Runtime/Execution/Native/Composites/Memory/`
- `Tests/Runtime/NativeExecution/LifecycleAndMemory/`

## Forbidden changes

- Recursive execution, hidden child work, managed handler registry, scheduler/jobs policy, or semantic changes to P1.

## Deliverables

- Atomic packed lifecycle machine, leaf execution, abort traversal, and memory composite transitions.

## Acceptance criteria

- Enter/Tick/Abort/Exit order, terminal exposure, once-per-update Tick, generations, memory lifetime, reentrancy rejection, and fault cleanup match P1.
- Child selection/acceptance each consume exactly one semantic step.
- Abort is deepest-first; terminal-pending Exit wins and suppresses later child acceptance.
- Empty/success/failure/running composite matrices match P1 traces.
- No recursion, managed allocation, reflection, or interface dispatch occurs after initialization.

## Required verification

```text
shared P1 lifecycle/memory behavior matrix
every illegal transition and descriptor negative
deep-tree stack/capacity test
warm allocation and Burst compile checks
```

## Handoff notes

- Reference executor remains unchanged and is the oracle.
