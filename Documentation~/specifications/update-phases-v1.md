# Update phases v1

Every backend preserves this logical phase order:

1. **Collect input**: integrations publish immutable world snapshot data, completion records, external events, and time values.
2. **Normalize input**: records are validated, stale async completions are discarded, and deterministic ordering keys are assigned.
3. **Select work**: scheduler chooses eligible tree instances, backend, batching, and allowed budget.
4. **Execute**: instances progress using their snapshot and private state. Agent/tree blackboard writes are visible to later nodes in the same instance update.
5. **Reduce shared writes**: declared shared operations are resolved deterministically.
6. **Publish commands**: command streams are merged by `(phase, treeInstanceId, sequence)`.
7. **Apply integrations**: allowed main-thread adapters consume commands.
8. **Publish trace and metrics**: diagnostics become observable after the semantic effects they describe.

Backends MAY combine phases physically, but MUST preserve their observable ordering.

## Stable identity and ordering

- Every tree instance has a stable `TreeInstanceId` for its lifetime.
- Every emitted command receives a monotonically increasing per-instance sequence.
- External events require a source ID and source sequence. Invalid duplicate ordering keys are diagnostics.
- Deterministic mode orders tree instances by `TreeInstanceId`, never by worker completion order.

## Snapshot rule

A scheduled execution pass reads one logical snapshot revision. Integrations MUST NOT mutate storage visible to a running pass. Pipelined results record the snapshot revision that produced them.

## Reentrancy

Node callbacks and command adapters MUST NOT directly update the same tree instance. Requested updates are queued for a later phase. Reentrancy violations are errors in development builds and are safely rejected in player builds.
