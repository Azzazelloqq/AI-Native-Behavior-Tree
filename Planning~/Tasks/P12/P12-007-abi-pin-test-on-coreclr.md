# P12-007 — The ABI layout pin test on CoreCLR (Unity 7)

Status: `Review` — filed and done 2026-11-25 from the orchestrator's full run on `dccab95` (Unity 7000.0.0a8).
Test only. Acceptance is the orchestrator's.

## What failed

`PublicBurstAbiContractTests.PinnedLayouts_ContextPrefixes_AndDefaultOpaqueValues_FailClosed` failed with
`System.ArgumentException : The specified Type must not be a generic type. (Parameter 'T')`, at
`Tests/Editor/CodeGen/Contracts/PublicBurstAbiContractTests.cs:77`, which is
`Marshal.SizeOf<BlackboardReadHandle<int>>()`.

## Cause

- **The runtime.** The Editor of 7000.0.0a8 runs on CoreCLR: `RuntimeInformation.FrameworkDescription` is
  `.NET 10.0.10-dev`, and `Mono.Runtime` is absent.
- **The API refuses generics there.**
  - CoreCLR's `Marshal.SizeOf<T>()` and `Marshal.SizeOf(Type)` refuse any generic type, closed ones included.
  - Mono answered for a closed generic struct, so the line passed on 6000.x.
- **What it means.** The check could not run on Unity 7, so the failure said nothing about the layout.
- **The rest of CoreCLR's `Marshal` accepts the same type** (measured in the Editor):
  - `Marshal.SizeOf(object)` with an instance;
  - `Marshal.OffsetOf(Type, string)`.

## The pins on Unity 7 (measured in the running Editor, 2026-11-25)

Every pin of the test holds. The ABI is unchanged.

| Pin | `Marshal` | `UnsafeUtility` / `Unsafe.SizeOf` |
| --- | --- | --- |
| `BlackboardReadHandle<int>` size | `SizeOf(object)` 8; `SizeOf(Type)` throws | 8 |
| `BlackboardReadHandle<int>` fields `_ordinal`, `_accessToken` | 0, 4 | 0, 4 |
| `BurstEnterContext` and `BurstTickContext` `_validationToken`, `_randomState`, `_randomIncrement` | 0, 8, 16 | 0, 8, 16 |
| `BurstCatalogValidationResult` size | 4 | 4 |
| `BurstExecutionResult` size (fields 0, 2, 4, 8) | 16 | 16 |

- The other handles (`BlackboardWriteHandle`, `BlackboardReadWriteHandle`, `SnapshotReadHandle`,
  `CommandHandle`, `CompletionHandle` of `int`, and `AsyncOperationHandle<int,int>`) are 8 bytes with fields
  at 0 and 4, read the same ways.
- The two contexts are 1632 bytes in full. The manifest pins only their prefix and `size=0`
  (`ExpectedPublicAbiV2.txt`, unchanged).

## Change

The one line becomes two assertions of the same 8-byte pin, each valid on Mono and on CoreCLR:

- `Marshal.SizeOf((object)default(BlackboardReadHandle<int>))` measures the marshalled size, as before.
- `UnsafeUtility.SizeOf<BlackboardReadHandle<int>>()` measures the layout that Burst and native code use.

Nothing else in the test changes, and every pinned number stays.

## The same pattern elsewhere

Searched for `Marshal.SizeOf` and `Marshal.OffsetOf` in `Assets/AIBT`.

- **Runtime:** `Runtime/Simulation/SimulationProgramMapV1.cs:359`, `Marshal.SizeOf(type)`.
  - Reached only for `type.IsPrimitive`, after `char`, `IntPtr` and `UIntPtr` are refused and `bool` is
    taken as 1.
  - Never a generic type, so it is safe on both runtimes. Not changed.
- **Tests:** `Tests/Runtime/NativeExecution/Blackboard/Shared/NativeSharedContextTests.cs:311`,
  `Marshal.SizeOf<T>()` in `AssertSharedDefault<T>`.
  - `T` is a built-in value type or `SharedRegisteredFixture` (non-generic), so it does not throw.
  - For `bool`, `Marshal.SizeOf` gives the marshalled 4, not 1. The call sizes a capacity, and the tests
    pass. Not changed; noted.
- **Not compiled by Unity:** `Spikes~/BurstNodeAbi` (`Runner/Program.cs`, `Harness/.../BurstCatalogTests.cs`).
  It uses `Marshal.SizeOf`/`OffsetOf` on non-generic types and `UnsafeUtility` elsewhere.
- **No other** `Marshal.SizeOf<T>()` or `SizeOf(Type)` on a generic type.

## Forbidden changes

`ExpectedPublicAbiV2.txt`, every pinned number, runtime code, `CodeGen~`, `package.json`.

## Evidence

`Planning~/Evidence/P12-007/README.md`.
