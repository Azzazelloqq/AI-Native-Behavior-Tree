# P4-003 — PipelinedJobs executor

Status: `Draft`

## Objective

Implement the `PipelinedJobs` execution policy (next-frame or explicit pipeline-stage latency, per `Documentation~/execution-and-scheduling.md`'s policy table) with proven semantic equivalence to the accepted reference oracle and to the already-implemented fixed policies — same observable results, latency only differs. `P2-019`'s own handoff explicitly deferred this to Phase 4.

## Depends on

- `P2-019` (batched same-frame Jobs executor — the scheduling/ownership/Reduce infrastructure this extends).
- `P2-020` (native behavior-case equivalence pattern this card's proof must follow).

## Required reading

- `Documentation~/execution-and-scheduling.md` ("Policies", "Semantic guarantees" — pipelined latency must be visible and never silently selected).
- `Documentation~/specifications/update-phases-v1.md`.
- `Documentation~/specifications/platform-backends-v1.md`.
- `P2-019`'s and `P2-020`'s evidence.

## Allowed changes

- `Runtime/Scheduling/Native/` (extends `P2-019`'s area).
- `Tests/Runtime/NativeExecution/Scheduling/`.

## Forbidden changes

- `Auto`/autotuning (separate cards).
- Any change to per-instance sequential semantics, Snapshot/Execute/Reduce/Publish phase order, or command/diagnostic/trace merge determinism `P2-019` already established.
- Silently defaulting or hiding the one-frame (or pipeline-stage) delay from the caller.

## Deliverables

- `PipelinedJobs` orchestration: work submitted this frame, results/completions surfaced on a defined later frame or explicit pipeline stage, never silently collapsed to same-frame latency.
- Semantic-equivalence proof against the reference oracle and against `Immediate`/`BatchedJobsSameFrame`'s own observable results (same golden cases, same final states — latency is the only permitted difference), following `P2-020`'s equivalence-testing shape.

## Acceptance criteria

- Every accepted Phase 1 golden case and Phase 2 native behavior-case fixture produces identical semantic results (root status, blackboard, commands, diagnostics) under `PipelinedJobs` as under `Immediate`, differing only in which frame/stage the result becomes observable.
- The pipeline delay is explicit and queryable by the caller (per "Pipelined and budgeted latency is visible and never silently selected") — nothing about it is inferred after the fact.
- One instance still cannot be scheduled twice concurrently; ownership guards from `P2-019` hold unchanged under pipelining.
- Unsupported domains/policies still return structured capability diagnostics without semantic fallback.
- Initialized pipelined execution performs zero managed allocation, matching the standard already established for `Immediate`/`BatchedJobsSameFrame`.

## Required verification

```text
Verify-Static.ps1
golden-case and native behavior-case equivalence matrix (Immediate vs. PipelinedJobs)
worker-order and cross-frame-boundary negative tests
Jobs/Burst Player compile
allocation/lifetime checks
```

## Handoff notes

- `P4-005` (`Auto`) selects among `PipelinedJobs` and the other fixed policies once this card and `P4-004` exist; it must not invent its own pipelining semantics.
- `P4-001`'s benchmark catalog has a placeholder entry for pipelined execution this card should fill in, not replace.
