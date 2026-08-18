# P2-015 — Native Parallel and core decorators

Status: `Done`

## Objective

Implement packed semantic Parallel plus Inverter, Succeeder, Failer, finite Repeater, Timeout, and Cooldown.

## Depends on

- `P2-013`.

## Required reading

- `Documentation~/specifications/execution-semantics-v1.md`
- `Documentation~/specifications/time-and-random-v1.md`
- P1 parallel/decorator behavior tests.

## Allowed changes

- `Runtime/Execution/Native/Composites/Parallel/`
- `Runtime/Execution/Native/Decorators/`
- `Tests/Runtime/NativeExecution/ParallelAndDecorators/`

## Forbidden changes

- Concurrent execution of siblings within one instance, completion-order decisions, hidden immediate loops, or host clock reads.

## Deliverables

- Fixed-capacity parallel branch state and all listed decorator transitions/config decoders.

## Acceptance criteria

- Parallel visits children in semantic order, decides only after the full visit, retains terminal children, and aborts remaining branches in reverse semantic order.
- Threshold/tie-break matrices match P1 and remain independent of worker scheduling.
- Repeater generations and Exit-before-reenter, Timeout equality/overflow, and Cooldown Instance persistence/restart match P1.
- Invalid configuration/layout faults before Enter.
- No managed allocation occurs after initialization.

## Required verification

```text
P1 parallel/decorator behavior and safety parity
nested retained branch and abort tests
signed-time boundary/overflow matrix
allocation/Burst checks
```

## Handoff notes

- Parallel is semantic; CPU parallelism remains across instances only.
