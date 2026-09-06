# P7-038 implementation plan

Status: completed 2026-09-06. Implemented and verified within accepted ADR AIBT-039.

## Scope

Replace per-wave allocations inside `GeneratedDispatchGroupExecutorV2` with bounded,
scheduler-owned multi-request workspaces. Preserve P7-037 bootstrap validation, P7-033 policy
selection and the exact one-catalog-wave/one-executor-call contract for both Jobs policies.

## 1. Establish workspace contracts

1. Add internal group workspace capacity and lease/result contracts beside the existing native
   dispatch workspace contracts; do not modify public API.
2. Define a scheduler-private cache key from `GeneratedBurstCatalogV2` and `SchedulingProfile`
   reference identities, matching the exact grouping boundary. Derive each entry's maxima from the
   validated catalog data and its registered cohort cardinality only.
3. Define how registration prewarms or reconfigures an entry. The production cache is leased only
   by a complete immediate dispatch wave, so it cannot collide with registration. No allocation may
   be hidden in a scheduler frame or inferred from an observed wave.
4. Add the owner/lease state machine, active-count validation and structural diagnostics for
   duplicate/out-of-range participants, capacity overflow and invalid phase transitions.
5. Unit-test creation, zero/overflow capacity, state order, retry after rejected input, lease
   exclusivity and disposal rules before integration.

## 2. Implement owned buffers and batch execution

1. Allocate persistent participant, request, offsets, configuration, memory, random, binding,
   live-value, target-ordinal, result and transaction buffers exactly once per cache entry.
2. Build active native slices for each wave so only active requests are visible to the catalog
   executor.
3. Run one immediate production catalog batch through the existing closed ABI. Preserve scheduled
   owner lifecycle coverage as an internal native test only; do not add scheduled group dispatch to
   the scheduler.
4. Consume result slices without allocating per-participant managed arrays or temporary native
   containers.
5. Test immediate/scheduled parity, exact executor invocation count, result isolation and atomic
   rejection.

## 3. Integrate cache, leases and the production group executor

1. Let `ProductionTreeScheduler` own and dispose the private catalog/profile workspace cache.
2. Make `ProductionBatchedGroupDriverV1` and `ProductionPipelinedGroupDriverV1` acquire the same
   cache-entry lease only for a complete group-dispatch wave, then pass it through
   `ProductionDispatchResolverV1` to `GeneratedDispatchGroupExecutorV2`.
3. Retain the existing single-dispatch path for a one-member wave; do not create or lease a group
   workspace solely for that path.
4. On unavailable lease or grouped-capacity failure, preserve the existing structured failure
   completion path for every member; do not alter scheduler policy or retry as single dispatch.
5. Pipeline completion and host removal leave a completed immediate lease behind. Scheduler
   destruction disposes every idle cache entry exactly once; native-owner tests cover scheduled
   work completion/rejection before disposal.

## 4. Prove behavior and allocation guarantees

1. Add repeated multi-wave, multi-run real generated-dispatch fixtures with different active member
   counts for `BatchedJobsSameFrame` and `PipelinedJobs`.
2. Prove removed/terminal/dispatch-bound hosts never reappear in a later workspace request slice.
3. Prove one participant's committed memory/binding result cannot reach another participant.
4. Prove aggregate capacity failure commits no participant and leaves the workspace reusable or
   cleanly rejected according to the accepted state contract.
5. Prove registration-time prewarm/reconfiguration is the only allocation point and measure zero
   managed allocations after warm-up for repeated waves and later runs; instrument owner lifetime
   to prove no persistent native allocation is created per wave/run.
6. Prove native-owner teardown around an outstanding scheduled group wave releases every owned
   buffer exactly once, and scheduler/host teardown disposes every cached immediate workspace once.

## Verification

1. Unity compilation and Console inspection through Unity MCP.
2. Focused native workspace and generated-dispatch integration tests.
3. `AIBT.Runtime.Tests` + `AIBT.Integration.Tests` and `AIBT.Editor.Tests`.
4. `Verify-Static.ps1`, `git diff --check`, documentation/API drift checks when applicable.
5. Unity Play Mode proof with repeated real grouped dispatch waves and published scheduler snapshots.

## Explicit non-goals

- No global pooling, auto-growth, reflection, policy fallback or command demultiplexing.
- No public API, profile, budget, latency, gameplay, sample or benchmark changes.
- No claim of cross-platform performance; P7-035 owns Player measurement.
