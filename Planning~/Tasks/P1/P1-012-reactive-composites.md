# P1-012 — Reactive sequence and selector

Status: `Done`

## Objective

Implement reevaluation and active-branch replacement for `ReactiveSequence` and `ReactiveSelector`.

## Required reading

- `specifications/execution-semantics-v1.md`
- `specifications/reference-executor-machine-v1.md`

## Depends on

- `P1-010`
- `P1-011`

## Allowed changes

- `Runtime/Execution/Reference/Composites/Reactive/`
- `Tests/Runtime/ReferenceExecutor/ReactiveComposites/`

## Forbidden changes

- Blackboard observer queues, parallel nodes, decorators, or scheduler budgets.

## Deliverables

- Explicit reactive handlers and branch-replacement trace coverage.

## Acceptance criteria

- Reevaluation always begins at child zero.
- A previous running subtree is aborted before reevaluation enters any candidate, and may be re-entered if selected again.
- Earlier failure/success rules exactly match the normative sequence/selector contracts.
- Memory and reactive node types are not runtime configuration modes of one ambiguous node.

## Required verification

- Table-driven multi-update cases with exact abort/enter/exit ordering.
