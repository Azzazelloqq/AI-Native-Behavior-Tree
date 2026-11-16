# P10-007 evidence — automatic selection of `JobsAgentFrame` from 64 consecutive eligible agents

Base: submodule `Assets/AIBT`, branch `feature/aibt-consumer-workflow`, `8e4e634` (P10-006). Card:
`Planning~/Tasks/P10/P10-007-agent-frame-auto-selection.md`. Owner decision (verbatim): "Автовыбор
с порога 64" — option (b) of `Planning~/Evidence/P10-005/README.md` section 10.2, in two steps.

Commits (local, not pushed):

- step 1 `ea7def6` — short `JobsAgentFrame` batches run in place;
- step 2 (the commit that adds this file) — automatic selection, opt-out, docs, measurement.

Raw data lives outside the repository: `C:\UnityProjects\_aibt_acceptance\perf-data\P10-007\`:

- `raw/il2cpp-*`: the four Player passes (`runs.jsonl` + one CSV per scenario, N and run);
- `logs/abba.log`, `logs/abba-{A1,B1,B2,A2}.out.txt`: runner log with the quiet-machine check and
  per-pass output; `logs/runplan.ps1`: the guarded runner (P10-005's, with a Player per pass);
- `logs/abba_p10007.py`: the script that computes the table in section 3 (`abba-table.md` is its output);
- `logs/build-il2cpp-*.log`: the two Player builds (the package path each one resolved is in the log);
- `tests-step2.txt`: the test-assembly results of step 2; `api/`: public API dumps.

## 1. What changed in default behaviour

**Step 1 (inside the `JobsAgentFrame` path, forced or automatic).** `ProductionTreeScheduler`
collects consecutive job-eligible members into a batch as before (P10-004); when the batch has
fewer than `AgentFrameMinimumBatchSize` members (default `DefaultAgentFrameMinimumBatchSize` = 64),
it now drives each member with `ProductionTreeHost.DriveOneUpdate` in due order, measured and
budget-accounted per member exactly like the `Immediate` path, and schedules no job. Entries keep
`SelectedPolicy = JobsAgentFrame` and set `SchedulerFrameEntry.AgentFrameRanInPlace = true`;
fallback entries are unchanged.

- Consequence for users who force `SchedulingProfile.JobsAgentFrame`: groups (or pieces between
  fallback members) of fewer than 64 agents now run at `Immediate` speed. That removes the
  +3…+17 µs overhead below the crossover and the 1.3–1.7× penalty of interleaved fallbacks
  (P10-005 sections 3 and 6), but it also removes the gain of trees with many reactive
  re-evaluations at small N (`shared-reactive16` at N = 10: 110 µs under the job vs 550 µs
  `Immediate` in P10-005). `AgentFrameMinimumBatchSize = 1` restores the previous forced behaviour.
- The zero-job-worker clause of the brief was **not** implemented (section 5).

**Step 2 (automatic selection).** In `ProductionTreeScheduler.Update`, a due entry whose profile
has no `ForcedPolicy` starts a search for the contiguous run of due entries that (a) are still
registered, (b) share its profile and catalog (by reference), and (c) report
`GetAgentFrameFallback() == None`. A run of at least `AgentFrameMinimumBatchSize` entries goes
through the same `DriveAgentFrameGroup` as the forced policy (one job per batch, same admission
code); a shorter run takes the unchanged direct `Immediate` path, and the search is not repeated
for its members in that frame.

- Selected entries: `SelectedPolicy = JobsAgentFrame`, `SelectionReason = AutomaticAgentFrameRun`
  (new value 8), `SelectionSource = DirectHostPath`, `AgentFrameFallback = None`.
- Not selected: exactly the previous entry (`Immediate` or `Budgeted`, reason `None`).
- Ineligible hosts (managed dispatch, catalog without agent-frame entry, `StepBudget`, …) end a run
  and keep their policy; no warning or error is ever logged by the automatic path. A host that
  becomes ineligible during the frame (e.g. a `StepBudget` set from a clock callback) is driven on
  the direct path with a direct entry.
- Only while the scheduler budget is `Unbounded` (the default); see section 5.
- `AutomaticAgentFrameSelection = false` turns the search off: the loop is the pre-P10-007 loop.
- Forced profiles (`Immediate`, `Budgeted`, `BatchedJobsSameFrame`, `PipelinedJobs`,
  `JobsAgentFrame`) never enter the search.
- `GeneratedTreeDispatchAdapterV2.GetAgentFrameFallback` now computes its binding/slot part once
  per adapter (both tables are fixed at construction); the catalog-executor check stays per call.

**Public API (additions only):** `ProductionTreeScheduler.DefaultAgentFrameMinimumBatchSize`
(const 64), `ProductionTreeScheduler.AgentFrameMinimumBatchSize` (get/set, ≥ 1, throws
`ArgumentOutOfRangeException` below 1), `ProductionTreeScheduler.AutomaticAgentFrameSelection`
(get/set, default true), `SchedulerFrameEntry.AgentFrameRanInPlace`,
`SchedulerSelectionReason.AutomaticAgentFrameRun = 8`. The internal `SchedulerFrameEntry`
constructor gained an optional parameter.

## 2. Tests

All in `Tests/Integration/NativeRuntime/JobsPolicyEquivalenceTests.cs` (assembly
`AIBT.Integration.Tests`, EditMode). "Equals Immediate" is the existing per-line comparison
(`AssertSame`) of every frame's trace records, root result, running state, run count, error,
delivered effects in order and, where noted, every byte of the Tree/Agent blackboard storage,
against a forced-`Immediate` run of the same population — the same observations whose per-frame
SHA-256 digests `AllPolicies_MatchTheOutcomeRecordedBeforeP10003_FrameByFrame` pins. 90 frames.

| Test | Covers |
|---|---|
| `JobsAgentFrame_BatchShorterThanTheMinimum_RunsInPlace_AndMatchesImmediate` | step 1: forced profile, 12 agents (3 trees), default minimum: 0 job batches, every entry `JobsAgentFrame` + `AgentFrameRanInPlace`, equals Immediate (with tree bytes) |
| `JobsAgentFrame_MinimumBatchBoundary(63, false)` / `(64, true)` | step 1 boundary: 63 → in place, 0 jobs; 64 → 90 jobs (one per frame), no entry in place; both equal Immediate |
| `JobsAgentFrame_FallbackMemberSplitsTheBatch_ShortPiecesRunInPlace(40, 40)` / `(64, 10)` | step 1 with a `StepBudget` member: 40 + 1 + 40 → both pieces in place, 0 jobs; 64 + 1 + 10 → first piece in the job, last 10 in place; the forced warning appears once; equal Immediate |
| `AutomaticSelection_IsObservablyIdenticalToImmediate_FrameByFrame` | step 2: 210 default-profile agents, 70 per tree (sequence with restarts, reactive effect emitter with aborts, looping parallel), grouped by tree: ≥ 90 job batches and > 6 300 automatic entries; equals Immediate (with tree bytes) |
| `AutomaticSelection_Boundary(63, false)` / `(64, true)` | 63 → every entry `Immediate`, reason `None`, 0 jobs; 64 → every entry `JobsAgentFrame`, reason `AutomaticAgentFrameRun`, not in place, 90 jobs; both equal Immediate |
| `AutomaticSelection_IneligibleMemberBreaksTheRun_Silently(40, 40)` / `(64, 10)` | 40 + 1 + 40 → nothing selected; 64 + 1 + 10 → only the first 64 (the budget member `Budgeted`, the last 10 `Immediate`); `LogAssert.NoUnexpectedReceived` (no warning); equal Immediate |
| `AutomaticSelection_CatalogWithoutAgentFrameEntry_StaysImmediate_WithoutWarnings` | 80 agents on a catalog whose executor hides the agent-frame entry (as before P10-004): no job, no automatic entry, no log; equals Immediate |
| `AutomaticSelection_DoesNotApplyUnderABoundedBudget_ShareAdmissionUnchanged` | fixed budget 1 s, profile with `MaximumBudgetShare = 1e-9`, 80 agents: no job; each frame exactly 1 host executed and 79 `DeferredProfileShare` entries (the per-host Immediate admission) |
| `AutomaticSelection_Off_RestoresThePreviousBehaviour` | `AutomaticAgentFrameSelection` is true by default; set false on the 210-agent population: 0 jobs, every entry `Immediate` / `None` / `DirectHostPath`; equals Immediate |
| `WarmedUpAutomaticJobsAgentFrameFrames_DoNotAllocateManagedMemory` | 70 default-profile reactive emitters, 200 warmed-up frames, every entry automatic: 0 GC allocations |

Changed existing tests (they cover the job driver itself, so they pin the old behaviour with
`AgentFrameMinimumBatchSize = 1`): the forced-`JobsAgentFrame` mode of the `Run` helper (used by
`JobsAgentFrame_IsObservablyIdenticalToImmediate_FrameByFrame`, the step-budget and old-catalog
fallback tests and `AllPolicies_MatchTheOutcomeRecordedBeforeP10003_FrameByFrame`),
`StepLimit_StopsATreeThatNeverYields_UnderImmediateBatchedJobsAndJobsAgentFrame`,
`WarmedUpJobsAgentFrameFrames_DoNotAllocateManagedMemory` and
`GeneratedDispatchLifecycleProofTests.SchedulerDestroyedInsideAnAgentFrameBatch_ReleasesTheAgentTableAndHosts`.
Without the pin most of them would pass through the in-place path and no longer test the job.
No expectation was changed.

## 3. Measurement

**Setup.** Bench project `C:\UnityProjects\_aibt_acceptance\P9Samples-6069` (the P10-005 copy,
Unity 6000.6.9f1) with the P10-007 harness (`PerfRunner.cs` from this commit, new set `p10007`).
Two IL2CPP non-development Players, stripping High, Burst on, built from the same project with the
AIBT dependency pointed at:

- **before** `C:\UnityProjects\_aibt_acceptance\aibt-8e4e634-6069` = `git archive 8e4e634`
  (Player `Build\P10007-before`, log `logs/build-il2cpp-20261116-231106.log`);
- **after** `C:\UnityProjects\_aibt_acceptance\aibt-p10007-6069` = the tracked files of this
  working tree at step 2 (Player `Build\P10007-after`, log `logs/build-il2cpp-20261116-231243.log`).

Each build log shows the package path it resolved. The bench manifest was restored to
`aibt-659a8b1-6069` afterwards, as P10-005 left it.

Set `p10007`: `shared-loop16` and `shared-reactive16`, default profile (no `ForcedPolicy`: before =
`Immediate`, after = automatic selection), N = 16, 63, 64, 256, 2 000; 200 warm-up + 1 000 measured
frames (N = 2 000: 120 + 250, measurement capped at 20 s, so `shared-reactive16` before has ≈190
measured frames). ABBA order: A1 before, B1 after, B2 after, A2 before (`raw/il2cpp-20261116-231429`,
`-231802`, `-231904`, `-232006`). 3 runs per pass, scenarios interleaved inside each run, 23 job
workers, Intel Core Ultra 9 275HX. Each pass started on a quiet machine (total CPU 3.8–6.3% over
5 s, no other Unity process using CPU; `logs/abba.log`). The Modules Editor was open and idle. Every
pass had 0 faults.

**Statistic.** Per run: arithmetic **mean** of `bracket_us` (main-thread µs per frame, first script
`Update` to last `LateUpdate`) over the measured frames. Per side: **median** of the 6 run means
(3 per pass × 2 passes), min–max of the run means in brackets. `after/before` = median after ÷
median before. `B1/A1` and `B2/A2` = the same ratio inside each half of the ABBA order (median of
each pass). Last column: `agentFrameStats` of the after Player divided by measured frames (the
counter window also covers the last warm-up frames, so 1.06 and 1.24 mean one job per frame).
Computed by `logs/abba_p10007.py`; output `abba-table.md`.

| Tree | N | before µs | after µs | after/before | B1/A1 | B2/A2 | after: job batches/frame, agents in jobs/frame |
|---|---:|---:|---:|---:|---:|---:|---|
| `shared-loop16` | 16 | 73.2 (70.4–76.6) | 74.0 (72.5–82.1) | 1.01 | 0.99 | 1.02 | 0.00, 0 |
| `shared-loop16` | 63 | 241.6 (240.3–243.3) | 246.1 (242.1–250.2) | 1.02 | 1.02 | 1.02 | 0.00, 0 |
| `shared-loop16` | 64 | 244.8 (243.0–246.6) | 100.3 (95.8–101.6) | 0.41 | 0.41 | 0.41 | 1.06, 68 |
| `shared-loop16` | 256 | 939.6 (925.5–954.9) | 259.7 (252.7–265.7) | 0.28 | 0.28 | 0.27 | 1.06, 271 |
| `shared-loop16` | 2000 | 8341.4 (8149.7–8475.6) | 2012.5 (1913.4–2379.9) | 0.24 | 0.26 | 0.24 | 1.24, 2480 |
| `shared-reactive16` | 16 | 861.6 (836.5–886.7) | 860.7 (841.7–877.2) | 1.00 | 1.01 | 0.99 | 0.00, 0 |
| `shared-reactive16` | 63 | 3208.2 (3172.4–3230.3) | 3257.2 (3210.2–3323.9) | 1.02 | 1.03 | 1.01 | 0.00, 0 |
| `shared-reactive16` | 64 | 3265.1 (3211.4–3320.8) | 326.1 (322.2–340.8) | 0.10 | 0.10 | 0.10 | 1.06, 68 |
| `shared-reactive16` | 256 | 13161.5 (13066.1–13289.8) | 1132.7 (1120.9–1183.4) | 0.09 | 0.09 | 0.09 | 1.06, 271 |
| `shared-reactive16` | 2000 | 105888.6 (105500.9–106323.5) | 9238.4 (9110.7–10167.0) | 0.09 | 0.09 | 0.09 | 1.24, 2480 |

- **Below 64 (N = 16, 63): no regression beyond noise.** After/before 1.00–1.02; both halves of
  the ABBA within 0.99–1.03. One Player differs from itself by up to 7% between runs (P10-005).
  N = 63 is +2% in both halves for both trees (+4.5 µs and +49 µs): inside the noise band, but in a
  consistent direction. A possible cause is the per-frame run search (one eligibility check per
  agent); not investigated further. No job ran (0 batches).
- **From 64 up: gains as in P10-005.** `shared-loop16` 0.41× at 64, 0.28× at 256, 0.24× at 2 000
  (P10-005 forced JobsAgentFrame: 0.45× at 50, 0.37× at 100, 0.27× at 500, 0.28× at 1 000);
  `shared-reactive16` 0.09–0.10× (P10-005: 0.09–0.11× at 50–1 000). One job per frame.
- The threshold is a sharp step: `shared-loop16` 246 µs at 63 vs 100 µs at 64, `shared-reactive16`
  3 257 µs vs 326 µs.

No number contradicts the expectation; the threshold was not tuned.

## 4. Verification

| Check | Result |
|---|---|
| `Tools/Unity/Invoke-UnityRecompile.ps1` (Modules, 6000.6.9f1) | compiled, no errors |
| `AIBT.Runtime.Tests` | 710/710 (baseline 710/710) |
| `AIBT.Editor.Tests` | 635/636: the known host-only `McpToolDispatcherTests.ZeroCustomNodesReturnsExactlyThePhase1BuiltInCatalog` (baseline 635/636). Includes the ABI v2 contract test (`ExpectedPublicAbiV2.txt` unchanged) and `McpDocumentationGeneratorsTests` against the regenerated `Documentation~/generated/api-reference-runtime.md` |
| `AIBT.Integration.Tests` | 102/102 (baseline 88 + 5 step-1 + 9 step-2 cases) |
| `AIBT.BehaviorCases.Tests` | 65/65 |
| `AIBT.NativeBurstDispatch.Tests` | 79/79 |
| `AIBT.NativeTreeAgentBlackboard.Tests` | 33/33 |
| `AIBT.NativeSharedBlackboard.Tests` | 36/36 |
| `AIBT.PlayMode.Tests` | 1/1 (`run_tests --mode playmode --async_tests` + `test_status`) |
| Step 1 alone (before its commit) | Runtime 710/710, Editor 635/636 (same failure), Integration 93/93 |
| Public API (`Get-FullPublicApi.ps1 -BaselinePath perf-data\P10-004\api\public-api-p10004.txt`, 6000.6.9f1) | 554 types / 2 542 members; 5 additions, no removals or renames (`api/added.txt`). P10-005 and P10-006 changed no public API, so the P10-004 dump is the baseline of `8e4e634` |
| `Tools~/Verification/Verify-Static.ps1` | passed (schemas 7, work items 171) |
| Package version | unchanged |

Raw test output: `perf-data\P10-007\tests-step2.txt` (the Integration run there was repeated after
the allocation test was added: 101/101 → 102/102).

## 5. Where reality differed from the brief

1. **Zero job workers.** The brief said JobsAgentFrame "with zero job workers is slower than
   Immediate" and asked to run batches in place when there are zero workers. P10-005 section 5 and
   P10-004 section 5 show the opposite: with `JobWorkerCount = 0` JobsAgentFrame takes 0.45–0.88×
   of Immediate's time at N = 1 000–20 000 (Burst-compiled bodies). Reported to the orchestrator,
   who confirmed that the brief misstated P10-005. The zero-worker rule was **not** implemented;
   the 64 threshold applies regardless of the worker count. Zero workers below N = 1 000 were never
   measured.
2. **"The selector" for default profiles.** Default-profile hosts never reached the native
   selector (`NativeAutoSelectionV1`); they took the scheduler's direct `Immediate` path. The rule
   lives in `ProductionTreeScheduler.Update`, before the direct path. `NativeAutoSelectionV1` is
   unchanged and entries keep `SelectionSource = DirectHostPath`.
3. **Bounded budgets.** The brief required budget admission with `MaximumBudgetShare` to stay
   unchanged. The agent-frame group admits a batch before it runs (by last measured or
   batch-average costs, and it admits unmeasured members on cold start). The `Immediate` path
   admits each host after the previous one ran. The two differ: under a tiny share the group
   admits the whole batch on the first frame. To keep admission exactly unchanged, automatic
   selection applies only with the default `Unbounded` budget. Forced JobsAgentFrame keeps its
   P10-004 admission. Giving default-profile users who set a budget the job too needs an owner
   decision on the admission rule.
4. **"Same catalog" means "same tree" for `NativeTreeAgent`.** `NativeTreeProgramCache` creates one
   native catalog per (tree asset, catalog factory). A run is therefore a run of consecutive agents
   of one tree in due order (≈ enable order, by host instance id). Agents of several trees enabled
   interleaved are never selected. The tests group agents by tree for this reason. Documented in
   `performance.md`; not changed (a job runs one catalog's executor).
5. **Reactive trees below 64.** P10-005 shows `shared-reactive16` 5× faster under the job already
   at 10 agents. With the 64 threshold, small groups of such trees stay `Immediate` under the
   default profile (no regression against before). Step 1 also makes a *forced* JobsAgentFrame
   group below 64 run in place, which is a slowdown in that case against `8e4e634` (≈110 → ≈550 µs
   at N = 10 by P10-005 numbers; not re-measured here). `AgentFrameMinimumBatchSize = 1` restores
   the old behaviour; documented.
6. **Frame entries of in-place batches** keep `SelectedPolicy = JobsAgentFrame` (the policy the
   group ran under) and add `AgentFrameRanInPlace`, instead of reporting `Immediate`, so no existing
   field changes meaning. The 63/64 boundary of the automatic rule shows as `Immediate`/`None`
   against `JobsAgentFrame`/`AutomaticAgentFrameRun`.
7. **Existing tests pinned to minimum 1** (section 2): with the new default they would exercise
   the in-place path instead of the job they are about.

## Not done / not verified

- No mobile or few-core device, no Web (single-thread) run; zero job workers not measured here.
- The interleaved-fallback population (P10-005 section 6) and forced JobsAgentFrame below 64 were
  not re-measured after step 1; their expected effect comes from P10-005's Immediate numbers.
- Mixed-tree populations under automatic selection were not measured (section 5, item 4).
- Only `shared-loop16` and `shared-reactive16` were measured, as the brief asked. `io-wait1` and
  `io-move1-fact-effect` gain less in P10-005 (0.55–0.70× at 50–100).
- The +2% at N = 63 was not profiled.
