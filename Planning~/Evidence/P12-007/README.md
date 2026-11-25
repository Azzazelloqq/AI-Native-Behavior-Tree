# P12-007 evidence — the ABI layout pin test on CoreCLR

Modules on Unity 7000.0.0a8, 2026-11-25.

## The failure (orchestrator's full run on `dccab95`)

From `C:\UnityProjects\_aibt_acceptance\P12-batch\full-run-dccab95-unity7.txt`:

```
assembly AIBT.CodeGen.ContractTests 5/6 (failed=1 skipped=0)
  FAIL AIBT.Tests.CodeGen.Contracts.PublicBurstAbiContractTests.PinnedLayouts_ContextPrefixes_AndDefaultOpaqueValues_FailClosed
    at ...PublicBurstAbiContractTests.cs:line 77
```

The message, from the orchestrator: `System.ArgumentException : The specified Type must not be a generic type. (Parameter 'T')`.

## Measurement in the running Editor

The measurement was a `unity command script eval --file` of a scratch script (`Temp/p12-abi-measure.cs` of
Modules, not kept). It read each type through `Marshal.SizeOf(Type)`, `Marshal.SizeOf(object)`,
`UnsafeUtility.SizeOf<T>()`, `Unsafe.SizeOf<T>()`, `UnsafeUtility.SizeOf(Type)`, `Marshal.OffsetOf` and
`UnsafeUtility.GetFieldOffset`. Output, trimmed to the pinned types:

```
runtime=.NET 10.0.10-dev mono=False
BlackboardReadHandle`1<Int32> Marshal.SizeOf(Type)=ArgumentException: The specified Type must not be a generic type. (Parameter 't') Marshal.SizeOf(obj)=8 UnsafeUtility.SizeOf<T>=8 Unsafe.SizeOf<T>=8 UnsafeUtility.SizeOf(Type)=8
   ._ordinal UInt32 Marshal.OffsetOf=0 UnsafeUtility.GetFieldOffset=0
   ._accessToken UInt32 Marshal.OffsetOf=4 UnsafeUtility.GetFieldOffset=4
BurstCatalogValidationResult<> Marshal.SizeOf(Type)=4 Marshal.SizeOf(obj)=4 UnsafeUtility.SizeOf<T>=4 Unsafe.SizeOf<T>=4 UnsafeUtility.SizeOf(Type)=4
BurstExecutionResult<> Marshal.SizeOf(Type)=16 Marshal.SizeOf(obj)=16 UnsafeUtility.SizeOf<T>=16 Unsafe.SizeOf<T>=16 UnsafeUtility.SizeOf(Type)=16
   ._codeWord 0/0  ._diagnosticNumber 2/2  ._instancesVisited 4/4  ._segmentSteps 8/8
BurstEnterContext<> Marshal.SizeOf(Type)=1632 ... UnsafeUtility.SizeOf<T>=1632
   ._validationToken 0/0  ._randomState 8/8  ._randomIncrement 16/16  ._runtime 24/24  ._frameId 1624/1624  ._role 1628/1628
BurstTickContext<> (same as BurstEnterContext)
```

- **The other handles.** `BlackboardWriteHandle`, `BlackboardReadWriteHandle`, `SnapshotReadHandle`,
  `CommandHandle` and `CompletionHandle` of `Int32`, and `AsyncOperationHandle<Int32,Int32>`, give the same
  lines as `BlackboardReadHandle`: `SizeOf(Type)` throws, every other measure gives 8, and the fields are at
  0 and 4.
- **Every pinned number holds on Unity 7.** No ABI finding.

## Run after the change

`Tools/Unity/Invoke-UnityTests.ps1`, 23:27:

```
testName PublicBurstAbiContractTests 4/4 (failed=0 skipped=0)
```

- No warning from the changed file in `Logs/Editor.log`.
- No Console error during the run.

## Not verified

- **On Mono.** The changed lines were not run on Mono (6000.x cannot be compiled in Modules).
  `Marshal.SizeOf(object)` and `UnsafeUtility.SizeOf<T>()` exist there, and the old Mono run passed the same
  size through `Marshal.SizeOf<T>()`.
- **The full suite was not rerun.**

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
