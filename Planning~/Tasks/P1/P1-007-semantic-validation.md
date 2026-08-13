# P1-007 — Structural and semantic validator

Status: `Draft`

## Objective

Validate a complete authoring document and return deterministic structured diagnostics without compiling or mutating it.

## Depends on

- `P1-002`
- `P1-003`
- `P1-004`
- `P1-005`

## Allowed changes

- `Authoring/Validation/`
- `Tests/Editor/Validation/`
- `Tests/Fixtures/Trees/Validation/`

## Forbidden changes

- Runtime code, document repair, compiler output, or editor UI.

## Deliverables

- Root, identity, reference, cycle, reachability, child-policy, parameter, blackboard, scope, capability, and project-policy validators.
- Stable diagnostic code catalog for implemented rules.

## Acceptance criteria

- Validator does not throw for arbitrary representable invalid documents.
- Diagnostic order is stable and independent of map insertion order.
- Every rule has positive and negative fixtures.
- Unsupported Phase 1 Agent/Shared execution is a capability diagnostic.

## Required verification

- Focused validator suite and fixture snapshot review.
