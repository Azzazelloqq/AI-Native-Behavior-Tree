# P1-014 — Tree blackboard runtime and observer queue

Status: `Draft`

## Objective

Implement Tree-scope runtime slots, declared access, change versions, and deferred observer reevaluation for the reference executor.

## Depends on

- `P1-005`
- `P1-009`
- `P1-012`

## Allowed changes

- `Runtime/Blackboard/Storage/`
- `Runtime/Execution/Reference/Observers/`
- Focused runtime tests.

## Forbidden changes

- Agent/Shared execution, implicit keys/conversions, recursive reevaluation, or jobs/native optimization.

## Deliverables

- Typed slot storage, defaults/reset, immediate same-instance visibility, equality-based versions, and stable reevaluation queue.

## Acceptance criteria

- Undeclared/type-invalid access is rejected.
- Equal writes do not increment versions.
- Writes never recursively update a tree.
- `Self`, `LowerPriority`, `Both`, and `None` produce specified abort traces.

## Required verification

- Focused type/access/version/observer ordering tests.
