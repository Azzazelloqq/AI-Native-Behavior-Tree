# P10-015 — Reference executor: a tree stop that replaces an abort in progress inside a Parallel

Status: `Ready` — filed 2026-11-19 from P10-014 open question 1. Not started; starting it needs the
owner's go-ahead, because it changes the reference executor's behaviour.

## Objective

Make the reference executor honour a tree stop requested while an abort is already in progress
inside a Parallel. Today the reference `RequestAbort` in that state loses the stop: it finishes the
abort it was doing and the owner then completes normally (for example `T timeout F; X timeout`).
The native executor lets the stop win: every frame up to the root is aborted with `TreeStopped`.
The specifications say a tree stop aborts the whole active path, so native is right.

## Decision record

Owner's decision 2026-11-19 (P10-014 evidence, "Owner decisions on the open questions", item 1):
native behaviour stays; the reference executor is aligned in this separate task.

## Depends on

- P10-014 (`c68df77`).

## Required reading

- `Planning~/Evidence/P10-014/README.md` (finding D1, open question 1).
- `Tests/Runtime/NativeExecution/ParallelAndDecorators/NativeParallelOuterAbortTargetTests.cs`
  (the four `StopsReplacingAPendingAbort` cases assert native records only).
- `Documentation~/specifications/execution-semantics-v1.md` (tree stop, abort).

## Scope

- `Runtime/Execution/Reference/**` (the abort request path), reference tests.
- The four `StopsReplacingAPendingAbort` cases: compare with the reference once it agrees.

## Forbidden changes

- The native executor; `Tests/Editor/CodeGen/Contracts/ExpectedPublicAbiV2.txt`; the compiled and
  trace formats; the package version; the meaning of the normative specifications; golden behaviour
  cases (a golden case that changes is reported to the owner before it is touched).

## Acceptance criteria

- A reference test that fails before the fix and passes after it for each of the four cases.
- The native cases compare records with the reference instead of asserting native alone.
- No other reference/native equivalence test or golden case changes; every changed existing
  expectation is listed in the evidence.

## Required verification

```text
Tools/Unity/Invoke-UnityRecompile.ps1
Tools/Unity/Invoke-UnityTests.ps1 -Filter <touched test classes>   (one runner per Editor)
```
