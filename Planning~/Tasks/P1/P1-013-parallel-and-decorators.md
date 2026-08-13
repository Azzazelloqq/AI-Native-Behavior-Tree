# P1-013 — Parallel and core decorators

Status: `Done`

## Objective

Implement reference `Parallel`, `Inverter`, `Succeeder`, `Failer`, finite `Repeater`, `Timeout`, and `Cooldown` semantics.

## Depends on

- `P1-010`
- `P1-011`

## Required reading

- `specifications/time-and-random-v1.md`
- `specifications/execution-semantics-v1.md`
- `specifications/reference-executor-machine-v1.md`

## Allowed changes

- `Runtime/Execution/Reference/Composites/Parallel/`
- `Runtime/Execution/Reference/Decorators/`
- `Tests/Runtime/ReferenceExecutor/ParallelAndDecorators/`

## Forbidden changes

- Concurrent child execution, unbounded repeaters against policy, platform clocks read directly by nodes, or scheduler implementation.

## Deliverables

- Stable child-order parallel handler, completion policies, tie-break validation inputs, and core decorator handlers using injected clock values.

## Acceptance criteria

- Terminal parallel children are not reticked during activation.
- Running children abort in reverse semantic order on completion/abort.
- Threshold ambiguity without tie-break is rejected before execution.
- Timeout/cooldown tests use controlled clocks.
- Repeater failure policy, exact deadline behavior, cooldown blocked result, and cooldown start policy match the normative contracts.

## Required verification

- Focused policy matrices and lifecycle traces.
