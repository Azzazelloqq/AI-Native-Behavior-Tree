# P7-035 implementation plan (draft, pre-measurement)

Written before any run. Per the card's own Forbidden-changes clause, once the canonical run begins
the workload/policy/population design below is locked -- a discovered defect stops the run rather
than getting silently retuned. This plan exists so the design is reviewed once, up front.

## Harness shape (mirrors P4-008's already-accepted pattern)

New `Benchmarks~/Phase7/SwarmArena/`:

- `Run-SwarmArenaPlayerBenchmark.ps1` -- builds a fresh isolated Unity project (copies `Runtime/`,
  `Authoring/`, `Samples~/SwarmArena/` unchanged, plus this card's own `Unity/` folder), builds a
  release, non-development, IL2CPP, Burst-enabled `StandaloneWindows64` Player, runs the built
  `.exe`, verifies build-proof metadata (target/arch/backend/Burst/non-dev) before accepting the run
  -- same acceptance gate `P4-008`'s driver already uses.
- `Unity/Editor/SwarmArenaBenchmarkBuild.cs` -- build step, mirrors
  `WindowsPlatformBenchmarkBuild.cs`.
- `Unity/Runtime/SwarmArenaBenchmarkProbe.cs` -- `[RuntimeInitializeOnLoadMethod]`-driven headless
  matrix runner + JSON writer, mirrors `WindowsPlatformSchedulingProbe.cs`. No keyboard input, no
  `OnGUI` (both are the showcase's own concerns, not the benchmark's).

The exact tree (`swarm-arena.aibt.json`), catalog (`SwarmArenaCatalog`), seed (`7034`), spawn ring
layout and 4-second x 5-phase scripted timeline (Patrol/Pursue/Flee/Attack/Return) are copied in
unchanged from `Samples~/SwarmArena/` -- same discipline as P4-008 copying `SchedulingScenarios.cs`
unchanged. The workload never changes between comparisons; only the assigned policy and population
do.

## Real deviation from the showcase, flagged explicitly

The showcase splits each population 50/50 between `SchedulingProfile.Normal` (Auto) and a
`PipelinedJobs`-forced profile, for a visually interesting mixed demo. That mixing is exactly what
this card must NOT do: `P7-035`'s own acceptance criteria require distinguishing scheduler cost
*per policy*. Every benchmark cell assigns **one policy to the entire population** for that cell:
`Auto` (unset `ForcedPolicy`), `Immediate`, `Budgeted`, `BatchedJobsSameFrame`, `PipelinedJobs`.

The showcase also configures `SchedulerJobsCapabilities` with placeholder values
(`minimumJobWorkloadNanoseconds=1.0`, `targetBatchWorkNanoseconds=1.0`, batch range fixed
`1..32`) -- adequate for a visual demo, not for measuring real Jobs-policy performance. This
benchmark instead derives its batch target from the already-accepted, already-recalibrated
`NativeWorkEstimatorV1.CalibratedNanosecondsPerNodeStep` (`60.275` ns/step, real Player data,
`P4-004`'s 2026-08-26 addendum) and lets batch size scale with population per
`Documentation~/execution-and-scheduling.md`'s own formula, rather than reusing the showcase's fixed
32 -- otherwise this benchmark would just re-demonstrate `P4-002`'s already-known "fixed batch size
does not amortize" artifact instead of measuring what the scheduler can actually do when sized
correctly. `BatchedJobsSameFrame` is still measured at both a fixed-32 cell AND a calibrated cell,
since the fixed-batch behavior is itself a real, disclosed comparison point, not something to hide.

## Population points

Proposing **16 / 64 / 256 / 512 / 1024**: 64/256/512 for direct continuity with the already-reviewed
Swarm Arena showcase story; 16 to anchor against Phase 4's own existing agent-count axis; 1024 as a
disclosed overload/stress point (`P4-002`'s own convention). All five are kept regardless of outcome
-- a losing or overloaded point is not a reason to drop it.

## Timing domains -> concrete sources

| Required field | Source |
| --- | --- |
| AIBT scheduler time | `ProductionTreeScheduler.LastFrameConsumedMicroseconds` (already the scheduler's own internal `Stopwatch`-measured figure, public) |
| registered/runnable/completed/deferred | `ProductionTreeScheduler.FrameEntryCount` / `TryGetFrameEntry` (`SchedulerFrameDisposition`) |
| movement/input-sampling cost | dedicated `Stopwatch` bracketing `SampleInputs`/`TryFlushInputs`/`Receive` for the whole population (the sample has no neighbor/spatial query -- only beacon/danger transform reads) |
| rendering cost | dedicated `Stopwatch` bracketing `SwarmArenaInstancedPresenter.Render`; a parallel rendering-disabled cell skips the call entirely for CPU isolation, same simulation work |
| whole-frame time | hi-res `Stopwatch` around the full `Update`, cross-checked against `Time.unscaledDeltaTime` |
| chosen policy/reason/confidence, batch size/count, worker count | `NativeAutoExplanationV1` (already surfaced for `Auto`; forced-policy cells record the forced policy directly, no explanation needed) |
| managed allocation after warmup | `Profiler.GetMonoUsedSizeLong` delta across a warmed sample window (no live Profiler connection needed for this one counter) |
| workload/content hashes, seed, scene/build identity, versions, backend, Burst, IL2CPP, dev flag, OS, CPU | build-proof JSON, same fields `P4-008`'s own `-build.raw.json` already captures |

`PipelinedJobs` is measured with its real delayed-result semantics (a wave's effects land at the
next idle barrier, per `ADR-P7-041`) -- never resampled as if it were same-frame data, per the
card's own explicit acceptance criterion.

## Warmup / sampling

Per (population, policy, render-mode) cell: discard the first full phase cycle (20 logical seconds:
5 phases x 4s) as warmup -- long enough to clear `PipelinedJobs`' own initial queue fill -- then
record 3 further full phase cycles as measured samples (60s), keeping every raw per-frame sample,
never averaging before storage.

## Matrix size and real wall-clock cost (disclosed up front, not discovered mid-run)

5 populations x 5 policies x 2 render modes = 50 cells x ~80s each (20s warmup + 60s measured) ~=
67 minutes of real run time, plus one real non-development IL2CPP/Burst Windows x64 build (typically
several minutes). `BatchedJobsSameFrame`'s extra fixed-batch-32 comparison cell adds 5 populations x
2 render modes x 80s ~= 13 more minutes. Total order of magnitude: ~80-90 minutes of unattended
Player run time after the build completes. This runs the isolated build's `.exe` directly, not the
Editor -- it does not require the Editor to stay open or undisturbed during the run.

## Allowed/forbidden changes (restated from the card, unchanged)

No scheduler/profile/tree/node/gameplay/population-selection tuning once the canonical run starts.
A discovered defect stops the run; the attempted result is retained as invalid evidence, not
silently discarded. No Editor numbers stand in for Player numbers anywhere in the final report.
