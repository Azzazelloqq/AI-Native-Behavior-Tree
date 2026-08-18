# P2-019 — Batched same-frame Jobs executor

Status: `Done`

## Objective

Execute many tree instances through fixed BatchedJobsSameFrame scheduling while preserving sequential semantics per instance and deterministic post phases.

## Depends on

- `P2-008`.
- `P2-009`.
- `P2-010`.
- `P2-011`.
- `P2-018`.

## Required reading

- `Documentation~/specifications/platform-backends-v1.md`
- `Documentation~/specifications/update-phases-v1.md`
- `Documentation~/execution-and-scheduling.md`
- `Documentation~/decisions.md` (`AIBT-015`)

## Allowed changes

- `Runtime/Scheduling/Native/`
- `Tests/Runtime/NativeExecution/Scheduling/`

## Forbidden changes

- Parallel children inside one instance, Auto/autotuning, silent pipelined latency, managed/main-thread node execution inside jobs, or worker-order publication.

## Deliverables

- Fixed Immediate and BatchedJobsSameFrame orchestration, grouping, ownership guards, Reduce, command/diagnostic/trace merge, and metrics.

## Acceptance criteria

- One instance executes sequentially and cannot be scheduled twice concurrently.
- Snapshot/Execute/Reduce/Publish phase order is preserved.
- Results, Shared reductions, commands, diagnostics, and trace are invariant across worker count and batch partition.
- Unsupported domains/policies return structured capability diagnostics without semantic fallback.
- Initialized job execution performs zero managed allocation and all native dependencies/disposal are correct.

## Required verification

```text
1..N instance and batch partition matrix
worker-order randomized completion tests
phase-order and ownership negatives
Jobs/Burst Player compile
allocation/lifetime checks
```

## Handoff notes

- PipelinedJobs and Auto remain Phase 4 work; this card does not invent latency or thresholds.
