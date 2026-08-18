# P2-016 — Native observer queue and reactive aborts

Status: `Done`

## Objective

Implement stable deduplicated observer evaluation and Self/LowerPriority/Both transitions between atomic native steps.

## Depends on

- `P2-007`.
- `P2-008`.
- `P2-014`.

## Required reading

- `Documentation~/specifications/blackboard-v1.md`
- `Documentation~/specifications/execution-semantics-v1.md`
- `Documentation~/specifications/trace-v1.md`

## Allowed changes

- `Runtime/Execution/Native/Observers/`
- `Tests/Runtime/NativeExecution/Observers/`

## Forbidden changes

- Recursive evaluation from a write, lifecycle callbacks for evaluator-only work, unstable queue order, or hidden observer steps.

## Deliverables

- Reverse adjacency, bounded deduplicated queue, result baseline, and scoped abort scheduling.

## Acceptance criteria

- Only changed writes queue; one observer is queued once and drained by runtime node index after each atomic step.
- First result seeds, repeated result is a no-op, and Running/invalid evaluation faults safely.
- Self/LowerPriority/Both directions, deepest-first abort, source/reason trace, and Exit-before-new-Enter match P1.
- Active owners are found across nested retained Parallel branches without mutating execution for nonmatching transitions.
- No managed allocation occurs after initialization.

## Required verification

```text
P1 observer matrix and exact-trace parity
dedup/multi-observer/nested-retained tests
write-during-callback nonrecursive test
allocation/Burst checks
```

## Handoff notes

- Shared-scope change queueing must follow the accepted post-Reduce visibility rule.
