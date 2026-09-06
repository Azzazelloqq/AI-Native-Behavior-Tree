# ADR P7-038: Bounded grouped generated-dispatch workspace

- Status: Accepted 2026-09-06
- Date: 2026-09-06
- Decision ID: AIBT-039

## Context

P7-037 establishes Player-safe generated catalog bootstrap and a reusable workspace for one
instance's dispatch request. P7-033 adds population grouping: a compatible generated catalog wave
must execute once for all of its participants, then commit each participant's own result.

`GeneratedDispatchGroupExecutorV2` currently materializes managed arrays, temporary native input
arrays and a new `NativeBurstDispatchBatchOwnerV2` for every group wave. P7-033 reuses its
lifecycle lanes and dispatch grouping lists, but cannot truthfully make the same allocation claim
for the remaining grouped-dispatch boundary. That boundary is reached by both
`BatchedJobsSameFrame` and `PipelinedJobs`, not only by a pipelined run.

The existing `NativeBurstDispatchWorkspaceOwnerV2` deliberately represents one request and borrows
one instance's mutable input. Reusing it for a group, silently resizing it, or invoking it once per
participant would change P7-037's explicit one-catalog-wave execution and atomic commit contract.

## Decision

### Ownership, key and lifetime

Introduce an internal `NativeBurstDispatchGroupWorkspaceOwnerV2`, cached by one
`ProductionTreeScheduler` for its lifetime. A cache entry is identified by the exact grouping
boundary already used by `DriveForcedJobsGroup`: `GeneratedBurstCatalogV2` **reference identity**
and `SchedulingProfile` **reference identity**. It is therefore never shared across schedulers,
catalogs or profiles, and does not add a public Runtime or generated-code API.

The cache serves both `ProductionBatchedGroupDriverV1` and
`ProductionPipelinedGroupDriverV1`. It is explicitly not owned by one pipeline run: that design
would allocate again at every later run and could not meet a scheduler-level warm-allocation claim.

The executor borrows an exclusive lease for one complete immediate dispatch wave:
`Prepare → Execute → Consume/Reject → Reset`. The lease releases before the next scheduler
operation. This exactly matches the current production scheduler, which invokes generated group
dispatch immediately. Scheduled multi-request execution remains an internal owner lifecycle test
case; integrating a scheduled group-dispatch lease into the production scheduler is outside
P7-038. A second immediate lease attempt is an internal structured phase failure, never a fallback
to single dispatch or a new allocation.

Scheduler disposal drains/rejects an outstanding wave according to P7-032, then disposes every
cached owner exactly once. Driver completion, host removal and normal pipeline teardown release
their current wave lease but leave an idle cache entry warm for later runs.

### Capacity

The workspace's fixed capacity is derived, without a heuristic multiplier, from:

1. the registered cohort's maximum participant count for this exact catalog/profile key;
2. the catalog's maximum per-case configuration and memory byte sizes;
3. the catalog's maximum random-state, binding, live-value, target-ordinal, session, staging,
   command and operation requirements per request; and
4. the exact fixed catalog metadata tables already validated by P7-037.

It owns persistent maximum-size participant, request, offset, input, result and transaction buffers.
Each wave uses explicit active counts and bounded native slices; capacity-only slots never enter the
catalog executor.

Cache creation and capacity expansion are configuration-time work: they must occur while
registration/profile-cohort changes are being prepared, never from the scheduler frame hot path.
Because a production cache lease is immediate, it cannot outlive that call and cannot collide with
registration. The implementation must make that registration/prewarm boundary explicit before
coding. It must not infer capacity from observed group sizes or grow when a wave arrives.

### Lifecycle

The owner state machine is:

`Idle → Prepared → Executing → Consumed → Idle`, plus `Disposed`.

- `Prepare` validates every active participant and every aggregate size before mutation or scheduling.
- `Execute` invokes the catalog exactly once, immediately or as one registered Job.
- `Consume` validates the completed batch and exposes per-participant result slices for commit.
- `Reset` clears only the active range and returns to `Idle` after all participants were committed
  or rejected.
- `Dispose` is refused while a Job is outstanding; the production owner completes or structurally
  rejects that work before disposal, following P7-032.

### Failure and commit

An invalid participant, aggregate overflow, executor failure, completion validation failure or
teardown failure returns the existing structured `BurstContextResult`. It does not resize storage,
fall back to single dispatch or commit partial memory/binding values. The existing group-executor
failure path completes every affected pending dispatch as failure and does not resume those hosts.

Successful consumption commits each participant's own memory, binding values and status from its
own bounded result slice, then resumes only those hosts through the existing lifecycle API.

Grouped command publication remains rejected. This decision does not define command
demultiplexing or change the existing explicit rejection.

## Consequences

- Repeated group waves and later scheduler runs for a warmed catalog/profile entry allocate neither
  managed memory nor native containers in the dispatch path.
- A group that is not covered by a prepared fixed capacity fails honestly rather than silently
  changing execution strategy.
- P7-035 can measure grouped-dispatch allocation only after this contract is implemented and
  proven; P7-034 remains independent.
- The implementation needs focused internal native ownership tests in addition to P7-037's
  single-request workspace coverage.

## Alternatives rejected

- **Global workspace pool:** obscures teardown and permits cross-scheduler ownership mistakes.
- **Run-owned workspace:** prevents reuse across later scheduler runs and leaves the same
  per-wave allocation problem at every run boundary.
- **Pipelined-only ownership:** omits `BatchedJobsSameFrame`, which reaches the same grouped
  generated-dispatch boundary.
- **Automatic grow-on-demand:** introduces non-deterministic hot-path allocation and hides capacity
  planning failures.
- **Repeated single-request dispatch:** violates one catalog wave/one executor invocation and loses
  grouped atomicity.
- **Reusing `NativeBurstDispatchWorkspaceOwnerV2` unchanged:** its one-request contract makes
  capacity-only slots or multiple mutable owners semantically invalid.
- **Changing scheduler policy/fallback on capacity failure:** mixes dispatch resource ownership with
  P7-033 policy selection.

## Approval

The earlier run-owned proposal was superseded by this scheduler-owned revision after fresh review.
Accepted by the owner on 2026-09-06. The implementation uses only scheduler-private immediate
leases and preserves scheduled lifecycle coverage outside the production scheduler path.
