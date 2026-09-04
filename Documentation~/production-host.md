# Production tree host

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

## Scope

This host supports Immediate/Budgeted. Population-level BatchedJobsSameFrame/PipelinedJobs,
automatic restart/pooling, and migration/hot-reload wiring are separate work. A working callback
adapter is still required; this component does not itself provide gameplay Actions.
