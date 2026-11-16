# P10-009 evidence — simulation container, stage G1

Card: `Planning~/Tasks/P10/P10-009-simulation-container-g1.md`. Design:
`Documentation~/decisions/ADR-P10-008-simulation-container.md`. User documentation:
`Documentation~/simulation-container.md`.

Submodule `Assets/AIBT`, branch `feature/aibt-consumer-workflow`, local commits (not pushed):

- `d436789` step 1: core extraction (`ProductionTreeAgentCore`), host delegates.
- `d181cb0` step 2: the container, tests, benchmark set, docs, this evidence.

Raw data outside the repository: `C:\UnityProjects\_aibt_acceptance\perf-data\P10-009\`
(`raw/il2cpp-*`, `sim-table.md`, `logs/`, `api/`).

## 1. What was built

### Step 1 — core extraction (`d436789`)

`ProductionTreeHost` kept its public and internal surface, and every member now delegates to an
internal `ProductionTreeAgentCore`. The core owns the lifecycle machine, the dispatch adapter, the
update bookkeeping, the effect queue and the agent-frame entry. It has no `MonoBehaviour`, and it
logs only when it has a log context (the host passes itself; the container passes none). The host
keeps `_driving`, `_clock` and `_owner`, because tests reflect on `_driving`. Three internal reset
helpers were added for restart in place: `NativeLifecycleMachineV1.TryResetInstance`, the
adapter's and its dispatch slots' `TryResetInstance`, and
`NativeBurstDispatchWorkspaceOwnerV2.TryResetLedgerBinding`. Hosts never call them. All eight
assemblies matched the baseline after the extraction (commit message).

### Step 2 — container (`AIBT.Simulation`, experimental until G5)

- `TreeSimulation`: settings, `TryAddGroup`, `TryRemoveGroup`, `Step(uint tick)`, `Dispose`,
  `LastTick`, `IsStepping`, `GroupCount`, `IsDisposed`.
- `TreeGroup`: pooled slots allocated at `TryAddGroup`/`TryReserve`; `TryAdd` (lowest free slot,
  generation + 1), `TryRemove` (deferred teardown in the next step), `IsAlive`, `TryRestart`,
  `TrySetThinkSchedule`, `TryWake`, `TryWakeMany`, `GetRootCompletions`, `GetFaults` (read-only
  views valid until the next step), `Capacity`, `Count`, `HighWaterIndex`, `Id`, `Program`,
  `Settings`, `IsValid`.
- `AgentHandle` (index, generation, group id), `ThinkSchedule` (`Every`, `Spread`),
  `TreeSimulationSettings`, `TreeGroupSettings`, `RootCompletionPolicy` (`Restart` default, `Stop`),
  `RootCompletion`, `AgentFault`, `SimulationResult`.
- Execution: each slot owns a `ProductionTreeAgentCore` created at reserve time. In a step, due
  agents of a group either run as one `AgentFrameCoreV1` job or in place (`RunSegment` on the
  calling thread). The job runs when `UseJobs` is on, the catalog has the agent-frame entry and at
  least `MinimumJobBatch` agents are due. The group keeps a persistent per-slot agent table (storage
  handles filled once per instance) and copies the due entries into a contiguous job array each
  step, because the job wants contiguous agents and due slots are not contiguous. The job completes
  inside `Step`.
- Restart in place: a root completion with `Restart` marks the slot; its next think resets the
  machine and adapter to a fresh instance with the next container-local instance id and reseeded
  streams, then runs.
- Container-local instance ids: 1, 2, … in add and restart order. Random streams are seeded from
  `RandomSeed`, the program's semantic hash, the instance id and the node index
  (`NativeRandomStreamDerivationV1.TryDerive`, `time-and-random-v1`).

Public API added: only the types above in the new namespace `AIBT.Simulation` (section 4).
Internal additions: `ProductionTreeProgram.Definition`/`Catalog`, adapter seed/read/test-copy
helpers.

## 2. Tests

`Tests/Integration/Simulation/` (assembly `AIBT.Integration.Tests`), EditMode. The container
tests create no scene, GameObject or `Update`. The host-equivalence tests create host GameObjects
for the host side only.

| Test | Proves |
|---|---|
| `Population_DigestsAreIdentical_AcrossRunsJobsAndWorkerCounts` | Patrol tree (repeater, reactive selector, cooldown, random roll) and errand tree (wait, random roll, set, check), 250 agents each with `Spread(5)`, 300 ticks from a plain loop. At ticks 60 and 180, 20 agents per group are removed; at 61 and 181, 20 are added (lowest free slots, generation + 1). Old handles get `StaleHandle`, a full group gets `CapacityExceeded`, and so does a removed slot before its teardown step. Deterministic `TryWake`/`TryWakeMany` (with invalid indices). Per-tick FNV digest of every slot (alive, generation, container instance id, updates, root result, failure, ready, active path, Tree-scope bytes) plus completions and faults. Identical across: in place twice, `UseJobs` with `MinimumJobBatch` 1, default settings (64), and `UseJobs` with `JobWorkerCount = 0`. The scenario does what it claims: 0 faults, more than 200 successes and more than 50 failures (errands roll), at least 3 distinct patrol paths, 0 jobs when off, at least one job per tick when on. |
| `RandomSeed_ChangesResults_AndProcessHistoryDoesNot` | Same seed gives the same digests after other containers ran; another seed gives different ones. |
| `Step_AllocatesNothing_After30WarmUpTicks(false/true)` | 500 agents, 100 measured ticks with wakes and restarts: 0 bytes (`GC.GetAllocatedBytesForCurrentThread`) and 0 `GC.Alloc` samples, jobs off and on. |
| `Schedule_Interval5Phase2_…` | Thinks exactly on ticks ≡ 2 (mod 5) plus one at tick 13 after a wake; a wake on a due tick adds nothing; `Spread(5)` gives phases 0..4 round-robin. |
| `Step_WithANonIncreasingTick_IsRefusedAndChangesNothing` | `TickNotIncreasing` for equal, lower and 0 ticks; digest unchanged; `uint.MaxValue` fits. |
| `CallsDuringStep_AreRefusedWithBusy_AndChangeNothing` | Every mutating call made from inside `Step` returns `Busy`. |
| `TwoContainers_SideBySide_DoNotAffectEachOther` | A second container (other seed, other ticks, interleaved adds) changes nothing in the first. |
| `TheContainer_CreatesNoGameObjectOrComponent` | Counts of GameObjects, Components and all Unity objects unchanged across 60 steps. |
| `RootCompletion_Restart_…_Stop_KeepsTheResult` | Restart: same handle, new container instance ids. Stop: result kept, no more thinks, `TryRestart` gives a fresh instance. |
| `ResetInPlace_EqualsAFreshlyCreatedInstance_InStateAndBehaviour` | After 40 updates and a teardown, the reset machine and adapter bytes (memory, frames, generations, blackboard, versions, every generated leaf slot, transaction control with the process-wide ledger token normalized) equal a freshly bootstrapped instance's, and the next 60 updates give identical digests. |
| `Reserve_GrowsCapacity_…` | `TryReserve` keeps handles and running agents; a removed group is torn down, released one step later, and the program can be disposed. |
| `InvalidSettingsAndArguments_AreRefused` | Constructor and argument validation, null handle, disposed container. |
| `ContainerAgents_MatchStandaloneHosts_AtTheSameTimes(false/true)` | 6 agents × 2 deterministic trees, various `Every` schedules, 90 ticks. A `ProductionTreeHost` per agent is driven (`DriveOneUpdate`) at the same ticks with the same clock. Root result, failure, update count, active path and Tree-scope bytes are equal every tick, in place and in the job. |
| `Hosts_HaveZeroedRandomStreams_AndARandomNodeCannotRun` | Section 3. |
| `ContainerStreams_FollowTimeAndRandomV1_AndRollBothWays` | Section 3. |

Result: 17/17. Regression: section 4.

## 3. Random streams — finding

**Hosts have the gap.** `GeneratedTreeDispatchAdapterV2` allocates every random-capable leaf's
stream words zeroed, and no host path seeds them. The test reads them after
`ProductionTreeProgram.TryBootstrap`: state 0, increment 0. The dispatch owner requires an odd
increment (`NativeBurstDispatchBatchOwnerV2.ValidateCreateInput`, the `(RandomIncrements[i] & 1) == 0`
check; the same check is in `BurstDispatchBridgeCoreV2`). So the first update of a tree with
`aibt.stdlib.random-condition` fails: the host logs
`AIBT ProductionTreeHost: NativeLifetimeStateInvalid: Callback rejected: InvalidEncoding`, stops,
and has no root result. Every host is affected, including `NativeTreeAgent`, which bootstraps a
`ProductionTreeHost` (by code reading; the test drives a host directly). Random nodes are
refused by the default `Phase1` compilation policy (`AIBT2040`), so only projects that turn on
nondeterministic nodes reach this. Host behaviour is unchanged, as instructed. The test pins the
current behaviour, so a fix will have to update it.

**Container.** Each slot's stream is `TryDerive(RandomSeed, CanonicalSemanticHash, instanceId,
nodeIndex)`, reseeded on restart with the new instance id. The test checks the words of 64 agents
against `TryDerive`, odd increments, both outcomes over 256 rolls, and 0 faults.

## 4. Verification

All on Unity 6000.6.9f1, Modules Editor, one assembly at a time with nothing else running
(`perf-data/P10-009/logs/tests-step2.txt`).

| Check | Result | Baseline |
|---|---|---|
| `AIBT.Runtime.Tests` | 710/710 | 710/710 |
| `AIBT.Editor.Tests` | 635/636 (`McpToolDispatcherTests.ZeroCustomNodesReturnsExactlyThePhase1BuiltInCatalog`, known) | 635/636 |
| `AIBT.Integration.Tests` | 119/119 (102 + 17 new) | 102/102 |
| `AIBT.BehaviorCases.Tests` | 65/65 | 65/65 |
| `AIBT.NativeBurstDispatch.Tests` | 79/79 | 79/79 |
| `AIBT.NativeTreeAgentBlackboard.Tests` | 33/33 | 33/33 |
| `AIBT.NativeSharedBlackboard.Tests` | 36/36 | 36/36 |
| `AIBT.PlayMode.Tests` | 1/1 | 1/1 |
| ABI v2 (`AIBT.CodeGen.ContractTests`, includes `ExpectedPublicAbiV2.txt`) | 6/6 | unchanged file |
| `Verify-Static.ps1` | passed (work items: 173, schemas: 7) | |
| Public API (`Get-FullPublicApi.ps1 -BaselinePath perf-data\P10-007\api\public-api-p10007.txt`; P10-008 changed no API) | 564 types / 2 596 members; 64 additions, all in the 10 new `AIBT.Simulation` types; no removals or renames (`api/added.txt`) | |
| Generated API reference | `Documentation~/generated/api-reference-runtime.md` regenerated (294 to 304 types, additions only); the two generated-docs Editor tests pass | |

An earlier attempt to run the regression (Integration) hit the pipeline's 1 800 s timeout because
an IL2CPP Player build ran at the same time. The cancelled run left the Editor not executing
main-thread CLI commands. With the orchestrator's approval the Editor was closed gracefully
(`CloseMainWindow`; no dialog appeared) and reopened, and the regression above was run afterwards.

## 5. Measurement

**Setup.** Bench project `C:\UnityProjects\_aibt_acceptance\P9Samples-6069` (Unity 6000.6.9f1), the
AIBT dependency pointed at `C:\UnityProjects\_aibt_acceptance\aibt-p10009-6069`. That is a copy of
this working tree's tracked and new files at step 2, before the docs. The build log
`logs/build-il2cpp-20261117-011623.log` shows the resolved path. The harness `PerfRunner.cs` is
this commit's. IL2CPP non-development Player `Build\P10009`, stripping High, Burst on. The manifest
was restored to `aibt-659a8b1-6069` afterwards (`logs/manifest-original.json`).

**Set `sim`.** Per tree (`shared-loop16`, `shared-reactive16`), N = 500, 200 warm-up and 1 000
measured frames, in this order inside each run:

- A1: `NativeTreeAgent` with a profile of `UpdateCadence` 5 (no forced policy), agents enabled over
  5 frames so their phases spread;
- B1: `TreeSimulation` with default settings (`UseJobs`, `MinimumJobBatch` 64), one group,
  `Spread(5)`, one `Step` per frame inside the frame bracket (`PerfSimulationStepper`, early
  `Update`);
- C1: the same with `UseJobs` off;
- B2, A2.

About 100 agents think per frame in every scenario. The native ones ran as one agent-frame job per
frame (automatic selection; `agentFrameStats` ≈ 1.06 batches and 106 agents per frame), so A against
B compares job with job. 3 runs per Player pass. Two passes: default job workers (23,
`raw/il2cpp-20261117-022359`) and `JobWorkerCount = 0` (`raw/il2cpp-20261117-022450`). The machine
was otherwise idle (the Modules Editor was idle; no build or test ran). 0 faults, 0 failed steps.

**Statistic** (`Tools/sim.py`). Per run (one CSV): over the measured frames, the median and p99 of
`sim_step_us` (the `Step` call alone), the mean of `sim_step_alloc_bytes`, and the mean, median and
p99 of `bracket_us` (main-thread time of the whole frame). Per scenario: the median of the per-run
values (6 runs for A and B, 3 for C), min–max in brackets. ABBA ratio: median of the per-run frame
**means**, B/A, and per half (B1/A1, B2/A2). Full table: `perf-data/P10-009/sim-table.md`.

| workers | tree | Step µs median | Step µs p99 | bytes/tick | container frame µs (mean) | native cadence 5 frame µs (mean) | B/A | B1/A1 | B2/A2 |
|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|
| 23 | `shared-loop16` | 56.7 (53.5–59.6) | 221.2 (186.2–269.4) | 0 | 78.8 | 235.0 | 0.34 | 0.32 | 0.36 |
| 23 | `shared-reactive16` | 316.6 (263.7–335.0) | 432.5 (323.1–476.6) | 0 | 338.5 | 638.2 | 0.53 | 0.52 | 0.53 |
| 0 | `shared-loop16` | 75.6 (74.7–124.0) | 1194.9 (1150.9–1824.0) | 0 | 188.4 | 336.9 | 0.56 | 0.56 | 0.54 |
| 0 | `shared-reactive16` | 2417.4 (2407.1–2431.8) | 4159.0 (3121.4–4410.5) | 0 | 2508.2 | 2733.6 | 0.92 | 0.93 | 0.91 |

Container with `UseJobs` off (C), Step µs median / p99: `shared-loop16` 136.7 / 2050.1 (23
workers), 136.2 / 2097.4 (0); `shared-reactive16` 4967.3 / 5448.3 (23), 4943.2 / 7079.6 (0).

- **Zero managed allocation per tick** in every container scenario, jobs on and off (also 0 bytes
  in the harness's 60-frame GC-disabled window).
- **Container vs NativeTreeAgent cadence 5:** the frame costs 0.34× (`shared-loop16`) and 0.53×
  (`shared-reactive16`) with 23 workers, and 0.56× and 0.92× with 0 workers. Both halves of the ABBA
  agree within 0.04. The container does not pay for components, the scheduler's frame planning
  or the automatic run search.
- **p99 of `shared-loop16`:** every 9th think of an agent is long (the tree's own loop). With
  `Spread(5)` that is 5 consecutive heavy ticks every 45 ticks
  (`sim-shared-loop16-nojobs-c1_n500_r1.csv`: steps above 1 ms at frames 22–26, 67–71, …). The
  native frame p99 shows the same (687 µs vs a 197 µs median). This comes from the tree, not the
  container.
- **In place vs job with 0 workers:** the job still wins (`shared-reactive16` 2 417 vs 4 943 µs),
  because it runs the Burst-compiled node bodies, and the in-place path goes through the managed
  dispatch path as `Immediate` does.

## 6. Where reality differed from the ADR

1. **Process-wide owner ids reach failures.** D6 says the process-wide owner-id counters never
   reach results, but `NativeRuntimeFailureV1.OwnerId` carries one: the lifecycle machine sets it
   on lifetime failures. The container clears `OwnerId` in `AgentFault`. Hosts are unchanged.
2. **Restart in place needs ledger rebinding.** A reset dispatch slot must rebind the workspace's
   first ledger token and floors. Added `TryResetLedgerBinding` (internal, idle workspace only).
   The transaction integrity tag hashes the ledger token, so the reset-equals-fresh test compares
   the tag recomputed with token 0.
3. **Effects are discarded in G1.** Agents run with `EffectFree = -1` (no effect queue). Nodes that
   emit effects run, and their effects are dropped. The ADR's effect outputs belong to G2/G3.
4. **`CatalogTooOld` is not a G1 result.** A catalog without the agent-frame entry runs in place
   (same results); the sketch's refusal was not needed. Programs whose generated nodes bind
   anything other than Tree/Agent blackboard keys or effects get `InvalidArgument` from
   `TryAddGroup`.
5. **Extra public members** beyond the sketch: `TreeGroup.Id`, `Settings`, `IsValid`,
   `HighWaterIndex`, `TreeSimulation.Settings`, `GroupCount`, `IsDisposed`, `IsStepping`,
   `TreeSimulationSettings.Default`, `TreeGroupSettings.Default`, `AgentHandle.Group`.
6. **Job input is a per-step copy of the persistent table.** Due slots are not contiguous and the
   agent-frame job takes a contiguous range, so the due entries of the persistent per-slot table
   are copied into a contiguous list (no allocation after warm-up) and written back after the job.
7. **`Dispose` runs no teardown.** Nothing runs outside `Step`; remove groups and step once if
   aborts matter.
8. **Random nodes are not deterministic-policy nodes.** Test programs need
   `ReferenceCompilationPolicy(requireDeterministicNodes: false)`; `Phase1` refuses them
   (`AIBT2040`). This affects which trees a game can use, not the container.

## 7. What G2 must know

- Inputs: the per-slot `ProductionTreeAgentCore` has the generated adapter. Bulk inputs can write
  Tree/Agent values through the adapter before the think phase (between steps or in a new phase 0
  of `Step`), and versions must advance as the host's external writes do. D9(b) wake on input
  change hooks into `_due` selection in `TreeGroup.Think`.
- Effects and commands: the core's effect queue is not configured (`EffectFree = -1`). G2/G3 need a
  group-owned queue with per-step views like completions and faults.
- Fault ids: keep clearing process-wide owner ids at the API boundary (`AgentFault`).
- The host's random-stream gap (section 3) is outside the container; a separate decision is needed
  to change host behaviour.
- `TreeGroup.Think` is one method for schedule, reset and execution. The G2 input phase should run
  before it, inside the same `Busy` guard.
