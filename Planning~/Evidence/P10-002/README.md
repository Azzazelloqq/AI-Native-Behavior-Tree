# P10-002 evidence — Jobs stage J1

Base commit: `c54bbaf` (submodule `Assets/AIBT`, branch `feature/aibt-consumer-workflow`).
Every number below comes from the IL2CPP non-development Windows Player of the P9Samples harness
(`Benchmarks~/Phase10/PlayerPerf`), stripping High, unless a row says otherwise.

Raw measurement data lives outside the repository and outside `Temp/`:
`C:\UnityProjects\_aibt_acceptance\perf-data\P10-002\`. It holds the following:

- `quick-baseline-il2cpp.json` and `quick-after-il2cpp.json`: quick summaries.
- `raw/il2cpp-*`: per-run CSV, `runs.jsonl` and `player.log`.
- `abba-*.json`: interleaved runs.
- `logs/`: build and ABBA logs.
- `api/`: public API dumps.

## 1. Quick benchmark mode

`Invoke-Perf.ps1 -Step Run -Quick` runs 20 cases:

- N = 1000 and 5000;
- `n-io-wait1`, `n-shared-loop16`, `n-shared-reactive16` and `n-io-move1-fact-effect`, each under
  Immediate and BatchedJobsSameFrame (`-jobs` suffix);
- plus `m-unity15-both` and `m-shared-shape15`.

Each case runs 2 times, with 120 warm-up and 250 measured frames.

- **Baseline and comparison.** `-SaveBaseline` stores `quick-baseline-<backend>.json`. Without it,
  the run is compared against that file (`Tools/quick.py compare`). The compare exits with code 2
  when a single-threaded scenario is more than 5% slower. `-jobs` cases are reported but not gated.
- **Duration.** About 10 minutes with 2 runs and a built Player: 10.0 min for the baseline, 9.0 min
  after the change. Building the IL2CPP Player adds about 1 minute when the cache is warm.
- **Measurement cap.** `n-shared-reactive16` at N = 5000 hits the 20 s measurement cap, so it
  measures about 30 frames instead of 250.

### Lost first baseline, and how it was re-measured

The first baseline (run `il2cpp-20261115-162047`) and the profile run (section 2) were written to
`C:\UnityProjects\Modules\Temp\perf\`. That folder was emptied when the Modules Editor was restarted
(section 7), and those raw files are gone. Their summary numbers survive only in an earlier draft
of this file. They are not used for any comparison below.

The baseline was re-measured honestly:

1. A detached worktree of the submodule at `c54bbaf`: `C:\UnityProjects\_aibt_acceptance\aibt-c54bbaf`.
2. A copy of the harness project whose manifest points at that worktree:
   `C:\UnityProjects\_aibt_acceptance\P9Samples-c54bbaf`.
3. A fresh IL2CPP Player built there: `Build\Quick-il2cpp-base`.
4. A quick run of that Player, run `il2cpp-20261115-173222`.

The table in section 3 uses it.

## 2. Frame profile (before)

- **Instrumentation.** `SchedulerFrameProfileV1` (internal) adds `[Conditional("AIBT_SCHEDULER_FRAME_PROFILE")]`
  laps and counters to `ProductionTreeScheduler`, `ProductionTreeHost` and the batched group
  driver. Without the define, the compiler removes every call site, so release builds pay nothing.
- **Profile build.** Built with `Invoke-Perf.ps1 -DefineSymbols AIBT_SCHEDULER_FRAME_PROFILE`, run
  `il2cpp-20261115-164637` (1 run). Parse a run with `Tools/profile.py <run folder>`. This run was
  written to `Temp/perf` and its raw files were lost (section 1); the table below is the copy made
  at the time.
- **Caveat.** During this run the Modules Editor was hung and kept about one core busy (section 7).
  Totals match the baseline within about 3%.

Median µs per frame by stage. Immediate stages are summed over all hosts; Jobs stages over all rounds.

| Scenario | N | Bridge | Due | Book | HostBegin | HostAdvance | HostRecord | HostDispatch | HostComplete | RoundBegin | Allocate | Schedule | Complete | Dispose | Handle | Resolve | Total | Rounds | Dispatches |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| n-io-wait1 | 1000 | 43 | 33 | 104 | 210 | 138 | 160 | 2 858 | 110 | | | | | | | | 3 656 | | |
| n-io-wait1-jobs | 1000 | 43 | 35 | 40 | | | | | | 184 | 506 | 430 | 522 | 76 | 398 | 2 146 | 4 407 | 2 | 1 000 |
| n-io-wait1 | 5000 | 441 | 275 | 669 | 2 003 | 1 195 | 814 | 16 795 | 556 | | | | | | | | 22 749 | | |
| n-io-wait1-jobs | 5000 | 437 | 285 | 246 | | | | | | 1 684 | 5 264 | 2 729 | 803 | 439 | 1 501 | 11 860 | 25 445 | 2 | 5 000 |
| n-shared-loop16 | 1000 | 51 | 35 | 117 | 289 | 373 | 238 | 6 936 | 216 | | | | | | | | 8 255 | | |
| n-shared-loop16-jobs | 1000 | 54 | 39 | 44 | | | | | | 195 | 1 826 | 1 232 | 1 464 | 216 | 447 | 5 477 | 11 026 | 5.58 | 2 344 |
| n-shared-loop16 | 5000 | 536 | 309 | 843 | 2 840 | 1 999 | 1 196 | 37 038 | 1 081 | | | | | | | | 45 844 | | |
| n-shared-loop16-jobs | 5000 | 461 | 285 | 282 | | | | | | 2 010 | 12 897 | 7 861 | 2 089 | 1 183 | 2 918 | 30 605 | 60 802 | 5.42 | 11 420 |
| n-io-move1-fact-effect | 1000 | 149 | 33 | 113 | 264 | 158 | 170 | 3 983 | 155 | | | | | | | | 5 025 | | |
| n-io-move1-fact-effect-jobs | 1000 | 185 | 37 | 44 | | | | | | 216 | 1 075 | 417 | 538 | 77 | 630 | 5 398 | 8 649 | 2 | 1 000 |
| n-io-move1-fact-effect | 5000 | 1 707 | 280 | 789 | 2 367 | 1 168 | 833 | 22 362 | 813 | | | | | | | | 30 318 | | |
| n-io-move1-fact-effect-jobs | 5000 | 1 784 | 291 | 360 | | | | | | 3 217 | 7 827 | 2 971 | 740 | 447 | 2 854 | 75 970 | 96 826 | 2 | 5 000 |
| n-shared-reactive16 | 1000 | 100 | 41 | 206 | 624 | 4 866 | 1 568 | 121 539 | 4 087 | | | | | | | | 133 031 | | |
| n-shared-reactive16-jobs | 1000 | 65 | 40 | 51 | | | | | | 206 | 21 951 | 17 784 | 21 726 | 3 131 | 4 996 | 104 299 | 174 285 | 77 | 46 000 |
| n-shared-reactive16 | 5000 | 743 | 349 | 1 995 | 3 621 | 25 336 | 8 336 | 635 832 | 21 752 | | | | | | | | 697 965 | | |
| n-shared-reactive16-jobs | 5000 | 500 | 292 | 344 | | | | | | 1 932 | 215 859 | 116 462 | 26 863 | 16 842 | 27 633 | 667 402 | 1 074 345 | 77 | 230 000 |

Lanes per round equal N in every Jobs case. The profile does not measure worker time per job;
`RoundComplete` is the main thread's wait for the workers.

Findings:

- **Dispatch dominates both policies.** It takes 70–90% of the frame (Immediate `HostDispatch`, Jobs
  `RoundResolve`). Most of that time is the managed framing around the Burst body.
- **Jobs pay round overhead on top of the same dispatch.** That overhead is Allocate + Schedule +
  Complete + Dispose:
  - about 1.5 µs per agent at N = 1000 with 2 rounds;
  - about 4.7 µs per agent with 5.6 rounds (loop16);
  - about 65 µs per agent with 77 rounds (reactive16).

  It grows with the number of rounds per frame.
- **Fact and effect at N = 5000.** Jobs `RoundResolve` is 76 ms against Immediate `HostDispatch`
  22 ms. The group dispatch path with effect capture costs 3.4× per member at that size. This
  outlier is for J2 to address.

## 3. Round overhead (step 3)

### What changed

- **Driver.** `ProductionBatchedGroupDriverV1` is now one instance per `ProductionTreeScheduler`. It
  used to be a static that built new lists and arrays every round. It keeps these across frames:
  - the open and next lane lists (swapped each round);
  - the pending-dispatch list and the dispatch group scratch;
  - a grow-only machine array;
  - one `NativeBatchedLifecyclePoolV1`.

  A reentrant call (a host updated from inside an effect handler) or a call after disposal uses a
  temporary driver, so the shared storage is never used twice at once. The scheduler reuses its
  same-frame group host list; the pipelined path still builds its own list, because it outlives
  the frame. The scheduler disposes the driver after the group workspaces.
- **Round pool.** `NativeBatchedLifecyclePoolV1` (internal) replaces the per-round
  `NativeBatchedLifecycleOwnerV1`. The old owner created a set of NativeArrays and one job per lane
  every round. The pool keeps grow-only Persistent `UnsafeList`s:
  - the machines;
  - the results, failures and success flags.

  It runs each round as one Burst `IJobFor.ScheduleParallel` over the lanes.
  - **Same validation.** It checks owner ids and lanes the same way, with the same failure codes.
  - **Same failure selection.** `TryComplete` reports failures exactly as the owner did: the first
    lane without success whose failure code is not `None`, in lane order.
  - **Unchanged scope.** Dispatch (`ProductionDispatchResolverV1`) is unchanged.
- **Semantics.** Unchanged:
  - the lanes run the same `NativeLifecycleMachineV1.TryAdvance`, one step per round;
  - dispatch order is the lane order, as before;
  - tick order, statuses, effect order, traces and diagnostics are unchanged;
  - scheduler defaults, ABI v2 and the public API are unchanged (section 9).

### Allocation

`WarmedUpBatchedJobsFrames_DoNotAllocateManagedMemory`: 32 non-restarting agents, forced
BatchedJobsSameFrame, 200 warmed-up frames.

- **Before** (runtime at `c54bbaf` plus profile hooks only): 1 131 200 managed allocations, so the
  test failed.
- **After:** 0, so it passes.

In the Player, every Jobs case went from about 1.1 KB per agent tick (wait1, move1), about 3 KB
(loop16) and about 37 KB (reactive16) to 0 B.

### Quick before/after (IL2CPP)

Before: run `il2cpp-20261115-173222` (`c54bbaf`, 10.0 min). After: run `il2cpp-20261115-174251`
(9.0 min). The before Player is `P9Samples-c54bbaf\Build\Quick-il2cpp-base`; the after Player is
`P9Samples\Build\Quick-il2cpp-after`. Both are built with the same harness and settings. Values
are mean µs per frame over 2 runs, with B per agent tick in brackets where it is not 0.

| Scenario | N | Before | After | Change | Gate |
|---|---:|---:|---:|---:|---|
| m-shared-shape15 | 1000 | 1 018 | 1 031 | +1.3% | single |
| m-shared-shape15 | 5000 | 5 961 | 6 292 | +5.6% | single, flagged |
| m-unity15-both | 1000 | 1 157 | 1 172 | +1.3% | single |
| m-unity15-both | 5000 | 7 301 | 7 659 | +4.9% | single |
| n-io-move1-fact-effect | 1000 | 4 896 | 5 025 | +2.6% | single |
| n-io-move1-fact-effect | 5000 | 28 447 | 28 224 | −0.8% | single |
| n-io-move1-fact-effect-jobs | 1000 | 8 460 (1 102 B) | 6 534 | −22.8% | jobs |
| n-io-move1-fact-effect-jobs | 5000 | 90 004 (1 067 B) | 80 034 | −11.1% | jobs |
| n-io-wait1 | 1000 | 3 688 | 3 840 | +4.1% | single |
| n-io-wait1 | 5000 | 20 719 | 21 886 | +5.6% | single, flagged |
| n-io-wait1-jobs | 1000 | 5 048 (1 102 B) | 3 185 | −36.9% | jobs |
| n-io-wait1-jobs | 5000 | 26 119 (1 067 B) | 18 564 | −28.9% | jobs |
| n-shared-loop16 | 1000 | 7 801 | 8 299 | +6.4% | single, flagged |
| n-shared-loop16 | 5000 | 42 675 | 43 447 | +1.8% | single |
| n-shared-loop16-jobs | 1000 | 11 512 (3 026 B) | 6 795 | −41.0% | jobs |
| n-shared-loop16-jobs | 5000 | 63 223 (2 758 B) | 38 096 | −39.7% | jobs |
| n-shared-reactive16 | 1000 | 128 171 | 127 937 | −0.2% | single |
| n-shared-reactive16 | 5000 | 641 554 | 636 838 | −0.7% | single |
| n-shared-reactive16-jobs | 1000 | 180 452 (38 339 B) | 119 287 | −33.9% | jobs |
| n-shared-reactive16-jobs | 5000 | 951 262 (36 868 B) | 626 632 | −34.1% | jobs |

Three single-threaded cases crossed the 5% gate (exit code 2). `m-shared-shape15` is a managed-layer
scenario that P10-002 does not touch. The Immediate native path differs from `c54bbaf` only by
profile calls that the compiler removes. That suggests run-to-run drift between two sequential
runs, so the flagged cases were re-measured with interleaved runs.

### Interleaved re-measurement (ABBA)

`abba2.ps1` (in `perf-data\P10-002\logs\`) ran the cases `m-shared-shape15`, `n-io-wait1` and
`n-shared-loop16`, each with its `-jobs` variant where one exists. Each pass ran them 3 times in
one Player process, in the order A1 (before), B1 (after), B2 (after), A2 (before):

| Pass | Player | Run | Duration | Faults |
|---|---|---|---:|---:|
| A1 | before | `il2cpp-20261115-205320` | 4.5 min | 0 |
| B1 | after | `il2cpp-20261115-205752` | 3.6 min | 0 |
| B2 | after | `il2cpp-20261115-210135` | 3.7 min | 0 |
| A2 | before | `il2cpp-20261115-210517` | 4.5 min | 0 |

Mean µs per frame:

| Scenario | N | A1 | B1 | B2 | A2 | A1→B1 | A2→B2 | A1→A2 (noise) |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| m-shared-shape15 | 1000 | 1 086 | 1 034 | 1 036 | 1 062 | −4.8% | −2.5% | −2.2% |
| m-shared-shape15 | 5000 | 6 992 | 6 310 | 6 223 | 6 720 | −9.8% | −7.4% | −3.9% |
| n-io-wait1 | 1000 | 3 887 | 3 743 | 3 784 | 3 921 | −3.7% | −3.5% | +0.9% |
| n-io-wait1 | 5000 | 23 570 | 22 215 | 22 131 | 21 846 | −5.7% | +1.3% | −7.3% |
| n-shared-loop16 | 1000 | 8 161 | 8 145 | 8 268 | 8 381 | −0.2% | −1.4% | +2.7% |
| n-shared-loop16 | 5000 | 43 625 | 44 078 | 43 975 | 45 163 | +1.0% | −2.6% | +3.5% |
| n-io-wait1-jobs | 1000 | 6 020 | 3 141 | 3 233 | 6 086 | −47.8% | −46.9% | +1.1% |
| n-io-wait1-jobs | 5000 | 27 053 | 18 303 | 18 393 | 27 868 | −32.3% | −34.0% | +3.0% |
| n-shared-loop16-jobs | 1000 | 12 856 | 6 706 | 6 816 | 12 969 | −47.8% | −47.4% | +0.9% |
| n-shared-loop16-jobs | 5000 | 63 680 | 37 915 | 37 833 | 67 091 | −40.5% | −43.6% | +5.4% |

- **No real regression.** No single-threaded case is more than 5% slower in either interleaved
  comparison. The three flags from the sequential run do not reproduce.
- **Noise is wider than the gate.** The same Player differs from itself by up to 7.3% (A1→A2,
  `n-io-wait1` N = 5000). A sequential before/after pair is therefore not enough to decide a 5% gate.
- **Jobs gains reproduce.** They match the sequential run: 32–48% less main-thread time, 0 B per
  agent tick.
- **Long runs complete.** The after Player completed 3 runs of every case in one process, including
  `n-shared-loop16-jobs` at N = 5000, twice.

## 4. Idle-agent skipping (step 4): not implemented

Skipping an agent that only waits cannot be observably equivalent today:

- **Waits count ticks.** `stdlib.wait` counts ticks (`memory.Elapsed++` in each `Tick`). Not ticking
  an agent changes the frame it completes on.
- **Every update is traced.** Each update records `UpdateStarted`, `NodeTicked` and
  `UpdateCompleted` with an `updateId`, so skipped updates change the trace and `TotalUpdates`.
- **No node declares a wake condition.** Neither a time deadline nor a key change is declared, so the
  scheduler cannot know when skipping is safe for a generated node.

Implementing it would need:

- a new node contract ("sleep until frame/time/key") in ABI v2 or alongside it;
- trace records for skipped updates, or a decision that they disappear;
- an `updateId` policy.

All of these are owner decisions, so this stage does not implement it.

## 5. Equivalence test

`Tests/Integration/NativeRuntime/JobsPolicyEquivalenceTests.cs` runs 12 agents (4 per tree) on three
trees for 90 frames under three profiles: the default profile, forced Immediate and forced
BatchedJobsSameFrame. The trees cover:

- restarts (Always);
- reactive aborts (a reactive sequence whose guard is cleared every few frames);
- per-frame facts;
- effects;
- parallel, memory-selector and repeater (a looping tree whose every iteration returns Running).

Each frame it compares, per agent:

- the trace records without the instance id;
- the root result, running state, run count and error;
- effects, with frame and agent.

It also asserts that effects are actually delivered (more than 20).

- **Before the runtime change:** passed. This was the runtime at `c54bbaf` plus profile hooks only.
  In that run `Integration` was 80/81; the only failure was the allocation test (section 3).
- **After:** passed.
- **Not covered.** Async completions: the facade passes no completions.

## 6. Step limit per update (orchestrator question)

Question: does the runtime have a protective limit on steps per update for Immediate or Jobs?
Answer: **no; none exists, so none fired.**

- **Immediate.** `ProductionTreeHost.RunSegment(StepBudget, …)` loops `TryAdvance` until `Waiting`,
  `Completed` or a failure. `StepBudget` is null by default (`uint?`), meaning unlimited. Only the
  Budgeted profile sets it, and then it suspends the update instead of failing.
- **BatchedJobsSameFrame.** `ProductionBatchedGroupDriverV1` repeats rounds `while (open.Count > 0)`
  with no round cap.
- **The machine itself.** `NativeLifecycleMachineV1.SemanticSteps` (`ulong`) is only incremented;
  nothing compares it with a limit.
- **Repeater.** The repeater counts completed children up to `count` (`uint`). The only overflow
  guard in the loop path is the node activation generation (`uint.MaxValue`). With
  `count = 4 000 000 000` (below `uint.MaxValue`), the repeater finishes first, so it does not fire
  either.
- **Reference executor.** It has `ReferenceStepBudget` (Limited or Unlimited), but the production
  native path does not use it.

So a subtree that completes in the same update (for example, a parallel whose reader fails
instantly) under a repeater with a huge `count` runs up to `count` iterations inside one
`Update()` call. Nothing changed here; per the task, semantics stay untouched. The options for the
owner (a per-update step guard with a new diagnostic, or a compile-time warning for a repeater over
a subtree that cannot return Running) are listed in the J2 ADR, item 5.

## 7. Incidents

### Hung Modules Editor (resolved)

- **Cause.** The first version of `JobsPolicyEquivalenceTests.LoopTree` had a bare blackboard reader
  inside the repeated parallel. When `live-target = 0`, the parallel failed in the same step, and the
  repeater (`count` 4e9, `stopOnFailure` false) looped inside one update (section 6).
- **Effect.** The test run hung the Editor (PID 3400: not responding, about one core busy).
  - My attempt to stop it was blocked by the session's permission rules.
  - The process was ended outside this session, with the owner's consent.
- **Fix in the test.** The reader is wrapped in a `succeeder` next to a `wait`, so every iteration
  returns Running. The fixed test was run and passes (section 5).

### Editor restart side effects

- **Temp emptied.** The restart emptied `C:\UnityProjects\Modules\Temp\`. The first quick baseline
  and the profile raw data were lost (section 1).
- **Safe Mode prompt.** The restart showed the Safe Mode prompt. Its cause was a compile error in a
  temporary spike test (a missing `using AIBT.Runtime.Scheduling;`).
  - The owner pressed Ignore.
  - The spike was then fixed and moved out of the compiled tree (`Spikes~/P10-002-J2/`).
  - `Tools/Unity/Resolve-UnityDialog.ps1` could not handle this prompt. It finds no Editor window
    without a main window handle, and it has no Ignore action.
- **`Tools/Unity/Start-UnityEditor.ps1` defect (parent repository, not changed here).** Under Windows
  PowerShell 5.1 with `$ErrorActionPreference = 'Stop'`, any stderr line from `unity open` becomes a
  terminating `NativeCommandError`. The script then reports failure even though the Editor starts.

### Stalled benchmark Player (not a runtime defect)

- **Symptom.** `AibtPerf.exe` PID 3276 (the after Player) stayed alive from 17:52:25 without progress.
  - Its last output was `n-shared-loop16-jobs` N = 5000 run 1, an empty CSV created at 17:53:24.
  - It had one thread left, in Wait/UserRequest, and no CPU use.
  - The first interleaved run (`abba.ps1`) waited for every `AibtPerf` process to exit before it
    started. It therefore waited on this one and produced nothing.
- **Cause.** PID 3276 was not started by the interleaved run. It was a manual check that I ran
  through Git Bash as `timeout 60 powershell … Invoke-Perf.ps1 … -Baseline …\abba-test.json`. Evidence:
  - **Command line.** The process's command line matches that test: the after Player, the same
    `-perfOnly` list, and its output folder `il2cpp-20261115-175225`.
  - **No run of its own.** The interleaved run's first step (A1, the base Player) had not created
    any output.
  - **Timing.** The Player started at 17:52:25. Its last file write was at 17:53:24 and its main
    thread was gone by then: exactly 60 s, the `timeout` limit.
  - **Parent gone.** Its parent process (PID 71532) no longer existed.
  - **Termination left pending.** After `Stop-Process`, `HasExited` became true, but one thread
    object was still listed. The process had been terminated mid-frame from outside, and its
    teardown never completed.
  - **No crash records.** There was no crash folder, no Windows Application Error or WER report, and
    no System event in 17:45–18:10. The only kernel reports in the WER queue date from May to
    November 4.
- **Why it is not a P10-002 defect.** The same scenario passed both runs in the regular after run
  (`il2cpp-20261115-174251`). The new interleaved run (B1 and B2: 3 runs of every case in one
  process, after-Player) completed (section 3).
- **Guard.** The new interleaved script (`abba2.ps1`, copied to `perf-data\P10-002\logs\`) refuses to
  start while a live `AibtPerf` exists. It stops a Player that runs longer than 600 s, so a stall
  can no longer block the work silently.

## 8. J2 design

See `Documentation~/decisions/ADR-P10-002-agent-frame-jobs.md` (Proposed) and
`Spikes~/P10-002-J2/README.md`.

- **Proposal.** An opt-in policy `JobsAgentFrame`: one Burst job advances each agent through its
  whole frame, steps and native dispatch included, up to `Waiting` or `Completed`. The current
  approach is one job round-trip per step.
- **Spike.**
  - A multi-step `IJobFor` produces the same step kinds as one-step rounds.
  - On a 9-step tree it is about 100× cheaper than 9 rounds in the Editor (pre-P10-002 owner).
  - The catalog's `ExecuteImmediate` compiles as a Burst function pointer; it was not invoked.
- **Changes it needs (owner decisions):**
  - a new policy value;
  - generator output for a job-callable dispatch;
  - a rule for shared-scope blackboard visibility (refuse and fall back, or double-buffer);
  - a fallback for managed leaves;
  - a runaway step guard with a new diagnostic;
  - new failure text.
- **Estimate.** About 0.5–0.8 ms of main thread at N = 1000 against 3.7 ms for Immediate, for wait1
  and loop16-like trees.

## 9. Verification

| Check | Result |
|---|---|
| `Tools/Unity/Invoke-UnityRecompile.ps1` | compiled, no errors |
| `AIBT.Runtime.Tests` | 707/707 |
| `AIBT.Editor.Tests` | 635/636; the failure is the known host-only `McpToolDispatcherTests.ZeroCustomNodesReturnsExactlyThePhase1BuiltInCatalog` (sample nodes of the host project), the same as at `c54bbaf` |
| `AIBT.Integration.Tests` | 79/79 (77 existing + 2 new) |
| `AIBT.BehaviorCases.Tests` | 65/65 |
| `AIBT.NativeBurstDispatch.Tests` | 71/71 |
| `AIBT.NativeCommandsAndAsync.Tests` | 20/20 |
| `AIBT.NativeSharedBlackboard.Tests` | 36/36 |
| `AIBT.NativeSnapshot.Tests` | 7/7 |
| `AIBT.NativeTreeAgentBlackboard.Tests` | 33/33 |
| `AIBT.PlayMode.Tests` | 1/1 (run asynchronously: `unity command run_tests --mode playmode … --async_tests`, then `test_status`; `Invoke-UnityTests.ps1` cannot run PlayMode synchronously) |
| Console | only errors that tests log on purpose |
| `Tools~/Verification/Verify-Static.ps1` | passed (166 work items) |
| Public API vs `c54bbaf` (`Get-FullPublicApi.ps1`) | 6 assemblies, 549 types, 2521 members, sha256 `9551c0c8…` identical for both; 0 added, no removals |
| `ExpectedPublicAbiV2.txt`, package version, scheduler defaults | unchanged |

## Not done / not verified

- **No after-profile.** The frame profile (section 2) was measured only before the change, and its
  raw data was lost when Temp was emptied. A profile Player of the after state was not built.
- **No worker-thread time.** Worker time per round is not measured. All Player numbers are main
  thread only.
- **Function pointer not invoked.** The Burst function pointer to `ExecuteImmediate` was compiled
  but never invoked, in a job or in IL2CPP.
- **Async completions.** The equivalence test does not cover async completions, and it does not
  cover `PipelinedJobs`. The pipelined path's storage was left as it was.
- **Full harness not run.** Per the task, only the quick set ran, so N = 20 000 and the other
  scenarios are not re-measured.
- **Idle-agent skipping** is not implemented (section 4).
- **`n-io-move1-fact-effect-jobs` is still slow.** At N = 5000 it is still about 2.8× slower than
  Immediate. Its cost is in group dispatch (`ProductionDispatchResolverV1`), which J1 did not change.
- **Spike timings** are Editor timings, not Player timings.
- **PlayMode outside the script.** The PlayMode tests ran through the raw Unity CLI, not
  `Invoke-UnityTests.ps1`.
- **Parent repository untouched.** Nothing was changed or committed there, including the
  `Start-UnityEditor.ps1` defect (section 7).
