# P0-005 — Windows validation CI

Status: `Review`

## Objective

Run deterministic package validation and EditMode tests on pull requests without embedding credentials or weakening local checks.

## Depends on

- `P0-002`
- User approval of CI provider and Unity licensing approach.

## Allowed changes

- `.github/workflows/`
- CI-specific documentation and task-owned test output configuration.

## Forbidden changes

- Runtime implementation, public contracts, committed licenses, tokens, or secrets.

## Deliverables

- Pinned workflow actions.
- Compile, JSON/link/diff, and EditMode-test jobs.
- Artifact retention for sanitized logs and test results.

## Acceptance criteria

- Pull request workflow fails on compile or test failure.
- Cache keys include Unity/package dependency inputs.
- Secrets are referenced only by provider secret names.
- Local verification entrypoints remain the source of CI commands.

## Required verification

- Successful CI run on the repository.
- Controlled failing branch or equivalent proof that failures propagate.

## Evidence

- Workflow: [`.github/workflows/validation.yml`](../../../.github/workflows/validation.yml)
- Local verification: [`Planning~/Evidence/P0-005/README.md`](../../Evidence/P0-005/README.md)
- A successful remote workflow run is still required before this task can become `Done`.
