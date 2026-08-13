# P1-005 — Blackboard schema and value model

Status: `Done`

## Objective

Implement authoring-time built-in value types, keys, defaults, and scope declarations from Blackboard Contract v1.

## Required reading

- `specifications/blackboard-v1.md`
- `specifications/canonical-json-v1.md`

## Depends on

- `P1-001`
- `P1-002`

## Allowed changes

- `Authoring/Model/Blackboard/`
- `Runtime/Blackboard/Types/`
- `Tests/Editor/BlackboardSchema/`
- `Tests/Runtime/BlackboardTypes/`

## Forbidden changes

- Runtime blackboard storage, shared reductions, implicit conversions, or Unity Entities references.

## Deliverables

- Stable type descriptors and value representation for every built-in v1 type.
- Key/scope/default model and registered unmanaged-type descriptor contract.
- Exact built-in equality and canonical typed JSON representations.

## Acceptance criteria

- Invalid default/type combinations are diagnostic inputs, not silently coerced.
- Float/vector/quaternion and fixed-string values round-trip without culture dependence.
- `EntityId` remains AIBT-owned and has no DOTS dependency.
- Managed object values are impossible in the Burst-compatible representation.

## Required verification

- Focused type/default/equality/culture tests.
