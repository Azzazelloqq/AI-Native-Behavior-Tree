# P10-001 — Managed zero-allocation tick, native facade scheduling and re-measurement

Status: `Review`

## Objective

A warmed-up `BehaviorTreeAgent` tick allocates no managed memory; forced Jobs scheduling profiles
work through `NativeTreeAgent` or fail with an explicit explanation; scheduler and world
registration are O(1) amortized; both layers are re-measured in an IL2CPP Player against the P9
baseline (`Temp/perf/raw/il2cpp-20261115-112453`).

## Depends on

- P9-001 (managed agent layer), P9-004 (native agent facade).

## Required reading

- `AGENTS.md`, `Planning~/AGENT_WORKFLOW.md`, `Documentation~/execution-and-scheduling.md`,
  `Documentation~/production-host.md`, ADR-P7-033 (global scheduler and profiles).

## Scope

- Managed execution: `Runtime/Execution/Reference/`, `Runtime/State/Reference/`,
  `Runtime/Commands/ReferenceCompletionInbox.cs`, `Runtime/Blackboard/Storage/`,
  `Runtime/Compiled/Model/CompiledBlackboardValueEncoder.cs`, `Agents/`.
- Native facade and scheduling: `Runtime/Integration/NativeTreeAgent.cs`,
  `Runtime/Integration/ProductionTreeWorld.cs`, `Runtime/Scheduling/Production/ProductionTreeScheduler.cs`.
- Tests: `Tests/Editor/Agents/`, `Tests/Integration/NativeRuntime/NativeTreeAgentTests.cs`.
- Documentation: `README.md`, `CHANGELOG.md`, `Documentation~/performance.md`,
  `Documentation~/roadmap.md`, `Documentation~/generated/api-reference-agents.md`.

## Forbidden changes

- Execution semantics (tick order, statuses, abort and observer behavior, traces, diagnostics),
  scheduler defaults, ABI v2 (`ExpectedPublicAbiV2.txt`), the package version, existing public APIs
  (additive changes only). A fix that needs any of these is reported to the owner, not made.

## Deliverables

- Allocation fixes in the managed execution path and regression tests with zero tolerance.
- Optional `BehaviorTreeAgent.StaggerIntervalTicks` (off by default) and its tests.
- Root cause and fix of the forced-Jobs fault through `NativeTreeAgent`; tests for every
  non-default profile through the facade.
- O(1) amortized registration and unregistration in `ProductionTreeScheduler` and `ProductionTreeWorld`.
- Linear lane-ownership checks in `NativeBatchedLifecycleOwnerV1` (were O(n²) per Jobs round).
- IL2CPP re-measurement, `Temp/perf/README-after.md`, `Documentation~/performance.md`, evidence.

## Acceptance criteria

- `IntervalStaggerTests` (4): default unchanged; staggered agents spread, receive real time, keep
  the interval and stay spread after a frame longer than the interval.
- `ManagedTickAllocationTests` (7): zero managed allocations in 400 warmed-up ticks for restarting,
  reactive, blackboard read/write (nodes and game code), decorator, parallel trees and explicit restarts.
- `NativeTreeAgentTests`: every non-default profile (Interactive, Background, forced Immediate,
  Budgeted, BatchedJobsSameFrame, PipelinedJobs) runs restarting and effect-emitting agents through
  the facade; a forced Jobs profile without capabilities faults with an explanation; leaving and
  joining the world keeps the registration order of effect delivery.
- Existing tests unchanged and green (one pre-existing host-only MCP failure).

## Required verification

```text
Tools/Unity/Invoke-UnityRecompile.ps1
Tools/Unity/Invoke-UnityTests.ps1 -Assemblies <every AIBT test assembly>   (and -Mode playmode)
Tools~/Verification/Verify-Static.ps1
Tools~/Verification/P7/Audit/Get-FullPublicApi.ps1 -BaselinePath <dump of 43dc15e>
AibtPerfTools/Invoke-Perf.ps1 -Step All -Backend il2cpp -Runs 3 ; analyze.py
```

## Handoff notes

- See `../../Evidence/P10-001/README.md` for results, numbers and what was not done.
- Owner decisions open: (1) whether `StaggerIntervalTicks` becomes the default; (2) whether the
  default native profile changes; (3) native restart without re-creating `ProductionTreeHost`
  needs the documented `NativeTreeAgent.Host` contract ("replaced on every restart") to change.
