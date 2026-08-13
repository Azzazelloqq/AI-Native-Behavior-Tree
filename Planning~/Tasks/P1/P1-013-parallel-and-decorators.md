# P1-013 — Parallel and core decorators

Status: `Draft`

## Objective

Implement reference `Parallel`, `Inverter`, `Succeeder`, `Failer`, finite `Repeater`, `Timeout`, and `Cooldown` semantics.

## Depends on

- `P1-010`
- `P1-011`

## Required reading

- `specifications/time-and-random-v1.md`

## Allowed changes

- `Runtime/Execution/Reference/Composites/Parallel/`
- `Runtime/Execution/Reference/Decorators/`
- Focused tests under `Tests/Runtime/ReferenceExecutor/`

## Forbidden changes

- Concurrent child execution, unbounded repeaters against policy, platform clocks read directly by nodes, or scheduler implementation.

## Deliverables

- Stable child-order parallel handler, completion policies, tie-break validation inputs, and core decorator handlers using injected clock values.

## Acceptance criteria

- Terminal parallel children are not reticked during activation.
- Running children abort in reverse semantic order on completion/abort.
- Threshold ambiguity without tie-break is rejected before execution.
- Timeout/cooldown tests use controlled clocks.

## Required verification

- Focused policy matrices and lifecycle traces.
