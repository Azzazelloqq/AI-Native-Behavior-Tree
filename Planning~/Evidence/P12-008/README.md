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
