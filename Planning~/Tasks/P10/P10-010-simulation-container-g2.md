# P10-010 — Simulation container, stage G2 (inputs, commands, wake on changed input)

Status: `Review`

## Objective

Stage G2 of `Documentation~/decisions/ADR-P10-008-simulation-container.md`: the game feeds a
`TreeSimulation` group bulk inputs (one `NativeArray<T>` row per agent slot, read by Burst nodes
through `SnapshotReadHandle<T>`) and reads struct commands the nodes emit through
`CommandHandle<T>`, collected per step in a deterministic order. Opt-in wake on changed input (D9).
External actions (`AsyncOperationHandle`, `CompletionHandle`) are G3: programs that use them are
refused.

## Owner decisions for this card

As for P10-009 (D1–D9). D3: generator changes additive only, an old catalog still runs. D9(b): wake
on changed input is opt-in per channel, off by default.

## Depends on

- P10-009 (G1).

## Scope

- Runtime: `Runtime/Simulation/` (`SimulationChannels.cs`, `SimulationNativeV1.cs`,
  `SimulationProgramMapV1.cs` new; `TreeGroup.cs`, `TreeSimulation.cs`, `SimulationTypes.cs`),
  `GeneratedTreeDispatchAdapterV2.cs`, `AgentFrameCoreV1.cs`, `AgentFrameContractsV1.cs`
  (`IGeneratedBindingIdentityV1`), `ProductionTreeAgentCore.cs` (container-only paths),
  `NativeNodeShortForms.cs` (`AibtInputAttribute`).
- Generator: `CodeGen~/AIBT.CodeGen/` (`AibtInput` short form, binding-id table), rebuilt
  `Analyzers/AIBT.CodeGen.dll` and its hash.
- Tests: `Tests/Integration/Simulation/TreeSimulationIoTests.cs`, `SimulationInputNodes.cs`,
  `Tests/Editor/CodeGen/GenerationP10010/` (fixture shard in its own assembly).
- Player build: `Editor/PlayerBuild/AibtLinkXmlProcessor.cs` (keeps `[AibtBurstValue]` types and
  attributes in a stripped Player) with its test in `Tests/Editor/PlayerBuild/`.
- `Benchmarks~/Phase10/PlayerPerf/` (`sim-io` cases; scene MonoBehaviours one per file).
- `Documentation~/simulation-container.md`, `Documentation~/generated/*` (regenerated),
  `CHANGELOG.md`, `Planning~/Evidence/P10-010/`, this card, `work-items.json`.

## Forbidden changes

- ABI v2 (`ExpectedPublicAbiV2.txt` byte-identical), execution semantics, the compiled format, the
  package version, the normative specifications; existing public and low-level APIs (additions
  only); the behaviour of `ProductionTreeHost`, `NativeTreeAgent`, `ProductionTreeScheduler`; G3
  work; pushing; committing the parent repository.

## Acceptance criteria

- Population: ≥ 500 agents in two trees, 300 ticks, adds/removes, inputs written every tick,
  commands emitted; a per-tick digest including commands is identical in place, with jobs, across
  job worker counts and across repeated runs.
- Reactive interrupt: a rising input pre-empts at the next think, and with wake on change at the
  very next step.
- Zero managed allocation per step after warm-up with inputs, commands and wake.
- Command overflow faults the agent with a cause, never throws; negative quota refused.
- Stale, unbound, wrong-length and wrong-type inputs refused with result codes; calls during a step
  return `Busy`; external actions refused with `Unsupported`.
- An old catalog (no binding ids) runs in place with commands discarded and inputs refused; hosts
  still refuse snapshot bindings as before.
- All eight AIBT test assemblies match the baseline plus the new tests; ABI v2 test, public-API
  additivity and `Verify-Static.ps1` pass.
- IL2CPP Player measurement: 500 agents, `Spread(5)`, inputs written each tick and a command per
  think: Step µs median and p99 and bytes per tick, against G1.

## Required verification

```text
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Unity/Invoke-UnityRecompile.ps1
unity command run_tests --mode editor --filter <each AIBT test assembly> --filter_type assembly --async_tests true (poll test_status)
powershell -NoProfile -ExecutionPolicy Bypass -File Assets/AIBT/Tools~/Verification/Verify-Static.ps1
Assets/AIBT/Tools~/Verification/P7/Audit/Get-FullPublicApi.ps1 -BaselinePath <P10-009 API dump>
Benchmarks~/Phase10/PlayerPerf/Tools/Invoke-Perf.ps1 -Set sim-io (IL2CPP Player)
```

Evidence: `Planning~/Evidence/P10-010/README.md`.
