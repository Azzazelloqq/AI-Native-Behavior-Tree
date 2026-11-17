# P10-011 evidence — Simulation container, stage G3 (external long-running actions)

Card: `Planning~/Tasks/P10/P10-011-simulation-container-g3.md`. ADR:
`Documentation~/decisions/ADR-P10-008-simulation-container.md`. Unity 6000.6.9f1, branch
`feature/aibt-consumer-workflow`, base `5b4ec02` (G2).

Commits: `9736622` runtime, tests and docs; the bench harness, this evidence, the card and
`work-items.json` follow in the next commit.

> **Status: Review.** All eight assemblies are at their baseline plus the new tests. ABI v2 is
> unchanged, the public API only grows, and the IL2CPP measurement is done.
> - Two findings need the owner's decision (§7): F1, the step-limit default; F2, actions under
>   reactive composites.
> - Neither changes G1/G2 behaviour.

## 1. What was built

**Public API (additive, `AIBT.Simulation`, experimental until G5):**

- `ActionOutcome` (`Succeeded`, `Failed`), `ActionEventKind` (`Started`, `Cancelled`),
  `ActionCancelReason` (`Preempted`, `Timeout`, `NodeEnded`, `Fault`, `AgentRemoved`,
  `AgentRestarted`, `GroupRemoved`).
- `ActionHandle` (`Agent`, `Operation`, `Channel` = FNV-1a 64 of the async binding id, `IsNull`,
  equality) and `ActionEvent` (`Kind`, `Action`, `CancelReason`, `FaultCancel`).
- `TreeGroup.GetActionEvents()`, `TryGetStartPayload<T>`, `TryGetCancelPayload<T>`,
  `TryComplete(ActionHandle, ActionOutcome)`, `TryComplete<T>(ActionHandle, ActionOutcome, in T)`,
  `ActionEventCapacity`.
- `TreeGroupSettings.ActionCapacityPerAgent` (0 = `DefaultActionCapacityPerAgent` = 2, negative
  refused).
- `SimulationResult.StaleAction`, `AlreadyCompleted` (appended). `Unsupported` is no longer returned.

**Generator.** Unchanged. The G2 binding-id table (`IGeneratedBindingIdentityV1`) names action and
completion channels.

**Runtime (container only).**

- `SimulationProgramMapV1` maps async-operation and completion bindings to action and completion
  channels (by binding id; channels with the same id must agree on types, else `TypeMismatch`).
  Without binding ids: `CatalogTooOld`.
- **The agent's action ledger is the source of truth.** Each agent has a fixed-capacity list of live
  actions (`SimulationActionRecordV1`) with their start, fault-cancel and completion values at a
  fixed stride, plus a per-step event log.
- **Before each dispatch** of a node with action bindings, `SimulationActionRoutinesV1.TryPrepare`
  rebuilds the dispatch slot's operation table and completion view from the ledger (the node's live
  actions of this activation; one completion per completion binding that accepts the game's value).
  The workspace's operation-count floor is lowered to match
  (`NativeBurstDispatchWorkspaceOwnerV2.TryRebaseOperationCountFloor`, internal).
- **After a successful dispatch**, `TryCapture` folds the published Start and Cancel commands and the
  consumed completions back into the ledger and writes events; on Exit it fault-cancels what the
  node left live (D4).
- Both routines run in the in-place dispatch and in the agent-frame job (Burst), so both paths make
  the same bytes.
- Hosts: the adapter still refuses async-operation and completion bindings without the program map,
  exactly as before.

**Rules.**

| Topic | Rule |
|---|---|
| Event order | Agents in slot order; each agent's events in the order they happened (teardown of an old instance before the new instance's think). Removed groups' teardown events are collected too and stay readable until the group is released. |
| Exactly one end | A started action ends once: its completion is consumed (no event), or one `Cancelled` event. |
| Cancel reason | Abort with `Timeout` → `Timeout`; abort with `TreeStopped` during a teardown → the teardown's reason (`AgentRemoved`, `AgentRestarted`, `GroupRemoved`); any other abort → `Preempted`; Exit with live actions → `NodeEnded` (or the teardown reason); agent fault → `Fault`. |
| Fault-cancel | Every `Cancelled` that is not the node's own cancel in Abort carries the start's fault-cancel value, with `FaultCancel = true`. |
| Completion | `TryComplete` records the outcome and value on the ledger record at once and wakes the agent for the next step. Completions for an agent that is not due are therefore never lost or delayed past one step. |
| Completion vs preemption | If the node is aborted in the step before it reads the completion, preemption wins: one `Cancelled`, the completion is dropped. Under a reactive composite that is every think (F2). |
| Capacity | `C = ActionCapacityPerAgent`: at most C live actions and C starts per think (a teardown starts nothing). Events per agent per step ≤ 3C (C starts, C cancels of earlier actions, C ends), so the group's event list (slots × 3C) never grows. A start over the limit faults the agent (`CapacityExceeded`) and its live actions are fault-cancelled (`Fault`). |
| Polling | A Tick dispatch whose only failure is `StaleCompletion` (poll before completion) is treated as `Running` with nothing published. |

## 2. Tests

New: `Tests/Integration/Simulation/TreeSimulationActionTests.cs`. The job node lives in a fixture
shard, `Tests/Editor/CodeGen/GenerationP10011/` (assembly
`AIBT.CodeGen.P10011SimulationActions.Tests`); the catalog `SimulationActionCatalog` (built-ins,
standard native, the P10-010 shard and the P10-011 shard) is in the Integration assembly. All value
types declare their fields out of id order. The G2 test `ExternalActions_AreRefusedWithUnsupported`
became `ExternalActions_RunInTheContainer_SinceG3_HostsStillRefuseThem` ×2.

| Test | What it proves | Result |
|---|---|---|
| `Population_Actions_DigestsAreIdentical_AcrossPathsWorkerCountsAndRuns_AndEveryActionEndsOnce` | (a)(c) 2 × 260 agents (crew `Spread(5)`, wake on changed `sense`; shift `Spread(3)`), 300 ticks, input written every tick, 20 removed/re-added per group at 60/61 and 180/181, 15 restarted per group at 100 and 220, both groups removed at the end. The game completes each start 1–6 ticks later (failure, success without a value, success with a value) or never (1 in 8). Digest per tick: agent states, root completions, faults, action events with payloads, JobDone commands. Identical in place ×2, jobs, default settings, 0 and 1 worker. Ledger: every started action cancelled once or consumed once | passed |
| `StartThenSuccess_…` ×2, `StartThenFailure_WithoutAValue_NodeReadsZeros` ×2 | (b) start → success / failure, payloads both ways, double complete → `AlreadyCompleted`, complete after consume → `StaleAction` | passed |
| `Completion_WakesTheAgent_ForTheNextStep` ×2 | Completion given after tick 3 to an agent thinking every 10 ticks is read in step 4 | passed |
| `ReactiveInterrupt_PreemptsTheJob_CancelledWithTheAbortValue` ×4 | (b) Cancelled `Preempted` at tick 20 without wake, at tick 13 (next step) with wake; in place and jobs | passed |
| `UnderAReactiveSelector_TheJobRestartsEveryThink_AndACompletionIsNeverConsumed` ×2 | (b) pinned: preemption wins over a completion in the same step; finding F2: under a reactive selector the job is cancelled (`Preempted`) and restarted at every think | passed |
| `AgentRemoved_…` ×2, `AgentRestarted_…` ×2, `GroupRemoved_…` ×2 | (b) teardown reasons, cancel value from Abort, restart order, events readable until release | passed |
| `NodeEnded_WithALiveAction_FaultCancelsIt` ×2 | D4: fire and forget, and a node giving up on time | passed |
| `TryComplete_RefusesStaleWrongGroupWrongTypeAndInvalidValues_ChangingNothing` | (b) null/foreign/wrong-operation/wrong-channel handles, wrong types, NaN/Inf, bad outcome; −0.0 normalised | passed |
| `StaleHandle_AfterSlotReuse_IsRefused` | (b) | passed |
| `RestartInPlace_WithLiveActions_EqualsAFreshAgent` ×2 | G1 reset-equals-fresh extended to the ledger: after the teardown the ledger is empty (as a fresh agent's, except the per-think start allowance that the next think resets); from the first think on, machine, adapter (operations, completions) and ledger bytes equal a fresh agent's with the same instance id after each of 30 steps | passed |
| `ActionCapacity_Overflow_FaultsTheAgentWithCapacityExceeded_NeverThrows` ×2 | (e) | passed |
| `ActionCapacity_NegativeIsRefused_ZeroIsTheDefault_AndProgramsWithoutActionsHaveNone` | capacity rule | passed |
| `Step_WithActionsCompletionsAndEvents_AllocatesNothing_AfterWarmUp` ×2 | (d) | passed |
| `CallsDuringStep_ReturnBusy` | (f) | passed |
| `OldCatalog_ProgramsWithActions_AreRefusedWithCatalogTooOld` | old catalogs | passed |
| `ExternalActions_RunInTheContainer_SinceG3_HostsStillRefuseThem` ×2 (in `TreeSimulationIoTests`) | (g) | passed |
| `RunawayTree_FaultsTheAgentAtTheStepLimit_AndCancelsItsLiveAction` ×2 | Finding F1: with `MaximumStepsPerThink = 1000` a tree that never yields faults with `NativeUpdateStepLimitExceeded` after exactly 1000 steps, in place and in the job; its live action gets one `Cancelled` (`Fault`, fault-cancel value); the calm agent beside it is untouched; Step returns | passed |
| `DefaultStepLimit_IsTheHostDefault` | Pins the current G1 default (1 000 000) until the owner decides on F1 | passed |

## 3. Player stripping

`TryComplete<T>` and the payload getters identify `T` by `[AibtBurstValue]` (or a built-in type) and
lay it out by `[AibtValueField]`, the same reflection as G2's inputs and commands. The G2 link.xml
processor already keeps both attributes and every `[AibtBurstValue]` type, so nothing new is needed.
Confirmed by the IL2CPP Player (§5, High stripping, no link.xml change):
- `TryComplete<PerfJobResult>` succeeded for every one of 5 500 (`act-loop16`) and 106 000
  (`act-reactive16`) completions per run (`completeFailures` = 0).
- The nodes read the values: the order checksum of `act-loop16` is the same in every run.

## 4. Verification

All test runs below used the reopened Editor, `run_tests --async_tests true` with polling (except
Runtime's first pass, which also passed synchronously), Unity 6000.6.9f1.

| Check | Result | Baseline |
|---|---|---|
| `AIBT.Runtime.Tests` | 710/710 | 710/710 |
| `AIBT.Editor.Tests` | 636/637; the one failure is the known `McpToolDispatcherTests.ZeroCustomNodesReturnsExactlyThePhase1BuiltInCatalog` (sample nodes in the project). The two documentation tests failed before regeneration (G3 types missing from `api-reference-runtime.md`) and pass after it (13/13) | 636/637 |
| `AIBT.Integration.Tests` | 174/174 (406 s): 138 − 1 replaced G2 test + 2 `ExternalActions_RunInTheContainer_…` cases + 35 `TreeSimulationActionTests` | 138/138 |
| `AIBT.BehaviorCases.Tests` | 65/65 | 65/65 |
| `AIBT.NativeBurstDispatch.Tests` | 79/79 | 79/79 |
| `AIBT.NativeTreeAgentBlackboard.Tests` | 33/33 | 33/33 |
| `AIBT.NativeSharedBlackboard.Tests` | 36/36 | 36/36 |
| `AIBT.PlayMode.Tests` | 1/1 | 1/1 |
| `AIBT.CodeGen.ContractTests` | 6/6 (ABI v2: `ExpectedPublicAbiV2.txt` unchanged) | |
| `Verify-Static.ps1` | passed (schemas 7, work items 175) | |
| Generated docs | `AIBT/MCP/Regenerate Documentation` writes to `Assets/AIBT-Documentation`; only `api-reference-runtime.md` differed from `Documentation~/generated` and was copied (314 public types). `node-catalog.md` differs only by the project's sample nodes and was not copied (as before). The temporary folder was removed. | |
| Public API (`Get-FullPublicApi.ps1 -BaselinePath perf-data\P10-010pi\public-api-p10010.txt`) | passed: 574 types / 2 644 members; no removals or renames; 33 additions, all in `AIBT.Simulation` (5 new types `ActionCancelReason`, `ActionEvent`, `ActionEventKind`, `ActionHandle`, `ActionOutcome`; `SimulationResult.StaleAction`/`AlreadyCompleted`; `TreeGroup.TryComplete` ×2, `TryGetStartPayload`, `TryGetCancelPayload`, `GetActionEvents`, `ActionEventCapacity`; `TreeGroupSettings.ActionCapacityPerAgent` and its default) — `perf-data/P10-011/api/added.txt` | |

**First run, before the fixes.** `TreeSimulationActionTests` ran 30/35 the first time:
- `ActionCapacity_NegativeIsRefused_…` was a test bug. An unbound input made that container's
  `Step` return `InputMissing`; the plain group now has its own container.
- The reactive-selector test failed ×2, which led to finding F2.
- The restart test failed ×2. Its probe ran after teardown but before the reset at the next think.
  It now checks the emptied ledger at the probe, and machine, adapter and ledger bytes after every
  step from the first think on.

**What blocked the Editor's command queue (2026-11-17, second Editor session).**
- **The run itself was fine.** The eight assemblies were first run through `Tools/Unity/Invoke-UnityTests.ps1`, which uses the
  synchronous `unity command run_tests … --timeout 3600`. Runtime (710/710) and Editor returned.
  Integration ran 17:35:24–17:41:54 and passed 174/174 in 390 s (`TestResults.xml`; the slowest
  fixtures were `TreeSimulationActionTests` 126 s, `JobsPolicyEquivalenceTests` 98 s and
  `TreeSimulationIoTests` 97 s).
- **The result never reached the client.** The CLI client (pid 42416) waited from 17:35 to 18:08.
  `editor_status`/`test_status` timed out after 25 s, while the Editor sat idle.
- **The Editor log shows a bug in `com.unity.pipeline`, not AIBT.**
  - `PipelineTestRunner.ExecuteSyncMode` registers one more `TestResultCollector` callback per
    synchronous run in the same Editor session: "Run started: 2208 test(s)" was logged 1, 2, 3, then 4
    times.
  - The later RunFinished calls were "Ignoring duplicate RunFinished (already completed)".
  - About 28 minutes after the run ended came "Test execution timed out, cancelling…" and "Ignoring
    RunFinished from cancelled run". The synchronous handler never answered, and the serial command
    queue stayed blocked even after the client process was stopped.
- **Recovery.** The Editor was closed gracefully (`CloseMainWindow`) and reopened.
- **From then on**, every assembly runs with `run_tests --async_tests true`, with polling of
  `test_status --timeout 20` (`run-async-tests.ps1` in the session scratchpad), as the brief asked.
- **Integration now takes about 6.5 minutes.** The 35 new action tests add 126 s, mostly the population
  test: 6 runs × 2 × 260 agents × 300 ticks with a scripted game, plus the allocation test's population.

## 5. Measurement

**Setup.** IL2CPP Player, Release, High stripping, Unity 6000.6.9f1, Intel Core Ultra 9 275HX
(24 logical), Windows 11.
- **Project.** `C:\UnityProjects\_aibt_acceptance\P9Samples-6069`. Its manifest pointed at a
  `git archive` copy of each commit; the project's harness and manifest were backed up to
  `P9Samples-6069-backup-before-p10011` and restored afterwards.
- **Players.**

  | Player | AIBT commit | Harness |
  |---|---|---|
  | `Build\P10011` | `9736622` (G3) | P10-011 |
  | `Build\P10011-base` | `14b6799` (G1) | that commit's own, plus the `host-check` scenario list |
  | `Build\P10011-g2` | `5b4ec02` (G2) | that commit's own, plus the `host-check` scenario list |

- **Raw data.** `perf-data/P10-011/raw/`.
- **Tables.** `sim-actions-table.md` (made by `sim_actions.py`) and `host-check-table.md` (made by
  `host_check.py`), both next to the raw data. Faults in every run: 0.

### 5.1 External actions (`sim-actions`)

**Scenarios.** 500 agents, `Spread(5)` (100 scheduled thinks per tick), jobs on, 200 warm-up and
1000 measured ticks.
- **Pairs.** ABBA in each run: B is the G2 `io-*` tree, E is its `act-*` twin.
- **The game** completes every started action 3 ticks later (`TryComplete` with a `PerfJobResult`),
  before that tick's `Step`.
- **Runs.** 3 with 23 job workers, 3 with 0.
- **Values.** The median of the per-run values, min–max in brackets. "Step" is the
  `TreeSimulation.Step` call alone, and bytes are managed allocation inside it.

| workers | scenario | Step µs median | Step µs p99 | bytes/tick | complete µs/tick | events read µs/tick | events/tick | orders/tick |
|---:|---|---:|---:|---:|---:|---:|---:|---:|
| 23 | `sim-io-loop16` (B) | 59.3 (58.6–60.6) | 178.3 (163.6–185.9) | 0 | – | – | 0 | 5.5 |
| 23 | `sim-act-loop16` (E) | 59.6 (58.9–60.5) | 211.6 (203.0–220.8) | 0 | 0.8 | 0.1 | 5.5 | 5.2 |
| 23 | `sim-io-reactive16` (B) | 226.2 (219.6–233.9) | 396.0 (385.6–432.4) | 0 | – | – | 0 | 100 |
| 23 | `sim-act-reactive16` (E) | 444.6 (427.6–472.4) | 690.0 (659.3–708.7) | 0 | 15.3 | 119.9 | 400 | 0 |
| 0 | `sim-io-loop16` (B) | 92.8 (88.4–107.8) | 1322.7 (1280.5–1360.3) | 0 | – | – | 0 | 5.5 |
| 0 | `sim-act-loop16` (E) | 96.7 (89.4–109.5) | 1678.8 (1654.1–1749.1) | 0 | 0.7 | 0.1 | 5.5 | 5.2 |
| 0 | `sim-io-reactive16` (B) | 2589.8 (2533.4–2656.9) | 3126.4 (2954.8–3323.3) | 0 | – | – | 0 | 100 |
| 0 | `sim-act-reactive16` (E) | 5476.8 (5415.7–5517.7) | 6207.6 (5964.1–6402.5) | 0 | 11.9 | 90.7 | 400 | 0 |

| workers | pair | E/B (Step median) | E1/B1 | E2/B2 |
|---:|---|---:|---:|---:|
| 23 | `act-loop16` / `io-loop16` | 1.01 | 1.01 | 1.00 |
| 23 | `act-reactive16` / `io-reactive16` | 1.96 | 2.04 | 1.86 |
| 0 | `act-loop16` / `io-loop16` | 1.04 | 1.10 | 1.00 |
| 0 | `act-reactive16` / `io-reactive16` | 2.11 | 2.15 | 2.09 |

**`act-loop16`, the consume path.**
- Per measured run: 5 800 starts, 5 500 completions, 5 500 consumed with one order each, and no
  cancels.
- The counts and the order checksum are identical with 23 and 0 workers.
- The `Step` median is the same as the G2 twin (1.00–1.04).
- The p99 is higher: +19% with 23 workers, +27% with 0. The ticks where completions wake agents
  add thinks to the scheduled ones.
- The game's side costs 0.8 µs per tick for `TryComplete` and 0.1 µs for reading events.

**`act-reactive16`, finding F2 in numbers.**
- The root reactive sequence restarts the job at every think. Per tick that is 200 starts, 200
  `Preempted` cancels and 100 completions, of which none are consumed and no orders result.
- 200 starts per tick with 100 scheduled thinks means about 200 thinks per tick: every completion
  wakes its agent for one more think, which then cancels and restarts.
- `Step` is 1.96× (23 workers) and 2.11× (0 workers) the G2 twin. Per think that is about the same:
  2.2 µs against 2.3 µs with 23 workers. So the doubling comes from the extra wake-ups, not from the
  action machinery.
- Reading 400 events per tick costs the game 120 µs. That is the benchmark's own loop: it looks up
  pending entries linearly to drop them on a cancel. It is outside `Step`.

**Allocation.** 0 bytes per tick inside `Step` in every scenario, and 0 bytes in the GC window.

### 5.2 Host check (`host-check`)

**Scenarios.** `NativeTreeAgent`, default profile (agent-frame job from 64 agents), `shared-loop16`
and `shared-reactive16` at N = 500 and 2 000.
- **Runs.** Two ABBA blocks, each Player run doing 2 runs, so 4+4 runs per side:
  - G1 block: base, G3, G3, base.
  - G2 block: G2, G3, G3, G2.
- **Values.** The median of the per-run medians of the frame bracket and of the scheduler drive.

| block | tree | N | base bracket µs | G3 bracket µs | G3/base | G3/base drive |
|---|---|---:|---:|---:|---:|---:|
| G1 `14b6799` | `shared-loop16` | 500 | 412.8 (398.3–422.8) | 431.2 (421.8–442.2) | 1.045 | 1.083 |
| G1 `14b6799` | `shared-loop16` | 2000 | 1811.4 (1750.4–2003.0) | 1865.7 (1860.8–1909.0) | 1.030 | 1.042 |
| G1 `14b6799` | `shared-reactive16` | 500 | 2342.2 (2300.2–2523.6) | 2396.4 (2351.6–2441.4) | 1.023 | 1.031 |
| G1 `14b6799` | `shared-reactive16` | 2000 | 9264.5 (9059.3–9624.7) | 9703.1 (9515.1–10059.7) | 1.047 | 1.047 |
| G2 `5b4ec02` | `shared-loop16` | 500 | 413.3 (389.9–428.2) | 413.1 (389.6–437.5) | 0.999 | 1.006 |
| G2 `5b4ec02` | `shared-loop16` | 2000 | 1940.0 (1886.9–2001.8) | 1836.2 (1795.9–1915.9) | 0.946 | 0.959 |
| G2 `5b4ec02` | `shared-reactive16` | 500 | 2374.4 (2332.4–2414.1) | 2381.8 (2370.3–2413.9) | 1.003 | 1.010 |
| G2 `5b4ec02` | `shared-reactive16` | 2000 | 9791.9 (9722.7–9991.7) | 9703.9 (9629.8–9806.3) | 0.991 | 1.014 |

**Reading.**
- **G3 against G2.** No measurable difference for the hosts: 0.95–1.00 for the bracket and
  0.96–1.01 for the drive, with overlapping ranges.
- **G3 against G1.** 2–5% slower. The G2 block does not reproduce that, and the same Player varies
  as much between blocks (G3 `shared-loop16` N = 2000: 1866 vs 1836 µs; the bases 1811 vs 1940 µs).
  So the G1 gap is within this session's drift. If any of it is real, it came from G2 (P10-010), not
  from G3.
- **Host paths G3 touches.** In the adapter and the agent-frame core, G3 adds a check of the
  container's flag (`simulation != null` / `SimulationMode`) only on a failed callback. It also
  rebases the workspace operation floor, but only for container agents.

## 6. Deviations from the ADR

1. **Cancel reasons.** The ADR listed `Preempted, Timeout, AgentStopped, Fault, …`; G3 uses
   `Preempted=0, Timeout=1, NodeEnded=2, Fault=3, AgentRemoved=4, AgentRestarted=5, GroupRemoved=6`.
   The D4 case (node ended) and the three teardown causes are distinguished instead of one
   "agent stopped".
2. **`ActionEvent.FaultCancel`** (not in the ADR) tells the game whether the cancel value came from
   the node's Abort or is the start's fault-cancel value.
3. **`AlreadyCompleted`** (not in the ADR) separates a second completion from a stale handle.
4. **Polling policy.** `TryConsume` before the completion returns `StaleCompletion`, which the
   dispatch treats as a failed frame (`burst-node-abi-v1.md`: such failures "fault the affected
   atomic step according to host policy"). The container's policy: a Tick dispatch whose callback
   failed exactly with `StaleCompletion` becomes Running with nothing published. Limitation: the rest
   of that frame is dropped, so a node must make early exits (time-outs) before polling.
5. **No short forms.** `[AibtExternalAction]`/`[AibtActionResult]` were not added; nodes use the
   long-form binding attributes.
6. **`Unsupported` is no longer returned** by the container (kept in the enum).
7. **A completion without a value** (`TryComplete(handle, outcome)`) delivers zeros to every
   completion binding of the node.

## 7. Findings

### F1. A runaway tree is stopped, but the default limit lets it hold `Step` for about a second per agent

**What happened.** The first Integration run of these tests did not return: the Modules Editor
spent 35+ minutes at 100% of one core inside the test run. The cause was the first version of the
test tree `CrewTree`:

```text
repeater(count 4e9) → memory-sequence [ reactive-selector [alarm, job], wait(ticks: 1) ]
```

`aibt.stdlib.wait` with `ticks: 1` succeeds on its first tick. So for an agent whose alarm is up, the
alarm and the hold both succeed at once and the repeater repeats inside the same think. With wake
on changed input, every crew agent thinks as soon as its alarm rises. Over 300 ticks nearly every
crew agent sees an alarm: P(no alarm in 37 eight-tick windows) = 0.8^37 ≈ 0.

**Why `MaximumStepsPerThink` did not stop it in time.**
- **The limit is enforced and was in effect.** The test settings use
  `TreeSimulationSettings.Default`. That sets `MaximumStepsPerThink =
  ProductionTreeHost.DefaultMaximumStepsPerUpdate` = **1 000 000**, which ADR-P10-008 (G1) fixes
  as the container default.
- **Both paths count steps.**
  - In place: `ProductionTreeAgentCore.RunSegment` → `TryCountStep`.
  - The agent-frame job: `PrepareAgentFrame` copies the limit into `AgentFrameAgentV1.MaximumSteps`.
  - When the limit is reached, the agent faults with `NativeUpdateStepLimitExceeded`, is recorded
    as a fault and stops (`TreeGroup.Think`: `!core.Ready` → skipped).
- **The limit is per agent and per think, not per `Step`.** A runaway agent spends a million
  lifecycle steps before it faults, each with an alarm dispatch.
  - At an estimated 1 µs per step (Editor; **not measured**), that is about 1 s of `Step` per
    runaway agent.
  - The test suite created roughly 2 000 of them: 6 population runs × (260 crew + about 70
    restarted or re-added), plus the single-agent crew tests and the allocation test.
  - So the run was bounded but would have taken on the order of 30–60 minutes. It is consistent
    with what was seen; it was not observed to the end.
  - Every one of those agents would then have been reported as a fault, failing the tests.

**Fix in the tests.** Both hold waits in `CrewTree` and `ShiftTree` are now `ticks: 2`, which
yields. Every other new tree was re-read for the same pattern:
- **Composites over nodes that can succeed or fail at once:**
  - `ShiftTree`: fire-and-forget job → `wait 2` → patient job.
  - `SingleJobTree`, `ImpatientTree`, `DoubleForgetTree`: no repeater.
  - The benchmark trees `act-reactive16` and `act-loop16`: the repeater's job starts a new action
    and returns Running.
- **None repeats inside one think.** In each loop at least one child returns Running before the
  repeater can come round again.
- **The job node's instant failures are faults, not Failure.** A failed `TryStart` (capacity, per
  think start limit), `TryEmit` or input read latches the callback result, so the agent faults
  (`ActionCapacity_Overflow_…` pins `CapacityExceeded`); no repeat follows.

**New test.** `RunawayTree_FaultsTheAgentAtTheStepLimit_AndCancelsItsLiveAction` (×2, in place and
job) uses an explicit limit of 1000. The `DefaultStepLimit_IsTheHostDefault` test pins today's
default.

**Proposal (owner's decision; not implemented, G1 behaviour unchanged).**
- **The problem.** A container steps many agents per tick, so its worst case is (runaway agents in
  that `Step`) × limit × cost per step. The host default (1 000 000) was chosen for one host and one
  frame (`ProductionTreeHost.DefaultMaximumStepsPerUpdate`: "the largest tree measured … needs a
  few hundred lifecycle steps per update").
- **Proposed change.** Lower `TreeSimulationSettings.Default`'s `MaximumStepsPerThink` to
  **10 000**. That is still more than ten times the largest measured need, and it caps a runaway
  agent at about 10 ms instead of about 1 s.
  - Hosts keep 1 000 000.
  - It changes the G1 default, so it needs an ADR-P10-008 amendment and a CHANGELOG line.
  - The container is experimental until G5.
- **Alternative.** A per-`Step` total step budget across agents. This is new API and a larger
  change, so it is better left to G4/G5 if wanted.
- **Not affected.** Explicit settings are unaffected either way.

### F2. Under a reactive composite an action never completes

**What the first run showed.** The test meant to pin "completion and preemption in the same step:
preemption wins" failed. After step 2, `TryComplete` returned `StaleAction`. The action had already
been cancelled (`Preempted`) in step 2 and a new one started, although the alarm was down.

**Why.** This is the normative rule, not a G3 bug. `execution-semantics-v1.md`, "Reactive selector":
"If it has a running child from the previous update, that subtree is aborted and exited before
reevaluation begins." Reactive sequences behave the same way.

**Consequences for external actions:**
- An action node anywhere below a reactive composite is cancelled and started again at every think.
- The game's completion wakes the agent, and that very think aborts the node before it can read the
  completion. So such an action is never consumed.
- This combines requirement 7 (reactive interrupts, by re-evaluation at every think) and requirement 6
  (long actions) badly.
- **Docs.** `simulation-container.md` ("Not under a reactive composite") and the CHANGELOG now say to
  put long actions under memory composites.

**Tests.** The test is now
`UnderAReactiveSelector_TheJobRestartsEveryThink_AndACompletionIsNeverConsumed`. It pins both the
restart in every think and preemption winning over a same-step completion. The other crew tests
were re-read:
- `ReactiveInterrupt_…` asserts a single `Cancelled` and no new `Started` at the interrupt, so it
  still shows that the alarm, not the restart, ended the cycle.
- The population run is self-consistent either way: its ledger counts every restart's `Cancelled`.

**Benchmark.** `act-reactive16` has the same shape: the job sits under the root reactive sequence.
- What it measures is a start and a `Preempted` cancel at every think, plus the completion and the
  wake-up think it causes.
- Its completions are never consumed, so it gives no orders.
- `act-loop16` (memory composites) measures the consume path: start, poll, complete, order.
- The benchmark README says so.

**For G4 (owner's decision).** A long action that a higher-priority branch should interrupt needs an
observer abort, which does not re-enter the running branch.
- `NativeLifecycleMachineV1.TryApplyObserverTransition` exists, but has no production caller
  (ADR-P10-008, requirement 7 "partial").
- The alternative is a memory composite with an explicit check.
- Both are outside G3.

## 8. What G4 must know

- The ledger lives in `SimulationAgentNativeV1` (`Actions`, `ActionPayload`, `Events`,
  `EventPayload`); it is rebuilt into the dispatch slot before each dispatch and folded back after.
  A trace channel can read the same records.
- The dispatch slot's operation table is not persistent in the container (rebuilt per dispatch);
  the workspace floor is lowered each time through an internal method.
- Event and payload storage are sized from `ActionCapacityPerAgent` and the program's largest
  start, cancel and completion values, so new event kinds need a new capacity rule.
