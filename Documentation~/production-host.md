# Production tree host

For the Editor-prepared asset and shared-program route, start with
[First production agent](consumer-quickstart.md). `ProductionTreeHost.Stop()` now exposes the same
permanent, idempotent cancellation/release path as destruction. Stop hosts before disposing their
shared program; reentrant cleanup finishes after the current callback.

`ProductionTreeHost` owns one compiled native tree and its trace channel on a GameObject.
Call `TryBootstrap` once before its first Update. Runtime does not compile authored JSON or depend
on Editor/Authoring; supply an already compiled program and a caller-owned dispatch adapter.

## Dispatch

The original overload accepting `DispatchLeaf(uint)` remains supported for Tick-only adapters.
Its non-Tick callbacks are no-ops. An Action that initializes, cancels or cleans up must use the
full lifecycle overload:

```csharp
bool ok = host.TryBootstrap(program, Dispatch, traceCapacity, clock: null, out var failure);

BurstContextResult Dispatch(in ProductionTreeHost.DispatchRequest request, out NodeStatus status)
{
    // The project adapter invokes the matching node callback using its own bound state/context.
    // request: NodeIndex, Phase, UpdateId, TimeMicroseconds, ExitReason, AbortReason.
    return projectAdapter.Dispatch(in request, out status);
}
```

`projectAdapter` above represents the application's adapter, not a new AIBT API. The host does not
automatically construct arbitrary generated-catalog workspaces. This main-thread delegate is not
a claim of scheduled Burst execution. Return the actual callback `BurstContextResult`; only Tick's
`NodeStatus` is consumed. Reasons are meaningful for their matching Exit or Abort phase.

## Clock, budget and completion

- A null clock uses scaled Unity time, converted to integer microseconds. Supply `Func<long>` for
  controlled time. Negative or backwards time faults the host; equal timestamps are allowed.
  `timeScale == 0` freezes this default clock but does not itself stop Tick callbacks.
- `StepBudget = null` is Immediate. A positive value limits native steps per Unity frame; zero
  performs no step. The property can change between frames.
- A budget resume preserves the same logical update ID and clock. It never starts an extra Tick
  or reactive reevaluation for that update. New clock input is read only for a new eligible update.
- Success/Failure remains in `LastRootResult`; subsequent frames do not restart or retick the tree.
  `TotalUpdates` counts logical updates, not Unity frames. There is no automatic restart API here.

Native hosts (`ProductionTreeHost`, `NativeTreeAgent`, `ProductionTreeScheduler` and
`TreeSimulation`) do not run observer re-evaluation today. A condition's `observer` block compiles
and is accepted, but nothing re-evaluates the condition when a watched key changes, and the block
changes no outcome. To guard a running branch natively, make the condition an earlier child of a
reactive composite (`aibt.core.reactive-sequence` or `aibt.core.reactive-selector`). The reactive
composite ticks the condition again at the start of every update. After a node or `SetFact`
changes the key, the condition's new result aborts the running memory composite at the next
update, not inside the update that changed the key. Validation refuses an `observer` block outside
a reactive composite (AIBT2034).

## Lifecycle and diagnostics

Disabling pauses stepping and preserves activation. The clock may keep advancing while this
component is disabled; the next eligible update observes elapsed time. Destruction cancels active
work, runs its required Exit callbacks, then disposes native storage. A never-executed instance
does not start its root merely to cancel it. Cleanup is not constrained by the normal frame budget.

Callback errors/exceptions stop execution and are reported once, with `LastFailure` retained.
Faulted callbacks are not retried during teardown; owned memory is still released. Repeated
bootstrap is rejected without replacing the live instance.

The existing debugger can attach to `TraceChannelOwner`. The host's Detailed channel includes
BudgetYielded/ExecutionResumed and actual leaf Exit/Abort reasons. Writer leases are released
between budget segments. Capacity remains caller-supplied and trace overflow follows the existing
channel contract; tracing does not change tree outcomes.

## Generated catalog, external facts and effects

The generated-catalog bootstrap overload owns the package's generated dispatch adapter. It can be
registered with `ProductionTreeScheduler` for Immediate, Budgeted, BatchedJobsSameFrame or an
explicitly permitted `PipelinedJobs` profile. The scheduler remains the sole population-level
owner; a host is never driven both by its own `Update` and by a scheduler.

For ordinary Unity-facing gameplay, use `ProductionTreeWorld` rather than reading native command
buffers. A participant stages Unity facts, the world flushes them only at the host's safe Tree-scope
external-write boundary, then receives already committed `ProductionTreeEffect` values on the main
thread. `TryResolveExternalTreeWrite<T>` and `TryWriteExternalTreeValue<T>` are deliberately
limited to the generated-catalog bootstrap path, Tree scope, and slots no tree node writes; see
`ADR-P7-039` and `blackboard-v1.md`.

The read is the mirror (`ADR-P11-004`): `TryResolveExternalTreeRead<T>` once, then
`TryReadExternalTreeValue<T>` every frame, with no key lookup and no managed allocation. Any
Tree-scope key with a built-in type is readable, whoever writes it. A host is readable from
bootstrap until `Stop`, also after its root finished or it faulted (after a fault the values are
whatever the storage held and may be mid-update), except while it is executing: then
`CanReadTreeValues` is false and the read returns `NativeLifetimeStateInvalid` without touching
memory. A `PipelinedJobs` host executes between frames, so it is mostly unreadable. Other refusals:
an unknown key (`BlackboardUndeclaredAccess`), an Agent- or Shared-scope key
(`BlackboardUnsupportedScope`), another type (`BlackboardTypeMismatch`) and a delegate-bootstrapped
host (`NativeCapacityPlanInvalid`). `NativeTreeAgent.TryGetValue` is the by-name form.

`TryConfigureEffectDelivery` must be called before generated bootstrap when the consumer needs
typed effects. The host's bounded queue is drained by `TryDrainEffects`; effect delivery is a
post-commit public boundary, not a license to inspect generated dispatch storage. Pipelined hosts
may deliver and accept new facts at their next idle barrier, never into an in-flight native wave.

The generated bootstrap seeds the random stream of every node that declares one
(`[AibtRandomStream]`, for example the `Native/Random` library nodes) from a root seed, the
program's semantic hash, the host's `InstanceId` and the node index, as `TreeSimulation` does
(`time-and-random-v1.md`). The seed is an argument of the
`TryBootstrap(..., randomSeed, out failure)` overloads (default `DefaultRandomSeed`, 0) and is
reported by `RandomSeed`; `NativeTreeAgent.RandomSeed` passes it for every run. A tree without such
nodes has no stream and is not affected. Instance ids are process-wide, so two hosts with one seed
draw different sequences. The stream advances the same way under every scheduling policy, including
the grouped job waves of a forced `BatchedJobsSameFrame` or `PipelinedJobs` profile; see the
Randomness recipe in [Native node library](native-node-library.md#randomness).

`NativeTreeAgent` is a ready-made world participant built only from these public pieces: it
creates a hidden host per run, restarts it by `NativeTreeRestartMode`, flushes `SetFact` values at
the same safe boundary and routes effects to handlers registered by command name (see
`ADR-P9-004`). The host itself still never restarts; pooling and migration/hot-reload wiring remain
separate work. A delegate-based host still requires its caller-owned adapter; it does not itself
provide gameplay Actions.
