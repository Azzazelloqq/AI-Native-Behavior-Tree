# P7-038 evidence — bounded grouped generated-dispatch workspace

Status: `Done` — 2026-09-06

## Implemented boundary

- `ProductionTreeScheduler` owns a private cache keyed by reference identity of both
  `GeneratedBurstCatalogV2` and `SchedulingProfile`, exactly matching its forced-Jobs grouping
  boundary.
- Cache capacity is prepared during registration from the full compatible registered cohort. A
  larger cohort replaces an idle entry during registration; scheduler frames never allocate or grow
  an entry.
- `GeneratedDispatchGroupWorkspaceV2` owns persistent native request, input, staging, result and
  transaction storage. Every wave exposes only bounded active `NativeArray` subarrays to the
  generated executor, so capacity-only participants cannot execute.
- Both `BatchedJobsSameFrame` and `PipelinedJobs` pass their shared cache entry through the common
  resolver. Production group dispatch remains immediate; the cached lease cannot survive a frame.
- Group results are prevalidated for every participant before the first commit, so an invalid
  commit cannot leave an earlier participant's memory or blackboard committed alone.
- Command publication remains rejected as before; no policy, profile, budget, latency or public API
  contract changed.

## Behavior-first coverage

- `GroupDispatch_TwoInstances_ShareOneCatalog_AndReachSuccessThroughOneBatchPerWave` now executes
  multiple real group waves through one warmed workspace, asserts no managed GC allocation after
  warm-up, preserves per-host Success, and disposes the owner safely.
- Existing real scheduler proofs cover the cache through both
  `BatchedJobsSameFrame_TwoInstances_ShareOneCatalog_DriveThroughProductionTreeScheduler_ReachSuccess`
  and `PipelinedJobs_TwoInstances_CrossFrameBoundaries_AndReachSuccessThroughGeneratedDispatch`.
- Existing pipeline teardown and host-removal proofs remain in the Integration suite.

## Verification

Confirmed through Unity MCP on Unity 6000.5.8f1:

- Compilation clean. Final Console inspection contained no compiler errors; the remaining
  `ProductionTreeHost` errors are expected diagnostics emitted by negative-path tests (invalid
  clock, unsupported forced policy and injected host failure), plus Unity's TestResults save logs.
- Focused group-workspace and both production Jobs proofs: 3/3 passed.
- `AIBT.Runtime.Tests` + `AIBT.Integration.Tests`: 734/734 passed.
- `AIBT.Editor.Tests`: 433/433 passed.
- `Tools~/Verification/Verify-Static.ps1`: passed, 143 work items.
- `git -C Assets/AIBT diff --check`: passed.

## Scope notes

- No Player benchmark is claimed; P7-035 remains the owner of measurement and reporting.
- No new Play Mode scene was authored. The live Unity evidence is Editor-driven real generated
  runtime/scheduler execution; a Player/Play Mode performance claim remains unverified.
