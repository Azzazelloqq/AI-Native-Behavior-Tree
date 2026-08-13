# P1-003 — Canonical semantic authoring model

Status: `Draft`

## Objective

Represent `.aibt.json` semantics independently of JSON libraries, Unity assets, runtime indices, and visual layout.

## Depends on

- `P1-001`

## Required reading

- `Documentation~/data-formats.md`
- `Schemas~/tree.schema.json`
- `specifications/blackboard-v1.md`

## Allowed changes

- `Authoring/Model/Tree/`
- `Tests/Editor/AuthoringModel/Tree/`

## Forbidden changes

- JSON parsing, validation rules, runtime compilation, or layout data.

## Deliverables

- Tree document, node document, ordered child references, parameters, tags, metadata, and version fields.
- Construction API that preserves invalid intermediate documents for diagnostics without silently repairing them.

## Acceptance criteria

- Model can represent every valid v1 schema document and relevant invalid states.
- Semantic order is explicit.
- No presentation coordinate, Unity object, runtime index, or generated cache field exists.
- Mutation does not bypass revision tracking.

## Required verification

- Focused model construction, identity, ordering, and revision tests.
