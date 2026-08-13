# P1-015 — Reference commands and async operations

Status: `Draft`

## Objective

Implement backend-neutral command records and reference async start/completion/cancellation behavior.

## Depends on

- `P1-001`
- `P1-010`

## Required reading

- `specifications/async-and-commands-v1.md`
- `specifications/update-phases-v1.md`

## Allowed changes

- `Runtime/Commands/`
- `Runtime/Execution/Reference/Async/`
- Focused runtime tests.

## Forbidden changes

- Task/coroutine adapters, actual Unity API integrations, managed payloads, or job scheduling.

## Deliverables

- Immutable command/completion model, deterministic merge keys, operation generation, reference async action, and cancellation behavior.

## Acceptance criteria

- Start emits once per activation.
- Matching completion is consumed once.
- Duplicate, unknown, cancelled, and stale-generation results cannot reactivate old work.
- Abort emits at most one cancellation and remains idempotent.

## Required verification

- Focused multi-update operation lifecycle and ordering tests.
