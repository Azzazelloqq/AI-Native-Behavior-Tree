# P1-018 — End-to-end golden semantic slice

Status: `Draft`

## Objective

Prove canonical JSON through parse, validate, compile, execute, and behavior assertions using representative trees.

## Depends on

- `P1-007`
- `P1-009`
- `P1-013`
- `P1-014`
- `P1-015`
- `P1-017`

## Allowed changes

- `Tests/Integration/SemanticSlice/`
- `Tests/Fixtures/Golden/`
- `Samples~/SemanticSlice/`

## Forbidden changes

- Fixing production behavior inside integration tests, adding editor UI, jobs, source generation, or MCP.

## Deliverables

- Patrol/react tree, async-action tree, parallel/decorator tree, invalid-tree corpus, and documented sample flow.

## Acceptance criteria

- Canonical files round-trip byte-stably.
- Golden compiled content is deterministic.
- Expected root statuses, lifecycle traces, blackboard changes, commands, aborts, and budget equivalence pass.
- Invalid fixtures fail with stable codes and locations.

## Required verification

- Full Phase 1 EditMode suite from a clean checkout.
