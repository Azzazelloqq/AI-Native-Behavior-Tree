# Runtime trace v1

Trace is structured diagnostic output and does not control behavior.

## Common fields

Every record contains trace-format version, update ID, snapshot revision, tree semantic hash, tree instance ID, monotonically increasing per-instance trace sequence, event kind, and optional runtime/authoring node identity.

## Event kinds

- `UpdateStarted`, `UpdateCompleted`;
- `NodeEntered`;
- `NodeTicked` with returned public status;
- `NodeAbortStarted` with reason and source node;
- `NodeExited` with success, failure, or aborted reason;
- `BlackboardChanged` with key, old/new version, and optional value according to redaction policy;
- `ObserverQueued`, `ObserverEvaluated`;
- `CommandEmitted`, `CompletionConsumed`, `CompletionDiscarded`;
- `BudgetYielded`, `ExecutionResumed`;
- `DiagnosticRaised`;
- `SchedulerDecision` for backends that schedule or budget work.

Event order follows actual semantic order and `update-phases-v1.md`. Worker completion time MUST NOT determine deterministic trace ordering.

## Levels

- `Off`: no trace records.
- `Errors`: diagnostics and rejected operations.
- `Lifecycle`: node lifecycle, abort, completion, and root outcome.
- `Detailed`: blackboard versions, commands, observers, budget, and scheduler decisions.

Value capture is separately configurable. Sensitive or project-designated blackboard values are redacted by default. Trace buffers are bounded; overflow produces one explicit dropped-record summary and never changes tree semantics.

Phase 1 may use an in-memory test recorder. Persisted/binary transport is not defined until its dedicated task.
