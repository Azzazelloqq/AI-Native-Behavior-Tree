# P12-008 evidence — the Shared contribution allocation test

Modules on Unity 7000.0.0a8 (CoreCLR, `.NET 10.0.10-dev`), 2026-11-25.

## The failure (orchestrator's full run on `dccab95`)

```
assembly AIBT.NativeSharedBlackboard.Tests 35/36 (failed=1 skipped=0)
  FAIL AIBT.Tests.Runtime.NativeExecution.Blackboard.Shared.NativeSharedContributionTests.InitializedContributionLifecycleAllocatesNoManagedMemory
```

It was reproduced in this session, before the change:

```
FAIL ...NativeSharedContributionTests.InitializedContributionLifecycleAllocatesNoManagedMemory
    Expected: 0
    But was:  6144
  at ...NativeSharedContributionTests.cs:line 345
```

## Per-step measurement

The measurement was a temporary test, `TempProbe_P12AllocationPerStep`, in `NativeSharedContributionTests`.
It is removed again, and the file is back to its committed text before the change.

- It repeated the steps of `RunOne` (and of the fixture's `TryBegin`, `TryAcquire` and `TryAbort`) for 33
  cycles.
- Each step was wrapped in `GC.GetAllocatedBytesForCurrentThread()`.
- The first cycle was not counted.
- NUnit asserts were replaced in the probe by a non-allocating check, so they are not in the measured spans.
- An empty `Mark() - Mark()` gave 0.

Output (Console, 23:25):

```
variant owner per cycle (as RunOne), 32 cycles after one warm-up; empty Mark()-Mark() = 0
  owner.TryCreate 6144 B (192 per cycle)
  entries Temp 0 B (0 per cycle)
  SelectionOwner.TryBegin 0 B (0 per cycle)
  Context.TryBeginUpdate 0 B (0 per cycle)
  TryAcquireExecutionLeaseV2 0 B (0 per cycle)
  TryAcquireContributionStream 0 B (0 per cycle)
  TryContribute 0 B (0 per cycle)
  TryRegisterDependency 0 B (0 per cycle)
  TrySealContributionStream 0 B (0 per cycle)
  TryReleaseExecutionLease 0 B (0 per cycle)
  Context.TryAbortUpdate 0 B (0 per cycle)
  SelectionOwner.TryAbort 0 B (0 per cycle)
  SelectionOwner.TryDispose 0 B (0 per cycle)
variant one owner reused, 32 cycles after one warm-up; empty Mark()-Mark() = 0
  (every step 0 B)
```

The 6144 B of the failure are exactly the 32 owner creations of the fixture. The runtime steps of the lifecycle
allocate nothing.

## Run after the change

`Tools/Unity/Invoke-UnityTests.ps1`, 23:27:

```
testName NativeSharedContributionTests 9/9 (failed=0 skipped=0)
```

- No warning from the changed file in `Logs/Editor.log`.
- No Console error during the run.

## The two skipped Integration tests of the full run

They are the two opt-in benchmarks. Each calls `Assert.Ignore` unless its Editor environment variable is set.
Run alone at 23:28, both were reported skipped:

- `TreeSimulationWakeCostBenchmark.WakeOnChangeCost_500Agents_Spread5` (P12-001, `AIBT_P12001_BENCHMARK`):
  `P12-001 measurement: set the Editor process environment variable AIBT_P12001_BENCHMARK=1 to run it.`
- `TreeSimulationIndexedInputBenchmark.IndexedAgainstSlotRows_500Agents_50Squads_Spread5` (P12-002,
  `AIBT_P12002_BENCHMARK`).

```
testName TreeSimulationIndexedInputBenchmark 0/1 (failed=0 skipped=1)
testName TreeSimulationWakeCostBenchmark 0/1 (failed=0 skipped=1)
```

## Not verified

- A Player build: the owner would be allocated there too, once per owner, but this was not measured.
- The full suite was not rerun.

## Acceptance (orchestrator, 2026-11-25, Unity 7000.0.0a8)

Accepted on `1bed4a4` as part of the batch `8396202..1bed4a4` (the P12-002 review follow-up and P12-003 to
P12-008); the status stays `Review` as for every item of phases 11 and 12.

- **Diff read.** No file under `CodeGen~`, `Tests/Fixtures` or `Documentation~/specifications` changed, nor
  `ExpectedPublicAbiV2.txt` or `package.json`; no existing public signature changed. Runtime code: one
  comparison that was false for every value removed (`NativePipelinedPhaseControllerV1.cs`, P12-005), a
  version-conditional call with the same result (`McpRuntimeTools.cs`), `new` on `BtNodeAttribute.TypeId`, and
  doc comments.
- **Full run of the ten assemblies on `1bed4a4`, 23:41-23:49:** Runtime 821/821, Integration 792 passed and
  2 skipped (the two opt-in benchmarks), Editor 917/917, BehaviorCases 65/65, NativeBurstDispatch 79/79,
  NativeTreeAgentBlackboard 33/33, NativeSharedBlackboard 36/36, CodeGen.ContractTests 6/6,
  CodeGen.GenerationTests 14/14, PlayMode 2/2; no console noise. Raw:
  `C:\UnityProjects\_aibt_acceptance\P12-batch\full-run-1bed4a4-unity7.txt`. The run before it, on `dccab95`
  (`full-run-dccab95-unity7.txt`), had two failures, both in tests the batch had not touched and both caused by
  the test on CoreCLR, not by the package: P12-007 and P12-008.
- **Independent read** of `8396202..dccab95` (a reader given the commits and neutral questions; it ran
  nothing): `C:\UnityProjects\_aibt_acceptance\P12-batch\independent-review.md`. No blocker; its should-fix
  findings and weak tests were taken in `3caa473`, `892cc6c`, `a1b1c1a` and `1bed4a4`. Those four commits and
  the two test fixes `7595b51` and `45dd89f` were read by the orchestrator, not by a second reader.
- **Cold Burst cache (P12-004)** was run by the orchestrator: the Editor started with `Library/BurstCache/JIT`
  moved aside and `BurstExecutionPathTests` as the first run; old test 1/3, changed test 3/3. Raw:
  `C:\UnityProjects\_aibt_acceptance\P12-004\cold-A-old-test.txt`, `cold-B-fixed-test.txt`.
- **What the acceptance does not cover.** Nothing here was compiled or run on 6000.x: Modules moved to
  7000.0.0a8 on 2026-11-25 and every earlier record is from 6000.6.9, so the two sets of evidence are from
  different Editors. No Player (IL2CPP) run. The tooling changed with the move (Pipeline package 1.0.0, the
  `Tools/Unity` scripts of the parent repository), so this is also the first full run through that tooling.
