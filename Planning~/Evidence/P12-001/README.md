# P12-001 evidence — Durations in ticks and reactive interrupts in the simulation container

Branch `feature/aibt-consumer-workflow`, base `fb7eb89`. Unity 6000.6.9f1, the Modules Editor (Pipeline port
7810). Brief: `C:\UnityProjects\_aibt_acceptance\SweetKingdom\brief-P12-001.md`; decisions 3 and 4 of
`plan-2026-11-24.md` in the same folder. Raw files: `C:\UnityProjects\_aibt_acceptance\P12-001\`. Only the
touched test classes were run, not the ten assemblies. Status: `Review` (acceptance is the orchestrator's).

No protected file changed: no ABI contract, compiled program format, `CodeGen~`, `package.json`,
`Tests/Fixtures`, `Documentation~/specifications`, execution semantics or public signature. No runtime code
changed at all; the work is tests, a test fixture shard, documents and planning records.

## What changed

| Path | What |
| --- | --- |
| `Tests/Integration/Simulation/SimulationTickDurationTests.cs` | durations in ticks, both recipes, Spread(5), limits, `aibt.stdlib.wait` |
| `Tests/Integration/Simulation/TreeSimulationReactivePatternTests.cs` | the consumer's reactive pattern, its three conditions, the retaining sequence, `TryWakeMany`; catalog set `ReactivePatternCatalog` |
| `Tests/Integration/Simulation/TreeSimulationWakeCostBenchmark.cs` | the wake-cost measurement (opt-in, ignored in a normal run) |
| `Tests/Editor/CodeGen/GenerationP12001/` | new test-only fixture assembly `AIBT.CodeGen.P12001ReactivePattern.Tests` (see below) |
| `Tests/Integration/AIBT.Integration.Tests.asmdef` | one added reference to that assembly, nothing else |
| `Documentation~/simulation-container.md` | sections "Durations in ticks" and "Reactive interrupts" |
| `Documentation~/native-node-library.md` | two sentences that said or suggested `aibt.stdlib.wait` counts ticks |
| `CHANGELOG.md` | one line |
| `Planning~/` | P12-001…P12-003 cards and work items, phase-12 paragraph and candidates in `MASTER_PLAN.md`, this file |

**The fixture assembly.** `Tests/Editor/CodeGen/GenerationP12001/` holds one asmdef and one source file, built the
same way as the existing `GenerationP10010` and `GenerationP10011` fixtures: an `[AibtCatalogShard]`
(`aibt.tests.p12001.reactive-pattern`) whose generated code comes from the existing analyzer
(`Analyzers/AIBT.CodeGen.dll`, unchanged) at compile time. It is Editor-only and a test assembly
(`optionalUnityReferences: TestAssemblies`), so it never reaches a consumer build. It has the nodes the
consumer's pattern needs and no existing fixture had: a trigger condition reading a one-field `TriggerRow`
through `[AibtInput("trigger")]`, and a walk action (an external action, so that a preemption shows as its
`Cancelled (Preempted)` event) reading a 16-byte `PositionRow` through `[AibtInput("position")]` at every
think. No existing file under `Tests/Editor/CodeGen` changed: the contract file
`Tests/Editor/CodeGen/Contracts/ExpectedPublicAbiV2.txt` and every other fixture are untouched (`git status`
lists only the new folder there). `AIBT.Integration.Tests.asmdef` changed only to reference the new assembly.

## 1. Durations in ticks: what is pinned, by which test

`SimulationTickDurationTests` (105 cases, all green). Every probe tree emits `aibt.native.signal` 1 when its
timer starts and 2 when it ends (the set-deadline and cooldown probes emit 7 each time they fire), then holds;
every case runs in place and in the agent-frame job (`MinimumJobBatch = 1`, job batches asserted) and the two
runs must give the same signals on the same ticks and the same thinks.

Time nodes and decorators covered (every node or decorator with a time parameter in the package library):
`aibt.native.wait-microseconds`, `aibt.native.wait-until-true` (`timeout-microseconds`),
`aibt.native.set-deadline` with `aibt.native.deadline-passed`, `aibt.native.wait-random-microseconds`,
`aibt.core.timeout`, `aibt.core.cooldown` (start policies `on-enter` and `on-successful-exit`). Searched for
others: the only other code that reads `TryGetTimeMicroseconds` is the Swarm Arena sample's own nodes
(`Samples~/SwarmArena/Runtime/Nodes/SwarmArenaNodes.cs`), which are not library nodes; the managed
`aibt.unity.*` nodes do not run in the container.

| Claim | Test | Cases |
| --- | --- | --- |
| (a) `d = n × 33333` ends exactly n ticks after it started, every agent thinking every tick; (b) the same with `TickMicroseconds = 1`, `d = n`; n = 1, 7, 10 | `ExactMultipleOfTickMicroseconds_EndsAfterExactlyNTicks_EveryThink` | 7 probes × 3 n × 2 recipes = 42 |
| a non-multiple rounds up: `7T − 1` → 7, `7T + 1` → 8, `1 000 000` → 31, `999 990` → 30 (T = 33333) | `NonMultiple_RoundsUpToTheNextTick` | 7 |
| with `Spread(5)` a duration ends at the first think at or after start + n: never earlier, at most 4 late (n = 1 → 5, 7 → 10, 10 → 10); no off-schedule think; five distinct phases | `UnderSpread5_EndsAtTheFirstThinkAtOrAfterTheDeadline` | 42 |
| a random wait over `[3T, 12T]` (or `[3, 12]` at T = 1) ends 3 to 12 ticks later for 24 agents, draws differ | `WaitRandomRange_EndsInsideTheRangeInTicks` | 2 |
| UInt32 limit: at 30 Hz 128 850 ticks (`4 294 957 050`) is exact; at T = 1 the full 4 294 967 295 ticks (tick 0 → `uint.MaxValue`) | `LargestUInt32Duration_IsExactInBothRecipes` | 4 (wait, wait-until-true, set-deadline, random) |
| a UInt32 parameter of 4 294 967 296 does not compile | `NativeDurationAboveUInt32_DoesNotCompile` | 1 |
| UInt64 (timeout, cooldown): 4 294 967 294 ticks exact at 30 Hz and at T = 1; `durationMicroseconds = long.MaxValue` faults the agent at activation (now + d overflows Int64) | `LargeUInt64Duration_IsExact_AndADeadlineBeyondInt64FaultsTheAgent` | 2 |
| `aibt.stdlib.wait` `ticks: 6` ends on its 6th think, the entering think included: 5 ticks later every tick, 25 under `Spread(5)`, both recipes | `StdlibWait_CountsThinks_NotTicks` | 4 |
| an extra `TryWake` shortens `aibt.stdlib.wait` (`ticks: 3` ends at 10 instead of 15) but not a microsecond wait (10 ticks still ends at 15) | `StdlibWait_IsShortenedByAnExtraWake_AMicrosecondWaitIsNot` | 1 |

Corrections to earlier records found on the way:

- **The UInt32 limit at 30 Hz is 128 850 ticks, not 128 849** (`requests-fact-check.md` §3(a)):
  `floor(4 294 967 295 / 33 333) = 128 850` and `128 850 × 33 333 = 4 294 957 050 ≤ 4 294 967 295`; pinned by the test.
- **`aibt.stdlib.wait` with `ticks: 6` under `Spread(5)` is 25 ticks, not "about 30"** (the reply of 2026-11-24,
  point 3): the think that enters it counts as the first, so the sixth think is 25 ticks after the first. In
  general `ticks: k` ends `(k − 1) × interval` ticks after it started when nothing wakes the agent.

Run: `Invoke-UnityTests.ps1 -Filter SimulationTickDurationTests` → 105/105. Mutation check (reverted): expecting
`n − 1` instead of `n` made all 42 `ExactMultiple…` cases fail with "Expected: n − 1 But was: n".

## 2. Displays of simulation time with `TickMicroseconds = 1`

A read-only search of `Runtime/`, `Editor/`, `MCP/`, `MCP~/`, `Authoring/` and the documents (a subagent's
read, checked against the cited lines for the container entries). **Nothing breaks and no display shows a wrong
value: no display anywhere shows the container's clock (`tick × TickMicroseconds`).** The clock exists only as
the local `time` in `TreeSimulation.Step` (`Runtime/Simulation/TreeSimulation.cs:156`), passed to
`TreeGroup.Think` and on to the nodes' `TryGetTimeMicroseconds`; nothing reads it back for display.

| Where | What it shows | Verdict with T = 1 |
| --- | --- | --- |
| `Runtime/Simulation/TreeGroup.Debug.cs:303-311` (`DescribeAgent`) | updates, "Thinks every N tick(s) at phase P", "last think at tick N", "next think at tick N" | fine (ticks) |
| `TreeGroup.Debug.cs:391` (trace summary) | "… at tick N" from `AgentTraceRecord.Tick` | fine (ticks) |
| `Runtime/Simulation/SimulationDebugTypes.cs` (`AgentTraceRecord`, `AgentDebugState`) | `Tick`, `LastThinkTick`, `NextThinkTick`; no time field | fine |
| `Runtime/Trace/Native/NativeTraceContractsV1.cs:66-93` (`NativeTraceRecordV1`) | no time field | fine |
| `MCP/McpSimulationTools.cs:61-65, 95, 159` (`describe_agent`, `read_agent_trace`) | schedule, last/next think tick, trace event tick | fine (ticks) |
| `MCP/McpSimulationTools.cs:241-274` (`runtime_simulations`) | `lastTick`, `tickMicroseconds` (the setting itself) | fine: reports 1 truthfully |
| `MCP~/Server/RuntimeTools.cs`, `MCP/McpOperationCatalog.cs:53-55`, `MCP~/Cli/Program.cs` | tool descriptions "with ticks"; CLI `agent describe` / `agent trace` | fine |
| MCP/CLI `simulate` (`MCP/Verification/McpVerificationToolDispatcher.cs:158-210`) | reference executor only, caller-supplied time, not echoed; cannot run a container | not reachable from the container |
| `Runtime/Integration/ProductionTreeAgentCore.cs:678-687` (clock error log) | host clock error | not reachable: container cores have no log context (`TreeGroup.cs:238`) |
| Editor (`Editor/Graph/*`, `Editor/Trace/TraceTimelineWindow.cs`) | refresh/animation seconds from `EditorApplication.timeSinceStartup` | fine: the Editor does not reference `TreeGroup` or `TreeSimulation` at all |
| Stopwatch timings (`ProductionTreeScheduler`, `AgentFrameCoreV1.cs:835`, `McpTestingJson.cs:47`) | measured CPU µs | fine (real time, not the clock) |

**What misleads (text only, none shows a value):**

1. Native timing node descriptions say "host time": `Authoring/Registry/Generated/StandardNative/StandardNativeNodes.cs:139,141`
   (`wait-microseconds`, which also says "`aibt.stdlib.wait` counts ticks instead"),
   `StandardNativeTimingNodes.cs:29,74,106`, `StandardNativeRandomNodes.cs:152,155` (which also says "to count
   ticks rather than host time use `aibt.stdlib.wait`").
2. `aibt.core.timeout` / `cooldown` manifest examples "After one second …" / "… one second after child entry" with
   `1000000`: `Authoring/Model/Nodes/BuiltInNodeManifests.cs:242,268` and the same text in
   `Runtime/Nodes/Contracts/RuntimeBuiltInCatalogAuthority.cs:92,730`.
3. `aibt.stdlib.wait`'s description says timeout/cooldown give "a wall-clock duration":
   `Authoring/Model/Nodes/BuiltInLeafManifests.cs:36-37` and the shard attribute in
   `Authoring/Registry/Generated/BuiltInLeaves/Runtime/BuiltInLeafNodes.cs:22-26`.
4. The recipe generator: "microseconds of host time" and "2 s / 3 s / 1 to 2 s" examples
   (`MCP/Documentation/McpRecipesDocumentGenerator.cs:122,170,177,189,190,203`).
5. The generated copies of 1-4 in `Documentation~/generated/node-catalog.md` and `recipes.md`.
6. Mild: the Graph inspector labels parameters with their names (`duration-microseconds`, …;
   `Editor/Graph/BehaviorTreeGraphWindow.Inspector.cs:147`). The names are contract identifiers, not labels.

**Not fixed here, and why.** Items 1-3 are node descriptions carried in the generated shard metadata and in the
built-in catalog authority, whose content feeds the shard descriptor and registry hashes; changing them is not
display-only (it may move catalog hashes and the generated documents, and the built-in catalog authority must
change in step with the manifests). I did not verify which hashes move, so I left them; they are a small follow-up
that needs that check first. Items 4-5 follow from 1-3 through regeneration. Item 6 would need a label or tooltip
layer in the inspector. The consumer documentation now says the "microseconds" names read as ticks with
`TickMicroseconds = 1` only through the recipe in "Durations in ticks"; the two sentences of
`Documentation~/native-node-library.md` that said `aibt.stdlib.wait` counts ticks were corrected.

## 3. Reactive interrupts: what is pinned, by which test

`TreeSimulationReactivePatternTests` (7 tests, all green). The consumer's pattern exactly: `loop: repeater >
top: aibt.core.reactive-selector-retaining [alert, walk]`, `alert = memory-sequence [triggered, flee
(signal 9), pause (3 ticks)]`; `triggered` is a Burst condition reading the trigger channel through
`[AibtInput]`; `walk` is an external action reading the 16-byte position row at every think. Two channels:
`trigger` (one Bool per agent, wake on) and `position` (16 bytes, no wake, every row rewritten before every
step). Ten agents, `Spread(5)`. Each scenario runs in place and in the job (job batches asserted) and the two
logs (every think with its reason, every action event, every signal, every fault, wake-by-input count) must be
equal.

| Claim | Test |
| --- | --- |
| positions rewritten every tick wake no one: for 30 ticks every think line equals the schedule, each walk starts once, no cancel, no signal, no wake by input | `PositionsRewrittenEveryTick_WakeNoOne_ThinksFollowTheSchedule` |
| a trigger written before `Step(13)` for agent 2 (next scheduled think 17) preempts inside `Step(13)`: the tick's log is exactly the scheduled thinks of agents 3 and 8 plus `think 2 off-schedule InputChanged`, `Cancelled 2 Preempted`, `signal 2 9`, one wake by input | `TriggerWrittenBeforeStep_PreemptsTheWalkInsideThatStep` |
| the same arrangement with `aibt.core.reactive-sequence-retaining` (`calm = inverter [triggered]` guards the walk; a memory selector runs the alert): the same log at 13 | `RetainingSequence_TriggerWrittenBeforeStep_PreemptsInsideThatStep` |
| `TryWakeMany` with wake off on every channel: `think 2 off-schedule WokenByGame`, `Cancelled 2 Preempted`, `signal 2 9` at 13; without the call nothing happens at 13 and the preemption is at 17 | `TryWakeMany_WithoutWakeOnChange_PreemptsInsideThatStep` |
| condition 1: not woken before the agent thought since it was added (trigger before tick 1: agent 0 first thinks at 5, where its alert runs first) or since wake was enabled (enabled before 8 with agent 0 raised: preempted only at 10; agent 1, which thought at 11, raised before 13: preempted inside 13) | `NotWoken_BeforeTheAgentThoughtSinceItWasAdded_OrSinceWakeWasEnabled` |
| condition 2: a guard after the running child is not a guard: the changed row wakes agent 2 at 13, but nothing is cancelled and no signal | `GuardAfterTheRunningChild_DoesNotPreempt` |
| condition 3: raised and lowered before the same step: not seen (no wake, no cancel); raised before 13 and lowered before 14: seen at 13; the same without wake: never seen | `RiseAndFallBetweenTwoSteps_IsNotSeen` |

The facts of the brief held: the pattern preempts in the same step, only the woken agent thinks off schedule,
and the three conditions are as stated. Run: `-Filter TreeSimulationReactivePatternTests` → 7/7. Mutation check
(reverted): with wake off in the second test, its tick-13 log was only the two scheduled thinks and the test
failed.

## 4. Documents

- `Documentation~/simulation-container.md`: new sections **"Durations in ticks"** (both recipes, the rounding of
  non-multiples, the next-think rounding under `Spread`, the limits, `aibt.stdlib.wait` counts thinks) and
  **"Reactive interrupts"** (the pattern, the latency, wake is per channel so the trigger gets its own channel,
  the retaining sequence, `TryWakeMany`, the three conditions, the measured cost). Every statement names the test
  that backs it, except the cost paragraph, which cites this measurement.
- `Documentation~/native-node-library.md`: the table row of `aibt.unity.wait-seconds` said "`aibt.stdlib.wait`
  counts ticks"; it now says it counts the agent's own updates (thinks in `TreeSimulation`), not time. The line
  "`wait-frames` (use `aibt.stdlib.wait`)" now says it counts the agent's updates.
- Not changed (see section 2): the node descriptions in generated shard metadata that say "host time" or that
  `aibt.stdlib.wait` "counts ticks"; `Samples~/ShowcaseTrees/README.md` already says "ticks are logical updates".
- `CHANGELOG.md`: one line under `[Unreleased]`.

## 5. Measurement: what wake on change costs at 500 agents

**Method.** `TreeSimulationWakeCostBenchmark.WakeOnChangeCost_500Agents_Spread5`, an EditMode test that is
ignored unless the Editor process has the environment variable `AIBT_P12001_BENCHMARK=1` (the Unity test pipeline
here does not run `[Explicit]` tests: tried, 0/0 by name and by category). The variable was set with a one-line
`unity command eval` before the runs and cleared after. 500 agents with `Spread(5)` (100 due per step), one
step per tick, `TickMicroseconds = 33333`, the tree of section 3 (`SelectorTree`), trigger rows never changing.
Variants: **A** no wake on any channel; **B** wake on the trigger channel; **C** as B with every position row
rewritten before every step (no wake on positions); **D** wake on the positions channel, rewritten before every
step (every agent wakes every step). Only `Step` is timed (`Stopwatch`), not the game's row writes. Eight
long-lived simulations (4 variants × in place / jobs, `MinimumJobBatch = 1`); 7 rounds, each round running A B C
D for both paths with the path order alternating; 60 warm-up and 300 measured steps per block; per block the
median step time; per variant the median of the 7 round medians and their range; differences paired inside each
round. A difference whose range across rounds contains 0 is "not confirmed". The whole run was done twice.
Within a path the variants always ran in the order A, B, C, D (only the path order alternates), so an order
effect inside a path is not excluded; B − A is about 45 % of the A step with a tight range over 14 rounds and C − B is
zero, which makes it unlikely but not proven.

**Where.** The Unity Editor 6000.6.9f1 (not a Player), Burst compilation on, **Burst safety checks on, jobs
debugger on**, 23 job workers, 24 logical processors. In the Editor the agent-frame job runs Burst-compiled
code and the in-place path runs managed code (P11-014 U3, `BurstExecutionPathTests`), so the in-place numbers are
an Editor artefact and not comparable with a Player. The existing `sim-*` Player harness
(`Benchmarks~/Phase10/PlayerPerf`) needs a separate consumer project and an IL2CPP build; it was not used.

**Machine load.** Busy: two foreign Editors were open (a Unity 2022.3 project, `C:\Work\clm-client`, with the most
CPU time of any process, and the SweetKingdom 7000.0.0a8 Editor), plus Docker and other apps
(`load-processes-before-run1.txt`, `load-processes-before-run2.txt`). Total CPU sampled every 2 s:
during the first 3.5 minutes of run 1 (23:29:45–23:33:21 local, 108 samples) median 13.9 %, 90th percentile
21.4 %, min 6.8 %, max 30.4 % (`load-cpu-run1.csv`); during the last 2.3 minutes of run 1 (23:39:53–23:42:11, 69 samples) median 13.2 %,
max 18.6 %; during run 2 (23:43:11–23:55:49, 378 samples) median 15.1 %, 90th percentile 22.5 %, min 4.4 %, max
47.8 % (`load-cpu-run1b.csv`). No samples exist for 23:33:21–23:39:53 (the first sampler had ended; the second
stopped on a counter error at 00:03, after both runs).

**Results** (µs per step; median of round medians [min, max]; raw:
`C:\UnityProjects\_aibt_acceptance\P12-001\run1-20261124-192955\` and `run2-20261124-194321\`, each with
`steps.csv`, `environment.json`, `summary.md`, `summary.json`, the test output; `analyze.py` recomputes them):

| Path | Variant | Run 1 | Run 2 | Thinks per step | Woken by input per step |
| --- | --- | --- | --- | --- | --- |
| jobs | A | 326.8 [313.3, 360.7] | 327.2 [321.6, 345.9] | 100 | 0 |
| jobs | B | 480.5 [466.6, 503.0] | 480.0 [464.6, 497.9] | 100 | 0 |
| jobs | C | 478.4 [466.9, 500.1] | 480.4 [465.4, 522.5] | 100 | 0 |
| jobs | D | 2801.8 [2748.9, 2839.6] | 2770.0 [2724.9, 2862.3] | 500 | 400 |
| in place | A | 36676 [31248, 38416] | 36166 [30423, 38001] | 100 | 0 |
| in place | B | 34336 [30293, 42149] | 36274 [29998, 39863] | 100 | 0 |
| in place | C | 35217 [29489, 42032] | 35151 [32140, 41870] | 100 | 0 |
| in place | D | 179634 [162062, 186042] | 178729 [173187, 194744] | 500 | 400 |

| Path | Difference (paired per round) | Run 1 | Run 2 | Verdict |
| --- | --- | --- | --- | --- |
| jobs | B − A (the trigger comparison) | +148.3 [137.4, 165.0] | +151.0 [143.0, 171.5] | confirmed, both runs |
| jobs | C − B (positions rewritten, no wake) | −0.2 [−15.3, 9.5] | +0.7 [−8.0, 24.9] | not confirmed (no measurable cost) |
| jobs | D − B (everyone wakes) | +2321.4 [2280.0, 2344.8] | +2293.9 [2253.2, 2371.2] | confirmed, both runs |
| in place | B − A | −954.9 [−4190.7, 8336.5] | −910.8 [−3894.2, 9440.4] | not confirmed (inside the spread) |
| in place | C − B | −532.7 [−7551.2, 3484.1] | +1025.4 [−5494.0, 4281.1] | not confirmed |
| in place | D − B | +145783.5 [126339.8, 146502.0] | +144791.8 [139329.6, 154880.9] | confirmed, both runs |

**Reading.**

- **The comparison itself (B − A) costs about 150 µs per step on the jobs path**, on a 327 µs step: the 400
  agents that are not due have their trigger row canonicalised and compared on the calling thread before the
  job (`TreeGroup.Think` → `InputChanged`), about 0.37 µs per agent in this Editor configuration. It grows with
  the number of agents that are not due and with the number of wake channels, not with the row changes. On the
  in-place path the same cost is hidden in a 36 ms managed step and not confirmed.
- **Rewriting a channel without wake costs nothing measurable (C − B)**: positions on their own channel do
  not add to the wake cost, which is the reason for the dedicated trigger channel.
- **The extra thinks dominate the worst case (D)**: wake on the rewritten positions channel makes all 500
  agents think at every step (400 woken by input per step): 2.8 ms instead of 0.48 ms per step in jobs, and
  about 5 times the in-place step. That is the cost the dedicated trigger channel avoids.
- These are Editor numbers with safety checks and the jobs debugger on, on a busy machine; the ratios, not the
  absolute values, are the result. A Player measurement was not done (open, below); the 0.4 µs per compared
  agent is Editor code with safety checks.

**About the two runs.** Run 1 finished and wrote all 16 800 rows, but the pipeline marked it failed: NUnit's
default 180 s timeout applies to an EditMode test and the run took about 12.5 minutes. The test then got
`Timeout(3_600_000)` and run 2 was started as the planned repeat; it passed 1/1. Run 1 was not hung and nothing was
killed. Both runs give the same picture; the table shows both.

## Verification

| Command | Result |
| --- | --- |
| `Tools/Unity/Invoke-UnityRecompile.ps1`, then the Console | compiled, no errors from the new files |
| `Tools/Unity/Invoke-UnityTests.ps1 -Filter SimulationTickDurationTests` | 105/105 passed |
| `Tools/Unity/Invoke-UnityTests.ps1 -Filter TreeSimulationReactivePatternTests` | 7/7 passed |
| `Tools/Unity/Invoke-UnityTests.ps1 -Filter TreeSimulationWakeCostBenchmark` with `AIBT_P12001_BENCHMARK=1` | run 2: 1/1 passed (run 1: data complete, marked failed by the 180 s timeout, see above) |
| `Tools~/Verification/Verify-Static.ps1` | passed (200 work items, 7 schemas) |

Acceptance full run on 85bd41a, 2026-11-25 00:06–00:24: Runtime 821/821, Integration 716/717 (1 ignored: the
benchmark), Editor 917/917, BehaviorCases 65/65, NativeBurstDispatch 79/79, NativeTreeAgentBlackboard 33/33,
NativeSharedBlackboard 36/36, CodeGen.ContractTests 6/6, CodeGen.GenerationTests 14/14, PlayMode 2/2; noise
unknown-command=0, main-thread-timeout=13 (unchanged); raw:
`C:\UnityProjects\_aibt_acceptance\P12-001\acceptance\full-run-85bd41a.txt`.

## Review fixes round 1

From the independent review (`C:\UnityProjects\_aibt_acceptance\P12-001\acceptance\independent-review.md`,
S1–S5, N3, N7), on the orchestrator's request of 2026-11-25:

- **S1, limits of `aibt.core.timeout` / `cooldown`, and 0.** New test
  `TimeoutAndCooldown_ZeroOrAboveInt64_AreRefused` (9 cases: timeout, cooldown on-enter, cooldown
  on-successful-exit × 0, `long.MaxValue + 1`, `ulong.MaxValue`; in place and in jobs). It walks the consumer's
  path (read the tree JSON, compile, `TryCreateProgram`, `TryAddGroup`, `TryAdd`, `Step`) and names the first
  stage that refuses. **Neither value reaches the container.** 0 is a compile error, `AIBT2028` (parameter
  constraint). A value above `long.MaxValue` is not even read: `CanonicalTreeJson.ReadSemanticValue` throws
  `InvalidCastException` (Newtonsoft reads the number as a `BigInteger`; the reader's `UInt64` fallback catches
  only `OverflowException` and `FormatException`, `Authoring/Serialization/Json/CanonicalTreeJson.cs:504-509`).
  That is an exception where a diagnostic was intended, an authoring bug outside this item; not fixed here.
  The runtime decoders' refusal (`NativeDecoratorPolicyV1.cs:71,83`) is therefore not reachable from a tree
  file. New test `ZeroDuration_InTheNativeTimeNodes` (4 cases, in place and in jobs): `wait-microseconds` 0 and
  `wait-random-microseconds` `min = max = 0` succeed on the entering think; `wait-until-true` timeout 0 means no
  timeout; `set-deadline` 0 writes the current time, so the probe that re-arms it fires at every think.
- **N3.** The `TickMicroseconds = 1` maximum of 4 294 967 295 ticks holds only for a timer started at tick 0
  (the tick counter ends at `uint.MaxValue`); said in the Limits bullet and the test comment.
- **S2.** New test `PositionsWithWakeOn_RewrittenEveryTick_WakeEveryAgentEveryTick` (`NewRig(positionWake:
  true)`): every agent that has thought once thinks at every tick, off schedule with `InputChanged`, and the
  woken-by-input count per tick equals those thinks; nothing is cancelled; in place and in jobs. Named in the
  "Wake is per channel" bullet.
- **S3.** CHANGELOG: "ends at the agent's first think at or after its deadline"; "preempts inside the next step
  after the write".
- **S4.** The overflow fault is its own test, `DeadlineBeyondInt64_FaultsTheAgentAtItsFirstThink` (3 cases:
  timeout, cooldown on-enter, cooldown on-successful-exit): two agents, each faults once at tick 1 with
  `NativeCapacityArithmeticOverflow`, in place and in jobs (job batches asserted), the same signals on both
  paths. The exactness test is now `LargeUInt64Duration_IsExact` and also covers cooldown on-successful-exit
  (3 cases).
- **S5.** Not rerun; the order caveat is in the method of section 5.
- **N7.** The snippet calls the `TreeGroup` `group`. The earlier examples of `simulation-container.md` (before
  P12-001) call it `guards`; the snippet says so in a comment, and the older examples were not changed.

Tests after the fixes: `-Filter SimulationTickDurationTests` 122/122 (105 − 2 old UInt64 cases + 3 + 3 + 9 + 4),
`-Filter TreeSimulationReactivePatternTests` 8/8. `Verify-Static.ps1` passed. Section 1's table above is the
state before the fixes; the renamed and added tests are listed here.

## Open follow-ups (not done in P12-001)

- The generated node descriptions that still say "host time" / "`aibt.stdlib.wait` counts ticks" (section 2,
  items 1-5); they need the catalog-hash check first.
- The Player measurement of the wake cost.
- Rotating the variant order in the benchmark (ABCD, BCDA, …).
- `CanonicalTreeJson` throws `InvalidCastException` for an integer above `long.MaxValue` instead of reading it as
  `UInt64` or giving a diagnostic (found in round 1).

## What is not done

- **A Player measurement.** The numbers are Editor numbers (Burst safety checks and the jobs debugger on, in-place
  path managed). The `sim-*` Player harness needs a separate consumer project and an IL2CPP build.
- **The misleading node descriptions** of section 2 (items 1-5): they live in generated shard metadata and the
  built-in catalog authority, and may move catalog hashes; they need that check first.
- **The inspector label** (section 2, item 6).
- **The Swarm Arena sample's own time nodes** are not covered by the duration tests (they are not library nodes).

## Unsure

- Whether changing the description strings of section 2 would move any catalog or registry hash. I did not check.
- The brief asked for "a CHANGELOG line"; the entry is one bullet under its own heading, as the other entries are.
- The in-place differences (B − A, C − B) are inside a spread of several milliseconds on a 36 ms managed step;
  they say nothing about the cost of the comparison on that path in a Player.

## Commits

Local, on `feature/aibt-consumer-workflow`, not pushed:

| Commit | What |
| --- | --- |
| `7303f62` | tests, the fixture assembly and the asmdef reference |
| `d81c74a` | `simulation-container.md`, `native-node-library.md`, `CHANGELOG.md` |
| `85bd41a` | P12-001…P12-003 cards, work items, `MASTER_PLAN.md`, this evidence |
| the commit that adds "Review fixes round 1" | the review fixes: tests, `simulation-container.md`, `CHANGELOG.md`, this evidence |
