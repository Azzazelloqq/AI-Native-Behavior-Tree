# P2-017 — Native async node lifecycle

Status: `Done`

## Objective

Connect generated async actions to native operations, completion consumption, command emission, abort, restart, and fault cleanup.

## Depends on

- `P2-010`.
- `P2-012`.
- `P2-013`.

## Required reading

- `Documentation~/specifications/async-and-commands-v1.md`
- `Documentation~/specifications/execution-semantics-v1.md`

## Allowed changes

- `Runtime/Execution/Native/Async/`
- `Tests/Runtime/NativeExecution/Async/`

## Forbidden changes

- Managed async primitives, implicit cancellation, operation reuse, unbounded payloads, or callback-owned mutable global state.

## Deliverables

- Native async action state and context services for start/consume/cancel/fault.

## Acceptance criteria

- Start emits once per activation; Running reticks do not re-emit.
- Succeeded/Failed/Cancelled mapping, pending completion, typed payload, and first-valid consume match P1.
- Abort emits at most one matching Cancel after tombstoning; terminal-pending completion wins where specified.
- Fault compensation, restart persistence, stale generation/high-water, and late completion behavior match P1.
- No allocation or managed fallback occurs in the initialized path.

## Required verification

```text
P1 async behavior/ordering parity
restart/fault/late-completion matrix
capacity and payload mismatch negatives
allocation/Burst checks
```

## Handoff notes

- Host application of commands remains outside jobs.
