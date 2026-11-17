# P10-012 evidence — Simulation container, stage G4 (debugging)

Card: `Planning~/Tasks/P10/P10-012-simulation-container-g4.md`. ADR:
`Documentation~/decisions/ADR-P10-008-simulation-container.md` (game requirement 10). Unity
6000.6.9f1, branch `feature/aibt-consumer-workflow`, base `15ac74e` (G3).

Commits:
- `0dc23d1`: runtime, MCP/CLI, tests and docs.
- The next commit holds the bench harness (`sim-trace`), this evidence, the card,
  `work-items.json`, and the revert of the host flag (§5.2).

> **Status: Review.** All assemblies are at their baseline plus the new tests. ABI v2 is unchanged,
> the public API only grows (+91 lines, 0 removed), tracing changes no digest, and `Step` allocates
> nothing while tracing. The IL2CPP measurement is done.
> - The host "has snapshots" flag recovered nothing measurable, so it was reverted (§5.2).
> - The AIBT Graph window binding was not done (§6, deviation 8; §8).

## 1. What was built

**Public API (additive, `AIBT.Simulation`, experimental until G5).** Full list:
`perf-data/P10-012/api/added.txt`.

| Area | API |
|---|---|
| Traces | `TreeGroup.TryConfigureTracing(in AgentTraceSettings)` (`MaximumTracedAgents` 8, `RecordsPerAgent` 1024, `RecordsPerThink` 256 by default), `TrySetTraced(agent, bool)`, `IsTraced`, `TracedCount`, `TryCopyTrace(agent, NativeList<AgentTraceRecord>, out AgentTraceInfo)`, `TryClearTrace` |
| Trace records | `AgentTraceRecord` (`Tick`, `Fact`, the embedded `NativeTraceRecordV1`, `GetValueByte`, `ValueHex`), `AgentTraceFact` (`Lifecycle`, `Think`, `Input`, `Command`, `ActionStarted`, `ActionCancelled`, `CompletionConsumed`, `Fault`, `Teardown`), `ThinkReasons` (flags: `Scheduled`, `WokenByGame`, `InputChanged`, `ActionCompleted`, `FreshInstance`), `AgentTraceInfo` (`Count`, `Recorded`, `Dropped`, `DroppedIsLowerBound`) |
| Active path | `TryGetActivePath` (into a `NativeList` or a `Span` of `ActivePathNode`) with `AgentDebugState` (`AgentRunState`, last and next think, the reason, update count, last root result, fault), `TryCopyLiveActions` (`LiveAction`), `TryGetAgentAt(slot)`, `DescribeAgent(agent[, nodeTypeName])` |
| Registry | `TreeSimulationDebug.Register(simulation, name)`, `Unregister`, `CopyRegistered(List<RegisteredSimulation>)`: opt-in, weak, for tools. `TreeSimulation.CopyGroups` |
| Results | `SimulationResult.NotTraced` (appended) |

**Recording (container only).**
- **Per traced agent**, a pooled scratch `NativeTraceChannelOwnerV1` and the package's existing
  `NativeTraceRecorderV1` are attached to the agent's `ProductionTreeAgentCore` through the
  internal `TryAttachTrace`/`TryDetachTrace`. The recorder only observes.
- **After each think or teardown**, the scratch channel is copied into the group's ring
  (`NativeArray<AgentTraceRecord>`, `RecordsPerAgent` per traced slot), re-sequenced per agent, with
  the tree instance id patched, and then reset.
- **Container facts** are appended as records of the nearest native kind:
  - the think and its reasons;
  - the input rows read at think start, as canonical rows;
  - commands; action starts, cancels and consumed completions;
  - faults and teardowns.
- **Job path.** The agent-frame job writes `AgentFrameTraceEventV1` events that are replayed into the
  same recorder on the main thread, so the traces of the in-place and job paths are identical
  (tested).
- **Untraced agents** pay one test per think.
- **Debug calls during `Step`** return `Busy`.

**Tools (read-only, `Read` permission).**
- MCP: `runtime_simulations`, `describe_agent`, `read_agent_trace` (paged).
- CLI: `aibt runtime simulations`, `aibt agent describe`, `aibt agent trace`.
- With nothing registered, `runtime_simulations` returns an empty list with a hint to call
  `TreeSimulationDebug.Register`. The agent tools return `AIBT9018` (malformed arguments) with the
  same text.

**Docs.**
- `Documentation~/simulation-container.md`: a "Debugging (G4)" section with examples.
- `CHANGELOG.md`.
- `Documentation~/generated/*`, regenerated: `api-reference-runtime.md`, `workflow-guide.md`,
  `anti-patterns.md`.

## 2. Tests

| Test | What it proves | Result |
|---|---|---|
| `TreeSimulationActionTests.Population_Tracing_ChangesNoDigest_AndTracesAreIdentical_AcrossPathsAndWorkerCounts` | 2 groups × 260 agents (crew and shift trees, inputs, commands, actions), 300 ticks, slots 0, 5, 60, 131 and 259 of each group traced (one removed at tick 60, others restarted). The digests equal the untraced run in place, with jobs, with default settings and with 0 and 1 workers. The trace digests are identical across all of them. The removed agent's trace is kept and readable at tick 61, and its handle is `StaleHandle` at tick 300 | passed (86 s) |
| `TreeSimulationActionTests.Step_…_AllocatesNothing_AfterWarmUp` ×4 | In place and jobs, × untraced and 16 traced agents (`RecordsPerAgent` 64, so the ring wraps): 0 bytes | passed |
| `TreeSimulationDebugTests.ScriptedAgent_TraceIsExactlyTheExpectedEventList` ×2 | A memory sequence over [first job, reactive selector [alarm, second job]]. The trace equals an exact 56-line expected list in place and with jobs, covering: think reasons (`Scheduled`, `WokenByGame`, `InputChanged`, `ActionCompleted`, `FreshInstance`), input rows, a reactive interrupt (`Preempted` cancel), action start, cancel and consumed completion, a commands row, a restart teardown and a removal teardown | passed |
| `Ring_Overflow_DropsTheOldest_AndCountsThem` | The ring keeps the newest `RecordsPerAgent` and counts the rest | passed |
| `Think_BeyondRecordsPerThink_CountsTheDrops_AndRecordsTheFault` ×2 | A runaway think: `Dropped` ≥ 96, `DroppedIsLowerBound` true, the step-limit fault recorded | passed |
| `Settings_AndSelection_AreValidated` | Bad settings, a full pool, untraced → `NotTraced`, a stale handle | passed |
| `ActivePath_RunningFinishedRestartedRemoved` ×2, `ActivePath_Faulted` ×2 | Path and state for running, waiting, finished, restarted, removed (stale) and faulted agents | passed |
| `DescribeAgent_TextIsPinned` | The exact text | passed |
| `Registry_IsOptIn_HoldsTwoContainers_AndForgetsDisposedOnes`, `Registry_PrunesACollectedContainer`, `Registry_HoldsContainersWeakly` | Nothing registered by default, opt in and out, rename, two containers, disposal; the prune of a collected container through a test seam (deterministic); real collection when the GC collects, otherwise `Inconclusive` (added after the full run, so Integration now has 192 tests) | passed |
| `DebugCalls_DuringStep_ReturnBusy` | `Busy` from inside a node callback | passed |
| `McpSimulationToolsTests` (3, Editor) | JSON for a registered 500-agent container (agent 7 traced), paging, `AIBT9018` when none is registered, the tools are read-only with `Read` permission | passed |

Old catalogs and hosts. G4 adds no host code path: the attach/detach and reset changes in
`ProductionTreeAgentCore` are reached only from the container, and the host flag was reverted.
Regression is covered by the unchanged host, scheduler, NativeTreeAgent and old-catalog tests
(Integration, Runtime, NativeBurstDispatch), all at baseline.

## 3. Verification

| Check | Result | Baseline (G3) |
|---|---|---|
| `AIBT.Runtime.Tests` | 710/710 | 710/710 |
| `AIBT.Editor.Tests` | 639/640; the one failure is the known `McpToolDispatcherTests.ZeroCustomNodesReturnsExactlyThePhase1BuiltInCatalog` (sample nodes in the project). +3 `McpSimulationToolsTests` | 636/637 |
| `AIBT.Integration.Tests` | 191/191 (8.5 min): +14 `TreeSimulationDebugTests`, +1 traced population, +2 traced zero-GC cases | 174/174 |
| `AIBT.BehaviorCases.Tests` | 65/65 | 65/65 |
| `AIBT.NativeBurstDispatch.Tests` | 79/79 | 79/79 |
| `AIBT.NativeTreeAgentBlackboard.Tests` | 33/33 | 33/33 |
| `AIBT.NativeSharedBlackboard.Tests` | 36/36 | 36/36 |
| `AIBT.PlayMode.Tests` | 1/1 | 1/1 |
| `AIBT.CodeGen.ContractTests` | 6/6 (`ExpectedPublicAbiV2.txt` unchanged) | 6/6 |
| After the flag revert (orchestrator's clean async run of all assemblies on the working tree) | Runtime 710/710, Editor 639/640 (known `ZeroCustomNodes`), Integration 190/191 (an earlier run on the same tree 191/191), BehaviorCases 65/65, NativeBurstDispatch 79/79, NativeTreeAgentBlackboard 33/33, NativeSharedBlackboard 36/36, PlayMode 1/1. The one failure was `Registry_HoldsContainersWeakly` (flaky, fixed below) | |
| Flaky-test fix | `Registry_HoldsContainersWeakly` compared the registry's total entry count before and after `GC.Collect`. Two things made that unreliable in a full run. Mono/Boehm scans the stack conservatively and may not collect the dropped container. Entries left by other tests can also be pruned in between. It now looks for its own uniquely named entry, and ends `Inconclusive` when its own weak probe shows the container survived the GC. New `Registry_PrunesACollectedContainer` pins the prune path deterministically: an internal test seam (`TreeSimulationDebug.ClearTargetForTests`, `EntriesNamedForTests`) clears the entry's weak target as the GC would. No public API or runtime behaviour changed. Async run of the affected sets afterwards: `TreeSimulationDebugTests` 15/15, `McpSimulationToolsTests` 3/3, `AIBT.CodeGen.ContractTests` 6/6 (`perf-data/P10-012/tests-flaky-fix/`) | |
| `Verify-Static.ps1` | passed (schemas 7, work items 176), after the revert and with this card | |
| Public API | Dumped inside the open Editor (the `PublicApiDump` body, no second Unity). Compared with `perf-data/P10-011/api/public-api-p10011.txt`: +91 lines, 0 removed, all in `AIBT.Simulation` (`added.txt`, `removed.txt` empty) | |
| Generated docs | Regenerated from the Editor (eval scripts run in the open Editor) into `Documentation~/generated/` | |

Raw test results: `perf-data/P10-012/tests/` (`summary.txt` and one JSON per assembly).

**Visual and real-tool evidence** (`perf-data/P10-012/demo/`, made by `evidence_setup.cs`,
`run_demo_cli.ps1` and `evidence_cleanup.cs` there). The source is a running
500-agent container `p10012-demo`: the crew tree, `Spread(5)`, wake on changed input, and the game
completing each job 3 ticks after it starts. It ran 60 ticks with agents 7 and 123 traced.
- `describe-agent.txt`: real `DescribeAgent` text for agents 7 and 123 (traced) and 250 (untraced).
- Real CLI answers:
  - `cli-runtime-simulations.json`
  - `cli-agent-describe-7.json`
  - `cli-agent-trace-7.json`: 40 events of 333 recorded.
  - `cli-agent-trace-250-untraced.json`: `NotTraced`.
  - `cli-agent-describe-wrong-name.json`: exit 1, a clear error.
- No PNG: the Graph window binding was not done, so there is no Editor window to capture.

## 4. Player stripping

Tracing adds no reflection. The IL2CPP Player (High stripping, no `link.xml` change) traced agents
in every `sim-trace` case without faults.

## 5. Measurement

**Setup.**
- **Hardware.** IL2CPP Player, Release, High stripping, Unity 6000.6.9f1, Intel Core Ultra 9 275HX
  (24 logical), Windows 11.
- **Project.** `C:\UnityProjects\_aibt_acceptance\P9Samples-6069`, with its manifest pointed at
  `aibt-p10012-6069` (`git archive 0dc23d1`). The harness and manifest were backed up to
  `P9Samples-6069-backup-before-p10012` and restored afterwards.
- **Players.**
  - `Build\P10012`: G4 `0dc23d1`.
  - `Build\P10011-base`: G1 `14b6799`.
  - `Build\P10011`: G3, whose runtime equals `15ac74e`.
- **Data.** Raw data in `perf-data/P10-012/raw/`. The runs come from `run_measurements.ps1` and were
  never run in parallel with Editor tests.
- **Tables.** `sim-trace-table.md` (made by `sim_trace.py`) and `host-check-table.md` (made by
  `host_check.py`).

### 5.1 Tracing cost (`sim-trace`)

**Scenarios.**
- **Agents.** 500 agents, `Spread(5)`, `io-loop16`, `io-reactive16` and `act-loop16`.
- **Traced agents.** 0, 1 or 8 (spread over the slots, default `AgentTraceSettings`), in ABCCBA order
  inside each run.
- **Runs.** 3 runs with the default job workers (23) and 3 with 0 workers, so 6 values per cell.
- **Values.** `Step` µs is the `TreeSimulation.Step` call alone (median and p99 per run; the median
  of those, with the range).

| workers | tree | T0 median µs | T1 median µs | T8 median µs | T1/T0 | T8/T0 | T8/T0 p99 | records/tick at T8 |
|---:|---|---:|---:|---:|---:|---:|---:|---:|
| 23 | `io-loop16` | 60.8 | 58.6 | 64.6 | 0.964 | 1.063 | 1.104 | 11.3 |
| 23 | `io-reactive16` | 238.1 (222–283) | 250.6 | 291.4 | 1.053 | 1.224 | 1.388 | 81.6 |
| 23 | `act-loop16` | 64.4 | 68.2 | 68.1 | 1.060 | 1.057 | 1.127 | 11.8 |
| 0 | `io-loop16` | 91.7 | 91.0 | 98.9 | 0.992 | 1.078 | 1.010 | 11.3 |
| 0 | `io-reactive16` | 2582.1 | 2606.1 | 2573.1 | 1.009 | 0.997 | 1.005 | 81.6 |
| 0 | `act-loop16` | 90.9 | 91.3 | 96.8 | 1.004 | 1.064 | 0.993 | 11.8 |

- **Allocation.** 0 bytes per tick inside `Step` and 0 bytes in the GC window, in every cell.
  Faults: 0.
- **Drops.** With the default 1024-record rings, a traced agent fills its ring in a few hundred
  ticks, after which the oldest records are dropped as designed. Over a whole run (1200 ticks): 744
  (`io-loop16` T1) to 94 820 (`io-reactive16` T8).
- **Reading.**
  - One traced agent costs nothing measurable (0.96–1.06, inside the run-to-run range).
  - Eight traced agents cost about 6% of `Step` on the loop trees (≈ 4 µs, about 0.3–0.4 µs per
    record).
  - On `io-reactive16` with workers, eight traced agents cost about 22% (≈ 53 µs for 82 records per
    tick). With workers, `Step` is short and the copy and replay into the ring run on the main thread.
    With 0 workers the same records are lost in the 2.6 ms step (0.997). This cell is the noisiest:
    its T0 range is 222–283 µs.

### 5.2 Host check (`host-check`) and the "has snapshots" flag

**What was tried.** G2 added `StageInputs` to every dispatch. The G3 measurement showed hosts 2–5%
slower than G1. `0dc23d1` added a per-slot "has snapshot bindings" flag, computed once at slot
creation, that skips the loop for slots without snapshot bindings (every host slot). It was added in
both the adapter's `Execute` and the agent-frame core's `ExecuteSlot`.

**Scenarios.** `NativeTreeAgent`, default profile (agent-frame job from 64 agents),
`shared-loop16` and `shared-reactive16` at N = 500 and 2000.
- Two ABBA blocks, with 2 runs per Player invocation, so 4+4 runs per side:
  - G1 block: base, G4, G4, base.
  - G3 block: G3, G4, G4, G3.
- Values: the median of per-run medians of the frame bracket and of the scheduler drive.

| block | tree | N | base bracket µs | G4 bracket µs | G4/base | G4/base drive |
|---|---|---:|---:|---:|---:|---:|
| G1 `14b6799` | `shared-loop16` | 500 | 394.8 (381.2–414.9) | 420.0 (401.0–440.8) | 1.064 | 1.082 |
| G1 `14b6799` | `shared-loop16` | 2000 | 1751.6 (1707.3–1770.2) | 1916.2 (1884.6–1968.0) | 1.094 | 1.125 |
| G1 `14b6799` | `shared-reactive16` | 500 | 2337.1 (2299.4–2374.9) | 2446.6 (2424.8–2473.9) | 1.047 | 1.048 |
| G1 `14b6799` | `shared-reactive16` | 2000 | 9231.0 (9086.7–9476.8) | 9705.9 (9636.5–9834.6) | 1.051 | 1.050 |
| G3 `15ac74e` | `shared-loop16` | 500 | 412.9 (392.3–428.9) | 422.8 (390.9–442.3) | 1.024 | 1.026 |
| G3 `15ac74e` | `shared-loop16` | 2000 | 1893.5 (1846.8–1914.3) | 1869.3 (1846.2–1892.6) | 0.987 | 0.988 |
| G3 `15ac74e` | `shared-reactive16` | 500 | 2433.1 (2390.5–2470.1) | 2429.6 (2420.2–2438.4) | 0.999 | 1.000 |
| G3 `15ac74e` | `shared-reactive16` | 2000 | 9733.7 (9580.5–9810.9) | 9658.6 (9633.2–9746.8) | 0.992 | 0.993 |

**Result.**
- G4 with the flag equals G3 (0.99–1.02, overlapping ranges), and is still 5–9% slower than G1 in
  this session.
- So skipping the `StageInputs` loop recovers nothing measurable: the host gap to G1 is not in that
  loop.
- As the brief required, **the flag was reverted**. `AgentFrameCoreV1.cs` and
  `GeneratedTreeDispatchAdapterV2.cs` are again byte-identical to `15ac74e`. Hosts are therefore
  exactly as in G3.
- The `Build\P10012` numbers above include the flag. Since it made no measurable difference to hosts,
  and container slots for the `io-*` trees have snapshot bindings anyway, §5.1 still holds.

## 6. Deviations from the ADR

1. **Active path type.** `ActivePathNode` records (node index, stable node id, node type id, status)
   instead of a `NativeList<uint>` of node indices: tools and `DescribeAgent` need ids and statuses.
2. **Per-node last result.** It comes from the trace, and only for traced agents, instead of an
   opt-in byte per node for every agent. Untraced agents pay nothing.
3. **Storage.** One pooled scratch channel per traced agent plus a group ring, instead of a native
   trace channel per agent: memory is bounded by `MaximumTracedAgents × RecordsPerAgent`.
4. **Inputs.** They are recorded once per think, at think start, as canonical rows, not at every
   read.
5. **Container facts.** They are mapped to the nearest native record kinds (with `AgentTraceFact` to
   tell them apart), not to new native kinds. The native trace format is unchanged.
6. **Drop count.** It is exact for ring overflow and for drops past `RecordsPerThink`. Past
   4 × `RecordsPerThink` in one think, the scratch channel faults at its emission capacity, so the
   count is a lower bound, flagged by `AgentTraceInfo.DroppedIsLowerBound` (new).
7. **Removed agents.** A removed agent's trace is kept and readable until it is untraced or its slot
   is reused (its handle is then stale).
8. **No Graph window binding.** Live highlighting or a trace timeline for a container agent in the
   AIBT Graph window was not done, because it is not small. It is described for G5 in §8.
9. **New result `NotTraced`**, for `TryCopyTrace`/`TryClearTrace` on an untraced agent.
10. **Additional API.** `TreeSimulation.CopyGroups`, `TreeGroup.TryGetAgentAt`, `TryCopyLiveActions`:
    the tools need to enumerate groups and agents, and to show live actions.
11. **Host flag reverted** (§5.2): an attempt, measured as nothing, and taken back.

## 7. Findings

- **The host gap to G1 is not in `StageInputs`.** In this session G4 = G3 is 5–9% slower than G1
  (the bracket at `shared-loop16` N = 2000 is the largest at 1.094). P10-011 measured G3/G1 at
  1.02–1.05, and G2 = G3 for hosts. So whatever there is came with G2 (P10-010), or it is drift. G4
  adds none. Finding where it comes from needs a profiler capture of the host's dispatch, which is
  outside G4.
- **Tracing cost scales with records, on the main thread.** About 0.3–0.6 µs per record. A
  reactive tree writes about 50 records per think, against about 7 for a loop tree. With a
  short parallel `Step`, eight traced reactive agents are visible (+22%). The trace is a debugging
  aid. Its documentation says to trace a handful of agents, and the default pool is 8.
- **Default rings wrap quickly on reactive trees** (1024 records ≈ 20 thinks of `io-reactive16`).
  This is by design, and `Dropped` says so, but a tool reading a long-running agent sees only the
  latest thinks. Raise `RecordsPerAgent` for longer histories.
- **`DescribeAgent` of an untraced agent** names node types only through the optional resolver;
  without it, the line shows the type hash (`describe-agent.txt`, agent 250). The MCP tool resolves
  names from the project catalog.
- F1 (step-limit default) and F2 (actions under reactive composites) remain open, unchanged.

## 8. What G5 must know

- **Graph window binding.** It is feasible as a third target kind in `GraphLiveDebugger`. A target is
  (registered simulation, group, agent handle). The window already maps native lifecycle records to
  nodes, and the container ring stores exactly those records (`AgentTraceRecord.Record`). The attach
  sites are `Editor/Graph/BehaviorTreeGraphExecution.cs:109,138`. Selection can reuse
  `TreeSimulationDebug.CopyRegistered`.
- **Every debug API refuses during `Step`** (`Busy`): an Editor window must read between steps (for
  example in `EditorApplication.update`).
- **The record layout** is `AgentTraceRecord` (tick, fact, `NativeTraceRecordV1`). Tick and sequence
  are per agent, and sequence restarts only when the trace is cleared. Instance ids are patched to
  the agent's current instance.
- **Untraced cost** is one test of the slot's trace index per think (`TreeGroup.Think`), plus one
  group-level count test per step. Traced agents use the job path's
  `AgentFrameTraceEventV1` replay, so a new native record kind needs a matching replay case.
- **The registry is weak.** A container that is collected or disposed leaves the list. Nothing is
  registered unless the game calls `Register`, and release builds should not.
- **Tooling gotcha.** `unity command eval_file` has a 5 s main-thread limit. Longer work completes
  anyway after the CLI reports a timeout, so check the side effect.
