# P10-011 — Simulation container, stage G3 (external long-running actions)

Status: `Review`

## Objective

Stage G3 of `Documentation~/decisions/ADR-P10-008-simulation-container.md` (game requirement 6, and
requirement 7, preemption): Burst nodes in a `TreeSimulation` group start external long-running
actions through `AsyncOperationHandle<TStart, TCancel>` and read the game's completion through
`CompletionHandle<T>`. The game sees per-step Started/Cancelled events in a deterministic order and
completes actions between steps with `TreeGroup.TryComplete`. Every started action ends exactly once.

## Owner decisions for this card

As for P10-009 (D1–D9). D4: actions a node leaves live when it ends are fault-cancelled (reason
`NodeEnded`). D3: no generator change needed (the G2 binding-id table names action channels).

## Depends on

- P10-010 (G2).

## Scope

- Runtime: `Runtime/Simulation/` (`SimulationActions.cs` new; `SimulationNativeV1.cs`
  (`SimulationActionRoutinesV1`), `SimulationProgramMapV1.cs`, `SimulationTypes.cs`, `TreeGroup.cs`,
  `TreeSimulation.cs`), `GeneratedTreeDispatchAdapterV2.cs`, `AgentFrameCoreV1.cs`,
  `NativeBurstDispatchWorkspaceOwnerV2.cs` (internal `TryRebaseOperationCountFloor`).
- Tests: `Tests/Integration/Simulation/TreeSimulationActionTests.cs`, `SimulationActionCatalog.cs`,
  `TreeSimulationIoTests.cs` (the G2 refusal test replaced), `Tests/Editor/CodeGen/GenerationP10011/`
  (fixture shard in its own assembly).
- `Benchmarks~/Phase10/PlayerPerf/` (`sim-actions` cases).
- `Documentation~/simulation-container.md`, `Documentation~/generated/*` (regenerated),
  `CHANGELOG.md`, `Planning~/Evidence/P10-011/`, this card, `work-items.json`.

## Forbidden changes

- ABI v2 (`ExpectedPublicAbiV2.txt` byte-identical), execution semantics, the compiled format, the
  package version, the normative specifications; existing public and low-level APIs (additions
  only); the behaviour of `ProductionTreeHost`, `NativeTreeAgent`, `ProductionTreeScheduler`; G4
  work; pushing; committing the parent repository.

## Acceptance criteria

- Population: ≥ 500 agents in two trees, 300 ticks, adds/removes/restarts, inputs written every
  tick, a scripted game completing actions later with success or failure, or never; a per-tick
  digest of agent state, commands and action events is identical in place, with jobs, across job
  worker counts and across repeated runs.
- Life cycle: start → success, start → failure; preempt by reactive interrupt (wake on change in the
  very next step); agent removed, restarted, group removed; completion and preemption in the same
  step (preemption wins); double complete, complete after cancel, stale handle after slot reuse.
  Under a reactive composite an action restarts at every think (normative re-evaluation; evidence F2).
- A runaway tree faults its agent at `MaximumStepsPerThink` (evidence F1; default unchanged, a lower
  container default is proposed to the owner).
- Exactly one `Cancelled` per started action that was not consumed, over the whole population run;
  restart in place equals a fresh agent, ledger included.
- Zero managed allocation per step after warm-up; capacity overflow faults the agent, never throws;
  calls during a step return `Busy`; old catalogs refuse programs with actions (`CatalogTooOld`);
  hosts still refuse external actions.
- All eight AIBT test assemblies match the baseline plus the new tests; ABI v2 test, public-API
  additivity and `Verify-Static.ps1` pass.
- IL2CPP Player measurement: 500 agents, `Spread(5)`, the game completing actions a few ticks later:
  Step µs median and p99, bytes and events per tick, ABBA against the G2 `io-*` twin; host check at
  N = 500/2000 against the G1 base.

## Required verification

```text
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Unity/Invoke-UnityRecompile.ps1
unity command run_tests --mode editor --filter <each AIBT test assembly> --filter_type assembly --async_tests true (poll test_status)
powershell -NoProfile -ExecutionPolicy Bypass -File Assets/AIBT/Tools~/Verification/Verify-Static.ps1
Assets/AIBT/Tools~/Verification/P7/Audit/Get-FullPublicApi.ps1 -BaselinePath <P10-010 API dump>
Benchmarks~/Phase10/PlayerPerf/Tools/Invoke-Perf.ps1 -Set sim-actions (IL2CPP Player)
```

Evidence: `Planning~/Evidence/P10-011/README.md`.
