# P1-002 — Structured diagnostics foundation

Status: `Draft`

## Objective

Create stable, machine-readable diagnostics shared by formats, validation, compiler, runtime tooling, editor, and MCP.

## Depends on

- `P0-006`
- `P1-001`

## Allowed changes

- `Authoring/Diagnostics/`
- `Tests/Editor/Diagnostics/`

## Forbidden changes

- Specific validators, exceptions as public control flow, or editor UI.

## Deliverables

- Severity, stable code, message, document/tree/node identity, JSON path, related locations, and optional safe suggested operation.
- Immutable diagnostic collection and deterministic ordering.
- Initial code-range registry by subsystem.

## Acceptance criteria

- Diagnostics serialize deterministically.
- Unknown location fields are represented explicitly rather than invented.
- Duplicate diagnostics have a defined stable policy.
- Consumers do not need to parse human message text.

## Required verification

- Focused serialization, ordering, equality, and invalid-input tests.
