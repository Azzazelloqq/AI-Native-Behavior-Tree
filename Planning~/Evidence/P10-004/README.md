# P10-004 evidence — `JobsAgentFrame`: one Burst job work item runs one agent's whole update (stage J2)

Base commit: `93e7522` (submodule `Assets/AIBT`, branch `feature/aibt-consumer-workflow`).
Design: `Documentation~/decisions/ADR-P10-002-agent-frame-jobs.md` (owner decisions 1–4 approved
for this card; option 3(a) for shared writes). Card: `Planning~/Tasks/P10/P10-004-agent-frame-jobs.md`.

Every Player number below comes from the IL2CPP non-development Windows Player of the P9Samples
harness (`Benchmarks~/Phase10/PlayerPerf`), stripping High, Burst on, quick set
(`Invoke-Perf.ps1 -Quick`), on an Intel Core i9 275HX (24 logical CPUs, 23 job workers). Raw data
lives outside the repository: `C:\UnityProjects\_aibt_acceptance\perf-data\P10-004\`:

- `gate/il2cpp-*`: Part A gate runs (`-agentframe` variants, normal workers and `-perfJobWorkers 0`).
- `fp-gate/`: emitted job vs function pointer (`summary.md`, raw runs).
- `raw/il2cpp-*`, `logs/abba-*.out.txt`, `abba-table.md`: the interleaved before/after passes.
- `jw0/`: the `-perfJobWorkers 0` pass used for total CPU time.
- `api/`: public API dumps of `93e7522` and of this change.
- `logs/runplan.ps1`: the guarded runner (time limit per pass, stops only Players it started).

The baseline Player is built from a `git archive` copy of `93e7522`
(`C:\UnityProjects\_aibt_acceptance\aibt-93e7522`) through a copy of the harness project whose
manifest points at it (`P9Samples-93e7522`, Player `Build\P10004-before`). The after Player is
`P9Samples\Build\P10004-after` on the working tree of this change.

## 1. What was built

- `SchedulingPolicy.JobsAgentFrame` (additive) and the built-in profile
  `SchedulingProfile.JobsAgentFrame`. Opt-in is one assignment:
  `nativeTreeAgent.Profile = SchedulingProfile.JobsAgentFrame;` before the agent is enabled. Defaults
  are unchanged and the automatic selector never picks the policy. It needs no
  `SchedulerJobsCapabilities`.
- Generator (`CodeGen~/AIBT.CodeGen/BurstNodeGenerator.cs`, rebuilt
  `Analyzers/AIBT.CodeGen.dll`): every catalog's `GeneratedExecutor` also implements
  `AIBT.AgentFrame.IGeneratedAgentFrameExecutorV1`, and the catalog emits a private
  `[BurstCompile] AibtAgentFrameJob : IJobFor` plus a private body struct that calls the catalog's
  own `ExecuteImmediate`. `ExpectedPublicAbiV2.txt` is byte-identical; the new public types live in
  `AIBT.AgentFrame` and are marked `[EditorBrowsable(Never)]` as generated-code infrastructure.
- Runtime: `ProductionAgentFrameDriverV1` (one `ScheduleParallel` + `Complete` per batch),
  `AgentFrameCoreV1.RunAgent` (the native framing, below), host begin/finish/abandon in
  `ProductionTreeHost`, scheduler grouping in `ProductionTreeScheduler.DriveAgentFrameGroup`.
- Fallback diagnostic: `SchedulerFrameEntry.AgentFrameFallback` (`AgentFrameFallbackReason`) plus a
  one-time warning per host. A fallback member runs `DriveOneUpdate` (Immediate, or Budgeted with a
  step budget) in its own due position; job-eligible members around it run in job batches.
- Step limit: `ProductionTreeHost.MaximumStepsPerUpdate` (default
  `DefaultMaximumStepsPerUpdate = 1 000 000`), enforced under Immediate, BatchedJobsSameFrame and
  JobsAgentFrame. Past the limit the host fails with the new diagnostic
  `NativeRuntimeDiagnosticCodeV1.NativeUpdateStepLimitExceeded` (4313).

### 1.1 Main thread and job, per frame

Main thread, in agent order: due collection and budget admission (as Immediate), then for each
member `BeginAgentFrame` (clock read, update id, machine and slot handles copied into the agent
table, tree values and effect capacity), one `ScheduleParallel` over the batch and `Complete`,
then for each member `FinishAgentFrame` (replays the trace log into `NativeTraceRecorderV1`,
enqueues the effect log into the host's `ProductionTreeEffectQueue`, terminal / failure / step-limit
bookkeeping, releases the drive lock). Effects are then delivered by the existing world path.

Job, one work item per agent (`AgentFrameCoreV1.RunAgent`): the loop of
`ProductionTreeHost.RunSegment` without a budget, with generated dispatch inlined.

### 1.2 Framing steps inside the job and their blittable path

Each step is the managed step of `GeneratedTreeDispatchAdapterV2.DispatchSlot.Execute` /
`NativeBurstDispatchWorkspaceOwnerV2`, re-expressed over native handles the slot already owns:

| Step (managed original) | In the job |
|---|---|
| Step limit (`TryCountStep`) | `StepsThisUpdate` / `MaximumSteps` in the agent record |
| `Machine.TryAdvance` | the same `NativeLifecycleMachineV1` struct, copied into the agent record |
| Trace `RecordStep` / `RecordDispatchCompletion` | `UnsafeList<AgentFrameTraceEventV1>` per agent, replayed after `Complete` |
| Effect capacity reservation (`MaximumEffectCommandCount` vs queue free) | `EffectFree` captured at begin |
| Activation generation, activation memory clear | `NativeArray<AgentFrameSlotStateV1>` (state now shared with the main-thread slot) |
| `SnapshotTreeValues` | precomputed `AgentFrameResolutionV1` table, tree-value `NativeArray` |
| Request build, `TryBeginRequest`, `ValidateRequestViews` | `NativeBurstDispatchWorkspaceArraysV1` (owner's arrays) |
| Acquire batch, `ExecuteImmediate` (node body) | `IAgentFrameBodyV1` → catalog `ExecuteImmediate`, Burst-compiled in the job |
| `TryConsumeResult`, `ValidateLedger` | same checks; ledger floors across requests are not tracked (see 6) |
| `CanCommitTreeValues` / `CommitTreeValues` (with versions) | tree values / versions arrays |
| `PublishCommands`, `TryAcknowledgePublishedCommands` | slot command / payload arrays, transaction ledger |
| `TryConsumePublishedEffects` | `AgentFrameEffectDescriptorV1` table → `UnsafeList<ProductionTreeEffect>` per agent |
| `TryReset` | control / claim / staging arrays |
| `TryCompleteDispatch` | the machine, as before |

Every step has a blittable path; none needed a semantic change.

## 2. Part A — gate

### 2.1 A generated node body runs inside a Burst job for a whole agent update

Player run `gate/il2cpp-20261116-105309` (23 workers) and `gate/il2cpp-20261116-105404`
(`-perfJobWorkers 0`, full quick set). The harness reads `AgentFrameDiagnosticsV1` (process-wide
counters; `[BurstDiscard]` marks an agent whose job did not run Burst-compiled):

- `managedAgents = 0` in every scenario: every agent's job ran Burst-compiled.
- With 23 workers, 94–96% of agents ran on worker threads (`agentsOnWorkers / agents`).
- Every dispatch of every measured agent ran inside the job (`dispatches` equals the scenario's
  dispatches per frame × frames).

### 2.2 Cost per dispatch inside the job

`jobMicroseconds / dispatches` (schedule + complete wall time on the main thread, including tree
stepping and framing), against ≈1.6–1.7 µs per main-thread dispatch after P10-003:

| Tree | single thread (`-perfJobWorkers 0`) µs | 23 workers, wall µs |
|---|---:|---:|
| `shared-reactive16` | 0.48 | 0.034–0.039 |
| `io-wait1` | 0.53–0.60 | 0.10–0.11 |
| `shared-loop16` | 0.61–0.74 | 0.07–0.09 |
| `io-move1-fact-effect` (fact + effect) | 0.98–1.17 | 0.14–0.16 |

### 2.3 Emitted job or function pointer

A harness-only variant (`-agentframe-fp`, `Assets/AibtPerf/Runtime/PerfAgentFramePointer.cs`)
swaps a catalog's executor for one whose job calls the body through
`BurstCompiler.CompileFunctionPointer` (a `ref` result parameter, since a function pointer cannot
return the struct by value). It compiled and ran in the IL2CPP Player. Per dispatch
(`fp-gate/summary.md`), the function pointer is within −4%…+3% of the emitted job in all 16 cases
(single thread and 23 workers): no measurable difference.

**Decision: emitted job.** Same speed, no runtime `CompileFunctionPointer` call, no
`MonoPInvokeCallback` / reflection, and one generic job type per catalog that Burst compiles AOT.

## 3. Part B — tests

| Test | Covers |
|---|---|
| `JobsPolicyEquivalenceTests.JobsAgentFrame_IsObservablyIdenticalToImmediate_FrameByFrame` | 12 agents, 3 trees (facts, effects, reactive aborts, parallel, loop, restarts, failures), 90 frames: trace records, root result, running state, run count, error, effects in order, every byte of tree values; every entry ran in the job |
| `JobsPolicyEquivalenceTests.AllPolicies_MatchTheOutcomeRecordedBeforeP10003_FrameByFrame` | `JobsAgentFrame` added: its frame digests equal those recorded on the runtime at `4dee8ec` |
| `JobsPolicyEquivalenceTests.JobsAgentFrame_StepBudgetHosts_FallBackInTheirDuePosition_AndMatchImmediate` | `StepBudgetConfigured`: half the agents fall back between job batches; the frames equal a forced Immediate run |
| `JobsPolicyEquivalenceTests.JobsAgentFrame_CatalogWithoutAgentFrameEntry_FallsBackForEveryAgent_AndMatchesImmediate` | `CatalogWithoutAgentFrameEntry` (executor wrapped to hide the entry, as a pre-P10-004 catalog); equals Immediate |
| `ProductionTreeSchedulerTests.JobsAgentFrame_ManagedDispatchHost_RunsAsImmediate_AndReportsTheReasonOnce` | `ManagedDispatch`; one warning for two frames |
| `ProductionTreeSchedulerTests.ImmediateProfile_EntriesReportNoAgentFrameFallback` | `None` on other policies |
| `JobsPolicyEquivalenceTests.WarmedUpJobsAgentFrameFrames_DoNotAllocateManagedMemory` | 32 agents, 200 warmed-up frames, GC.Alloc recorder: 0 |
| `GeneratedDispatchLifecycleProofTests.SchedulerDestroyedInsideAnAgentFrameBatch_ReleasesTheAgentTableAndHosts` | scheduler destroyed from host A's clock during begin: no exception, host B not begun, host A finished, no drive lock held, driver storage released |
| `JobsPolicyEquivalenceTests.StepLimit_StopsATreeThatNeverYields_UnderImmediateBatchedJobsAndJobsAgentFrame` | limit 1000: a non-yielding repeater fails with `NativeUpdateStepLimitExceeded` after exactly 1000 steps under all three policies (inside the job for J2); a bounded repeater (50 iterations, more than 50 and fewer than 1000 steps) succeeds |
| `ProductionTreeSchedulerTests.MaximumStepsPerUpdate_DefaultsToOneMillion_AndRejectsZero` | default and setter |

The fallback reasons `SharedOrWorldScopeWrite` and `AsyncOrManagedLeaf` are checked by
`GeneratedTreeDispatchAdapterV2.GetAgentFrameFallback` but cannot be reached by a generated host
today: the adapter already refuses Shared-scope bindings and async/completion bindings when it is
created, so no test can produce them. They are kept as defensive checks.

## 4. Part C — interleaved before/after

Four passes of the quick set, 2 runs each, in the order B1 (`93e7522`), A1, A2 (this change),
B2, with the guarded runner `logs/runplan.ps1`; no faults, every Player exited normally. Full table
with every case and both comparisons: `perf-data\P10-004\abba-table.md`.

**Existing cases.** No single-threaded or Jobs case is more than 5% slower in both comparisons
(Δ1 = A1 vs B1, Δ2 = A2 vs B2). The largest positive deltas are `n-shared-reactive16-jobs`
N = 5000 (−2.9% / +4.8%) and `n-io-wait1` N = 5000 (−8.4% / +3.6%). The after passes are slightly
faster than the before passes on average, including the managed controls (`m-unity15-both`
N = 5000 −13% in both), which this change does not touch: run-to-run drift of the machine, not an
effect of the change. The step limit adds one compare per lifecycle step to Immediate and
BatchedJobsSameFrame; it is not visible in these numbers.

**Main-thread µs per frame by policy** (mean of A1 and A2; frame wall time in brackets):

| Tree | N | Immediate (default) | BatchedJobsSameFrame | JobsAgentFrame | Immediate / JobsAgentFrame |
|---|---:|---:|---:|---:|---:|
| `io-wait1` | 1000 | 1 939 (2 136) | 1 998 (2 149) | 922 (1 070) | 2.1× |
| | 5000 | 12 761 (13 038) | 12 793 (13 127) | 5 820 (6 137) | 2.2× |
| `shared-loop16` | 1000 | 3 638 (3 788) | 3 814 (4 021) | 1 050 (1 175) | 3.5× |
| | 5000 | 22 715 (22 960) | 25 279 (25 591) | 7 780 (8 120) | 2.9× |
| `shared-reactive16` | 1000 | 53 192 (53 451) | 62 232 (62 521) | 4 956 (5 221) | 10.7× |
| | 5000 | 264 714 (265 015) | 381 465 (381 555) | 24 729 (25 226) | 10.7× |
| `io-move1-fact-effect` | 1000 | 2 636 (2 811) | 2 748 (2 904) | 1 135 (1 304) | 2.3× |
| | 5000 | 16 992 (17 286) | 17 549 (17 900) | 7 566 (7 883) | 2.2× |

Per agent and frame that is 0.9–1.2 µs (`io-wait1`), 1.0–1.6 µs (`shared-loop16`), 5 µs
(`shared-reactive16`) and 1.1–1.5 µs (`io-move1-fact-effect`) under JobsAgentFrame. The
`-agentframe-fp` cases are within ±8% of `-agentframe` in the same passes (mostly within ±3%).

**What remains on the main thread.** The job's own schedule + complete wall time
(`agentFrameStats.jobMicroseconds`) is a small part of the frame: for example `io-wait1` N = 1000
≈110 µs of ≈920 µs, `shared-reactive16` N = 1000 ≈1.8 ms of ≈5.0 ms. The rest is the per-agent
managed begin/finish (clock, copying the machine and slot handles in and out, trace replay, effect
enqueue), the world bridge, due collection and frame entries, which are linear in N and not
parallel. The gain is therefore largest for trees with many dispatches per update and smallest for
one-dispatch trees.

## 5. Total CPU and frame wall time

Method: the after Player with `-perfJobWorkers 0` (`JobsUtility.JobWorkerCount = 0`), so every
job runs on the main thread inside the measured bracket; the bracket is then the total CPU time of
the frame's agent work on one thread. Pass `jw0/il2cpp-20261116-115201`, quick set, 2 runs, no
faults. µs per frame (frame wall time in brackets):

| Tree | N | Immediate | BatchedJobsSameFrame | JobsAgentFrame | JobsAgentFrame / Immediate |
|---|---:|---:|---:|---:|---:|
| `io-wait1` | 1000 | 2 013 (2 112) | 1 753 (1 965) | 1 291 (1 394) | 0.64 |
| | 5000 | 13 097 (13 314) | 15 504 (15 801) | 9 360 (9 617) | 0.71 |
| `shared-loop16` | 1000 | 3 776 (3 859) | 3 464 (3 729) | 2 366 (2 570) | 0.63 |
| | 5000 | 23 574 (23 796) | 26 669 (26 904) | 16 091 (16 317) | 0.68 |
| `shared-reactive16` | 1000 | 54 599 (54 864) | 54 219 (54 488) | 24 940 (25 152) | 0.46 |
| | 5000 | 272 615 (272 914) | 395 704 (396 136) | 124 368 (124 622) | 0.46 |
| `io-move1-fact-effect` | 1000 | 2 925 (3 139) | 2 491 (2 632) | 2 087 (2 200) | 0.71 |
| | 5000 | 18 212 (18 478) | 19 386 (19 680) | 13 447 (13 717) | 0.74 |

JobsAgentFrame does not only move work off the main thread: on one thread it needs 26–54% less
total CPU than Immediate, because the node bodies and the framing run Burst-compiled. With 23
workers the worker share is roughly the difference between this table and the main-thread table
in section 4 (for example `io-wait1` N = 1000: ≈1.29 ms total, ≈0.92 ms of it on the main
thread). This is an estimate: the single-thread run has no worker scheduling or cache effects, and
per-thread worker time was not measured directly.

Frame wall time with 23 workers is in brackets in the section 4 table: for JobsAgentFrame it is
0.13–0.5 ms above the main-thread bracket, as for the other policies (the bracket excludes the
engine's own frame work).

## 6. Known differences and limits

- **Clock reads.** For a job batch, every member's clock callback runs (in agent order) before the
  job; under Immediate each member's clock runs just before its own update. A custom clock that
  observes another agent's update in the same frame would see a difference. Unity's default clock
  is per frame, so the default is unaffected.
- **Corruption paths.** The job checks the workspace ledger per request but does not track the
  owner's cross-request ledger floors, and after a consume/ledger failure the owner's state
  machine is not reproduced. These paths only trigger on corrupted native state; the agent fails in
  both policies, but the failure code may differ.
- **Failure text.** Failures inside the job are codes; the host turns them into the same messages
  as the Immediate path ("Cannot advance execution.", "Callback rejected: …", the step-limit
  message).
- **Batched entries.** A `BatchedJobsSameFrame` frame entry carries the group's disposition, so a
  member that fails on the step limit still shows `Executed` there (unchanged by this card; its
  host's `LastFailure` is set).
- **Fallback scope.** Fallback is per host: `ManagedDispatch` (no generated dispatch),
  `StepBudgetConfigured` (`ProductionTreeHost.StepBudget` set), `CatalogWithoutAgentFrameEntry`
  (catalog generated before this version). `SharedOrWorldScopeWrite` and `AsyncOrManagedLeaf` are
  defensive (section 3).

## 7. Verification

| Check | Result |
|---|---|
| `Tools/Unity/Invoke-UnityRecompile.ps1` | compiled, `compilationFailed=False` |
| `AIBT.Runtime.Tests` | 710/710 (707 before + 3 new) |
| `AIBT.Editor.Tests` | 635/636; the failure is the known host-only `McpToolDispatcherTests.ZeroCustomNodesReturnsExactlyThePhase1BuiltInCatalog`. `Documentation~/generated/api-reference-runtime.md` was regenerated (the new public members; additions only) with the generator `McpDocumentationGeneratorsTests` compares against |
| `AIBT.Integration.Tests` | 88/88 (82 before + 6 new) |
| `AIBT.BehaviorCases.Tests` | 65/65 |
| `AIBT.NativeBurstDispatch.Tests` | 79/79 |
| `AIBT.NativeTreeAgentBlackboard.Tests` | 33/33 |
| `AIBT.NativeSharedBlackboard.Tests` | 36/36 |
| `AIBT.PlayMode.Tests` | 1/1 (`run_tests --mode playmode --async_tests`) |
| Console | new errors are the ones tests log on purpose (clock validation, unsupported forced policy, throwing managed node); compile clean |
| `Tools~/Verification/Verify-Static.ps1` | passed (168 work items) |
| Public API vs `93e7522` (`Get-FullPublicApi.ps1 -BaselinePath`, Unity 6000.7.0b1) | baseline 549 types / 2521 members (sha256 `9551c0c8…`, same as P10-003), now 554 / 2537; 21 additions, no removals or renames (`perf-data\P10-004\api\added.txt`) |
| `ExpectedPublicAbiV2.txt`, package version, scheduler defaults | unchanged |
| Analyzer | `Analyzers/AIBT.CodeGen.dll` rebuilt twice byte-identically (SHA-256 `24a64461…`), `.sha256` updated |
| Benchmark Players | every `AibtPerf.exe` started here exited; PID 3276 is an older exited process from an earlier stage, untouched |

## Not done / not verified

- **Async completions and managed bridges.** Not covered by the job (they fall back, or are refused
  earlier by the adapter). `SharedOrWorldScopeWrite` and `AsyncOrManagedLeaf` cannot be produced
  by a generated host, so they have no test.
- **Worker time per thread.** Total CPU is measured with zero workers (section 5); worker time with
  23 workers is inferred, not measured.
- **Other targets.** Measured on one 24-thread desktop CPU, Windows IL2CPP. Mono, Android, Web and
  CPUs with few cores were not measured; N = 20 000 and the non-quick harness scenarios were not run.
- **Corruption paths.** Cross-request ledger floors are not checked in the job, and after a
  consume/ledger failure the failure code may differ from Immediate (section 6). No test corrupts
  native state under the new policy.
- **Clock order.** Clock callbacks of a job batch run before the job (section 6); not tested with a
  clock that depends on other agents.
- **Step-limit default.** 1 000 000 is a judgement: far above any measured tree (a few to a few hundred
  steps per update) and still bounded. How long a runaway update takes to reach it was not measured,
  and it is not tuned per platform.
- **Console.** Checked through `Get-UnityConsole.ps1 -New`, a 30-line sample after the full test
  runs; not every line of the buffer was read.
