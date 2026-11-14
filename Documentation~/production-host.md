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

`TryConfigureEffectDelivery` must be called before generated bootstrap when the consumer needs
typed effects. The host's bounded queue is drained by `TryDrainEffects`; effect delivery is a
post-commit public boundary, not a license to inspect generated dispatch storage. Pipelined hosts
may deliver and accept new facts at their next idle barrier, never into an in-flight native wave.

`NativeTreeAgent` is a ready-made world participant built only from these public pieces: it
creates a hidden host per run, restarts it by `NativeTreeRestartMode`, flushes `SetFact` values at
the same safe boundary and routes effects to handlers registered by command name (see
`ADR-P9-004`). The host itself still never restarts; pooling and migration/hot-reload wiring remain
separate work. A delegate-based host still requires its caller-owned adapter; it does not itself
provide gameplay Actions.
