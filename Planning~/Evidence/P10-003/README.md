# P10-003 evidence — cheaper dispatch framing (stage F1)

Base commit: `4dee8ec` (submodule `Assets/AIBT`, branch `feature/aibt-consumer-workflow`).
Every number below comes from the IL2CPP non-development Windows Player of the P9Samples harness
(`Benchmarks~/Phase10/PlayerPerf`), stripping High, quick set (`Invoke-Perf.ps1 -Quick`), unless a
row says otherwise. All numbers are main-thread time.

Raw data lives outside the repository and outside `Temp/`:
`C:\UnityProjects\_aibt_acceptance\perf-data\P10-003\`:

- `raw/il2cpp-*`: per-run CSV, `runs.jsonl`, `player.log` and, for profile Players, `profile.json`.
- `profile-*.md`: the profile tables printed by `Tools/profile.py`.
- `abba-A1.json` … `abba-A2.json`: quick summaries of the interleaved passes.
- `abba-table.md`, `abba-managed-*.json`: the interleaved comparison and the managed-control re-run.
- `api/`: public API dumps of `4dee8ec` and of this change.
- `logs/`: build logs, the guarded runner `runplan.ps1` and its logs.

The baseline Player is built from a detached worktree of the submodule at `4dee8ec`
(`C:\UnityProjects\_aibt_acceptance\aibt-4dee8ec`) through a copy of the harness project whose
manifest points at it (`P9Samples-4dee8ec`, Player `Build\Quick-il2cpp-base`).

## 1. Per-step profile of one dispatch

`SchedulerFrameProfileV1` (compiled only with `AIBT_SCHEDULER_FRAME_PROFILE`) gained step laps
inside dispatch: on the single-host path (`ProductionTreeHost.RunSegment` →
`GeneratedTreeDispatchAdapterV2.Execute` → `NativeBurstDispatchWorkspaceOwnerV2`) and on the group
path (`ProductionDispatchResolverV1` → `GeneratedDispatchGroupWorkspaceV2.TryExecute`).
`Tools/profile.py` prints µs per dispatch for each step; dispatches are single-host dispatches plus
group members.

Before any change (profile Player built from the unchanged tree, run `il2cpp-20261115-215045`,
1 run; steps below 0.05 µs everywhere are summed in "other"):

| scenario | N | Reserve | Activation | Snapshot | RequestBuild | BeginValidate | BeginClear | AcquireBatch | Body | Commit | EffectCapture | MachineComplete | TraceComplete | GroupParticipant | GroupPack | GroupValidate | GroupReserve | GroupValidateCommits | GroupPublish | other | sum | Body share |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| n-io-move1-fact-effect | 1000 | 0.06 | 0.06 | 0.09 | 0.05 | 0.69 | 0.08 | 0.06 | 3.02 | 0.03 | 0.08 | 0.04 | 0.09 | 0.00 | 0.00 | 0.00 | 0.00 | 0.00 | 0.00 | 0.19 | 4.54 | 67% |
| n-io-move1-fact-effect | 5000 | 0.13 | 0.07 | 0.15 | 0.06 | 0.82 | 0.10 | 0.06 | 2.99 | 0.03 | 0.08 | 0.05 | 0.09 | 0.00 | 0.00 | 0.00 | 0.00 | 0.00 | 0.00 | 0.19 | 4.80 | 62% |
| n-io-move1-fact-effect-jobs | 1000 | 0.00 | 0.04 | 0.06 | 0.00 | 0.00 | 0.00 | 0.00 | 2.46 | 0.08 | 0.08 | 0.09 | 0.17 | 0.06 | 0.05 | 1.97 | 0.05 | 0.05 | 0.25 | 0.04 | 5.44 | 45% |
| n-io-move1-fact-effect-jobs | 5000 | 0.00 | 0.04 | 0.12 | 0.00 | 0.00 | 0.00 | 0.00 | 2.43 | 0.11 | 0.13 | 0.14 | 0.36 | 0.10 | 0.08 | 9.76 | 0.12 | 0.13 | 1.07 | 0.07 | 14.66 | 17% |
| n-io-wait1 | 1000 | 0.05 | 0.05 | 0.03 | 0.04 | 0.50 | 0.07 | 0.06 | 2.09 | 0.03 | 0.04 | 0.04 | 0.08 | 0.00 | 0.00 | 0.00 | 0.00 | 0.00 | 0.00 | 0.18 | 3.27 | 64% |
| n-io-wait1 | 5000 | 0.14 | 0.07 | 0.06 | 0.05 | 0.72 | 0.10 | 0.06 | 2.16 | 0.03 | 0.04 | 0.04 | 0.09 | 0.00 | 0.00 | 0.00 | 0.00 | 0.00 | 0.00 | 0.19 | 3.74 | 58% |
| n-io-wait1-jobs | 1000 | 0.00 | 0.04 | 0.02 | 0.00 | 0.00 | 0.00 | 0.00 | 1.69 | 0.08 | 0.04 | 0.09 | 0.14 | 0.05 | 0.05 | 0.03 | 0.02 | 0.07 | 0.00 | 0.04 | 2.36 | 72% |
| n-io-wait1-jobs | 5000 | 0.00 | 0.04 | 0.03 | 0.00 | 0.00 | 0.00 | 0.00 | 1.62 | 0.08 | 0.06 | 0.10 | 0.22 | 0.07 | 0.10 | 0.04 | 0.08 | 0.06 | 0.00 | 0.06 | 2.56 | 63% |
| n-shared-loop16 | 1000 | 0.07 | 0.07 | 0.04 | 0.05 | 0.64 | 0.08 | 0.05 | 2.03 | 0.03 | 0.04 | 0.04 | 0.07 | 0.00 | 0.00 | 0.00 | 0.00 | 0.00 | 0.00 | 0.18 | 3.39 | 60% |
| n-shared-loop16 | 5000 | 0.11 | 0.09 | 0.07 | 0.06 | 0.76 | 0.10 | 0.05 | 2.04 | 0.03 | 0.04 | 0.04 | 0.07 | 0.00 | 0.00 | 0.00 | 0.00 | 0.00 | 0.00 | 0.19 | 3.64 | 56% |
| n-shared-loop16-jobs | 1000 | 0.00 | 0.06 | 0.03 | 0.00 | 0.00 | 0.00 | 0.00 | 1.66 | 0.10 | 0.04 | 0.13 | 0.09 | 0.09 | 0.08 | 0.03 | 0.03 | 0.07 | 0.00 | 0.04 | 2.42 | 69% |
| n-shared-loop16-jobs | 5000 | 0.00 | 0.07 | 0.03 | 0.00 | 0.00 | 0.00 | 0.00 | 1.59 | 0.15 | 0.05 | 0.12 | 0.19 | 0.16 | 0.15 | 0.03 | 0.08 | 0.09 | 0.00 | 0.06 | 2.77 | 57% |
| n-shared-reactive16 | 1000 | 0.03 | 0.05 | 0.02 | 0.04 | 0.51 | 0.06 | 0.05 | 1.93 | 0.03 | 0.04 | 0.04 | 0.06 | 0.00 | 0.00 | 0.00 | 0.00 | 0.00 | 0.00 | 0.18 | 3.04 | 63% |
| n-shared-reactive16 | 5000 | 0.03 | 0.06 | 0.02 | 0.04 | 0.53 | 0.06 | 0.05 | 1.94 | 0.03 | 0.04 | 0.04 | 0.06 | 0.00 | 0.00 | 0.00 | 0.00 | 0.00 | 0.00 | 0.18 | 3.10 | 63% |
| n-shared-reactive16-jobs | 1000 | 0.00 | 0.07 | 0.03 | 0.00 | 0.00 | 0.00 | 0.00 | 1.63 | 0.12 | 0.04 | 0.11 | 0.09 | 0.09 | 0.09 | 0.03 | 0.03 | 0.08 | 0.00 | 0.04 | 2.43 | 67% |
| n-shared-reactive16-jobs | 5000 | 0.00 | 0.08 | 0.03 | 0.00 | 0.00 | 0.00 | 0.00 | 1.57 | 0.16 | 0.05 | 0.10 | 0.20 | 0.12 | 0.14 | 0.03 | 0.07 | 0.09 | 0.00 | 0.06 | 2.71 | 58% |

Each lap costs one `Stopwatch.GetTimestamp` (≈20–25 ns), so a sum includes ≈0.4 µs of lap cost.

### "Body" is not Burst code on the main thread

"Body" is the call `catalog.Executor.ExecuteImmediate(ref batch)`. The generated catalog class is not
`[BurstCompile]` and `ExecuteImmediate` returns a struct, so in a Player that call and every node
body behind it run as IL2CPP-compiled C#; only the generated `CatalogJob` (the Schedule path) is
Burst. **The Burst share of a main-thread dispatch is therefore 0%.** The node body is the 56–72%
"Body" share above, and most of that is the bridge, not the node logic (next table).

To split Body, `Tools/instrument_bridge_laps.py` temporarily wraps every
`BurstGeneratedRuntimeBridge` entry point in a lap (time before a bridge call counts as generated
code, time inside it as the bridge). It was applied only for the profile Players P2–P4 below and
reverted; the bridge laps add about 0.4–0.5 µs per dispatch themselves. Profile P2 (code of
section 2.1, run `il2cpp-20261115-222244`), µs per dispatch:

| scenario | N | generated code (node logic, per-call catalog `Validate`) | bridge: batch/frame | bridge: contexts | bridge: complete | bridge: value access |
|---|---:|---:|---:|---:|---:|---:|
| n-io-wait1 | 1000 | 0.59 | 0.54 | 0.36 | 0.52 | 0.61 |
| n-shared-loop16 | 1000 | 0.55 | 0.54 | 0.39 | 0.47 | 0.60 |
| n-shared-reactive16 | 1000 | 0.52 | 0.51 | 0.40 | 0.43 | 0.60 |
| n-io-move1-fact-effect | 1000 | 1.08 | 0.53 | 0.37 | 0.57 | 1.19 |
| n-io-wait1-jobs | 1000 | 0.31 | 0.26 | 0.36 | 0.50 | 0.58 |

The bridge took 2.0–2.6 µs of a 2.5–3.7 µs Body. Section 2.2 explains why.

## 2. Changes, one at a time, with measured effect

Profile Players were rebuilt after each change (P0 → P4, 1 run each, quick set). Sums are µs per
dispatch; frame is µs of main thread per frame. P2–P4 carry the bridge laps, so compare P2→P3→P4
with each other, and P0→P1 with each other.

| scenario | N | P0 sum | P1 sum | P2 sum | P3 sum | P4 sum | P0 frame | P1 frame | P2 frame | P3 frame | P4 frame |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| n-io-move1-fact-effect | 1000 | 4.54 | 4.26 | 4.86 | 3.01 | 2.85 | 5551 | 5220 | 5799 | 3920 | 3703 |
| n-io-move1-fact-effect | 5000 | 4.80 | 5.34 | 5.26 | 3.43 | 3.26 | 30623 | 36246 | 32995 | 23482 | 22419 |
| n-io-move1-fact-effect-jobs | 1000 | 5.44 | 3.32 | 3.84 | 2.40 | 2.33 | 6594 | 4271 | 4944 | 3428 | 3299 |
| n-io-move1-fact-effect-jobs | 5000 | 14.66 | 4.28 | 4.21 | 2.70 | 2.60 | 81593 | 30931 | 27736 | 19948 | 19466 |
| n-io-wait1 | 1000 | 3.27 | 3.10 | 3.51 | 2.26 | 2.07 | 4024 | 3948 | 4317 | 3030 | 2807 |
| n-io-wait1 | 5000 | 3.74 | 3.46 | 3.84 | 2.65 | 2.44 | 24209 | 22863 | 24799 | 18572 | 17330 |
| n-io-wait1-jobs | 1000 | 2.36 | 2.24 | 2.64 | 1.61 | 1.54 | 3300 | 3167 | 3541 | 2428 | 2489 |
| n-io-wait1-jobs | 5000 | 2.56 | 2.56 | 2.88 | 1.91 | 1.76 | 18516 | 18524 | 19939 | 15073 | 14069 |
| n-shared-loop16 | 1000 | 3.39 | 3.10 | 3.60 | 2.28 | 2.09 | 9179 | 8594 | 9753 | 6489 | 6001 |
| n-shared-loop16 | 5000 | 3.64 | 3.26 | 3.72 | 2.51 | 2.33 | 49631 | 45958 | 50456 | 36959 | 34813 |
| n-shared-loop16-jobs | 1000 | 2.42 | 2.34 | 2.74 | 1.77 | 1.61 | 7510 | 7110 | 8193 | 5537 | 5359 |
| n-shared-loop16-jobs | 5000 | 2.77 | 2.75 | 3.20 | 2.09 | 1.92 | 40771 | 39542 | 44815 | 31739 | 29872 |
| n-shared-reactive16 | 1000 | 3.04 | 2.79 | 3.27 | 2.10 | 1.90 | 149318 | 137909 | 160013 | 106144 | 96700 |
| n-shared-reactive16 | 5000 | 3.10 | 2.78 | 3.36 | 2.07 | 1.90 | 760640 | 687376 | 822189 | 522073 | 483210 |
| n-shared-reactive16-jobs | 1000 | 2.43 | 2.39 | 2.76 | 1.79 | 1.68 | 130778 | 128723 | 146572 | 99341 | 93727 |
| n-shared-reactive16-jobs | 5000 | 2.71 | 2.70 | 3.06 | 2.07 | 1.90 | 689429 | 685426 | 769027 | 539075 | 498457 |

Runs: P0 `il2cpp-20261115-215045`, P1 `il2cpp-20261115-221547`, P2 `il2cpp-20261115-222244`,
P3 `il2cpp-20261115-222923`, P4 `il2cpp-20261115-223509`. Single profile runs are noisy (P1 shows
`n-io-move1-fact-effect` N = 5000 slower with no change on its path except a smaller BeginValidate);
the decision for the whole change set is the interleaved run in section 4.

### 2.1 Validation of the dispatch input (P0 → P1)

Three changes, built into one Player. Their laps are separate, so each effect is visible:

1. **Catalog metadata validated once per workspace.** Every dispatch re-validated the whole
   catalog shape (cases, configuration, memory and value fields, bindings, canonical rules) in
   `NativeBurstDispatchBatchOwnerV2.ValidateCreateInput` and
   `NativeBurstDispatchBindingValidationV2.Validate`. The workspace and group workspace own
   immutable copies of that metadata, so after one successful full validation they pass
   `catalogValidated: true`, and only the checks that read the request (its bytes, resolved
   bindings, live values, completions) run. The per-request checks were also moved out of the
   per-case loop (one pass over requests instead of cases × requests; same conjunction).
   Effect: **BeginValidate 0.50–0.82 → 0.19–0.42 µs** per single-host dispatch (move1-fact-effect
   N = 5000: 0.82 → 0.71).
2. **Live-value alias check O(N²) → O(N) for disjoint layouts.** The group path validates one
   create input for all members; the live-binding alias check compared every pair of live values
   across all requests. When every request's live values and target ordinals occupy their own
   block, ordered by request (as the grouped layout does), pairs from different requests cannot
   overlap, so only pairs inside one request are compared; any other layout still compares all
   pairs. Effect with change 1: **GroupValidate 1.97 → 0.07 µs (N = 1000) and 9.76 → 0.07 µs
   (N = 5000)** per member for `n-io-move1-fact-effect-jobs`.
3. **Command owner lookup by binary search.** `TryPublishGroupedCommands` found the participant of
   each command by a linear scan over all participants (O(commands × members)). The target ranges
   are cumulative and disjoint, so a binary search finds the same owner. Effect: **GroupPublish
   0.25 → 0.06 µs (N = 1000) and 1.07 → 0.12 µs (N = 5000)**.

### 2.2 Dispatch backing read by reference (P2 → P3)

The dispatch backing (`BurstDispatchBackingV2`, 30 native containers plus the catalog handshake,
≈0.5 KB) sits inside every public carrier struct (`BurstExecutionBatch`, `BurstDispatchFrame`,
contexts, readers, writers). The bridge read it through `internal BurstDispatchBackingV2 Runtime =>
_runtime;` and passed it by value to ≈50 helpers. The IL2CPP C++ confirms a full copy per access:
`BurstDispatchBridgeCoreV2_TryGetExecutionRequest` copies the batch into a local, copies the
backing out of it, and passes that copy by value to `TryControl`.

Change: internal `RuntimeRef(this in T)` extensions return `ref readonly` to the field (the fields
became `internal`), the bridge cores use them, and every internal helper takes the backing as
`in`. Six writes through an indexer now go through a local copy of the `NativeArray` (a view of
the same memory). Public structs, their layout and the public bridge are unchanged.

Effect: **Body 2.5–3.7 → 1.3–1.95 µs per dispatch; frame time −25% to −35%** in every scenario
(table above; e.g. `n-shared-loop16` N = 1000: 9 753 → 6 489 µs).

### 2.3 Read-only internal members (P3 → P4)

`BurstExecutionBatch`, `BurstMemoryAccessor`, `BurstValueReader`, `BurstValueWriter`,
`BurstEnterContext` and `BurstTickContext` are not `readonly` structs. Reading an internal getter
(`Role`, `FrameId`, `ValidationToken`, …) on an `in` parameter of such a struct makes a defensive
copy of the whole struct, backing included. The internal getters are now `readonly` members (no
public member changed). Effect: **another −0.1 to −0.17 µs per dispatch, frame −3% to −8%**.

### 2.4 Not made

- **Burst-compiled `ExecuteImmediate`.** After 2.2–2.3 the whole Body is ≈0.85 µs per dispatch in
  `n-io-wait1` (P4 Body 1.25 minus ≈0.4 µs of bridge laps). Running it as Burst would need the
  generator to emit a Burst entry point with a blittable signature (`ExecuteImmediate` returns a
  struct) and catalogs to be regenerated on the consumer side. That is forbidden here. It is an
  estimate, not a measurement: Burst could remove up to ≈0.7 µs of the ≈1.65 µs that a single-host
  dispatch now costs (about 40%), less the cost of entering Burst per call.
- **Generated per-call catalog check.** Under Immediate, generated code costs ≈0.2 µs more per
  dispatch than in a group (0.53 vs 0.30), because each `ExecuteImmediate` calls the handshake and
  the catalog's own `Validate` (hash comparisons). Removing it needs a generator change.
- **Contexts carry a copy of the backing.** Creating a frame, context, reader or writer still copies
  the backing into the new public struct (bridge "contexts" ≈0.2 µs per dispatch). Replacing the
  embedded backing by a handle changes the layout of public ABI structs; not done.

## 3. The group-path outlier (`n-io-move1-fact-effect-jobs`, N = 5000)

Before: 14.66 µs per member against 4.80 µs for Immediate; GroupValidate 9.76 µs and GroupPublish
1.07 µs per member, both growing with N. Cause: two quadratic loops over one group's members —
the live-binding alias check in `NativeBurstDispatchBindingValidationV2.Validate` (every pair of live
values across all requests) and the linear owner scan per command in
`GeneratedDispatchGroupWorkspaceV2.TryPublishGroupedCommands`. Only this scenario has live values
(a fact read) and commands (an effect), so only it showed the cost. Changes 2.1(2) and 2.1(3)
remove both; after them the scenario is no longer slower under Jobs than under Immediate (P4:
2.60 vs 3.26 µs per dispatch at N = 5000; frame 19.5 ms vs 22.4 ms).

## 4. Interleaved before/after (ABBA)

`logs/runplan.ps1` ran the quick set (20 cases, 2 runs each, 120 warm-up and 250 measured frames,
time limit 1500 s per pass) in the order A1 (before), B1 (after), B2 (after), A2 (before). Before is
the Player built from `4dee8ec`; after is `P9Samples\Build\F1-after`, built from the final tree
without the profile define. The two harness projects differ only in the AIBT package path.

| Pass | Player | Run | Duration | Faults |
|---|---|---|---:|---:|
| A1 | before | `il2cpp-20261115-224001` | 9.0 min | 0 |
| B1 | after | `il2cpp-20261115-224904` | 6.4 min | 0 |
| B2 | after | `il2cpp-20261115-225531` | 6.4 min | 0 |
| A2 | before | `il2cpp-20261115-230159` | 9.0 min | 0 |

Mean main-thread µs per frame; "change" compares the means of both A and both B passes:

| scenario | N | A1 | B1 | B2 | A2 | before (A mean) | after (B mean) | change | A1→B1 | A2→B2 | noise A1→A2 | B/agent-tick A→B | flag |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|
| m-shared-shape15 | 1000 | 1,014 | 1,031 | 1,017 | 1,026 | 1020 | 1024 | +0.4% | +1.7% | -0.9% | +1.2% | 0.0→0.0 |  |
| m-shared-shape15 | 5000 | 6,152 | 6,213 | 6,129 | 6,032 | 6092 | 6171 | +1.3% | +1.0% | +1.6% | -2.0% | 0.0→0.0 |  |
| m-unity15-both | 1000 | 1,137 | 1,226 | 1,179 | 1,154 | 1145 | 1203 | +5.0% | +7.9% | +2.1% | +1.6% | 0.0→0.0 |  |
| m-unity15-both | 5000 | 7,388 | 8,212 | 7,488 | 7,418 | 7403 | 7850 | +6.0% | +11.1% | +0.9% | +0.4% | 0.0→0.0 |  |
| n-io-move1-fact-effect | 1000 | 4,929 | 2,700 | 2,741 | 5,036 | 4982 | 2721 | -45.4% | -45.2% | -45.6% | +2.2% | 0.0→0.0 |  |
| n-io-move1-fact-effect | 5000 | 28,631 | 16,947 | 17,117 | 28,614 | 28622 | 17032 | -40.5% | -40.8% | -40.2% | -0.1% | 0.0→0.0 |  |
| n-io-move1-fact-effect-jobs | 1000 | 6,483 | 2,681 | 2,661 | 6,523 | 6503 | 2671 | -58.9% | -58.7% | -59.2% | +0.6% | 0.0→0.0 |  |
| n-io-move1-fact-effect-jobs | 5000 | 81,510 | 17,458 | 17,592 | 80,161 | 80835 | 17525 | -78.3% | -78.6% | -78.1% | -1.7% | 0.0→0.0 |  |
| n-io-wait1 | 1000 | 3,681 | 1,946 | 1,986 | 3,815 | 3748 | 1966 | -47.6% | -47.1% | -48.0% | +3.6% | 0.0→0.0 |  |
| n-io-wait1 | 5000 | 21,431 | 12,648 | 12,663 | 21,896 | 21663 | 12655 | -41.6% | -41.0% | -42.2% | +2.2% | 0.0→0.0 |  |
| n-io-wait1-jobs | 1000 | 3,255 | 2,018 | 1,992 | 3,189 | 3222 | 2005 | -37.8% | -38.0% | -37.5% | -2.0% | 0.0→0.0 |  |
| n-io-wait1-jobs | 5000 | 18,662 | 12,557 | 12,683 | 18,370 | 18516 | 12620 | -31.8% | -32.7% | -31.0% | -1.6% | 0.0→0.0 |  |
| n-shared-loop16 | 1000 | 7,942 | 3,831 | 3,855 | 8,168 | 8055 | 3843 | -52.3% | -51.8% | -52.8% | +2.8% | 0.0→0.0 |  |
| n-shared-loop16 | 5000 | 43,650 | 23,287 | 23,388 | 43,598 | 43624 | 23337 | -46.5% | -46.7% | -46.4% | -0.1% | 0.0→0.0 |  |
| n-shared-loop16-jobs | 1000 | 6,852 | 3,863 | 3,887 | 6,681 | 6766 | 3875 | -42.7% | -43.6% | -41.8% | -2.5% | 0.0→0.0 |  |
| n-shared-loop16-jobs | 5000 | 36,596 | 24,898 | 24,671 | 36,613 | 36604 | 24785 | -32.3% | -32.0% | -32.6% | +0.0% | 0.0→0.0 |  |
| n-shared-reactive16 | 1000 | 128,530 | 53,617 | 54,351 | 128,346 | 128438 | 53984 | -58.0% | -58.3% | -57.7% | -0.1% | 0.0→0.0 |  |
| n-shared-reactive16 | 5000 | 640,767 | 267,681 | 271,685 | 644,657 | 642712 | 269683 | -58.0% | -58.2% | -57.9% | +0.6% | 0.0→0.0 |  |
| n-shared-reactive16-jobs | 1000 | 122,602 | 62,445 | 63,390 | 121,067 | 121835 | 62917 | -48.4% | -49.1% | -47.6% | -1.3% | 0.0→0.0 |  |
| n-shared-reactive16-jobs | 5000 | 605,917 | 358,847 | 367,356 | 616,901 | 611409 | 363102 | -40.6% | -40.8% | -40.5% | +1.8% | 0.0→0.0 |  |

- **Native layer.** Every native scenario is faster in both comparisons: single-threaded Immediate
  by 40–58%, BatchedJobsSameFrame by 31–78%. The noise between the two before passes is at most
  3.6%. No allocation changed (0 B per agent tick everywhere).
- **The outlier is gone.** `n-io-move1-fact-effect-jobs` at N = 5000 went from 80.8 ms to 17.5 ms
  per frame; it is now about as fast as Immediate (17.0 ms) instead of 2.8× slower.
- **No flagged regression.** No single-threaded case is more than 5% slower in both interleaved
  comparisons. `m-unity15-both` (the managed control; no file on its path changed) is +7.9% and
  +11.1% in A1→B1 but +2.1% and +0.9% in A2→B2, which puts its mean change at +5.0% / +6.0%.

A second interleaved run of only the two managed controls (3 runs per pass, `abba-managed-*`,
runs `il2cpp-20261115-231124`, `-231156`, `-231228`, `-231300`):

| Scenario | N | A1 | B1 | B2 | A2 | A1→B1 | A2→B2 | noise A1→A2 | mean change |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| m-shared-shape15 | 1000 | 1 029 | 1 030 | 1 047 | 1 016 | +0.1% | +3.1% | −1.3% | +1.6% |
| m-shared-shape15 | 5000 | 6 302 | 6 210 | 6 319 | 6 043 | −1.5% | +4.6% | −4.1% | +1.5% |
| m-unity15-both | 1000 | 1 149 | 1 162 | 1 220 | 1 170 | +1.1% | +4.3% | +1.8% | +2.7% |
| m-unity15-both | 5000 | 7 516 | 7 640 | 7 976 | 7 504 | +1.6% | +6.3% | −0.2% | +4.0% |

The +11.1% does not reproduce (+1.6%). The managed controls stay 1.5–4% slower on average in the
after Player, inside the noise of a Player against itself (up to 4.1%), and in only one of two
comparisons above 5%. The managed tick does not execute any file this task changed, so this is
not explained by a code change on its path; whole-assembly code layout of the rebuilt IL2CPP
Player is a possible cause, not verified.

## 5. Native Immediate versus the managed controls

Main-thread µs per agent and frame (means of the ABBA passes; both layers single-threaded):

| Case | N = 1000 before | N = 1000 after | N = 5000 before | N = 5000 after |
|---|---:|---:|---:|---:|
| managed `m-shared-shape15` (15-node tree) | 1.02 | 1.02 | 1.22 | 1.23 |
| managed `m-unity15-both` (15-node tree, Unity components) | 1.15 | 1.20 | 1.48 | 1.57 |
| native Immediate `n-io-wait1` (one waiting leaf) | 3.75 | 1.97 | 4.33 | 2.53 |
| native Immediate `n-io-move1-fact-effect` | 4.98 | 2.72 | 5.72 | 3.41 |
| native Immediate `n-shared-loop16` (looping 16-node tree) | 8.06 | 3.84 | 8.72 | 4.67 |
| native Immediate `n-shared-reactive16` (46 dispatches per agent and frame) | 128.4 | 54.0 | 128.5 | 53.9 |

The native layer is still slower per agent than the managed layer for these small trees: one
waiting leaf costs about 1.7× a whole managed 15-node tick at N = 1000 (it was 3.3×), and a looping
16-node tree about 3.2× (it was 6.9×). The remaining native cost per dispatch is now about 1.6–1.7 µs (profile Player, laps included),
of which about half is the IL2CPP-compiled node body and bridge (section 2.4).

## 6. Tests

- `JobsPolicyEquivalenceTests.AllPolicies_MatchTheOutcomeRecordedBeforeP10003_FrameByFrame`: three
  trees, 90 frames, under each of three profiles (default, forced Immediate, forced
  BatchedJobsSameFrame). Each frame's observation — trace records, root result, running state, run
  count, error, delivered effects and every byte of each agent's tree-value storage — is hashed
  (SHA-256, first 8 bytes) and compared with 90 per-frame digests recorded on the unchanged
  `4dee8ec` runtime: the four runtime files with changes at that point were restored to `4dee8ec`
  (the other edits were profile laps, compiled out without the define), the digests recorded, and
  all three profiles matched them there. They pass after every later change. Any change in order, status, trace, effect or
  blackboard value fails it.
- `JobsPolicyEquivalenceTests.WarmedUpImmediateFrames_DoNotAllocateManagedMemory`: 32
  `NativeTreeAgent`s (looping and reactive-effect trees) under a forced Immediate profile, 60
  warm-up and 200 measured frames, `GC.Alloc` recorder on the main thread: **0 allocations**. It also
  passes on `4dee8ec`. `WarmedUpBatchedJobsFrames_DoNotAllocateManagedMemory` stays green.
- `NativeBurstDispatchBindingTransactionTests`: two new live-alias layouts (same target in separate
  ranges → rejected; separate requests with reversed target order → accepted through the full pair
  check), and `CatalogValidatedFlag_KeepsEveryRequestCheck` (each per-request forgery is rejected
  with the catalog flag set, exactly as without it).
- `GeneratedDispatchLifecycleProofTests.SchedulerDestroyedInsideABatchedRound_ReleasesTheRoundPool`:
  see section 7.

Test trees use repeaters that return Running, so no tree loops inside one update.

## 7. Pool leak on dispose during a round (P10-002 defect)

`ProductionBatchedGroupDriverV1.Dispose()` called while `_running` skipped releasing the round pool
(`NativeBatchedLifecyclePoolV1`), leaking its Persistent buffers. `TryRun` now releases the pool in
its `finally` when the driver was disposed during the round. The new test destroys the scheduler
from a host's clock inside a batched round; it failed on the old code ("must be released") and
passes now.

## 8. Incidents and findings outside the scope

- **Pre-existing zombie benchmark process.** `AibtPerf.exe` PID 3276 (from P10-002, folder
  `P9Samples\Build\Quick-il2cpp-after`) was already listed by the OS with `HasExited = True` and one
  thread when this task started. It was not started by this task and was left alone; this task
  did not use that build folder. Every Player this task started exited by itself (`runplan.ps1`
  checks after each pass; no live `AibtPerf` remained).
- **Runner bug, fixed before the ABBA run.** `powershell -File runplan.ps1 -Plan 'a','b'` passes one
  comma-joined string; the runner now splits it. Profile runs used one entry each and were not
  affected.
- **Destroying the scheduler inside a batched round (separate defect, not fixed).** After the round
  pool fix, the new test still shows a pre-existing failure: `ProductionTreeScheduler` keeps using
  `_due[member]` after `TryRun` returns, but `OnDestroy` (called from inside the round) cleared
  `_due`, so it throws `ArgumentOutOfRangeException`; the hosts of that round log
  `NativeLifetimeStateInvalid: Callback rejected: InvalidHandle`. The test tolerates both and checks
  only the pool release. Fixing it changes the diagnostics of an interrupted round, so it is left
  for a follow-up.
- **Possible leak, not verified.** `GeneratedDispatchGroupWorkspaceV2.TryDispose` refuses while the
  workspace is leased; if its owner is disposed during a lease, the workspace may stay allocated.
  No path that reaches it was found or tested.
- **Editor tooling.** `Invoke-UnityRecompile.ps1` often returned `timeout` after 120 s while the
  Editor was still compiling; `Wait-UnityIdle.ps1` was run after it every time. The Console keeps
  the errors of an earlier failed compile in its buffer; `compilationFailed=False` and
  `Get-UnityConsole.ps1 -New` were used to tell them apart.
- **Public API baseline.** `Get-FullPublicApi.ps1` takes the AIBT root from its own location and
  needs a project root two levels up, so the baseline ran from a `git archive` copy of `4dee8ec` at
  `C:\UnityProjects\_aibt_acceptance\api-base\Assets\AIBT`, with Unity 6000.7.0b1.

## 9. Verification

| Check | Result |
|---|---|
| `Tools/Unity/Invoke-UnityRecompile.ps1` | compiled, no errors |
| `AIBT.Runtime.Tests` | 707/707 |
| `AIBT.Editor.Tests` | 635/636; the failure is the known host-only `McpToolDispatcherTests.ZeroCustomNodesReturnsExactlyThePhase1BuiltInCatalog` (sample nodes of the host project) |
| `AIBT.Integration.Tests` | 82/82 (79 before + 3 new) |
| `AIBT.BehaviorCases.Tests` | 65/65 |
| `AIBT.NativeBurstDispatch.Tests` | 79/79 (71 before + 8 new cases) |
| `AIBT.NativeCommandsAndAsync.Tests` | 20/20 |
| `AIBT.NativeSharedBlackboard.Tests` | 36/36 |
| `AIBT.NativeSnapshot.Tests` | 7/7 |
| `AIBT.NativeTreeAgentBlackboard.Tests` | 33/33 |
| `AIBT.PlayMode.Tests` | 1/1 (`unity command run_tests --mode playmode … --async_tests`, then `test_status`) |
| Console | only errors that tests log on purpose (including two `Callback rejected: InvalidHandle` from the new interrupted-round test) |
| `Tools~/Verification/Verify-Static.ps1` | passed (167 work items) |
| Public API vs `4dee8ec` (`Get-FullPublicApi.ps1 -BaselinePath`) | 6 assemblies, 549 types, 2521 members, sha256 `9551c0c8…` identical for both; 0 added, no removals (`perf-data\P10-003\api\`) |
| `ExpectedPublicAbiV2.txt`, package version, scheduler defaults, generated catalogs | unchanged |

## Not done / not verified

- **Body not Burst.** The largest remaining cost per dispatch (node body and bridge, IL2CPP code)
  needs a generator change to run as Burst; it is only estimated (section 2.4).
- **Single profile runs.** The per-change profile tables (P0–P4) are one run each. Only the whole
  change set has an interleaved before/after; individual changes do not.
- **Bridge laps.** P2–P4 were measured with temporary bridge laps; the final Player (ABBA) has none.
  The split of Body in section 1 is therefore approximate.
- **No worker-thread time.** All numbers are main thread; worker time of `BatchedJobsSameFrame` and
  the Burst job path were not measured.
- **Managed controls.** Their 1.5–4% average slowdown in the after Player is within the noise and
  not flagged, but its cause is not identified (section 4).
- **Quick set only.** N = 20 000, `PipelinedJobs` and the other harness scenarios were not
  re-measured.
- **Equivalence coverage.** The frame-digest test covers three trees under three profiles; it does
  not cover async completions or `PipelinedJobs`.
- **Interrupted round.** The `ArgumentOutOfRangeException` after destroying the scheduler inside a
  round is not fixed (section 8).
- **Parent repository untouched.** Nothing was changed or committed in `C:\UnityProjects\Modules`
  outside the submodule.
