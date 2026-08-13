# P1-002 — Structured diagnostics foundation

Status: `Done`

## Objective

Create stable, machine-readable diagnostics shared by formats, validation, compiler, runtime tooling, editor, and MCP.

## Depends on

- `P1-001`

## Required reading

- `specifications/diagnostics-v1.md`
- `specifications/canonical-json-v1.md`

## Allowed changes

- `Authoring/Diagnostics/`
- `Tests/Editor/Diagnostics/`

## Forbidden changes

- Specific validators, exceptions as public control flow, or editor UI.

## Deliverables

- Authoring extensions for message, JSON path, related document locations, optional safe suggested operation, and canonical JSON representation over Runtime diagnostic primitives.
- Initial code-range registry by subsystem.

## Acceptance criteria

- Diagnostics serialize deterministically.
- Unknown location fields are represented explicitly rather than invented.
- Duplicate diagnostics have a defined stable policy.
- Consumers do not need to parse human message text.

## Required verification

- Focused serialization, ordering, equality, and invalid-input tests.
