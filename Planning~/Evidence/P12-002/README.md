# P12-002 evidence — Indexed input channel: group or squad data read by every member

Branch `feature/aibt-consumer-workflow`, base `c3e4bab`. Unity 6000.6.9f1, the Modules Editor (Pipeline port
7810). Brief: `C:\UnityProjects\_aibt_acceptance\SweetKingdom\brief-P12-002.md`. Specification: the nine
"Semantics" items of `decision.md` in this folder (design read with file:line: `design-read.md`). Raw files:
`C:\UnityProjects\_aibt_acceptance\P12-002\`. Only the touched test classes were run, not the ten assemblies.
Status: `Review` (acceptance is the orchestrator's).

No protected file changed: the ABI contract file, the compiled program format, `CodeGen~`, `package.json`,
`Tests/Fixtures`, `Documentation~/specifications` and execution semantics are untouched. Public API: two
additions, no existing signature changed: `TreeGroup.TryBindInput<T>(InputChannel<T>, NativeArray<T>,
NativeArray<int>)` and `AgentTraceRecord.InputRow`.

## What changed

| Path | What |
| --- | --- |
| `Runtime/Simulation/SimulationNativeV1.cs` | `SimulationInputChannelV1` gains `Indexed` and `RowOf` (internal); new `SimulationAgentRoutinesV1.SelectRow`, the one routine that picks the row; `TryStage` uses it |
| `Runtime/Simulation/TreeGroup.cs` | the overload; one private `BindRows` for both overloads; indexed staging (rows of any count, one index per slot); validation of the indices; the step-entry copy; doc of `TryAdd` and `TrySetWakeOnChange` |
| `Runtime/Simulation/TreeGroup.Debug.cs`, `SimulationDebugTypes.cs` | the trace's `Input` record carries the row (`InputRow`, -1 when zeros were read) |
| `MCP/McpSimulationTools.cs` | `read_agent_trace` adds `"row"` to input events |
| `Tests/Integration/Simulation/TreeSimulationIndexedInputTests.cs` | 18 tests, 30 cases (new) |
| `Tests/Integration/Simulation/TreeSimulationIndexedInputBenchmark.cs` | the measurement (opt-in, reported as ignored in a normal run; new) |
| `Tests/Integration/AIBT.Integration.Tests.asmdef` | one added reference, `AIBT.Mcp` (for the MCP row test) |
| `Documentation~/simulation-container.md` | new section "Group data: an indexed input channel"; two cross-references |
| `Documentation~/decisions/ADR-P10-008-simulation-container.md` | amendment 2026-11-25 (two routes for group data); corrected header status and `InputMissing` line |
| `Documentation~/generated/api-reference-runtime.md` | regenerated: the two new members, nothing else moved |
| `CHANGELOG.md` | one entry |
| `Planning~/` | card, work item, master plan line, this file |

## Semantics → code → tests

Every item of `decision.md` "Semantics" is named (`S1`…`S8`) in the summary of at least one test; every test
that can run both ways runs in place and in the agent-frame job (`MinimumJobBatch = 1`, job batches asserted).

| Item | Code | Tests (`TreeSimulationIndexedInputTests`) |
| --- | --- | --- |
| S1 rows any length, copied whole at step start, cost follows the array | `EnsureStaging` (rows × stride for an indexed channel), `BeginStep` | `MembersReadTheirRow_NotTheirSlotRow` (3 rows, capacity 16), `Bind_RefusalsAndAnEmptyRowsArray` (length 0 accepted), `TryReserve_NeedsTheIndicesRebound_NotTheRows`, `Step_WithIndexedInputAndWake_AllocatesNothing_AfterWarmUp` |
| S2 indices ≥ `Capacity`, first `HighWaterIndex` copied; jobs never read game memory; a step sees values at entry | `TryBindInput` checks, `ValidateInputs`, `BeginStep` | `WritesDuringAStep_AreSeenOnlyByTheNextStep`, `TryReserve_NeedsTheIndicesRebound_NotTheRows`, `IndicesOrRowsWrittenByARunningGameJob_RefuseTheStepWithInputMissing`, `Bind_Refusals…` |
| S3 negative index reads zeros | `SelectRow` returns -1 | `NegativeIndex_ReadsZeros` (-1 and `int.MinValue`) |
| S4 out-of-range index of a live agent refuses the step with `InvalidArgument`; other slots unchecked | `ValidateInputs` (slots below the high-water index whose state is `Alive`) | `OutOfRangeIndexOfALiveAgent_RefusesTheStep_NothingRuns` (tick not taken, no update), `OutOfRangeIndexOfASlotWithoutALiveAgent_DoesNotRefuse` (removed in teardown and after, free, above high water; a reused slot keeps the old index and is refused until written) |
| S5 wake = existing value compare through the index | `InputChanged` / `RecordSeenInputs` stage through `TryStage` → `SelectRow` | `WakeOnChange_ARowChange_WakesTheMembersOfThatRowOnly`, `WakeOnChange_AnIndexChange_WakesOnlyWhenTheValueDiffers` |
| S6 one routine for jobs, in place, wake, trace | `SelectRow`, called by `TryStage` (job and in place) and by `TraceThinkStarted` | `Population_LogsAreIdentical_AcrossPathsWorkerCountsAndRuns` (200 agents, membership changes, removals and adds; in place twice, jobs batch 1 and 64, 0 and 1 workers), `MembersReadTheirRow…` |
| S7 binding again with the other overload switches the mode | `BindRows` sets the mode and restages | `BindingAgain_SwitchesBetweenSlotRowsAndIndexedRows` (indexed → slot → indexed, unbind → `InputMissing`) |
| S8 trace and MCP say the row, additively | `AgentTraceRecord.InputRow` (new property, constructor parameter defaults to -1); MCP adds a `row` key | `Trace_TheInputFactCarriesTheRowAndItsValue` (rows 2, -1, 0; slot mode → the slot), `McpReadAgentTrace_ReportsTheRowOfAnInputFact` |
| S9 documents | see "What changed" | `McpDocumentationGeneratorsTests` |

Also from the design read's list of 18: `OneIndexArray_SharedByTwoChannels` (item 10), `NonCanonicalRow_FaultsEveryReaderOfThatRowOnly`
(14), `IndexedBind_DuringAStep_ReturnsBusy` (16); item 18 is `TreeSimulationIoTests` (it holds
`Hosts_StillRefuseSnapshotBindings…`) and the documentation generator tests, both green below.

**Why S8 is additive.** No pinned format holds the trace record or the MCP JSON: `read_agent_trace` is described
in prose only (`MCP/McpOperationCatalog.cs:55`, `Documentation~/simulation-container.md`), no golden file or
contract test pins its keys, and the record's constructor is internal. The value bytes (16 kept) are unchanged.

## Tests run (`Tools/Unity/Invoke-UnityTests.ps1`, one run at a time)

| Class | Result |
| --- | --- |
| `TreeSimulationIndexedInputTests` | 30/30 in three runs; the last two after the last code change (the benchmark harness), with the agent-frame job under Burst (see below) |
| `TreeSimulationIoTests` | 20/20 |
| `TreeSimulationDebugTests` | 16/16 |
| `McpSimulationToolsTests` | 4/4 |
| `McpDocumentationGeneratorsTests` | 14/14 (after regenerating the runtime API reference) |
| `TreeSimulationIndexedInputBenchmark` | 1/1 twice (opt-in measurement, below) |

The first run of the new class had 8 failures. Three were test mistakes, fixed without changing what the tests
claim. Four wake cases expected exactly `InputChanged`: the report tree's root succeeds and restarts every
think, so `FreshInstance` comes with it; they now require `InputChanged` set and `Scheduled` not set. The
allocation test (both paths) did not call `GetCommands` in its warm-up, so that call's first JIT fell in the
measured window (208 samples). The same loop measured again after it allocated 0; the warm-up now makes every
call the measured loop makes, as `TreeSimulationIoTests` does. The jobs failures are the Burst finding below.

## The Burst finding (state of this Editor, not of the code)

After the runtime change, the jobs path passed on the first runs after a recompile and then failed once Burst
had compiled the agent-frame job. The values read were exactly those of the old routine: row = slot, zeros
past the row count. The Console showed why: `Burst internal compiler error: ... Error while hashing ... in
AIBT.Integration.Tests ... Failed to resolve field with name hash ... in type definition '200020A'`, raised
while hashing a test method that reads the new fields. Burst's hash cache had a view of `SimulationInputChannelV1`
without them, so it compiled jobs against the pre-change `AIBT.Runtime`, although every assembly on disk was
rebuilt. Until the probes that referenced the new fields from the test assembly were removed, every job of
that assembly ran managed.

What restored it is not certain. I made two `RequestScriptReload` reloads and called Burst's internal
`NotifyAssemblyCompilationFinished` for the AIBT assemblies plus `NotifyCompilationFinished`, the calls the
Editor itself makes after a compile, and removed the probe jobs. After that the agent-frame job ran under
Burst with the current routine. Both measurement runs record 0 agents run without Burst on the jobs path in
every block (`managedAgentsLastBatch` in `blocks.csv`), with order digests equal to in place and the indexed
variant equal to the slot-row one. That would be impossible with the old routine, which reads row = slot and
gives zeros past row 49. The test class then passed twice more. No Burst error since. Burst options are as
they were: enabled, async, safety checks on.

Consequence for acceptance: a jobs-path result obtained in an Editor whose Burst cache predates a change to
an internal struct may be wrong without any error. A fresh Editor before the full run removes the doubt.
Notes for the orchestrator: `side-session-notes/…/P12-002-burst-stale-runtime.md`.

## Measurement

Harness `TreeSimulationIndexedInputBenchmark`: 500 agents in 50 squads of 10, `Spread(5)`, one step per tick
(33 333 µs), the same row type (`SenseInput`, 12 bytes) and tree (one order per think from the row read).
Variants:

- **S, slot rows:** 500 rows; the game writes each member's squad value into its slot row every tick.
- **I, indexed:** 50 rows written every tick; `rowOfSlot = slot / 10`, written once.
- **SW, IW:** the same with wake on change. Five squads change value per tick.

Only `Step` is timed; the game's writes are timed apart. 8 rounds × 2 paths × 4 variants, 60 warm-up and 300
measured steps per block. The path order alternates per round and **the variant order rotates by one position
per round**: each variant is in each position twice per path. Two runs:

- `run0-validation-20261125-064951`: the first run, also the harness check;
- `run1-20261125-070402`: the repeat.

Raw data in each folder: `steps.csv` (every step), `blocks.csv` (per block: GC.Alloc samples, order digest,
faults, agents run without Burst), `environment.json`, `load-cpu.csv`, `load-processes-before.txt`,
`summary.md`. Scripts: `analyze.py`, `load_summary.py`.

Median of the 8 round medians, µs per step (min–max across rounds):

| path | variant | run0 | run1 | game writes / step | orders (= thinks) / step | woken by input / step |
| --- | --- | --- | --- | --- | --- | --- |
| in place | S | 41 032 (38 294–50 524) | 41 127 (36 481–44 114) | 15.2 | 100 | 0 |
| in place | I | 41 277 (36 119–54 272) | 40 749 (35 976–43 629) | 2.5 | 100 | 0 |
| in place | SW | 57 938 (52 495–75 379) | 57 141 (51 272–60 996) | 15.2 | 140 | 40 |
| in place | IW | 56 898 (50 035–73 691) | 57 421 (51 725–63 879) | 2.5 | 140 | 40 |
| jobs | S | 715 (662–764) | 739 (665–801) | 14.5 | 100 | 0 |
| jobs | I | 736 (685–781) | 724 (683–766) | 1.9 | 100 | 0 |
| jobs | SW | 1 741 (1 707–1 805) | 1 765 (1 738–1 810) | 14.9 | 140 | 40 |
| jobs | IW | 1 795 (1 693–1 824) | 1 816 (1 793–1 884) | 2.1 | 140 | 40 |

Paired differences inside each round (µs; "confirmed" = the same sign in all 8 rounds):

| path | difference | run0 median (min–max) | run1 median (min–max) | verdict |
| --- | --- | --- | --- | --- |
| in place | I − S | −457 (−6 116–10 843) | −863 (−3 403–5 104) | no difference measurable; noise ±10% |
| in place | IW − SW | −1 152 (−14 691–2 640) | +1 150 (−5 781–7 027) | no difference measurable |
| jobs | I − S | +22 (−33–69) | −16 (−50–60) | not confirmed in either run (±3%) |
| jobs | IW − SW | +27 (−19–83) | +49 (+23–92), confirmed | about +1.5–3% (27–49 µs at 100 + 40 thinks): confirmed in run1 only |

- **What the numbers say.** Without wake, reading squad data through the index: no difference was detected at
  this resolution (about ±5 % of a 0.7 ms step), on either path. With wake on, the jobs path may pay a few tens
  of µs per step for the index-aware compare: it stages each not-due agent's value on the main thread through
  `SelectRow` (the in-place path is too noisy to see it). The three-run table in "Acceptance" below is the
  reference for both readings.
- **Correctness inside the measurement.** 64 blocks per run:
  - 0 GC.Alloc samples over the measured steps (with wake, with rows written every tick);
  - 0 faults;
  - order digests equal for I and S, for IW and SW, and for jobs and in place, every round;
  - 0 agents run without Burst on the jobs path.
- **Load.** Machine CPU median 30% (p90 53%) in run0 and 23% (p90 31%) in run1, from `load_summary.py`.
  Another Unity Editor (pid 23100, another project) holds the most CPU time besides this one in every sample.
  The in-place spread (about ±10% across rounds) is managed code in the Editor plus this load. The median by
  position in the round shows no trend in jobs (1 233–1 284 µs) and no monotone one in place.
- **Not measured.** A Player build; Burst without safety checks; larger rows (a squad struct of 64 bytes would
  copy 3.2 KB per step instead of 600 B; the cost of the copy follows the rows array).

## Documents

- `simulation-container.md`, new section "Group data: an indexed input channel":
  - the example;
  - the step-entry copy, its cost, negative and out-of-range indices;
  - the reused-slot hazard (also in `TryAdd`'s doc), rebinding after `TryReserve`;
  - wake through the index and the moving-anchor warning (split the data into a channel without wake; two
    channels can share one index array);
  - traces keep 16 bytes; the row in the trace and MCP;
  - the limits: library nodes and blackboard keys do not see the rows, nodes cannot write them, another
    squad's row is P12-003's question.
- ADR-P10-008: amendment 2026-11-25 (route 1 built, route 2, the Shared-scope context, still the provisional
  slot). Corrected:
  - the header, which said nothing was implemented and the owner decisions were open;
  - the API sketch's `InputMissing` line: a short array is `InvalidArgument`, as the G2 code returns since
    `3cdb6e7`.
- CHANGELOG entry; `api-reference-runtime.md` regenerated: two lines added, the new overload and `InputRow`.

## Not done

- The full run of the ten assemblies (the brief limits me to the touched classes).
- A Burst-backed run of the jobs tests in a freshly started Editor (see the Burst finding). In this Editor
  they pass under Burst after the cache recovered.

## What I am unsure of

- What exactly cleared the stale Burst view: the reloads, the internal notify calls, or removing the probe
  jobs. The record above is what I did and what I saw.
- Whether the wake-on cost in jobs (IW − SW) is real: confirmed in one run of two, about 2–3%.

## Acceptance (orchestrator, 2026-11-25)

Accepted on `8ed016a`; the status stays `Review` as for every item of phases 11 and 12.

- **Diff read.** `14d3070` adds one public overload (`TreeGroup.TryBindInput<T>(channel, rows, rowOfSlot)`) and
  one public property (`AgentTraceRecord.InputRow`); no existing public signature changes. One routine,
  `SimulationAgentRoutinesV1.SelectRow`, chooses the row for the job path, the in-place path, wake on change and
  the trace, and bounds-checks the slot and the row. Nothing under `CodeGen~`, `Tests/Editor/CodeGen/Contracts`,
  `Tests/Fixtures`, `Documentation~/specifications`; `package.json` is untouched by this item (the version
  change below is a separate commit on the owner's word).
- **Full run of the ten assemblies on `8ed016a`, 2026-11-25 11:18–11:48:** Runtime 821/821, Integration 765
  passed and 1 ignored (the opt-in P12-001 benchmark), Editor 917/917, BehaviorCases 65/65, NativeBurstDispatch
  79/79, NativeTreeAgentBlackboard 33/33, NativeSharedBlackboard 36/36, CodeGen.ContractTests 6/6,
  CodeGen.GenerationTests 14/14, PlayMode 2/2; noise unknown-command=0, main-thread-timeout=17 (13 before this
  item; constant through the run, so the four new lines come from the work before it, not from the tests). Raw:
  `C:\UnityProjects\_aibt_acceptance\P12-002\acceptance\full-run-8ed016a.txt`.
- **An unintended third measurement inside that run.** The Editor process still had
  `AIBT_P12002_BENCHMARK=1`, so `TreeSimulationIndexedInputBenchmark` ran as part of the Integration assembly
  (11:23–11:38, passed) and overwrote `steps.csv`, `blocks.csv` and `environment.json` of
  `run1-20261125-070402\` (its `summary.json` and `summary.md` are still run 1's; a note file in the folder says
  so). The orchestrator had recomputed run 1 from the original file before that. The third run is kept as
  `run2-inside-full-run-20261125-1138\`. The variables were cleared in the Editor afterwards.
- **Numbers recomputed** with a script of the orchestrator's own (per round, path and variant the median of 300
  steps; median of the eight round medians; differences paired inside a round), jobs path, µs per step:

  | | run 0 | run 1 | run 2 (inside the full run) |
  | --- | --- | --- | --- |
  | S / I | 715.1 / 735.9 | 739.0 / 723.6 | 676.8 / 670.9 |
  | I − S | 21.9 [−32.7, 68.5], 7 of 8 positive | −15.7 [−50.3, 59.9], 2 of 8 | −0.2 [−35.8, 19.9], 4 of 8 |
  | SW / IW | 1741.2 / 1795.1 | 1764.6 / 1816.3 | 1738.0 / 1787.0 |
  | IW − SW | 27.3 [−18.6, 83.1], 5 of 8 | 49.2 [23.0, 91.9], 8 of 8 | 42.8 [8.4, 71.2], 8 of 8 |

  Runs 0 and 1 equal the table of "Measurement". In all three: 64 blocks, zero `GC.Alloc` samples, zero faults,
  no agent whose last job ran without Burst, and one order digest per round and wake setting across indexed and
  slot rows, jobs and in place. Reading: without wake no difference was detected at this resolution (about
  ±5 % of a 0.7 ms step); with wake the indexed channel costs about 45 µs per step (2.5 %), with one sign in
  every round of two runs out of three. The sentence of "Measurement" that compares this with the game's saved
  writes is not supported (about 12.5 µs saved against 27–49 µs) and is to be corrected.
- **Independent read** (a reader given the commit, the decision and neutral questions; it ran nothing):
  `C:\UnityProjects\_aibt_acceptance\P12-002\acceptance\independent-review.md`. No blocker: the nine semantics
  items are built as written; no path still assumes row = slot for an indexed channel; no out-of-bounds read
  is reachable; programs that do not use the overload behave as before (additions only: the MCP `row` key, four
  more bytes in a trace record); the `AIBT.Mcp` reference of the integration tests is Editor-only on both sides.
  Should-fix findings, sent back as a follow-up and not a gate (owner, 2026-11-25: "остальное если что
  поправим"): S1 the jobs variants assert that a job batch ran, not that it ran under Burst; the
  `GetAllocatedBytesForCurrentThread` assertion cannot fail in this Editor; S2 the result-code texts
  (`SimulationResult`, `TreeSimulation.Step`, the table of `simulation-container.md`) do not name the new cause
  of `InvalidArgument`; S3 the measurement sentence above; the untested cases listed in §4 of the read.
- **Decided, not changed (S4).** `ValidateInputs` checks the game's index array, and the step then copies it.
  A game job that writes the array between the two is already refused where collection checks exist, is a
  misuse without them, and the worst outcome is zeros for that agent (`SelectRow` bounds the row). Recorded as
  a known limit rather than reordered.
- **What the acceptance does not cover.** The overload never ran in a Player (IL2CPP) and never in a freshly
  started Editor; the Burst finding above was met and cleared in a long-lived Editor and its cause is not
  established. Unity 7 is not verified (stage G5 is postponed by the owner).

## Review follow-up (2026-11-25)

The should-fix findings of the independent read and the orchestrator's two process notes. Status stays `Review`.

| Finding | Change |
| --- | --- |
| Benchmarks overwrote a run folder | `TreeSimulationIndexedInputBenchmark` and `TreeSimulationWakeCostBenchmark` refuse (fail before writing anything) a folder that already holds `steps.csv`. From now on a benchmark switch is cleared in the same step that reads the result. |
| S1: jobs variants did not prove Burst | New `[UnityTest] AgentFrameJob_RunsTheIndexedRoutineBurstCompiled`, built as P11-014's `BurstExecutionPathTests`. One step per Editor frame. Every step's orders must be the values only the indexed routine reading this step's copy gives: rows are rewritten every step, no slot reads its own row, slot 11 reads -1. The test waits until `AgentFrameAgentV1.RanManaged` is 0 for every agent for 10 steps in a row. Burst off: `Assume` (Inconclusive). Not settled within 120 steps and 90 s: `Assert.Inconclusive`. `BurstCompiler.Options` is never changed. |
| Allocation assertion that cannot fail | `GC.GetAllocatedBytesForCurrentThread` removed from `Step_WithIndexedInputAndWake_AllocatesNothing_AfterWarmUp`; the `GC.Alloc` recorder assertion stays. |
| S2: result texts | The XML docs of `SimulationResult.InvalidArgument` and `TreeSimulation.Step`, the `SimulationResult` row and "One step" item 1 of `simulation-container.md` now name the causes: an index array shorter than `Capacity`, or an index in a live agent's slot at or beyond the rows' length. Doc comments only. `api-reference-runtime.md` lists signatures only, so it is unchanged and was not regenerated. |
| S3: measurement sentence | "Measurement" now says "no difference was detected at this resolution (about ±5 % of a 0.7 ms step)". The comparison with the saved game writes is removed. It points to the three-run table in "Acceptance". |
| S4 (decided, not changed) | One sentence in "Group data" of `simulation-container.md`: the index array is checked before it is copied, so do not write it from a job that can still run when `Step` is called. |
| §4 untested cases | Seven tests, each in place and in jobs: a rows array of length 0 with a live index of 0 or above (refused); a group with no agents and an indexed channel (steps, and a later agent is checked); unbind then bind indexed again (new arrays read, old index array not read); indexed to indexed with rows of another length (6 then 3 rows); `TryReserve` with wake on (what agents last saw is kept, no spurious wake, a grown slot reads its row); a mode switch with wake on; rows longer than `Capacity`. |

**The pinned behaviour of a mode switch with wake on.** `BindRows` does not touch an agent's last-seen value. The first compare after a switch therefore sets the value the agent now reads (its slot row, or the row its index selects) against the value it read at its last think under the old mode. An agent that is not due wakes when, and only when, the two differ. The switch itself wakes no one.

**Tests run** (`Tools/Unity/Invoke-UnityTests.ps1`, one run at a time):

- `TreeSimulationIndexedInputTests`: 45/45. Before this follow-up there were 30 cases; the S1 test adds 1 and the seven §4 tests add 14.
  - The S1 test settled under Burst at once: 10 steps, 0.5 s, the first step already Burst, 0 managed steps. The job was already compiled in this Editor, so the Inconclusive branch did not run.
- `TreeSimulationIndexedInputBenchmark` and `TreeSimulationWakeCostBenchmark` with the switches cleared: ignored, 0/1 each.
- The refusal, exercised once: `AIBT_P12002_BENCHMARK=1` was set with a scratch folder that held a dummy `steps.csv`. The benchmark failed at once with the refusal message and left the file unchanged. The switches were cleared in the same command and read back as unset.
  - The P12-001 refusal was not exercised: its folder name is a per-second time stamp, so it cannot be seeded in advance.

**Left out.** The case of a teardown that reads an input: no fixture node reads an input outside `Tick` (every `TryReadSnapshot` in `Tests/Editor/CodeGen` is in a `Tick`), and `Tests/Fixtures` is protected.

## On Unity 7000.0.0a8 (2026-11-25)

Modules moved to 7000.0.0a8, with built-in Burst 2.0.0, a shim that forwards to the Editor's Burst module.

- **`AgentFrameJob_RunsTheIndexedRoutineBurstCompiled` (the S1 test) passed under Burst** at 23:04, in the Editor
  that the P12-004 cold run B had started: `10 steps, 0,5 s, first Burst step 1, managed steps 0, Burst steps 10`.
  Each step's orders are values that only the indexed routine gives, so the current routine ran Burst-compiled.
- **After a recompile, its Inconclusive branch ran once, as designed.** At 23:05 `AIBT.Integration.Tests` was
  recompiled after a doc-comment edit. The next run (23:07–23:09) gave
  `1790 steps, 90,0 s, first Burst step none, managed steps 1790`, while the job compiled asynchronously.
  The rerun at 23:11 passed under Burst. `TreeSimulationIndexedInputTests` in that run: 44/45, 0 failed.
- **The stale Burst view was not re-provoked.** That needs a change to an internal struct in a running Editor;
  no runtime struct was changed. No `Burst internal compiler error`, `Failed to resolve field` or
  `Error while hashing` line is in `Logs/Editor.log` of that session. Whether Burst 2.0.0 of 7000.0.0a8 can keep
  a stale view is therefore not established. The rule above stands: after an internal struct change, check the
  "ran managed" signal before trusting a jobs result.
- Details: `Planning~/Evidence/P12-004/README.md`.

## After the independent review of `8396202..dccab95` (2026-11-25)

The review noted that the S1 test still ended Inconclusive under the old caps (at least 120 steps; at
most 6000 steps or 90 s), so it could not fail when the job ran managed, as it did after the recompile at
23:05.

**Decision: the same approach as P12-004, and a failure instead of Inconclusive.** A test meant to prove that
the indexed routine runs Burst-compiled proves nothing if a managed run only makes it Inconclusive. The test
now:
- turns `BurstCompiler.Options.EnableBurstCompileSynchronously` on for its own run if it was off, and turns it
  back off in a `finally`;
- waits up to 300 s (`BurstWaitSeconds`), counted in seconds only, with at least 120 steps; the 6000-step cap
  is gone;
- fails (`Assert.That(settled, Is.GreaterThanOrEqualTo(10))`) when the agents did not run Burst for 10 steps
  in a row within that bound;
- has `[Timeout(900000)]`, as P12-004's class.

The cost is the same as P12-004's: on a cold cache the first step blocks the Editor's main thread while Burst
compiles, and an Editor killed during the test leaves the option on in EditorPrefs. The 300 s bound is still
needed: a request scheduled asynchronously earlier in the same domain may still be compiling when the option
is turned on. Burst off still gives `Assume` (Inconclusive): there is nothing to prove then.

**Run** (recompiled first; with `TreeSimulationSquadRowsTests` and `BurstExecutionPathTests`, 23:35–23:37):

```
testName TreeSimulationIndexedInputTests 45/45 (failed=0 skipped=0)
P12-002 review S1: 10 steps, 47,9 s, first Burst step 1, managed steps 0, Burst steps 10, synchronous Burst compilation on for this test
```

- The recompile had made the job stale, as at 23:05. This time the first step waited about 48 s for the
  compilation and every step ran Burst; nothing ran managed.
- The option read before and after the run: False both times, in the options and in EditorPrefs.
- **Not run:** the failing branch, and Burst off.

**`TryReserve_WithWakeOn_KeepsWhatEachAgentLastSaw` (a §4 test above) is strengthened.** The review found that
it could not tell a kept last-seen value from a cleared seen flag: `InputChanged` skips an agent whose seen
flag is 0, so an agent that lost its flag would also not wake, and the old checks passed either way. The test
now changes row 0 before tick 7, when agent 0 is not due (it last thought at tick 5, before the reserve),
and asserts that tick 7 runs exactly agents 0, 2 and 4: agent 0 woken by the input against what it saw
before the reserve, agent 2 due, the newcomer every tick, and both readers of row 0 read 444. A cleared seen
flag would leave agent 0 asleep and fail that assertion. The rest of the test is unchanged. Passed in the
run above (both `False` and `True` cases are among the 45).
