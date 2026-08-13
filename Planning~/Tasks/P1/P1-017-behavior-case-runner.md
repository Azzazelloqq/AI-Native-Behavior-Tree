# P1-017 — Behavior-case reader and reference runner

Status: `Done`

## Objective

Turn `.aibtcase.json` into reusable observable-behavior tests without coupling cases to executor internals.

## Required reading

- `specifications/behavior-case-v1.md`
- `specifications/canonical-json-v1.md`
- `Schemas~/behavior-case.schema.json`

## Depends on

- `P1-006`
- `P1-014`
- `P1-015`
- `P1-016`

## Allowed changes

- `Tests/BehaviorCases/Framework/`
- `Tests/Fixtures/Cases/`
- Necessary schema-conformance tests.
- `Tests/BehaviorCases/AIBT.BehaviorCases.Tests.asmdef`

## Forbidden changes

- Runtime semantics, implementation-order assertions not present in expected traces, live model/API calls, or other assembly definitions.

## Deliverables

- Strict case reader, controlled world/clock/event inputs, runner, assertions for root status, blackboard, commands, and invariants.

## Acceptance criteria

- Invalid cases produce structured test diagnostics.
- Cases run with unlimited and configured step budgets.
- Float expectations require explicit tolerance where exact equality is inappropriate.
- Runner can later accept another executor implementation without changing case files.
- No free-form assertion or invariant language is executed.

## Required verification

- Positive, negative, cancellation, budget, and locale-independent case fixtures.
