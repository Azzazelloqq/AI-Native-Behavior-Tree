# P10-012 — Simulation container, stage G4 (debugging)

Status: `Review`

## Objective

Stage G4 of `Documentation~/decisions/ADR-P10-008-simulation-container.md` (game requirement 10):
make `AIBT.Simulation` debuggable. Selected agents are traced (the package's native lifecycle records
plus the container's own facts), any agent's active path and state can be queried between steps, and
Editor tools find the game's containers through a registry the game opts into. Tracing never changes
results.

## Owner decisions for this card

As for P10-009 (D1–D9). F1 (`MaximumStepsPerThink` default) and F2 (reactive composites restart
actions every think) stay open and are not acted on here.

## Depends on

- P10-011 (G3).

## Scope

- Runtime: `Runtime/Simulation/` (`SimulationDebugTypes.cs`, `TreeGroup.Debug.cs`,
  `TreeSimulationDebug.cs` new; `TreeGroup.cs`, `TreeSimulation.cs`, `SimulationTypes.cs`),
  `ProductionTreeAgentCore.cs` (internal trace attach/detach), `NativeLifecycleMachineV1.cs` (internal
  active-path accessors); host perf attempt: `GeneratedTreeDispatchAdapterV2.cs`, `AgentFrameCoreV1.cs`
  (per-slot "has snapshot bindings" flag, measured, recovered nothing, reverted).
- MCP: `MCP/McpSimulationTools.cs` (new), `McpToolDispatcher.cs`, `McpBuiltInTools.cs`,
  `McpOperationCatalog.cs`, `MCP~/Server/RuntimeTools.cs`, `MCP~/Cli/Program.cs`, the workflow-guide
  and anti-patterns generators.
- Tests: `Tests/Integration/Simulation/TreeSimulationDebugTests.cs` (new),
  `TreeSimulationActionTests.cs` (traced population, traced zero-GC cases),
  `Tests/Editor/Mcp/McpSimulationToolsTests.cs` (new).
- `Benchmarks~/Phase10/PlayerPerf/` (`sim-trace` set, `sim_trace.py`).
- `Documentation~/simulation-container.md` ("Debugging (G4)"), `Documentation~/generated/*`
  (regenerated), `CHANGELOG.md`, `Planning~/Evidence/P10-012/`, this card, `work-items.json`.

## Forbidden changes

- ABI v2 (`ExpectedPublicAbiV2.txt` byte-identical), execution semantics, the compiled format, the
  package version, the normative specifications; existing public and low-level APIs (additions
  only); the behaviour of `ProductionTreeHost`, `NativeTreeAgent`, `ProductionTreeScheduler`; F1, F2;
  G5 work; pushing; committing the parent repository.

## Acceptance criteria

- Tracing changes no digest: ≥ 500 agents in two trees with inputs, commands and actions, 300 ticks,
  a handful of traced agents (one restarted, one removed), in place, with jobs, default settings and
  0/1 job workers; the traces themselves are identical on every path.
- A scripted single agent's trace equals an exact expected list (wake reasons, a reactive interrupt,
  action start/cancel/consumed completion, restart and removal teardowns), in place and with jobs.
- Ring overflow drops the oldest and counts; a think beyond `RecordsPerThink` counts its drops; zero
  managed allocation in `Step` while tracing; untraced agents pay one test per think.
- Active path and state for a running, waiting, finished (Stop), faulted, restarted and removed
  (stale) agent; `DescribeAgent` text pinned; debug calls during `Step` return `Busy`.
- Registry: nothing registered by default, opt-in/out, rename, two containers, disposal and
  collection leave the list. MCP tools: JSON for a registered 500-agent container, a clear error when
  none is registered, read-only, `Read` permission.
- All AIBT test assemblies match the baseline plus the new tests; ABI v2, public-API additivity and
  `Verify-Static.ps1` pass.
- IL2CPP Player: 500 agents, `Spread(5)`, `io-loop16`, `io-reactive16`, `act-loop16` with 0/1/8
  traced agents (Step µs median/p99, bytes/tick, ratio to untraced); host check against G1 `14b6799`
  and G3 `15ac74e` for the host flag.

## Required verification

```text
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Unity/Invoke-UnityRecompile.ps1
unity command run_tests --mode editor --filter <each AIBT test assembly> --filter_type assembly --async_tests true (poll test_status)
powershell -NoProfile -ExecutionPolicy Bypass -File Assets/AIBT/Tools~/Verification/Verify-Static.ps1
public-API dump (PublicApiDump body) compared with perf-data/P10-011/api/public-api-p10011.txt
Benchmarks~/Phase10/PlayerPerf/Tools/Invoke-Perf.ps1 -Set sim-trace, -Set host-check (IL2CPP Player)
```

Evidence: `Planning~/Evidence/P10-012/README.md`.
