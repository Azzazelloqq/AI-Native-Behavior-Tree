# P10-001 evidence — managed zero-allocation tick, native facade scheduling, re-measurement

Card: [P10-001](../../Tasks/P10/P10-001-performance.md). Host: `Modules` on Unity 6000.7.0b1,
branch `feature/aibt-consumer-workflow`, base `43dc15e`. Machine: Intel Core Ultra 9 275HX (24
logical processors, 23 job workers), 32 GB, Windows 11. Player measurements: IL2CPP, stripping
High, non-development, `P9Samples` consumer project referencing the package by `file:` path.

## A. Managed layer: per-tick allocations

### What allocated (Editor probe, allocations per tick, warmed up)

Measured with the `GC.Alloc` profiler recorder filtered to the main thread (Mono's
`GC.GetAllocatedBytesForCurrentThread` returns 0 in the Editor), call stacks from the profiler's
allocation call stacks.

| Tree | before | after |
|---|---:|---:|
| one leaf (`succeed`, `is-true`, `compare-float`, `set-float`, `stdlib.wait`) | 5 | 0 |
| the same leaf behind 14 reactive levels | 47 (`set-float` 89, `compare-float` 61) | 0 |
| `shared-reactive64` (64 reactive conditions) | 191 | 0 |
| `shared-shape15`, `shared-loop16` (restart every ~22 ticks) | 5.6–5.7 | 0 |
| `unity-shape15-both` | 6.9 | 0 |
| game code `Set`/`Get`/`TryGet` per tick, `Restart(false/true)` | allocated | 0 |

Sources removed (behavior, statuses, traces and diagnostics unchanged):

- `new ReferenceFrame` per node activation → one frame per node, reset on reuse (`ReferenceFrame.Reset`).
- `Enum.IsDefined` boxing on every completion and in parallel/cooldown configuration decoding → range checks.
- `List.GetRange`/`AddRange` (Mono copies through a temporary array) when suspending/resuming
  parallel branches → reusable branch storage and an append loop.
- `QueueObserversForSlot`: a new list and a sorting lambda per blackboard write → reusable list, insertion sort.
- `ReferenceCompletionInbox.Normalize` allocated with no records → early return.
- Blackboard encode/decode: `new byte[]` per write, `BitConverter` arrays per float → a reusable
  scratch buffer and span-based little-endian writes; float bits through an explicit-layout struct.
- `ReferenceBlackboardStorage.Reset` and `ManagedTreeInstance.Restart` arrays/lists per call → reused.
- Per-step handler lookups (up to five dictionary lookups per step) → a per-node binding array
  resolved once; the order of checks is unchanged.

Not removed (documented): fixed-string blackboard values still allocate their UTF-8 bytes on write
and their string on read; log nodes build strings.

### Regression tests

`Tests/Editor/Agents/ManagedTickAllocationTests.cs` (7 tests, tolerance zero allocations in 400
warmed-up ticks after 300 warm-up ticks): recorder self-check; a tree that completes and restarts
every few ticks (memory selector, inverter, wait-frames, stdlib wait); a reactive sequence
re-evaluating 4 conditions every tick; node writes, a `both` interrupt and game-code
`Set`/`Get`/`TryGet`; repeater/timeout/cooldown/failer/succeeder and a reactive selector
reactivated every few ticks; a parallel that suspends and resumes branches every tick; explicit
`Restart(resetBlackboard: false/true)`.

### Interval stagger

`BehaviorTreeAgent.StaggerIntervalTicks` (new, off by default): an Interval agent starts at a
golden-ratio fraction of its interval and keeps the remainder of its accumulated time modulo the
interval, so agents created in the same frame spread over the interval and stay spread after a frame
longer than the interval. Every tick receives the real time since the previous tick; the agent still
ticks at most once per frame. The default path (stagger off) is the code before P10-001.

First version and why it was changed: it only moved the first tick and then reset the accumulated
time to 0 as the default path does. Its Editor tests passed, but in the Player the frame after
spawning 1 000 agents is longer than 100 ms, so every agent was due on that frame and they all
stayed in step: run `il2cpp-20261115-132554` shows the same ≈1.5 ms frame every 100 ms with and
without stagger (7 vs 8 such frames in 3 000 at N=1 000). The remainder-keeping version is measured
below (section C).

`IntervalStaggerTests` (4): default unchanged (all 120 agents tick on the same frames); with stagger
max per frame ≤ fair share + 2; a staggered agent's deltas add up to the real time and it ticks
once per interval (±1 frame); after a 0.5 s first frame the default ticks together and the staggered
agents keep their phases.

Trade-off for the owner: as a default it would remove the frame spike of Interval agents created
together, but agents created together would no longer tick on the same frame and tick deltas would
carry the remainder instead of dropping it, which a game might rely on. Not switched.

### Per-node-step cost (Editor, Mono, µs per agent-tick, 100 agents, best of 5)

| Tree | before | after (binding array) |
|---|---:|---:|
| `shared-shape15` | ≈9.2 | ≈6.7–7.3 |
| `shared-reactive16` | ≈91–103 | ≈88–100 (no clear change) |
| `unity-shape15-both` | ≈6.7–10.3 | ≈5.5–8.2 |

Left (each needs evidence before change): trace record construction per event, the remaining
registry lookups outside the step loop, per-update observer topology validation, fixed strings.

## B. Native facade

### Forced Jobs profiles through `NativeTreeAgent`

Root cause of "The native host stopped: NativeCapacityPlanInvalid" with a `BatchedJobsSameFrame`
profile: `ProductionTreeScheduler` has no `SchedulerJobsCapabilities` until a caller sets them, and
by ADR-P7-033 there is no hidden default, so every forced Jobs update is rejected
(`FailUnsupportedForcedPolicy`, code `NativeCapacityPlanInvalid`). The shared world created by
`NativeTreeAgent` never had them, and nothing said so.

Fix (no default changed): the agent's fault message now names the missing call when its profile
forces a Jobs policy and the world's scheduler has no capabilities; the documentation shows
`world.GetComponent<ProductionTreeScheduler>().SetJobsCapabilities(...)`. With capabilities set,
every non-default profile works through the facade (`NativeTreeAgentTests`, 6 cases: Interactive,
Background, forced Immediate, Budgeted, BatchedJobsSameFrame, PipelinedJobs; 3 restarting and 3
effect-emitting agents each, 12 frames, no fault). `PipelinedJobs` at cadence 1 advances a tree
about once every 3 frames (a round spans frames and the member becomes eligible the frame after it
completes): a property of the scheduler, not changed.

### Restart without re-creating `ProductionTreeHost` — not done, owner decision

Cost of one native restart (Editor, 50 agents, one-node tree): stop ≈12 µs and 1 allocation,
start ≈83 µs and 38 allocations, of which `AddComponent` ≈11 µs / 1 allocation and
`ProductionTreeProgram.TryBootstrap` ≈64 µs / 40 allocations (native agent, trace channel,
generated dispatch adapter, effect queue, kinds array). Re-using only the component would save
≈23 µs and 2 allocations; the cost is the bootstrap.

The documented contract is `NativeTreeAgent.Host` "replaced on every restart", and
`NativeTreeAgentTests.RestartPolicy_ControlsWhatHappensAfterTheRootFinishes` asserts a new host
after each completion; `ProductionTreeHost` cannot be bootstrapped twice. Options:

1. Restart in place: keep the host and reset its native state (machine, blackboard to defaults,
   trace channel, adapter, effect queue) without freeing it. Changes the `Host` contract and the
   test; new native reset path. Saves most of the ≈95 µs and 39 allocations.
2. Keep a new host per run but pool the bootstrap storage between runs. Contract unchanged, larger
   internal change.
3. Keep as is and document it: prefer a looping root (repeater) to `RestartMode.Always` for trees
   that finish every few ticks.

### `n-native15-fact` allocation

Not a per-fact cost. `native-shape15` is a memory selector: with `target-visible` true the short
chase branch completes in ~7 ticks instead of ~22, so the tree completes and the host is re-created
2.2× as often (P9 player run at N=1000: 71 vs 32 completions per agent in 550 ticks). Allocation
per tick follows the restart rate (see the player table for bytes per restart).

### Registration O(1)

`ProductionTreeScheduler.TryRegister` counted the cohort with a full scan for every registration
with a generated catalog (O(n), O(n²) to spawn n agents) and `TryUnregister` used `List.Remove`;
`ProductionTreeWorld` used `List.Contains`/`List.Remove`. Now: cohort counts in a dictionary (exact
fallback scan only while a host registered before its bootstrap is present), vacant slots with
compaction once half the list is vacant (survivors keep their order), and a reference-identity
dictionary in the world. Compaction never runs inside a world phase loop.

One behavior difference, a fix: before, a participant unregistering inside an effect handler during
delivery shifted the list and the next participant's effects were skipped until the next drain;
now no participant is skipped. Test: `LeavingAndJoiningTheWorld_KeepsTheRegistrationOrderOfEffectDelivery`.

### `BatchedJobsSameFrame` lane ownership O(n²)

Found in the Player re-measure: `n-native15-jobs` took 5.5 s per frame at N=20 000.
`NativeBatchedLifecycleOwnerV1` checked that owner ids (in `TryCreate`) and active lane ids (in
`TrySchedule` and `TryComplete`, every round) are unique by comparing every pair. Now a set of owner
ids and a reused flag array per owner; the same inputs fail with the same codes, first failure
first. Native assemblies pass unchanged. Player effect (run 132554 → 150752, mean µs per frame):

| Scenario | N=1 000 | N=5 000 | N=20 000 |
|---|---|---|---|
| `n-io-wait1-jobs` | 10 049 → 14 962 (noisy) | 90 567 → 61 389 | 1 535 391 → 178 116 |
| `n-native15-jobs` | 37 916 → 35 792 | 333 240 → 181 069 | 5 457 693 → 1 194 785 |
| `n-shared-loop16-jobs` | 24 127 → 36 269 (noisy) | 268 751 → 120 308 | 4 736 271 → 383 358 |

At N=1 000 the change is within run-to-run noise of these GC-heavy scenarios. Still left in the
Jobs path (not changed, each needs evidence and design): per-round lists and machine arrays and
`Persistent` lane allocations in `ProductionBatchedGroupDriverV1` / `NativeBatchedLifecycleOwnerV1`
(≈1.1 KB per agent-tick for one waiting leaf), and `BatchedJobsSameFrame` remains slower on the
main thread than `Immediate` at every measured N.

### Idle floor (one waiting leaf)

Editor breakdown per agent-update (2 000 agents): scheduler update 79 µs, of which the host's own
update 75 µs (generated leaf dispatch 66, machine begin/advance/complete 6.4, trace records 0.2,
clock 0.3); world phases and scheduler bookkeeping ≈4 µs. In the Player the whole update is
≈3.3 µs inside the scheduler (P9 run), so the Editor split of the dispatch is not representative.
The fixed cost is the per-host update itself, not registration or scheduling bookkeeping. No
defect found; growth of the per-agent cost with N was not explained (not verified).

## C. Player re-measurement

Full tables: `Modules/Temp/perf/README-after.md` (not in Git; raw runs next to it in
`Temp/perf/raw/`: before `il2cpp-20261115-112453`, after `il2cpp-20261115-132554`, Jobs after the
lane fix `il2cpp-20261115-150752`, stagger `il2cpp-20261115-155326`). Same harness, machine and Player settings;
the `api-only` control (no AIBT) is ≈5% slower in the after run, so native changes under ~10% are
noise. Harness committed as `Benchmarks~/Phase10/PlayerPerf/` (its README has the method).

Managed (mean µs per frame, N=1 000, before → after; B/agent-tick; GC per 1 000 frames):

| Scenario | mean | p50 | p99 | B/agent-tick | GC/1k | agents at 1 ms | agents at 4 ms |
|---|---|---|---|---|---|---|---|
| `m-unity15-both` | 3 831 → 1 203 | 1 613 → 709 | 36 574 → 6 414 | 437 → 0 | 72.5 → 0 | 247 → 827 | 1 048 → 2 700 |
| `m-shared-shape15` | 2 780 → 1 029 | | | → 0 | → 0 | 344 → 972 | 1 401 → 3 243 |
| `m-shared-reactive16` | 37 556 → 9 633 | | | → 0 | → 0 | | |
| `m-shared-reactive64` | 163 706 → 39 254 | | | → 0 | → 0 | | |
| `m-realistic` | 2 810 → 1 148 | | | → 0 | → 0 | 361 → 877 | 1 283 → 2 384 |

Every managed scenario allocates 0 B per agent-tick after the change, with no GC during
measurement (empty cells: see the full tables). The p99 of `m-unity15-both` stays ≈5× its mean
(periodic frames where many agents restart together); not investigated.

Native: idle and fact/effect scenarios unchanged within noise (+3…+12%, control +5%). Scenarios
that restart hosts gain from O(1) registration at large N: `n-native15` 254 828 → 166 050 (10 000)
and 578 167 → 315 446 (20 000); `n-shared-shape15` 525 599 → 281 436 (20 000); `n-native15-fact`
1 633 400 → 688 660 (20 000). Allocation per agent-tick of restarting trees is unchanged
(≈2 KB, from host re-creation, see above). Jobs policies: `Documentation~/performance.md`.

Interval stagger after the remainder-keeping fix (run `il2cpp-20261115-155326`, both scenarios
in the same Player, `unity-shape15-both` every 100 ms, 3 000 frames, µs per frame):

| N | stagger | mean | p99 | max frame (3 runs) |
|---:|---|---:|---:|---:|
| 1 000 | off | 78 | 130 | 6 230–6 922 |
| 1 000 | on | 81 | 137 | 252–306 |
| 5 000 | off | 363 | 441 | 19 890–21 091 |
| 5 000 | on | 416 | 660 | 1 035–1 789 |

The 100 ms frame spike is gone (no frame above median + 300 µs at N=1 000 in any run, 9–10 per run
without stagger); at N=5 000 the mean is ≈15% higher with stagger (416 vs 363; more frames carry
ticks). Owner decision on the default
stands.

## Verification

Editor `Modules` on Unity 6000.7.0b1, Unity CLI, after the last code change (2026-11-15):

| Assembly | Result |
|---|---|
| AIBT.Runtime.Tests | 707/707 |
| AIBT.Editor.Tests | 635/636 — pre-existing `McpToolDispatcherTests.ZeroCustomNodesReturnsExactlyThePhase1BuiltInCatalog` (sample nodes present in this host project) |
| AIBT.Integration.Tests | 77/77 |
| AIBT.BehaviorCases.Tests | 65/65 |
| AIBT.NativeBurstDispatch.Tests | 71/71 |
| AIBT.NativeCommandsAndAsync.Tests | 20/20 |
| AIBT.NativeSharedBlackboard.Tests | 36/36 |
| AIBT.NativeSnapshot.Tests | 7/7 |
| AIBT.NativeTreeAgentBlackboard.Tests | 33/33 |
| AIBT.CodeGen.ContractTests | 6/6 |
| AIBT.CodeGen.GenerationConsumerTests | 1/1 |
| AIBT.CodeGen.GenerationTests | 12/14 — pre-existing host-only failures (`PackageInfo.FindForAssembly` returns null for the embedded package in this host) |
| AIBT.CodeGen.P7039ExternalTreeWrite.Tests | 0/0 |
| AIBT.PlayMode.Tests (Play Mode) | 1/1 |

Console: no compile errors; error entries are those logged on purpose by tests.
`Tools~/Verification/Verify-Static.ps1`: passed (165 work items, 7 schemas).
Public API against `43dc15e` (`Get-FullPublicApi.ps1`): types 549 → 549, members 2 520 → 2 521,
0 removed; the one addition is `BehaviorTreeAgent.StaggerIntervalTicks`. `ExpectedPublicAbiV2.txt`,
package version and scheduler defaults unchanged. Later code changes touched only private and
internal members.

## Not done / not verified

- Native restart without re-creating `ProductionTreeHost`: not done; needs an owner decision
  (options in B). Restarting native trees still allocate ≈2 KB per agent-tick.
- Jobs path allocations per round (`ProductionBatchedGroupDriverV1`, lane storage) not removed;
  `BatchedJobsSameFrame` remains slower on the main thread than `Immediate`. Worker-thread time
  of Jobs policies not measured.
- The reused lane-flag array in `NativeBatchedLifecycleOwnerV1` (replacing a per-round `bool[]`)
  was made after the Jobs re-measure; tested, not re-measured in the Player.
- Growth of the native per-agent idle cost with N (3.8 µs at 1 000 → 4.9 µs at 20 000) not explained.
- `m-unity15-both` p99 ≈5× the mean in the Player (periodic frames) not investigated.
- Defaults not changed (owner decisions): `StaggerIntervalTicks` default, the default native
  profile, host reuse.
- Measurements on one machine only; no Mono Player, no mobile target.
- Two GenerationTests and one Editor test fail in this host project before and after the change.
