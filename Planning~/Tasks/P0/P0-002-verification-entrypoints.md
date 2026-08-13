# P0-002 — Add repeatable verification entrypoints

Status: `Draft`

## Objective

Provide repository-owned commands for compile validation, focused EditMode tests, full EditMode tests, JSON/schema checks, Markdown-link checks, and diff hygiene.

## Depends on

- `P0-001`

## Allowed changes

- `Tools~/Verification/`
- `Documentation~/development-commands.md`
- Focused tooling tests under `Tests/Editor/Verification/`

## Forbidden changes

- Runtime behavior, package dependencies, CI workflow, or platform build logic.

## Deliverables

- PowerShell entrypoints with explicit Unity path argument or documented task-specific environment variable.
- Deterministic exit codes and task-owned logs/results.
- Development command reference.

## Acceptance criteria

- Commands work from any current directory.
- Missing Unity/schema tools fail with actionable messages.
- Commands never modify package sources or delete paths outside task-owned output directories.
- Focused and full test modes are distinct.

## Required verification

- Run every entrypoint once on a clean checkout.
- Demonstrate one intentional failure is returned as a nonzero exit code.
