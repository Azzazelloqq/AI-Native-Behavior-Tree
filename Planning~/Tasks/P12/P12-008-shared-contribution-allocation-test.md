# P12-008 — Shared contribution allocation test counts its own fixture on CoreCLR

Status: `Review` — filed and done 2026-11-25 from the orchestrator's full run on `dccab95` (Unity 7000.0.0a8).
Test only. Acceptance is the orchestrator's.

## What failed

`NativeSharedContributionTests.InitializedContributionLifecycleAllocatesNoManagedMemory`
(`Tests/Runtime/NativeExecution/Blackboard/Shared/NativeSharedContributionTests.cs:345` at `dccab95`):
- expected 0 allocated bytes, measured 6144;
- that is 192 bytes for each of the 32 `RunOne` calls.

## Why it shows now

- The test reads `GC.GetAllocatedBytesForCurrentThread`. On Mono that counter returned 0 in this Editor, so the
  assertion could not fail there.
- The Editor of 7000.0.0a8 runs on CoreCLR (`.NET 10.0.10-dev`), where the counter works.

## Where the 192 bytes come from (measured)

A temporary probe test in the same class repeated `RunOne`'s steps one by one, 32 cycles after one warm-up,
with the same counter around each step. NUnit asserts were kept out of the measured spans. The probe is
removed; its output is in the evidence.

- With a selection-window owner created per cycle, as `RunOne` did through `ContributionFixture.TryBegin`:
  - `NativeExecuteSelectionWindowOwnerV1.TryCreate` took 6144 B, 192 per cycle.
  - Every other step took 0:
    - the `Allocator.Temp` entries;
    - `SelectionOwner.TryBegin`, `Context.TryBeginUpdate`;
    - `TryAcquireExecutionLeaseV2`, `TryAcquireContributionStream`;
    - `TryContribute`, `TryRegisterDependency`, `TrySealContributionStream`;
    - `TryReleaseExecutionLease`;
    - `Context.TryAbortUpdate`, `SelectionOwner.TryAbort`, `SelectionOwner.TryDispose`.
- With one owner created before the loop and reused: every step took 0.

So the bytes are the test fixture's.

- `NativeExecuteSelectionWindowOwnerV1` is a `sealed class`
  (`Runtime/State/Native/NativeExecuteSelectionWindowV1.cs:90`).
- `TryCreate` allocates it (`:168`), and the fixture created one for every update.
- The runtime path the test names (contribution, dependency, seal, leases) allocates nothing.

Creating an owner is set-up: no runtime code calls `NativeExecuteSelectionWindowOwnerV1.TryCreate`. Only tests
create owners; `NativeSharedContextOwnerV1` and `NativeAgentContextV1` take one as an argument. A Player would
allocate the owner object too, once per owner, not per update.

## Change (test only)

- `InitializedContributionLifecycleAllocatesNoManagedMemory`:
  - creates one selection-window owner before the warm-up and the measured loop;
  - passes it to `RunOne`;
  - disposes it after.
- `ContributionFixture.TryBegin(NativeExecuteSelectionWindowOwnerV1 reused)`:
  - with `null` (the old `TryBegin()`) it creates and owns an owner as before;
  - otherwise it uses the given one, and `TryAbort`/`Dispose` leave it alive.
- Every other test of the class takes the old path unchanged.

## The other allocation measurements on Unity 7

- `GC.GetAllocatedBytesForCurrentThread` is used by three other tests, each next to the `GC.Alloc` recorder:
  - `TreeSimulationTests` (`:254`);
  - `TreeSimulationIoTests` (`:442`);
  - `TreeSimulationActionTests` (`:1455`).

  The orchestrator's "ten" does not match the code; the other allocation tests use the `GC.Alloc` profiler
  recorder or `GcAllocIs.Not.AllocatingGCMemory()`.
- On Unity 7 the counter is live: in the same Editor it measured the 192 B of an owner. Those three assertions
  therefore now assert something.
- None passes by measuring nothing. Each measures 100 warmed-up ticks and asserts the work done in them:
  - root completions > 50;
  - commands read > 1000;
  - actions started > 1000, completions consumed > 200, cancelled > 500.
- The recorder-based checks still detect allocations on Unity 7: their canaries passed in the full run. For
  example, `NativeExecutionAllocationTests` "allocation instrumentation canary" in `AIBT.Runtime.Tests` 821/821.

## Forbidden changes

Runtime code (none needed), `Tests/Fixtures`, `package.json`.

## Evidence

`Planning~/Evidence/P12-008/README.md`.
