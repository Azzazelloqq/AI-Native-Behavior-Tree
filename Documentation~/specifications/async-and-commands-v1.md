# Async operations and commands v1

## Boundary

The runtime does not execute `Task`, `Task.Run`, threads, coroutines, navigation, animation, or scene APIs. Nodes emit commands and observe completion records supplied by integrations.

## Operation identity

An asynchronous start allocates an opaque `OperationId` associated with tree instance, node ID, activation generation, and a monotonically increasing local sequence. IDs are not reused while stale results may exist.

## Start

1. The node emits one start command containing `OperationId`.
2. The node stores the ID in node memory.
3. The node returns `Running`.
4. Reticks MUST NOT emit another start command unless the node contract explicitly models retries.

## Completion

Integrations publish completion records before the collect-input phase. A record contains operation ID, terminal outcome, optional typed payload, source sequence, and snapshot revision.

The owning node consumes a matching completion during an eligible update and returns its declared terminal status. Unknown, duplicate, stale-generation, and already-consumed completions are ignored safely and reported according to diagnostic configuration.

## Cancellation

On abort, an active asynchronous node emits at most one cancellation command and marks the operation cancelled. Cancellation is idempotent. The integration may acknowledge cancellation later, but late completion cannot reactivate or complete the old activation.

Hot reload applies the same rule to operations whose node identity, type version, configuration compatibility, or memory layout is incompatible.

## Commands

Commands are immutable unmanaged records with stable command type ID, version, operation ID where applicable, payload offset/size, tree instance ID, and per-instance sequence. Commands are merged in the order defined by `update-phases-v1.md`.

Applying a command is an integration responsibility. Failure to apply produces a completion or diagnostic; adapters MUST NOT throw through the runtime boundary.

## Main-thread nodes

Explicit managed/main-thread nodes execute in the integration phase through a separate contract. They are never dispatched inside a Burst job and must be visible in validation, profiling, and node manifests.
