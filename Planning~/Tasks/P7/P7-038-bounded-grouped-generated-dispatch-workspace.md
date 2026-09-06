# P7-038 — Bounded grouped generated-dispatch workspace

Status: `Done`

Completed 2026-09-06: ADR AIBT-039 is accepted in
`Documentation~/decisions/ADR-P7-038-bounded-grouped-dispatch-workspace.md`; implementation and
verification evidence are recorded in `Planning~/Evidence/P7-038/README.md`.

## Objective

Remove per-wave managed and native allocation from production grouped generated dispatch without
changing its observable contract: one compatible catalog wave invokes that catalog executor once,
then commits each participant's own result atomically. A scheduler-owned, per-catalog/profile
workspace remains warm between compatible runs, has an explicit fixed capacity, and is disposed
with its scheduler.

P7-033's scheduler/lifecycle lanes are already reusable. This card owns the remaining grouped
dispatch allocation boundary in `GeneratedDispatchGroupExecutorV2`; it does not reopen scheduler
policy, profile, budget or latency semantics.

## Depends on

- `P2-012` — closed native dispatch ABI v2.
- `P7-032` — native ownership/recovery rules.
- `P7-033` — production pipeline run ownership and dispatch-wave integration.
- `P7-037` — production generated catalog/bootstrap and single-request workspace contracts.

## Required reading

- `Documentation~/decisions/ADR-P7-037-production-generated-dispatch-bootstrap.md`.
- `Runtime/Integration/GeneratedDispatch/GeneratedDispatchGroupExecutorV2.cs` and
  `GeneratedTreeDispatchAdapterV2.cs`.
- `Runtime/Execution/Burst/Dispatch/NativeBurstDispatchBatchOwnerV2.cs`,
  `NativeBurstDispatchWorkspaceOwnerV2.cs` and their tests.
- `Runtime/Scheduling/Production/ProductionPipelinedGroupDriverV1.cs`,
  `ProductionBatchedGroupDriverV1.cs`, `ProductionDispatchResolverV1.cs` and
  `ProductionTreeScheduler.cs`.

## Mandatory planning gate

Before production changes, record an accepted focused decision defining:

1. The scheduler-owned cache key, lease state machine and both Jobs-driver ownership boundaries.
2. Capacity derivation from one catalog's cases and the exact registered catalog/profile cohort;
   no guessed growth factor or observed-wave growth is allowed.
3. The registration/prewarm boundary, exact over-capacity failure and commit behavior. The
   production cache lease must remain immediate and cannot extend across registration.
4. How active request slices are represented without exposing capacity-only participants to the
   generated executor.
5. Native-owner teardown while a scheduled wave is outstanding, plus cache disposal exactly once.

The existing `NativeBurstDispatchWorkspaceOwnerV2` is single-request. It must not be silently
repurposed as a grouped workspace or replaced by sequential single dispatches.

## Allowed changes

- Focused internal additions in `Runtime/Execution/Burst/Dispatch/` for bounded multi-request
  workspace ownership.
- `Runtime/Integration/GeneratedDispatch/` group-executor integration.
- Scheduler-private cache/lease integration, and both Jobs group drivers only as required by the
  accepted design.
- Focused Runtime/Integration allocation, lifecycle, failure and teardown tests.
- `Planning~/Evidence/P7-038/` and the accepted decision record.

## Forbidden changes

- No global/static workspace pool, dependency-injection scope or cross-scheduler sharing.
- No automatic capacity growth, fallback to sequential single dispatch, reflection or managed
  callback replacement in the Burst path.
- No change to scheduler policy selection, SchedulingProfile, budget admission or pipeline latency.
- No command demultiplexing: grouped command publication remains explicitly rejected until it has
  its own ownership contract.
- No benchmark result, default-policy recommendation or gameplay/sample work.

## Acceptance criteria

- Repeated grouped waves and later scheduler runs for the same warmed catalog/profile key allocate
  no managed memory and create no native allocation in the dispatch path, for both Jobs policies.
- Each wave still reaches the generated catalog through exactly one executor invocation; every
  participant receives only its own committed memory, binding values and status.
- A terminal, dispatch-bound or removed lane is never present in a later request slice.
- A request exceeding fixed capacity fails structurally, commits no partial participant state and
  does not resize or silently change dispatch strategy.
- Workspace reset, host destruction and scheduler destruction complete or reject outstanding work
  safely, release leases, and dispose each native allocation exactly once.
- Existing single-instance generated dispatch behavior and P7-037 bootstrap validation remain
  unchanged.

## Required verification

```text
Verify-Static.ps1
focused multi-request workspace state/capacity/retry/dispose tests
focused grouped generated-dispatch parity, atomic-failure and no-allocation-after-warmup tests
AIBT.Runtime.Tests + AIBT.Integration.Tests
AIBT.Editor.Tests
generated documentation/API-diff checks if any public surface changes
live Unity pipeline proof with repeated grouped dispatch waves
git diff --check
```

## Handoff

P7-035 may measure and claim grouped-dispatch allocation behavior only after this card is Done.
P7-034 remains independent: it may build the deterministic gameplay workload while this internal
workspace work is in progress.
