# P2-010 — Native operations, completions, and command streams

Status: `Done`

## Objective

Implement bounded native operation state, completion normalization/persistence, per-instance command emission, and deterministic post-execution merge.

## Depends on

- `P2-002`.
- `P2-006`.
- `P2-009`.

## Required reading

- `Documentation~/specifications/async-and-commands-v1.md`
- `Documentation~/specifications/update-phases-v1.md`
- `Documentation~/specifications/determinism-v1.md`

## Allowed changes

- `Runtime/Commands/Native/`
- `Tests/Runtime/NativeExecution/CommandsAndAsync/`

## Forbidden changes

- `Task`, coroutines, threads, hidden managed queues, ID reuse, unbounded payloads, or worker-order publication.

## Deliverables

- Native operation ledger, persistent completion inbox/high-water state, Execute/Cancel buffers, payload arena, and deterministic merge.

## Acceptance criteria

- Operation IDs never reuse across restart; cancel/consume are idempotent and preserve tombstones.
- Duplicate/stale/unknown/cancelled/consumed completions match P1 classifications and ordering.
- Valid pending completion survives until consume/cancel.
- Commands merge by `(phase, treeInstanceId, sequence)` regardless of worker completion.
- Sequence, payload, operation, and capacity overflow never wrap or partially publish.
- No managed allocation occurs after initialization.

## Required verification

```text
P1 async normalization parity matrix
permuted job completion and merge tests
restart/fault/late-completion tests
capacity/overflow atomicity tests
allocation/lifetime probes
```

## Handoff notes

- Command materialization for host consumers occurs after jobs and outside the measured Burst tick region.
