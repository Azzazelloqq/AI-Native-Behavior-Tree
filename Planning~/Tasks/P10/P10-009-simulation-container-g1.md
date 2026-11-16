# P10-009 — Simulation container, stage G1 (container, slots, schedule, step)

Status: `Review`

## Objective

Stage G1 of `Documentation~/decisions/ADR-P10-008-simulation-container.md`: a GameObject-free
`TreeSimulation` the game steps with `Step(tick)`, holding groups of agents that run one
`ProductionTreeProgram` each. It runs trees whose generated nodes use blackboard keys only. Inputs,
commands and actions (G2, G3) are out of scope.

Two commits:

1. **Core extraction.** The per-agent state machine and dispatch of `ProductionTreeHost` move into an
   internal MonoBehaviour-free `ProductionTreeAgentCore`. The host delegates to it and its behaviour
   is unchanged (all existing tests are the proof).
2. **Container.** `TreeSimulation`, `TreeGroup`, `AgentHandle`, `ThinkSchedule`, settings, result
   and output types in `AIBT.Simulation` (experimental until G5), tests, benchmark, docs.

## Owner decisions for this card

D1(a) namespace `AIBT.Simulation`, experimental until G5; D2(a) internal core extracted from the
host, which delegates; D3(a); D4(a); D5(b) a new specification later, existing specifications
untouched; D6(a) the process-wide owner-id counters stay but never reach results; D7(a) time in
microseconds = tick × `TickMicroseconds`; D8(a) root completion default `Restart`; D9(b) opt-in wake
on input change (G2).

## Depends on

- P10-008 (ADR), P10-007, P10-006.

## Scope

- `Runtime/Integration/ProductionTreeAgentCore.cs` (new), `ProductionTreeHost.cs`,
  `ProductionTreeProgram.cs` (internal accessors), reset/seed helpers in
  `NativeLifecycleMachineV1.cs`, `GeneratedTreeDispatchAdapterV2.cs`,
  `NativeBurstDispatchWorkspaceOwnerV2.cs` (internal, used only by the container and tests).
- `Runtime/Simulation/` (new), `Tests/Integration/Simulation/` (new).
- `Benchmarks~/Phase10/PlayerPerf/` (`sim` set, `Tools/sim.py`).
- `Documentation~/simulation-container.md`, `Documentation~/generated/api-reference-runtime.md`
  (regenerated), `CHANGELOG.md`, `Planning~/Evidence/P10-009/`, this card, `work-items.json`.

## Forbidden changes

- ABI v2, execution semantics, the compiled format, the package version, the normative
  specifications; existing public APIs (additions only); the behaviour of `ProductionTreeHost`,
  `ProductionTreeScheduler`, `NativeTreeAgent`, `BehaviorTreeAgent` (including their random
  streams); G2/G3 work; pushing; committing the parent repository.

## Acceptance criteria

- Two programs × 250 agents, `Spread(5)`, 300 ticks from a plain loop, 20 agents per group removed
  and re-added at fixed ticks with slot reuse and stale-handle refusal. A per-tick digest is equal
  across two runs, `UseJobs` off/on (`MinimumJobBatch` 1 and default), and 0 vs default job workers.
- Zero managed allocation in 100 ticks after warm-up, jobs on and off.
- Schedule exactness (interval 5 phase 2, wake adds one think, spread phases), non-increasing tick
  refused without change, `Busy` during a step, two containers independent, no Unity object created.
- An in-place restart equals a fresh instance (state bytes and behaviour).
- Container agents equal standalone hosts fed the same times.
- Random streams: the host behaviour is established by a test and reported; container streams
  follow `time-and-random-v1`.
- All eight AIBT test assemblies match the baseline plus the new tests; ABI v2 test, public-API
  additivity and `Verify-Static.ps1` pass.
- IL2CPP Player measurement: 500 agents, `Spread(5)`, Step µs median and p99, bytes per tick, with 0
  and default job workers, ABBA against `NativeTreeAgent` with `UpdateCadence` 5.

## Required verification

```text
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Unity/Invoke-UnityRecompile.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Unity/Invoke-UnityTests.ps1 -Assemblies <each AIBT test assembly>
powershell -NoProfile -ExecutionPolicy Bypass -File Assets/AIBT/Tools~/Verification/Verify-Static.ps1
Assets/AIBT/Tools~/Verification/P7/Audit/Get-FullPublicApi.ps1 -BaselinePath <P10-007 API dump>
Benchmarks~/Phase10/PlayerPerf/Tools/Invoke-Perf.ps1 -Set sim (IL2CPP Player)
```

Evidence: `Planning~/Evidence/P10-009/README.md`.
