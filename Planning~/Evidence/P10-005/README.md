# P10-005 evidence — measurement of the native policies and a proposal for automatic selection (stage J3)

Base: submodule `Assets/AIBT`, branch `feature/aibt-consumer-workflow`, `659a8b1` (P10-004). Card:
`Planning~/Tasks/P10/P10-005-policy-measurement.md`. This card changes the benchmark harness and
documentation only: no file under `Runtime/`, `Agents/`, `Authoring/`, `Editor/` or `Analyzers/`
changed, so no test assembly was rerun (section 11).

Raw data lives outside the repository: `C:\UnityProjects\_aibt_acceptance\perf-data\P10-005\`:
`raw/il2cpp-*` (Player runs), `logs/runplan.ps1` (guarded runner), `logs/j3*.runner.txt` (pass log
with the quiet-machine check), `j3-all.json` (summary of every J3 pass), `tables.md` (all tables,
also copied here as [`tables.md`](tables.md)), `package-copy-check.txt`, `package-copy-patch.diff`,
`compile-warnings-6000.6.9f1.txt`.

## 0. Unity version change (read first)

Unity 6000.7.0b1, used for P10-001…P10-004, was uninstalled from the machine before this stage
(Unity Hub log: "uninstall 6000.7.0b1"; 6000.6.9f1 installed the same day, its Windows IL2CPP
module added by the owner at 17:11). The owner chose to run the whole stage on **Unity 6000.6.9f1**.

- **No before/after against P10-001…P10-004.** Every conclusion here compares policies inside one
  Player build. Where P10-004 numbers are shown (section 9), they are "another Unity version", not a
  before/after comparison.
- **Bench project.** `C:\UnityProjects\_aibt_acceptance\P9Samples-6069`, a copy of `P9Samples`
  (Assets, Packages, ProjectSettings, AibtPerfTools; new Library). Changes in the copy only:
  `ProjectVersion.txt` → 6000.6.9f1; three built-in modules that do not exist in 6000.6.9f1 removed
  from `Packages/manifest.json` and `packages-lock.json` (`com.unity.modules.localizationruntime`,
  `com.unity.modules.smartstrings`, `com.unity.modules.spritemask`; nothing in the bench uses them);
  the AIBT dependency points at the package copy below. No other package changed: **Burst 2.0.0**
  and **Collections 6.6.0** are built in, the same version numbers as under 6000.7.0b1.
- **Original `P9Samples`** was not changed, except its `Library/`, which a first 6000.6.9f1
  batch-mode build attempt (17:05, failed at package resolution) partly rewrote.
- **Package copy.** The bench builds against `C:\UnityProjects\_aibt_acceptance\aibt-659a8b1-6069`,
  `git archive 659a8b1` of the package plus one line (0.1). `Runtime/`, `Agents/`, `Authoring/` and
  `Analyzers/` in the copy equal `659a8b1` blob for blob (`git hash-object --no-filters` of every
  file against `git ls-tree -r 659a8b1`: 388, 54, 198 and 4 files, 0 differences).

### 0.1 Finding: AIBT at `659a8b1` does not compile on Unity 6000.6.9f1

**AIBT at `659a8b1` does not compile on Unity 6000.6.9f1 (CS0619 in
`Editor/Graph/BehaviorTreeImporter.cs:43`); the fix is the condition `UNITY_6000_6_OR_NEWER`; not
done in the submodule, it needs the Editor tests in the Modules project.**

`AssetDatabase.GetAssetPath(int)` is obsolete as an error in 6000.6.9f1, and the method selects the
`EntityId` overload only under `#if UNITY_6000_7_OR_NEWER`. Applied only in the bench package copy
(`package-copy-patch.diff`):

```diff
         [OnOpenAsset(0)]
-#if UNITY_6000_7_OR_NEWER
+#if UNITY_6000_6_OR_NEWER
         private static bool OpenTree(UnityEngine.EntityId instanceId, int line)
 #else
         private static bool OpenTree(int instanceId, int line)
```

It is the only compile error. AIBT warnings in the same compile (8 unique, none blocking):

| File | Warning |
|---|---|
| `Agents/Nodes/BtAttributes.cs:19` | CS0114 `BtNodeAttribute.TypeId` hides `Attribute.TypeId` |
| `Agents/Runtime/ManagedNodeCatalog.cs:186` | UAC0005 `AppDomain.GetAssemblies()` |
| `Editor/Registry/GeneratedNodeReflectionHarness.cs:40`, `:144` | UAC0005 `AppDomain.GetAssemblies()` |
| `MCP/Documentation/McpApiReferenceGenerator.cs:49` | UAC0005 `AppDomain.GetAssemblies()` |
| `MCP/McpRuntimeTools.cs:163` (2) | CS0618 `FindObjectsByType(FindObjectsInactive, FindObjectsSortMode)` and `FindObjectsSortMode` obsolete |
| `Runtime/Scheduling/Native/NativePipelinedPhaseControllerV1.cs:182` | CS0652 comparison to an integral constant outside the range of `int` |

## 1. Harness changes

`Benchmarks~/Phase10/PlayerPerf/` (no runtime code):

- `PerfRunner.cs`: `-perfSet` takes a comma list. New sets `j3-small`, `j3-cross`, `j3-scale`,
  `j3-scale20k`, `j3-workers`, `j3-mixed`, `j3-cadence`, `full-immediate` and `full-immediate-5k`
  (the full plan without the forced Jobs scenarios, the latter also without N > 5 000). Policies of
  one tree and N are adjacent in the plan, so every run interleaves them.
- Mixed population: `-agentframe-mixed` gives every second `JobsAgentFrame` host
  `StepBudget = 1 000 000` (fallback reason `StepBudgetConfigured`; the budget never binds, so the
  work is the same); `-agentframe-mixed-grouped` gives it to the second half of the hosts instead.
- Native cadence: `-cadence6` uses a profile with `UpdateCadence = 6` (forced `JobsAgentFrame` for
  the `-agentframe` variant). The native layer has no `Interval` tick mode and no
  `StaggerIntervalTicks`; `-stagger` enables the agents in 6 portions over 6 frames, so their phases
  spread (the scheduler sets the first eligible frame at registration).
- CPU window: `cpuWindowProcessUs` and `cpuWindowMainThreadUs` in `runs.jsonl`, the process and
  main-thread CPU time over the measured frames (`GetProcessTimes` / `GetThreadTimes`), with
  `cpuWindowWallUs`. Process CPU includes the render thread, the job workers (also while they spin)
  and the engine. Its resolution is the OS tick (15.6 ms) over windows of 0.3–20 s.
- `Invoke-Perf.ps1`: `-Set <set>` and `-JobWorkers <n>`.
- `Tools/j3.py` (summary per scenario, N and worker count over several folders) and
  `Tools/j3_tables.py` (the tables of this document).

Per-frame main-thread time is the existing `bracket_us` column, frame wall time the existing
`frame_dt_us` column, the agent-frame driver time the existing `scheduler_drive_us` column.

## 2. Method

- **Player.** `P9Samples-6069\Build\P10005`: IL2CPP, non-development, stripping High, Burst on,
  Unity 6000.6.9f1, 640×360 window, no vSync. Built three times from the same package copy; the
  rebuilds only added harness sets (`-agentframe-mixed-grouped`, `j3-cross`, `full-immediate-5k`),
  the scenarios already measured were unchanged.
- **Machine.** Intel Core Ultra 9 275HX, 24 logical processors (23 job workers by default),
  32 GB, Windows 11, RTX 5070 Laptop GPU. **No other Unity process ran during the measurement**: the
  owner had the clm-client Editor and a PhysicsSandbox batch-mode Editor closed before the first
  pass; only the Unity Hub service remained. The runner checked before every pass that total CPU was
  below 12% over 5 s and that no Unity / UnityShaderCompiler process used CPU (every pass started
  at 4.9–11.5%; no pass was marked noisy).
- **Frames.** `j3-small`: 200 warm-up and 1 000 measured frames; `j3-cross`: 200 / 1 500; N = 1 000
  and 5 000: 120 / 250 (the quick-set configuration); N = 20 000: 60 / 150; `j3-cadence`: 120 / 600.
  Every phase is capped (warm-up 5 s, measurement 20 s), so the slowest cases (≈1 s frames) have
  ≈20 measured frames.
- **Statistic.** Mean main-thread bracket per run; the tables show the median of the run means and
  the min–max of the run means in brackets.
- **Repeats.** Small N: `j3-small` twice (3 runs each, 6 runs) and `j3-cross` twice (2 runs each)
  at N = 10…75, so N = 10 and 50 have 10 runs, N = 20…75 have 4. Scale: 2 runs (N = 20 000) or 4
  runs (N = 1 000 / 5 000: `j3-scale` plus the Immediate and JobsAgentFrame cases of `j3-mixed`).
  Worker counts 1, 3 and 7: 1 run each (the cut below); 0 workers: 2 runs.
- **Total CPU.** Two ways: (1) the zero-worker method of P10-004 (`-JobWorkers 0`: every job runs on
  the main thread inside the bracket, so the bracket is the total CPU time of the agents' work on one
  thread); (2) process CPU per frame from the CPU window, which includes the engine and the render
  thread, so only differences between policies of the same N mean anything.
- **Player time.** 8 + 24 (scale) + 2 + 5 + 3×2.5 + 10 (mixed) + 1 (cadence) + 8 (N = 20 000, 0
  workers) + 8 + 2 + 7 (full) ≈ 83 min. The planned 12-pass plan would have taken ≈2.5 h (the N = 20 000
  cases hit the phase caps). Cut: worker counts 1, 3 and 7 one run instead of two, no separate
  23-worker repeat of `j3-workers` (the scale pass has the same cases), the full plan refresh without
  N > 5 000. Small N was not cut.

## 3. Small N: crossover

Main-thread µs per frame, median of runs (run spread), 23 workers. AF = JobsAgentFrame.
Frame wall time includes ≈200 µs of engine work at every N.

### io-wait1 (one waiting leaf)

| N | Immediate | BatchedJobsSameFrame | JobsAgentFrame | AF / Imm | AF − Imm µs | frame wall Imm / AF µs |
|---:|---:|---:|---:|---:|---:|---:|
| 1 | 15.7 (15.1–16.7) | 27.6 (26.3–30.8) | 20.3 (16.6–20.9) | 1.30 | +4.7 | 224 / 231 |
| 10 | 32.8 (29.9–41.5) | 104.3 (85.0–110.7) | 49.3 (40.6–51.5) | 1.50 | +16.5 | 273 / 261 |
| 20 | 54.9 (52.0–56.1) | - | 59.0 (58.4–60.9) | 1.08 | +4.1 | 274 / 264 |
| 30 | 76.5 (73.0–85.9) | - | 63.3 (62.4–64.4) | 0.83 | −13.2 | 276 / 255 |
| 40 | 96.3 (92.2–106.5) | - | 70.2 (65.6–73.0) | 0.73 | −26.2 | 297 / 273 |
| 50 | 111.4 (110.0–130.1) | 162.0 (160.1–170.0) | 77.7 (72.8–80.5) | 0.70 | −33.7 | 311 / 303 |
| 75 | 160.5 (157.1–183.4) | - | 95.1 (93.6–100.6) | 0.59 | −65.4 | 320 / 297 |
| 100 | 214.8 (208.7–222.4) | 244.8 (242.0–275.7) | 117.1 (107.0–124.2) | 0.55 | −97.7 | 336 / 282 |
| 200 | 391.3 (377.1–397.5) | 410.2 (407.2–420.4) | 188.7 (186.1–197.3) | 0.48 | −202.6 | 524 / 343 |
| 500 | 968.6 (958.2–990.5) | 999.9 (931.8–1 027.0) | 398.0 (387.2–416.3) | 0.41 | −570.6 | 1 105 / 523 |

### shared-loop16 (16-node looping tree)

| N | Immediate | BatchedJobsSameFrame | JobsAgentFrame | AF / Imm | AF − Imm µs |
|---:|---:|---:|---:|---:|---:|
| 1 | 17.7 (14.6–18.5) | 37.9 (34.0–39.3) | 21.7 (17.6–23.6) | 1.23 | +4.0 |
| 10 | 52.1 (47.1–60.8) | 140.2 (132.2–148.0) | 52.4 (45.5–59.8) | 1.01 | +0.3 |
| 20 | 92.2 (83.0–101.4) | - | 63.8 (61.7–73.8) | 0.69 | −28.5 |
| 30 | 134.2 (121.0–151.9) | - | 74.3 (68.0–82.8) | 0.55 | −60.0 |
| 50 | 199.9 (190.6–231.1) | 312.9 (298.8–320.7) | 90.4 (85.4–100.0) | 0.45 | −109.5 |
| 100 | 387.3 (378.6–401.0) | 502.6 (490.0–517.7) | 144.4 (138.4–155.3) | 0.37 | −242.9 |
| 500 | 1 934.0 (1 873.4–2 111.4) | 2 027.7 (1 965.2–2 099.5) | 530.8 (506.5–581.1) | 0.27 | −1 403.2 |

### shared-reactive16 (16 reactive levels)

| N | Immediate | BatchedJobsSameFrame | JobsAgentFrame | AF / Imm | AF − Imm µs |
|---:|---:|---:|---:|---:|---:|
| 1 | 69.7 (67.7–72.4) | 259.5 (253.7–283.5) | 42.2 (39.1–45.2) | 0.61 | −27.5 |
| 10 | 549.8 (527.5–566.1) | 1 693.6 (1 593.1–1 851.3) | 110.3 (102.3–120.7) | 0.20 | −439.4 |
| 50 | 2 636.3 (2 533.5–2 735.8) | 4 454.7 (4 248.4–4 663.4) | 286.7 (274.9–302.3) | 0.11 | −2 349.6 |
| 100 | 5 226.2 (5 101.7–5 579.0) | 7 358.5 (7 258.5–7 594.1) | 519.8 (487.1–528.5) | 0.10 | −4 706.4 |
| 500 | 27 228.7 (26 748.2–27 587.1) | 32 609.0 (32 112.5–33 073.6) | 2 469.7 (2 401.2–2 776.3) | 0.09 | −24 759.1 |

### io-move1-fact-effect (reads a fact, emits an effect)

| N | Immediate | BatchedJobsSameFrame | JobsAgentFrame | AF / Imm | AF − Imm µs |
|---:|---:|---:|---:|---:|---:|
| 1 | 16.8 (16.1–19.7) | 28.5 (26.3–30.4) | 20.1 (18.8–22.7) | 1.19 | +3.3 |
| 10 | 44.6 (41.7–47.0) | 109.9 (108.4–113.8) | 54.0 (51.4–57.7) | 1.21 | +9.4 |
| 20 | 72.5 (71.5–82.2) | - | 64.2 (62.9–65.6) | 0.89 | −8.2 |
| 30 | 105.5 (97.1–113.6) | - | 73.0 (72.4–75.0) | 0.69 | −32.4 |
| 50 | 153.7 (148.6–170.6) | 202.4 (195.6–207.6) | 90.7 (85.8–95.9) | 0.59 | −63.0 |
| 100 | 275.8 (266.9–278.3) | 316.9 (312.5–320.6) | 139.4 (136.2–147.9) | 0.51 | −136.4 |
| 500 | 1 289.5 (1 259.9–1 301.8) | 1 397.5 (1 380.6–1 432.0) | 506.1 (495.5–516.1) | 0.39 | −783.4 |

All N of every tree: [`tables.md`](tables.md).

### 3.1 Crossover per tree

| Tree | JobsAgentFrame ≥10% faster from | More than 5% slower at | Worst absolute cost |
|---|---:|---|---:|
| `io-wait1` | N = 30 | N = 1 (+30%), 10 (+50%), 20 (+8%) | +16.5 µs at N = 10 |
| `shared-loop16` | N = 20 | N = 1 (+23%) | +4.0 µs at N = 1 |
| `shared-reactive16` | N = 1 | never | — |
| `io-move1-fact-effect` | N = 20 | N = 1 (+19%), 10 (+21%) | +9.4 µs at N = 10 |

Below the crossover JobsAgentFrame costs at most 17 µs more per frame than Immediate; at N = 1 it
is +3…+5 µs, ≈2% of the frame's wall time. From N = 50 on it is 30–89% cheaper for every tree.
Where it comes from (section 5.1): one schedule + complete costs ≈10 µs with one agent (the item runs
on the main thread) and ≈31–34 µs from N = 10 on (≈90% of the items run on workers, so the main
thread waits for the wake-up), against ≈2–4 µs per agent of Immediate's own work.

`BatchedJobsSameFrame` is slower than Immediate at every N ≤ 500 for every tree (+3…+272%).

## 4. Scale

Main-thread µs per frame / frame wall µs / process CPU µs per frame, 23 workers (runs: 4 at
N = 1 000 / 5 000, 2 at 20 000).

| Tree | N | Immediate | BatchedJobsSameFrame | JobsAgentFrame | Imm / AF |
|---|---:|---|---|---|---:|
| `io-wait1` | 1 000 | 2 006 / 2 219 / 2 625 | 1 969 / 2 157 / 3 906 | 898 / 1 005 / 2 875 | 2.2× |
| | 5 000 | 12 654 / 12 915 / 13 469 | 12 713 / 13 063 / 16 906 | 5 849 / 6 167 / 12 438 | 2.2× |
| | 20 000 | 61 494 / 62 052 / 62 344 | 72 273 / 73 026 / 89 219 | 35 897 / 36 509 / 74 792 | 1.7× |
| `shared-loop16` | 1 000 | 3 975 / 4 151 / 5 000 | 4 221 / 4 480 / 10 938 | 1 128 / 1 259 / 4 312 | 3.5× |
| | 5 000 | 23 287 / 23 546 / 24 250 | 24 922 / 25 248 / 33 656 | 7 795 / 8 159 / 22 719 | 3.0× |
| | 20 000 | 103 987 / 104 427 / 104 115 | 142 810 / 149 634 / 178 404 | 40 301 / 40 947 / 117 969 | 2.6× |
| `shared-reactive16` | 1 000 | 54 152 / 54 441 / 54 438 | 62 302 / 62 593 / 146 656 | 4 917 / 5 156 / 34 125 | 11.0× |
| | 5 000 | 265 051 / 265 323 / 264 391 | 383 064 / 383 452 / 534 060 | 24 128 / 24 595 / 180 562 | 11.0× |
| | 20 000 | 1 072 616 / 1 072 759 / 1 069 010 | 2 172 227 / 2 173 560 / 2 803 125 | 106 321 / 106 866 / 828 385 | 10.1× |
| `io-move1-fact-effect` | 1 000 | 2 656 / 2 892 / 3 344 | 2 684 / 2 843 / 5 000 | 1 165 / 1 313 / 3 719 | 2.3× |
| | 5 000 | 16 187 / 16 455 / 17 094 | 17 356 / 17 718 / 21 344 | 7 217 / 7 528 / 16 156 | 2.2× |
| | 20 000 | 84 679 / 85 193 / 85 365 | 99 035 / 99 678 / 112 500 | 46 936 / 47 647 / 92 240 | 1.8× |
| `m-shared-shape15` (managed) | 1 000 | 1 148 / 1 310 / 1 625 | | | |
| | 5 000 | 5 816 / 6 045 / 6 594 | | | |
| | 20 000 | 27 536 / 27 836 / 28 125 | | | |
| `m-unity15-both` (managed) | 1 000 | 1 150 / 1 278 / 1 594 | | | |
| | 5 000 | 7 465 / 7 709 / 7 875 | | | |
| | 20 000 | 36 398 / 37 046 / 36 979 | | | |

- JobsAgentFrame is the fastest native policy on the main thread at every N ≥ 1 000: 1.7–3.5× for the
  small trees, 10–11× for `shared-reactive16`. The ratio falls with N for the small trees (the
  managed begin/finish per agent grows with N, section 5.1).
- Frame wall time is 100–600 µs above the bracket for every policy (engine work outside the
  bracket); JobsAgentFrame does not add wall time beyond its bracket.
- `BatchedJobsSameFrame` is slower than Immediate at N = 20 000 (1.2–2.0×).

**Total CPU, zero-worker method** (`-JobWorkers 0`, bracket = all the agents' work on one thread,
µs per frame; JobsAgentFrame / Immediate in the last column):

| Tree | N | Immediate | BatchedJobsSameFrame | JobsAgentFrame | AF / Imm |
|---|---:|---:|---:|---:|---:|
| `io-wait1` | 1 000 | 1 998 | - | 1 165 | 0.58 |
| | 5 000 | 13 894 | - | 9 103 | 0.66 |
| | 20 000 | 61 001 | 75 824 | 44 262 | 0.73 |
| `shared-loop16` | 1 000 | 4 247 | - | 2 382 | 0.56 |
| | 5 000 | 24 808 | - | 14 953 | 0.60 |
| | 20 000 | 107 457 | 155 269 | 71 110 | 0.66 |
| `shared-reactive16` | 1 000 | 55 966 | - | 27 103 | 0.48 |
| | 5 000 | 286 888 | - | 130 241 | 0.45 |
| | 20 000 | 1 051 988 | 2 090 731 | 476 676 | 0.45 |
| `io-move1-fact-effect` | 1 000 | 3 108 | - | 2 113 | 0.68 |
| | 5 000 | 18 351 | - | 12 457 | 0.68 |
| | 20 000 | 75 283 | 99 821 | 66 146 | 0.88 |

JobsAgentFrame needs 12–55% less total CPU than Immediate at every measured N (the node bodies and
the framing run Burst-compiled). **Process CPU with 23 workers** tells the other half: for the small
trees at N = 1 000 process CPU under JobsAgentFrame is within ±15% of Immediate (`io-wait1` 2 875 vs
2 625 µs per frame, `shared-loop16` 4 312 vs 5 000, `io-move1-fact-effect` 3 719 vs 3 344), and at
N = 10…500 it is 0.3–1.2 ms per frame higher (for example `io-wait1` N = 100: 1 125 vs 719 µs): woken job workers spin
before they sleep. For `shared-reactive16` process CPU is lower at every N ≥ 1 000 (34 vs 54 ms at
N = 1 000). With 0 workers process CPU follows the bracket (1 469 vs 2 469 µs for `io-wait1`
N = 1 000). This matters on battery-powered devices; it was not measured there.

## 5. Worker count (simulation on a desktop CPU)

`JobsUtility.JobWorkerCount` set to 0, 1, 3 and 7 on the 24-thread desktop CPU, to approximate what a
1-, 2-, 4- and 8-core device sees (main thread plus N workers). **This is a simulation, not a
measurement on such a device**: the cores here are faster, the caches larger, the clock higher, and
other engine threads (render, audio) still have their own cores. One run per count for 1, 3 and 7
(so ±7% noise, and the series is not always monotonic); 2 runs for 0; 4 for 23.

Main-thread µs per frame under JobsAgentFrame (process CPU µs per frame in brackets):

| Tree | N | Immediate | AF, 0 workers | AF, 1 | AF, 3 | AF, 7 | AF, 23 |
|---|---:|---:|---:|---:|---:|---:|---:|
| `io-wait1` | 1 000 | 2 006 | 1 165 (1 469) | 1 082 (1 750) | 862 (1 312) | 962 (2 188) | 898 (2 875) |
| | 5 000 | 12 654 | 9 103 (10 188) | 7 134 (10 000) | 6 867 (10 625) | 5 633 (8 812) | 5 849 (12 438) |
| `shared-loop16` | 1 000 | 3 975 | 2 382 (2 781) | 1 628 (2 688) | 1 351 (3 062) | 1 138 (2 312) | 1 128 (4 312) |
| | 5 000 | 23 287 | 14 953 (15 531) | 11 254 (15 812) | 10 771 (20 750) | 7 687 (17 500) | 7 795 (22 719) |
| `shared-reactive16` | 1 000 | 54 152 | 27 103 (28 156) | 14 409 (25 812) | 10 733 (30 688) | 6 279 (27 750) | 4 917 (34 125) |
| | 5 000 | 265 051 | 130 241 (129 644) | 72 541 (126 625) | 44 993 (126 625) | 37 235 (150 938) | 24 128 (180 562) |
| `io-move1-fact-effect` | 1 000 | 2 656 | 2 113 (2 594) | 1 441 (2 375) | 1 457 (2 562) | 1 124 (2 000) | 1 165 (3 719) |
| | 5 000 | 16 187 | 12 457 (13 344) | 10 937 (15 938) | 7 904 (13 250) | 9 360 (12 625) | 7 217 (16 156) |

- Even with no worker at all, JobsAgentFrame is faster than Immediate on the main thread
  (0.49–0.80×), so a device with few cores does not lose: it gains less.
- One worker already brings the small trees to 0.41–0.68× of Immediate and `shared-reactive16` to
  0.27×; beyond 3 workers the small trees gain little more (the main thread is then bound by its own
  begin/finish and the rest of the frame, 5.1). `shared-reactive16` keeps gaining up to 23 workers.
- Immediate itself does not depend on the worker count (within run noise, `shared-reactive16`
  N = 1 000 54–62 ms).

### 5.1 Where the main thread goes under JobsAgentFrame

µs per frame, 23 workers. driver = the scheduler's agent-frame batches (`scheduler_drive_us`:
begin + schedule + wait + finish); job = schedule + complete wall time
(`AgentFrameDiagnosticsV1.jobMicroseconds`); begin/finish = driver − job; rest = bracket − driver −
feeder (due collection, world bridge and effect delivery, frame entries, `NativeTreeAgent`
component work).

| Tree | N | bracket | job | begin/finish | rest | begin/finish per agent | rest per agent |
|---|---:|---:|---:|---:|---:|---:|---:|
| `io-wait1` | 1 | 20 | 10 | 1 | 9 | — | — |
| | 10 | 49 | 32 | 4 | 13 | 0.37 | 1.35 |
| | 1 000 | 897 | 127 | 446 | 325 | 0.45 | 0.32 |
| | 5 000 | 5 849 | 648 | 3 154 | 2 047 | 0.63 | 0.41 |
| | 20 000 | 35 897 | 2 825 | 23 416 | 9 655 | 1.17 | 0.48 |
| `shared-loop16` | 1 000 | 1 128 | 206 | 427 | 495 | 0.43 | 0.50 |
| | 5 000 | 7 795 | 1 314 | 3 166 | 3 315 | 0.63 | 0.66 |
| | 20 000 | 40 301 | 5 346 | 20 978 | 13 977 | 1.05 | 0.70 |
| `shared-reactive16` | 1 000 | 4 917 | 2 169 | 2 031 | 717 | 2.03 | 0.72 |
| | 5 000 | 24 128 | 9 856 | 10 365 | 3 906 | 2.07 | 0.78 |
| | 20 000 | 106 321 | 47 571 | 40 710 | 18 041 | 2.04 | 0.90 |
| `io-move1-fact-effect` | 1 000 | 1 165 | 195 | 487 | 462 | 0.49 | 0.46 |
| | 5 000 | 7 217 | 938 | 3 209 | 2 875 | 0.64 | 0.57 |
| | 20 000 | 46 936 | 3 935 | 26 588 | 15 433 | 1.33 | 0.77 |

**The largest remaining main-thread cost is the managed per-agent begin/finish around the job**
(clock read, update id, copying the machine and slot handles into the agent table and back, trace
replay into the recorder, effect enqueue, terminal bookkeeping): 38–65% of the bracket for the small
trees, 38–43% for `shared-reactive16` (whose begin/finish is ≈2 µs per agent, consistent with replaying
many trace events per update). It grows from ≈0.45 µs per agent at N = 1 000 to 1.0–1.3 µs at
N = 20 000, faster than linearly; a likely cause is cache misses over thousands of managed host
objects (not verified). The second cost is the rest of the frame (0.3–0.9 µs per agent), the third
the wait for the job (8–45% of the bracket, largest for `shared-reactive16`).

*Estimate (an upper bound, not measured):* if begin/finish moved entirely into native code (a
persistent native agent table, trace replay only when a recorder listens), the bracket would lose
that share: at N = 1 000 the small trees would go from 0.9–1.2 µs to ≈0.45–0.7 µs per agent
(1.6–2× on the main thread), at N = 20 000 from 1.8–2.3 µs to ≈0.6–1.0 µs (2.1–2.9×);
`shared-reactive16` ≈1.7× at N = 1 000, then bounded by the job wait. The clock callback, the managed
`Faulted` and effect callbacks and the recorder's managed side cannot move without a semantic change,
so the real gain is below these figures.

## 6. Mixed population

N agents, half of them with a `StepBudget` (fallback `StepBudgetConfigured`, which runs the member as
Budgeted in its due position), 23 workers, 2 runs, µs per frame. "Expected" is the mean of the pure
Immediate and pure JobsAgentFrame runs of the same pass.

| Tree | N | Immediate | JobsAgentFrame | expected | alternating (every 2nd falls back) | vs expected | grouped (2nd half falls back) | vs expected |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| `io-wait1` | 1 000 | 2 253 | 1 054 | 1 653 | 3 843 | +132% | 1 644 | −1% |
| | 5 000 | 13 011 | 5 979 | 9 495 | 22 506 | +137% | 9 780 | +3% |
| `shared-loop16` | 1 000 | 4 248 | 1 107 | 2 677 | 5 599 | +109% | 3 007 | +12% |
| | 5 000 | 25 286 | 7 967 | 16 626 | 29 544 | +78% | 15 562 | −6% |
| `shared-reactive16` | 1 000 | 56 102 | 4 898 | 30 500 | 42 823 | +40% | 32 553 | +7% |
| | 5 000 | 263 951 | 23 703 | 143 827 | 212 012 | +47% | 146 151 | +2% |
| `io-move1-fact-effect` | 1 000 | 2 998 | 1 223 | 2 110 | 5 163 | +145% | 2 202 | +4% |
| | 5 000 | 15 876 | 7 074 | 11 475 | 24 735 | +116% | 12 190 | +6% |

**A fallback member closes the current job batch** (`ProductionTreeScheduler.DriveAgentFrameGroup`
runs the batch collected so far, then the fallback member, to keep due order). With fallbacks
spread through the due order every batch holds one agent (`agentFrameStats.batches` = number of
eligible agents per frame), each pays a full schedule + complete, and the population is **1.3–1.7×
slower than all-Immediate** for the small trees (`shared-reactive16` stays faster than Immediate).
Grouped, the cost is the expected mix within ±12%. This is current behavior, not a defect, but it
matters for any automatic selection (section 10).

## 7. Non-quick scenarios under Immediate

Pass `full5k`: the non-quick P9 plan under the default policy (Immediate) once, without N > 5 000
(cut, section 2) and without the jobs, pipelined and agent-frame variants. 1 run, 0 faults in every
case, 23 workers. P10-001 "after" values were measured on Unity 6000.7.0b1 with another Player: **not
a before/after comparison**, the column is there only to show that nothing is far off. Main-thread
µs per frame (the full list is in `tables.md` and `perf-data/P10-005/j3-full5k.json`):

| Scenario | N | P10-001 "after" (6000.7) | this build (6000.6.9f1) | difference |
|---|---:|---:|---:|---:|
| `m-unity15-both` | 1 000 | 1 203 | 1 282 | +7% |
| | 5 000 | - | 7 975 | |
| `m-shared-shape15` | 1 000 | 1 029 | 1 113 | +8% |
| `m-shared-reactive16` | 1 000 | 9 633 | 10 042 | +4% |
| `m-shared-reactive64` | 1 000 | 39 254 | 39 136 | −0.3% |
| `m-realistic` | 1 002 | 1 148 | 1 022 | −11% |
| `m-unity15-both-interval100ms` | 1 000 | 78 | 73 | −6% |
| | 5 000 | 363 | 329 | −9% |
| `m-unity15-both-interval100ms-stagger` | 1 000 | 81 | 81 | 0% |
| | 5 000 | 416 | 371 | −11% |
| `n-io-wait1` (per agent) | 1 000 / 5 000 | 3.8 / 4.4 (before P10-002) | 1.89 / 2.52 | P10-002 |
| `n-shared-loop16` (per agent) | 1 000 / 5 000 | 8.3 / 9.0 (before P10-002) | 4.00 / 4.96 | P10-002 |

Other cases, this build only: `m-shared-loop16` 1 221 / 7 013 (N = 1 000 / 5 000),
`m-shared-reactive16` 50 484 at 5 000, `m-shared-reactive2` 1 155, `n-shared-reactive16` 53 739 /
269 716, `n-shared-reactive64` 213 134, `n-native15` 9 545 / 51 526, `n-shared-shape15` 7 410 /
42 197, `n-native15-fact` 16 174 / 79 284, `n-io-move1-fact-effect` 2 609 / 16 312, `api-only`
262 at 1 002.

- No case is more than 10% above its P10-001 value; the two above 5% (`m-unity15-both`,
  `m-shared-shape15`) are single runs on another Unity version, within the −6…+11% the quick set
  shows between the versions (section 9). Not a regression finding.
- Native Immediate is about half of the pre-P10-002 values; that is P10-002's work, mixed with the
  version change, and not separated here.

## 8. Cadence with stagger under JobsAgentFrame

The native layer has `UpdateCadence` (frames), not `Interval`/`StaggerIntervalTicks`. Profile with
cadence 6, Immediate (no forced policy) or forced JobsAgentFrame; "stagger" enables the agents in 6
portions over 6 frames. 2 runs, 600 measured frames, µs per frame:

| Tree | N | policy | stagger | mean | p99 | max |
|---|---:|---|---|---:|---:|---:|
| `io-wait1` | 1 000 | Immediate | off | 542 | 2 691 | 3 282 |
| | | Immediate | on | 549 | 689 | 891 |
| | | JobsAgentFrame | off | 344 | 1 378 | 1 534 |
| | | JobsAgentFrame | on | 388 | 512 | 605 |
| | 5 000 | Immediate | off | 3 108 | 15 080 | 19 241 |
| | | Immediate | on | 2 980 | 3 322 | 4 078 |
| | | JobsAgentFrame | off | 2 112 | 9 550 | 9 945 |
| | | JobsAgentFrame | on | 2 195 | 2 625 | 3 079 |
| `shared-loop16` | 1 000 | Immediate | off | 874 | 15 155 | 24 699 |
| | | Immediate | on | 893 | 4 217 | 4 383 |
| | | JobsAgentFrame | off | 429 | 3 712 | 6 112 |
| | | JobsAgentFrame | on | 482 | 1 205 | 1 330 |
| | 5 000 | Immediate | off | 5 097 | 79 860 | 125 450 |
| | | Immediate | on | 4 988 | 21 594 | 22 450 |
| | | JobsAgentFrame | off | 2 759 | 22 443 | 34 614 |
| | | JobsAgentFrame | on | 2 787 | 6 267 | 6 917 |

- **The combination works**: 0 faults; staggered and unstaggered JobsAgentFrame runs dispatch the
  same number of times (for example `shared-loop16` N = 1 000: 254 000 dispatches in both), staggered
  in 6× as many, 6× smaller batches (660 vs 110 per window).
- Stagger cuts p99 by 2.7–4.5× and the worst frame by 2.5–5.6× under both policies; the mean
  changes by −4…+13%. JobsAgentFrame with stagger has the lowest p99 in every case.
- A cadence-6 frame costs far more than 1/6 of an every-frame update: `io-wait1` N = 1 000 Immediate
  542 µs against 2 006 / 6 = 334 µs. The scheduler visits every registration every frame to find the
  due ones (≈0.2 µs per registered agent per frame here; an inference from these numbers, not
  profiled).
- `shared-loop16` keeps a high p99 even staggered (Immediate 21.6 ms at N = 5 000): its updates are
  uneven (loop iterations) and the agents stay in step inside the loop; not investigated.

## 9. Relation to P10-004 (another Unity version)

N = 1 000 and 5 000 here use the quick-set configuration (120 / 250 frames). Main-thread µs per frame,
P10-004 on Unity 6000.7.0b1 (mean of A1 and A2) next to this build on 6000.6.9f1.
**Not a before/after comparison**: Unity version, Library and Player differ.

| Tree | N | Immediate 6000.7 / 6000.6 | BatchedJobsSameFrame | JobsAgentFrame |
|---|---:|---|---|---|
| `io-wait1` | 1 000 | 1 939 / 2 006 | 1 998 / 1 969 | 922 / 898 |
| | 5 000 | 12 761 / 12 654 | 12 793 / 12 713 | 5 820 / 5 849 |
| `shared-loop16` | 1 000 | 3 638 / 3 975 | 3 814 / 4 221 | 1 050 / 1 128 |
| | 5 000 | 22 715 / 23 287 | 25 279 / 24 922 | 7 780 / 7 795 |
| `shared-reactive16` | 1 000 | 53 192 / 54 152 | 62 232 / 62 302 | 4 956 / 4 917 |
| | 5 000 | 264 714 / 265 051 | 381 465 / 383 064 | 24 729 / 24 128 |
| `io-move1-fact-effect` | 1 000 | 2 636 / 2 656 | 2 748 / 2 684 | 1 135 / 1 165 |
| | 5 000 | 16 992 / 16 187 | 17 549 / 17 356 | 7 566 / 7 217 |
| `m-shared-shape15` | 1 000 / 5 000 | ≈1 030 / ≈6 190 → 1 148 / 5 816 | | |
| `m-unity15-both` | 1 000 / 5 000 | ≈1 170 / ≈7 480 → 1 150 / 7 465 | | |

The two builds land within −6…+11% of each other case by case, no systematic direction; the
policy ratios are the same.

## 10. Analysis and proposal

### 10.1 What the numbers say

1. **Crossover.** JobsAgentFrame is ≥10% faster than Immediate on the main thread from N = 20
   (`shared-loop16`, `io-move1-fact-effect`), N = 30 (`io-wait1`) and N = 1 (`shared-reactive16`).
   Below that it costs +3…+17 µs per frame (up to +50% of a 33 µs frame cost). From N = 50 on it
   is 1.4–9× faster, at N ≥ 1 000 1.7–11×.
2. **It never costs total CPU in the work itself.** On one thread it needs 12–55% less CPU than
   Immediate at every N. With many workers the process burns more CPU in idle-spinning workers for
   small trees (+0.3…+1.2 ms per frame at N = 10…500).
3. **Few cores.** Faster than Immediate even with 0 workers (simulation); most of the gain for small
   trees is there from 1–3 workers.
4. **Fallbacks inside a group are the one way to lose.** Interleaved fallback members split every
   batch: 1.3–1.7× slower than plain Immediate for small trees.
5. `BatchedJobsSameFrame` is never the best native policy in these trees (slower than Immediate at
   N ≤ 500 and at 20 000, ≈equal at 1 000–5 000) and is dominated by JobsAgentFrame everywhere.

### 10.2 Options for the owner

**(a) Keep JobsAgentFrame manual (status quo).**
- Changes: nothing; documentation gets the "which policy when" table (done in this card).
- Risk: none. Cost: users who do not read the docs keep 2–11× more main-thread time.
- Test first: nothing.

**(b) Let the automatic selector pick JobsAgentFrame for eligible hosts above a threshold.**
- Rule proposed: a host whose profile has no forced policy, generated dispatch, a catalog with the
  agent-frame entry and no `StepBudget` (`GetAgentFrameFallback() == None`) runs as JobsAgentFrame
  when its **contiguous run of eligible members in the due order** (same profile and catalog) has at
  least **64 members**; shorter runs stay Immediate. 64 is ≈2× the largest crossover (30), and every
  measured tree is ≥30% faster at 50. Counting contiguous eligible members, not total N, is what
  keeps option 4 of 10.1 from turning into a regression; it needs a small change in
  `DriveAgentFrameGroup` (run a short batch in place as Immediate, which is observably identical).
- What users would observe:
  - **Clock read order.** In a job batch every member's clock callback runs before the job, not just
    before its own update. Only a custom clock that depends on other agents' updates in the same
    frame sees the difference; Unity's default clock does not.
  - **Regenerated catalogs.** Only catalogs generated by P10-004 or later have the entry. Old
    catalogs must not be auto-selected and must not log the fallback warning (today a forced
    JobsAgentFrame warns once per host; an automatic choice should fall back silently, or every
    project with old catalogs gets thousands of warnings).
  - **Fallback warnings.** Same: automatic selection must never produce them; they stay for an
    explicitly forced profile.
  - **Diagnostics.** `SchedulerFrameEntry.Policy` reports JobsAgentFrame;
    `LastMeasuredMicroseconds` of a member becomes the batch average, so `MaximumBudgetShare` and
    the global frame budget admit job batches by average cost (the admission code already handles
    this for forced JobsAgentFrame, but default-profile users would now meet it).
  - **Threads and CPU.** Agent work moves to job workers and competes with the game's own jobs;
    process CPU rises for small trees at N = 64…500 with many workers.
  - Results, traces, effects and their order are identical (P10-004 equivalence tests).
- Risk: medium — default behavior changes for every eligible project at N ≥ 64. The corruption-path
  failure codes and cross-request ledger floors (P10-004 section 6) become reachable by default.
- Test first: equivalence of auto-selected versus Immediate on the existing digest test; the
  threshold boundary (63/64 contiguous members, interleaved fallbacks); no warning for old catalogs
  on the automatic path; budget admission with `MaximumBudgetShare`; a Player measurement on a 4- or
  8-core mobile device (Android IL2CPP) with the game's own jobs running; the 6000.6 compile fix
  (0.1) before any of it, since the package does not compile on the owner's current Unity.

**(c) Make JobsAgentFrame the default for `NativeTreeAgent` only.**
- Changes: `NativeTreeAgent` gets `SchedulingProfile.JobsAgentFrame` unless the user assigns a
  profile; direct `ProductionTreeHost` users keep Immediate.
- What users would observe: everything in (b), at every N, including N < 30 where it costs
  +3…+17 µs per frame; and old catalogs fall back with the warning (unless that is also changed).
- Risk: higher than (b) for small populations and for projects with old catalogs; simpler to
  explain ("the component uses jobs").
- Test first: as (b), plus the small-N regression accepted explicitly, and the warning behavior.

### 10.3 Recommendation

**(b), in two steps, after the 6000.6 compile fix.** First the in-place Immediate run of short
batches (no observable change, removes the interleaved-fallback regression and the N < 30 overhead
for forced users too), then the selector rule with the 64-member threshold and silent fallback.
Until both are tested, keep (a) and recommend the profile in the docs for populations of 64+ agents
(done in `Documentation~/performance.md`). (c) is not recommended: it pays the small-N overhead for
the benefit of the one-line change, and (b) gets the same users above the threshold.

### 10.4 Native against managed, per agent

Main-thread µs per agent and frame, 23 workers:

| Tree | N | native Immediate | native JobsAgentFrame | managed |
|---|---:|---:|---:|---:|
| `io-wait1` (native) / `shared-shape15` (managed, 15 nodes) | 1 000 | 2.0 | 0.90 | 1.15 |
| | 5 000 | 2.5 | 1.17 | 1.16 |
| | 20 000 | 3.1 | 1.79 | 1.38 |
| `shared-loop16` | 1 000 | 4.0 | 1.13 | 1.22 |
| | 5 000 | 4.7 | 1.56 | 1.40 |
| `shared-reactive16` | 1 000 | 54 | 4.9 | 10.0 |
| | 5 000 | 53 | 4.8 | 10.1 |

The managed `shared-loop16` and `shared-reactive16` values come from the single `full5k` run, the
others from 4 runs. Under JobsAgentFrame the native layer is at about the managed cost per agent up
to N = 5 000 (0.9–1.1× of managed for the small trees) and 2× cheaper for `shared-reactive16`; at
N = 20 000 it is above managed (1.79 against 1.38), where the native begin/finish grows (5.1).
Managed `loop16` / `reactive16` at N = 20 000 were not measured. Under Immediate the native layer
stays 1.7–5.4× above the managed layer.

## 11. Verification

- `Tools~/Verification/Verify-Static.ps1`: passed (JSON schemas 7, work items 169).
- No file under `Runtime/`, `Agents/`, `Authoring/`, `Editor/` or `Analyzers/` changed in this card
  (only `Benchmarks~/`, `Planning~/`, `Documentation~/performance.md` and `CHANGELOG.md`), so the
  EditMode/PlayMode tests were not rerun. `ExpectedPublicAbiV2.txt`, the package version, scheduler
  defaults and public APIs are unchanged.
- Every pass: Player exit 0, 0 faults; the runner left no `AibtPerf` process (checked after the last
  pass). The package copy check (byte-equal `Runtime/`, `Agents/`, `Authoring/`, `Analyzers/`) and
  the one-line patch are in `perf-data/P10-005/package-copy-check.txt` and `package-copy-patch.diff`.

## Not done / not verified

- **No real device.** Worker counts 0/1/3/7 are `-JobWorkers` on a 24-thread desktop CPU, a
  simulation of fewer cores, not a mobile measurement; no Android/iOS IL2CPP run.
- Worker counts 1, 3 and 7 have one run each; the full-plan refresh (section 7) is one run and
  skips N > 5 000; the separate 23-worker `j3-workers` repeat was cut (scale pass covers it).
- Worker-thread time per thread was not measured; only process CPU (15.6 ms timer resolution, so
  per-frame values at small N are coarse) and the zero-worker method.
- Managed `shared-loop16` / `shared-reactive16` at N = 20 000 not measured.
- The cause of the begin/finish growth with N (5.1) and of the cadence fixed cost (section 8) is an
  inference from the numbers, not profiled; the `shared-loop16` staggered p99 was not investigated.
- The 6000.6 compile fix (0.1) is not made; the measurement used a patched package copy.
- P10-001 raw tables are gone (their Temp folder was wiped); section 7 compares to the values quoted
  in P10-001 evidence only.
- Mono backend not measured.
- Nothing of 10.2/10.3 is implemented: no selector, no in-place short-batch run, no default change.
