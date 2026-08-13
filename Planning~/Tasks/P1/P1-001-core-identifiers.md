# P1-001 — Core semantic identifiers and statuses

Status: `Draft`

## Objective

Implement the minimal backend-neutral value contracts used by all later Phase 1 work.

## Depends on

- `P0-006`

## Required reading

- `specifications/execution-semantics-v1.md`
- `specifications/update-phases-v1.md`
- `specifications/determinism-v1.md`
- `specifications/identity-and-hashing-v1.md`

## Allowed changes

- `Runtime/Core/Identity/`
- `Runtime/Core/Execution/NodeStatus.cs`
- `Tests/Runtime/Core/Identity/`

## Forbidden changes

- Node callback APIs, executors, authoring JSON, or native memory layouts.

## Deliverables

- Strong immutable IDs for tree, authoring node, tree instance, agent, entity, operation, and revision.
- Public `NodeStatus` with exactly `Success`, `Failure`, and `Running`.
- Internal state and exit/abort reason enums required by the lifecycle specification.

## Acceptance criteria

- Default/invalid identity behavior is explicit and tested.
- Equality, ordering where specified, formatting, and parsing round-trip deterministically.
- FNV-1a and SHA-256 representations match independent fixed vectors.
- Public nodes cannot return internal `Inactive` or budget states.

## Required verification

- Focused EditMode tests through the P0 verification entrypoint.
