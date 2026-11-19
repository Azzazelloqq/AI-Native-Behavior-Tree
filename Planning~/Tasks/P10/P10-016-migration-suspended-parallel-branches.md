# P10-016 — Native state migration of a Parallel with suspended branches

Status: `Ready` — filed 2026-11-19 from P10-014 open question 3. Not started.

## Objective

Find out, with a test, whether `NativeHotReloadStateMigration.TryMigrate` loses the resting
branches of a v1 Parallel. A Parallel keeps its resting branches in the suspended-frame region
between updates, and `TryMigrate` copies only the active frames, `Depth` and no `ParallelBranches`
records (unchanged since `3e4b09e`). The P10-014 R1 guard deliberately does not refuse this case.

## Decision record

Owner's decision 2026-11-19 (P10-014 evidence, "Owner decisions on the open questions", item 3):
separate small task with a test. If the test shows lost branches, the result goes to the owner with
the options (refuse the migration, or carry the branches) before any production change.

## Depends on

- P10-014 (`c68df77`).

## Required reading

- `Planning~/Evidence/P10-014/README.md` (finding R1, open question 3).
- `Runtime/Execution/Native/HotReload/NativeHotReloadStateMigration.cs`.
- `Tests/Runtime/NativeExecution/HotReload/NativeHotReloadStateMigrationTests.cs`.

## Scope

- New tests under `Tests/Runtime/NativeExecution/HotReload/`. No production change in this task
  unless the owner approves one after seeing the result.

## Forbidden changes

- Existing migration behaviour and test expectations; the native executor;
  `Tests/Editor/CodeGen/Contracts/ExpectedPublicAbiV2.txt`; formats; the package version.

## Acceptance criteria

- A test migrates a v1 Parallel with one and with two resting branches (unchanged tree, and a
  reorder) and records what happens: branches kept, branches lost, or migration refused.
- The evidence states the observed behaviour and, if branches are lost, the options for the owner.

## Required verification

```text
Tools/Unity/Invoke-UnityRecompile.ps1
Tools/Unity/Invoke-UnityTests.ps1 -Filter <new test class>   (one runner per Editor)
```
