# P0-006 — Phase 0 integration gate

Status: `Draft`

## Objective

Independently verify Phase 0 evidence and declare Phase 1 implementation ready or return precise blockers.

## Depends on

- `P0-001`
- `P0-002`
- `P0-003`
- `P0-004`
- `P0-005`

## Allowed changes

- `Planning~/Evidence/P0-GATE/`
- Coordinator-owned status updates after review.

## Forbidden changes

- Fixing implementation inside the gate task or redefining acceptance criteria.

## Deliverables

- Independent verification report covering exact toolchain, Windows CI, Android build, Web decision, repository cleanliness, and open user actions.

## Acceptance criteria

- Every predecessor has an accepted handoff.
- Required commands pass from a clean checkout with initialized submodules.
- Any unverified platform claim remains explicitly unverified.
- Phase 1 task frontier is identified from the machine-readable dependency graph.
