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
whose id contains one of the parts, `-Quick` shortens warm-up and measurement, `-MaxN`/`-MinN`
bound the population. Raw output goes to `<project>/PerfResults/raw/<backend>-<timestamp>/`
(`-OutDir` to change it): `environment.json`, `runs.jsonl` (one line per scenario, N and run) and
one CSV per run with a row per measured frame.

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
