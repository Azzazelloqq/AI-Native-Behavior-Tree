# Player performance harness (P9 baseline, P10-001)

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

`-Step Run -Quick` runs a fixed set of 20 cases for a before/after check of a runtime change:
N = 1000 and 5000 of `n-io-wait1`, `n-shared-loop16`, `n-shared-reactive16` and
`n-io-move1-fact-effect`, each with and without `-jobs`, plus `m-unity15-both` and
`m-shared-shape15`. The cases run 2 times by default, with 120 warm-up and 250 measured frames,
which takes about 10 minutes in an IL2CPP Player.

- **Baseline.** `-SaveBaseline` writes the summary to `-Baseline` (default
  `<OutDir parent>/quick-baseline-<backend>.json`).
- **Comparison.** Without `-SaveBaseline` the run is compared with that file: `quick.py compare`
  prints a before/after table and exits with code 2 when a single-threaded case (not `-jobs`) is
  more than 5% slower.
- **Location.** Keep `-OutDir` outside a Unity project's `Temp/`: the Editor clears `Temp/` when it
  restarts.

`-DefineSymbols AIBT_SCHEDULER_FRAME_PROFILE` builds a Player with the scheduler's per-stage frame
profile (`SchedulerFrameProfileV1`). Without the define its calls are compiled out.

- **Output.** The runner writes a `frameProfile` object per scenario to `runs.jsonl`.
- **Report.** `python profile.py <run folder>` writes `profile.json` and prints the median µs per
  frame for each stage, rounds per frame, lanes per round and dispatches per frame.
- **Not for timing.** Use the profile build to see where time goes, not as the timing reference.

## What is measured

- **Bracket:** main-thread time from the first script `Update` to the last `LateUpdate` of a frame
  (`PerfClock`), so it contains the agents' own work and nothing of the harness.
- **Allocation:** bytes allocated in a 60-frame window with the GC disabled, divided by agent-ticks.
- **GC/1000 frames:** collections during the measured frames.
- **Scheduler drive:** `ProductionTreeScheduler.LastFrameConsumedMicroseconds` for native scenarios.
- Worker-thread time of Jobs policies is **not** measured; only main-thread time is.

Scenario ids: `m-*` managed, `n-*` native, `-jobs` a forced `BatchedJobsSameFrame` profile,
`-pipelined` a forced `PipelinedJobs` profile (both with `SchedulerJobsCapabilities(1, 1, 1, 64,
64, pipelined)` set on the shared world's scheduler), `-interval100ms(-stagger)` Interval ticking
every 100 ms (with `StaggerIntervalTicks`), `-fact`/`-bbwrite`/`-effect` game code writing facts or
blackboard values or receiving effects every frame.
