# P0-001 — Validate exact Unity toolchain

Status: `Done`

## Objective

Import the package and compile its empty assemblies using the repository's exact Unity version without changing project or package versions.

## Depends on

- User-approved baseline Unity `6000.5.8f1`, Android Build Support, and Web Build Support.

## Allowed changes

- `Planning~/Evidence/P0-001/`
- Fixes limited to existing package/asmdef metadata when an actual import error proves they are required; report before expanding scope.

## Forbidden changes

- Runtime implementation, Unity version upgrades, dependency upgrades, or unrelated parent-project files.

## Deliverables

- Batch-mode Editor compile log.
- Exact Unity/module/package version record.
- Clean Git diff except intentional evidence or approved metadata corrections.

## Acceptance criteria

- Unity exits with code 0.
- `AIBT.Runtime`, `AIBT.Authoring`, `AIBT.Editor`, and test assemblies compile.
- No package import errors or warnings caused by AIBT.

Evidence: `Planning~/Evidence/P0-001/`. The isolated AIBT harness passes. A full parent-project batch compile remains blocked by an unrelated UniTask editor API incompatibility and is not attributed to AIBT.

## Required verification

Run the exact editor executable in batch mode with `-projectPath`, `-quit`, and a task-owned log file. Record the full command with credentials and machine-specific tokens removed.
