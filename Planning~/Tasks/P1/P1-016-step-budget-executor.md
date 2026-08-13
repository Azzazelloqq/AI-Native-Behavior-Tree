# P1-016 — Deterministic step-budget execution

Status: `Done`

## Objective

Allow the reference executor to suspend and resume between node steps without changing public statuses or lifecycle behavior.

## Required reading

- `specifications/reference-executor-machine-v1.md`
- `specifications/execution-semantics-v1.md`
- `specifications/trace-v1.md`

## Depends on

- `P1-012`
- `P1-013`
- `P1-014`
- `P1-015`

## Allowed changes

- `Runtime/Execution/Reference/Budgeting/`
- Focused budget/equivalence tests.

## Forbidden changes

- Wall-clock Auto scheduling, Jobs, platform detection, or returning budget state from a node.

## Deliverables

- Step counter, suspension cursor/state, resume API, metrics, and zero/one/small/unlimited budget handling.

## Acceptance criteria

- Suspension never splits a callback.
- Suspension calls neither abort nor exit.
- Unlimited and arbitrarily partitioned step budgets produce the same final observable result for frozen inputs.
- Metrics distinguish executed steps from deferred work.
- Zero budget executes no callback; resume preserves update ID and frozen inputs.

## Required verification

- Property/table tests over multiple budget partitions and composite shapes.
