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
