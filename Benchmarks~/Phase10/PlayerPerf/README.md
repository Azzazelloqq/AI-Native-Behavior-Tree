# Player performance harness (P9 baseline, P10-001 … P10-005)

Measures both runtime layers in a standalone Player: per-frame main-thread time, allocation per
agent-tick, GC collections, creation cost and memory, for managed (`BehaviorTreeAgent`) and native
(`NativeTreeAgent`) agents from 1 to 20 000. The P9 baseline and the P10-001 re-measurement were
made with it; their analysis is in `Documentation~/performance.md` and
`Planning~/Evidence/P10-001/README.md`.

This folder is source only. It is not compiled by the package (`Benchmarks~` is ignored by Unity).

## Setup

1. A Unity 6 consumer project that references the AIBT package and has imported the **Simple
   Agent** and **First Agent** samples (the harness uses `SimpleAgent` components for the realistic
   scenario and the First Agent move node in its catalog).
2. Copy `Assets/AibtPerf/` into the project's `Assets/` and `Tools/` to `<project>/AibtPerfTools/`.
3. Run from `<project>/AibtPerfTools`:

```text
powershell -NoProfile -ExecutionPolicy Bypass -File Invoke-Perf.ps1 -Step All -Backend il2cpp -Runs 3 -UnityPath <Unity.exe>
python analyze.py <output folder>            # writes summary.json and tables.md next to the raw data
```

`-Step Prepare` regenerates the trees (`gen_trees.py`), prepares the native runtime assets and
writes the scene; `-Step Build` builds a non-development Player (`-Backend il2cpp|mono`,
`-Stripping High`); `-Step Run` runs every scenario `-Runs` times. `-Only a,b` keeps the scenarios
whose id contains one of the parts, `-Short` shortens warm-up and measurement, `-MaxN`/`-MinN`
bound the population. Raw output goes to `<project>/PerfResults/raw/<backend>-<timestamp>/`
(`-OutDir` to change it): `environment.json`, `runs.jsonl` (one line per scenario, N and run) and
one CSV per run with a row per measured frame.

## Quick mode and frame profile (P10-002)

`-Step Run -Quick` runs a fixed set of 36 cases for a before/after check of a runtime change:
N = 1000 and 5000 of `n-io-wait1`, `n-shared-loop16`, `n-shared-reactive16` and
`n-io-move1-fact-effect`, each plain (default Immediate), `-jobs`, `-agentframe` and
`-agentframe-fp` (P10-004), plus `m-unity15-both` and `m-shared-shape15`. The cases run 2 times by
default, with 120 warm-up and 250 measured frames, which takes about 15 minutes in an IL2CPP Player.

- **Baseline.** `-SaveBaseline` writes the summary to `-Baseline` (default
  `<OutDir parent>/quick-baseline-<backend>.json`).
- **Comparison.** Without `-SaveBaseline` the run is compared with that file: `quick.py compare`
  prints a before/after table and exits with code 2 when a single-threaded case (not `-jobs` or `-agentframe*`) is
  more than 5% slower.
- **Location.** Keep `-OutDir` outside a Unity project's `Temp/`: the Editor clears `Temp/` when it
  restarts.

`-DefineSymbols AIBT_SCHEDULER_FRAME_PROFILE` builds a Player with the scheduler's per-stage frame
profile (`SchedulerFrameProfileV1`). Without the define its calls are compiled out.

- **Output.** The runner writes a `frameProfile` object per scenario to `runs.jsonl`.
- **Report.** `python profile.py <run folder>` writes `profile.json` and prints the median µs per
  frame for each stage, rounds per frame, lanes per round and dispatches per frame.
- **Not for timing.** Use the profile build to see where time goes, not as the timing reference.

## Stage J3 sets and CPU time (P10-005)

`-Set <a,b,...>` (`-perfSet` in the Player) runs one or more named sets; `-JobWorkers <n>` sets
`JobsUtility.JobWorkerCount` (`-perfJobWorkers`). Policies of one tree and N are adjacent in every
set, so each run interleaves them.

| Set | Cases |
|---|---|
| `j3-small` | N = 1, 10, 50, 100, 200, 500 × `io-wait1`, `shared-loop16`, `shared-reactive16`, `io-move1-fact-effect` × Immediate, `-jobs`, `-agentframe`; 200 warm-up and 1000 measured frames |
| `j3-cross` | N = 10, 20, 30, 40, 50, 75 × the same trees × Immediate and `-agentframe`; 200 warm-up and 1500 measured frames (repeats around the crossover) |
| `j3-scale` | N = 1 000, 5 000, 20 000 × the same trees and policies, plus `m-unity15-both` and `m-shared-shape15` (the N = 1 000 / 5 000 part has the quick-set configuration) |
| `j3-scale20k` | the native part of `j3-scale` at N = 20 000 only (for `-JobWorkers 0`) |
| `j3-workers` | Immediate and `-agentframe` at N = 1 000 and 5 000 (run with several `-JobWorkers`) |
| `j3-mixed` | Immediate, `-agentframe`, `-agentframe-mixed` (every second host has `StepBudget = 1 000 000`, so it falls back with `StepBudgetConfigured`) and `-agentframe-mixed-grouped` (the second half of the hosts falls back) |
| `j3-cadence` | `-cadence6` (profile with `UpdateCadence = 6`, Immediate or forced `JobsAgentFrame`) and `-cadence6-stagger` (agents enabled in 6 portions over 6 frames, so their phases spread) |
| `full-immediate` | the full plan without the forced Jobs scenarios |
| `full-immediate-5k` | the same without N > 5 000 |
| `p10007` | P10-007: `shared-loop16` and `shared-reactive16` with the default profile (no forced policy) at N = 16, 63, 64, 256, 2 000, with `agentFrameStats` so the automatic choice is visible; for a before/after Player pair |

`runs.jsonl` also has `cpuWindowProcessUs`, `cpuWindowMainThreadUs` and `cpuWindowWallUs`: process and
main-thread CPU time over the measured frames (`GetProcessTimes` / `GetThreadTimes`, Windows Player
only, -1 elsewhere). Process CPU includes the render thread, job workers (also idle spinning) and
the engine, so compare it only between policies of the same N.

```text
python j3.py <summary.json> <run folder> [<run folder> ...]   # per scenario, N and worker count
python j3_tables.py <summary.json> [...] > tables.md          # the P10-005 tables
```

`j3_tables.py` also splits the main thread under JobsAgentFrame into the agent-frame driver
(`scheduler_drive_us`), the job's schedule + complete (`agentFrameStats.jobMicroseconds`), the
managed begin/finish (their difference) and the rest of the frame.

**Unity version.** From P10-005 on the harness project runs on Unity 6000.6.9f1 (6000.7.0b1 was
removed from the machine). The copy needs three built-in modules removed from its manifest
(`localizationruntime`, `smartstrings`, `spritemask`), and the AIBT package at `659a8b1` needs
`#if UNITY_6000_6_OR_NEWER` in `Editor/Graph/BehaviorTreeImporter.cs` to compile there
(`Planning~/Evidence/P10-005/README.md`, section 0). Numbers before P10-005 come from 6000.7.0b1
and are not directly comparable.

## Simulation container inputs and commands (P10-010)

Set `sim-io` measures `TreeSimulation` (stage G2) with game inputs and commands.
- **Agents and timing.** 500 agents, `Spread(5)`, jobs on, 200 warm-up and 1000 measured ticks.
- **Pairs.** Each pair runs ABBA: `sim-shared-loop16` vs `sim-io-loop16`, and `sim-shared-reactive16`
  vs `sim-io-reactive16`.
- **The `io-*` trees** (`Tools/gen_trees.py`, `io_trees`) use the nodes in `Assets/AibtPerf/IoNodes`
  (`PerfIoShard`, own assembly `AIBT.Perf.IoNodes`).
  - Their input condition reads the agent's `PerfSense` row.
  - Their order nodes emit `PerfOrder` commands.
- **Per tick.** Before `Step`, the runner writes every row of the bound `PerfSense` array (outside
  the timing). After `Step` it reads the commands.
- **`runs.jsonl`.** `sim.io`, `sim.commandsMeasured`, `sim.ticksMeasured` and `sim.commandChecksum`.

```text
python sim_io.py <run folder> [<run folder> ...] > sim-io-table.md   # per scenario and ABBA D/B
```

`PerfFrameBegin`, `PerfFrameEnd` and `PerfFeeder` live in files named after their class (P10-010).
When they shared `PerfClock.cs`, Unity 6000.6 embedded their MonoScripts in the scene. With a
catalog containing an `[AibtInput]` node, one of them then resolved as missing in the build, and
the Player crashed loading `level0` (`Planning~/Evidence/P10-010/README.md`, deviation 10).

## What is measured

- **Bracket:** main-thread time from the first script `Update` to the last `LateUpdate` of a frame
  (`PerfClock`), so it contains the agents' own work and nothing of the harness.
- **Allocation:** bytes allocated in a 60-frame window with the GC disabled, divided by agent-ticks.
- **GC/1000 frames:** collections during the measured frames.
- **Scheduler drive:** `ProductionTreeScheduler.LastFrameConsumedMicroseconds` for native scenarios.
- **Frame wall time:** `frame_dt_us` in each CSV (`Time.unscaledDeltaTime`).
- Worker-thread time of Jobs policies is **not** measured directly. For total CPU time run the
  Player with `-ExtraPlayerArgs @('-perfJobWorkers','0')` (sets `JobsUtility.JobWorkerCount = 0`,
  so every job runs on the main thread inside the bracket; call `Invoke-Perf.ps1` with `&`, not
  `powershell -File`, so the array survives).
- `-agentframe` scenarios add `agentFrameStats` to `runs.jsonl`: job batches, agents, agents that
  ran on a worker thread, agents whose job ran without Burst, dispatches, and µs spent in schedule
  plus complete (`AgentFrameDiagnosticsV1`, read through reflection).

Scenario ids: `m-*` managed, `n-*` native, `-jobs` a forced `BatchedJobsSameFrame` profile,
`-agentframe` `SchedulingProfile.JobsAgentFrame`, `-agentframe-fp` the same with the node body
reached through a Burst function pointer instead of the catalog's emitted job (benchmark only:
`PerfAgentFramePointer.cs` swaps the catalog's executor through reflection),
`-pipelined` a forced `PipelinedJobs` profile (both with `SchedulerJobsCapabilities(1, 1, 1, 64,
64, pipelined)` set on the shared world's scheduler), `-interval100ms(-stagger)` Interval ticking
every 100 ms (with `StaggerIntervalTicks`), `-fact`/`-bbwrite`/`-effect` game code writing facts or
blackboard values or receiving effects every frame.
