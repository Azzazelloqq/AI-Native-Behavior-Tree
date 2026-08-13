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

Integrations publish completion records before the collect-input phase. A record contains operation ID, terminal outcome (`Succeeded`, `Failed`, or `Cancelled`), optional registered unmanaged payload type ID/version and payload bytes, nonzero source ID, monotonically increasing source sequence, and snapshot revision. `Succeeded` maps to the async node's declared success status and `Failed` to its declared failure status. `Cancelled` never completes a still-active node successfully and is handled by the node's declared cancellation result, which Phase 1 fixture async actions define as `Failure`.

Normalize input orders completions by `(sourceId, sourceSequence)`. Duplicate ordering keys are errors and none of the conflicting records are consumed. Each tree instance retains a per-source high-water mark across updates. Gaps are allowed, but a later input whose source sequence is less than or equal to the accepted high-water mark is rejected as a non-increasing source sequence. Rejected duplicate groups do not advance the high-water mark.

A structurally valid completion for an active operation remains in the instance's pending inbox until the owning node consumes it or the operation is cancelled. It is not lost merely because the owning node is not ticked in the update that collected it. The owning node consumes the first valid matching completion in normalized order during an eligible update and returns its declared terminal status. Unknown operation IDs are warnings, while duplicate, stale-generation, cancelled, and already-consumed completions are informational diagnostics; policy may suppress informational records but behavior is unchanged. A completion's snapshot revision is provenance metadata and is not required to equal the consuming update's snapshot revision.

## Cancellation

On abort, an active asynchronous node emits at most one cancellation command and marks the operation cancelled. Cancellation is idempotent. The cancelled state is committed before attempting to append the cancellation command, so command rejection cannot allow a late completion to reactivate the operation. The integration may acknowledge cancellation later, but late completion cannot reactivate or complete the old activation.

Hot reload applies the same rule to operations whose node identity, type version, configuration compatibility, or memory layout is incompatible.

## Commands

Commands are immutable unmanaged records with stable command type ID, version, operation ID where applicable, payload offset/size, command phase (`Execute` or `Cancel`), tree instance ID, and per-instance sequence. Commands are merged by phase order (`Execute`, then `Cancel`), tree instance ID, and sequence as defined by `update-phases-v1.md`. Sequence overflow rejects the emission with a structured error; it never wraps.

If start and abort occur in one update, the start command remains observable before the cancellation command and both use the same operation ID. Retrying allocates a new local sequence. Per-instance operation and command counters and operation tombstones survive tree restart; restart never makes an old ID reusable. Operation sequence and activation generation overflow reject restart/start safely with an error.

Phase 1 implements this boundary with immutable records, byte payloads, and internal reference-executor services. It does not expose a user-node ABI or command registry and does not execute `Task`, coroutines, or integration work.

If the reference machine faults after publishing a start command, every still-active issued operation is first committed as a `Cancelled` tombstone. Its pending completions are discarded and the machine emits at most one compensating `Cancel` command using the cancellation type and payload captured by the start contract. A cancellation-command append failure is diagnostic but does not revert the tombstone. Consumed and already-cancelled operations emit no compensating command. This cleanup is deterministic and occurs before fault state and activation-memory cleanup become observable.

Applying a command is an integration responsibility. Failure to apply produces a completion or diagnostic; adapters MUST NOT throw through the runtime boundary.

## Main-thread nodes

Explicit managed/main-thread nodes execute in the integration phase through a separate contract. They are never dispatched inside a Burst job and must be visible in validation, profiling, and node manifests.
