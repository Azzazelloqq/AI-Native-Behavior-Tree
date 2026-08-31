# ADR P6-015: Native trace production-wiring

- Status: Accepted 2026-08-31
- Date: 2026-08-31
- Decision ID: AIBT-027

## Context

`P6-008` assumed a real running native tree's lifecycle steps already get wired into a
`NativeTraceChannelOwnerV1`. Investigation (recorded in that card's own narrowing, 2026-08-29) found
this wiring does not exist anywhere in production code: the only two things that ever write trace
records are `NativeExecutionDebuggerSessionTests.cs`'s hand-authored `Scenario` helper and an
unrelated Burst compile probe -- both synthetic, proving only the read side (`P3-010`/`P3-011`).
`P3-010`'s own evidence already disclosed the root cause: no production Play-mode host drives a
native lifecycle machine at all. This card decides, on paper plus a disposable spike, where the
real-step-to-trace-record translation should live and exactly which `NativeLifecycleStepKindV1`
values map to which `NativeTraceEventKindV1` values -- without touching any production file.

## Spike evidence (`Spikes~/NativeTraceProductionWiring/`, 2026-08-31, this workstation)

A disposable NUnit spike (`SpikeNativeTraceProductionWiring`, run live via Unity MCP `run_tests`
against the real, unmodified `6000.5.8f1` Editor) drove a real, unmodified `NativeLifecycleMachineV1`
through a hand-built 3-node compiled tree (`MemorySequence` root, two `GeneratedLeaf` children --
the same construction `NativeLifecycleMachineTests.cs` already uses, satisfying this card's own
"reuse `SchedulingPolicyDriver`'s or equivalent single-agent construction" requirement) with an
external `SpikeTraceRecorder` hooking exactly the same call sites `SchedulingPolicyDriver.
TryHandleStep`'s own step switch already uses in production (`TryAdvance`'s return value, plus the
status the caller is about to feed into `TryCompleteDispatch`) -- no change inside
`NativeLifecycleMachineV1` itself.

1. **End-to-end real-tree wiring.** The recorder wrote real `NativeTraceRecordV1`s into a real
   `NativeTraceChannelOwnerV1` across two updates (one yielding on a still-`Running` leaf, one
   resuming and completing). Read back through the real, unmodified `NativeExecutionDebuggerSession.
   TryReadTrace` and `TraceTimelineModel.Build`: zero dropped/faulted records, correct
   `UpdateStarted`/`NodeEntered`/`NodeTicked`/`NodeExited`/`UpdateCompleted` ordering, and the
   timeline's own unmodified active-node replay correctly showed the ticked leaf active after its
   `NodeEntered` and inactive after its `NodeExited`. **Passed.**
2. **Real, disclosed finding: `NativeLifecycleStepResultV1.CompositeExited` carries no status.**
   Unlike `Completed` (`HasRootStatus`/`RootStatus`), `CompositeExited` exposes only `NodeIndex` --
   confirmed by reading every call site (`AdvanceComposite`, `AdvanceDecorator`, `AdvanceParallel`,
   `AdvanceAbort`) and by a second spike test that drives a *nested* composite (root sequence → child
   sequence → leaf) to its own `CompositeExited` step and shows, by construction, that no `ExitReason`
   can be derived from that step alone. The root's own exit is not affected -- the recorder can defer
   and fold the root's `CompositeExited` into the immediately-following `Completed` step, which does
   carry `RootStatus`; a *nested* composite's exit has no such fallback. **Confirmed, not assumed.**

Full raw output is in `Planning~/Evidence/P6-015/README.md`.

## Decision

1. **The translation lives outside `NativeLifecycleMachineV1`, as an external recorder co-located
   with whatever already drives it.** Not a wrapper around the machine, not a change inside it. Any
   production driver that calls `TryBeginUpdate`/`TryAdvance`/`TryCompleteDispatch` in a loop
   (`SchedulingPolicyDriver` today; a future Play-mode host tomorrow) additively calls the recorder
   at the exact same points it already branches on `step.Kind` -- an additive hook, not a rewrite of
   the driver's own control flow.
2. **Mapping table** (`NativeLifecycleStepKindV1` / dispatch phase → `NativeTraceEventKindV1`):

   | Source | Trace event | Notes |
   | --- | --- | --- |
   | `TryBeginUpdate` succeeds | `UpdateStarted` | recorder-level hook, not a step result |
   | `CompositeEntered` | `NodeEntered` (Status=Running) | |
   | `DispatchRequired`, completion phase=`Enter` | `NodeEntered` (Status=Running) | recorded at `TryCompleteDispatch`, once Enter actually completes |
   | `DispatchRequired`, completion phase=`Tick` | `NodeTicked` (Status=caller-supplied) | |
   | `DispatchRequired`, completion phase=`Exit` | `NodeExited` (ExitReason from caller-supplied status) | |
   | `DispatchRequired`, completion phase=`Abort` | `NodeAbortStarted` | |
   | `CompositeExited`, non-root | `NodeExited`, **no ExitReason** | disclosed gap -- see below |
   | `CompositeExited`, root (`NodeIndex==0`) | deferred, folded into `Completed` | |
   | `CompositeAborted` | `NodeAbortStarted` | |
   | `Completed` (`HasRootStatus`) | `NodeExited` (root, ExitReason from `RootStatus`) then `UpdateCompleted` (Status=`RootStatus`) | |
   | `Completed` (aborted, no root status) / `Waiting` | `UpdateCompleted` (no status) | both are real "this update's loop ends here" boundaries |
   | `ChildSelected`, `ChildAccepted`, `ReactiveReset`, `ParallelBranchSuspended` | none | pure internal bookkeeping; no new node-boundary information beyond what `NodeEntered`/`NodeExited` already capture |

3. **`SnapshotRevision`/`TreeSemanticHash`/`TreeInstanceId` are caller-supplied, not machine-derived.**
   `NativeLifecycleControlV1` carries `UpdateId`/`TimeMicroseconds` only -- no semantic-hash or
   snapshot-revision concept exists inside the lifecycle machine at all (those are compiled-program/
   authoring-level concepts). The recorder must be constructed with these once per compiled program's
   lifetime, mirroring exactly how `NativeReferenceTraceProjectionV1` (the reference-executor's own
   existing, unrelated trace projection) already receives them externally rather than deriving them.
4. **Abort reason and in-flight-abort status are driver-tracked, not machine-exposed.** The driver
   already knows the reason it called `TryRequestAbort(reason)` with, and already knows whether an
   abort is currently in flight (it called `TryRequestAbort` and has not yet observed the terminal
   `Completed`/`RootAborted`) -- both needed to populate `AbortReason` and to distinguish a
   `CompositeExited`/leaf-Exit's `ExitReason` as `Aborted` rather than `Success`/`Failure`. This
   requires zero new accessors on the machine: it is state the caller already necessarily has.
5. **The non-root `CompositeExited` status gap is real and left open, not silently worked around.**
   An external recorder driven only by `TryAdvance`'s return value cannot populate `NodeExited`'s
   `ExitReason`/`Status` for a *nested* composite's own exit -- confirmed by the second spike test,
   not assumed. Closing it would need either a small, additive, non-breaking widening of
   `NativeLifecycleStepResultV1` (an optional `HasExitStatus`/`ExitStatus` pair on `CompositeExited`,
   mirroring `Completed`'s own existing `HasRootStatus`/`RootStatus` shape) or the driver
   independently tracking each frame's own pending status itself. Both are production changes this
   card's own Forbidden-changes clause does not permit; left to the future implementation card.
6. **Self-driven scope only, unchanged.** This wiring works for any caller that drives the machine
   itself (a test harness today, a future Play-mode host). It does not attach to, discover, or claim
   anything about an arbitrary already-running game process -- the same boundary `P3-010`/`P3-011`/
   `P5-008` already accepted, not narrowed or widened here.

## Consequences

- A future, not-yet-numbered implementation card builds a real `Runtime/Trace/Native/` recorder type
  per this ADR's mapping table, wires it into `SchedulingPolicyDriver`'s three drive methods (or
  their eventual Play-mode-host equivalent) as an additive hook, and implements `P6-008`'s originally
  scoped `trace`/`compare-trace` MCP tools on top of it.
- That future card must either accept the disclosed non-root `CompositeExited` status gap as a
  documented trace-fidelity limitation, or separately escalate the small `NativeLifecycleStepResultV1`
  widening decision item 5 describes -- this ADR does not pre-decide which.
- `P6-008`'s `trace`/`compare-trace` tools may claim a real, self-driven native tree's trace is
  readable end-to-end through the existing debugger/timeline read side. They may not claim attachment
  to an arbitrary running game process, or full per-node exit-status fidelity for nested composites,
  until the item-5 gap is separately closed.

## Explicitly unverified (stated, not generalized)

- Only `Immediate`-style single-step-at-a-time driving was exercised (mirroring
  `NativeLifecycleMachineTests`'s own construction). `SchedulingPolicyDriver`'s `Budgeted` and
  `BatchedJobsSameFrame` paths call the same `TryHandleStep`-shaped switch but were not separately
  spiked; the mapping table is expected to apply unchanged since it keys only on `step.Kind`/phase,
  not on which drive mode produced the step, but this was not empirically re-verified for those two
  modes.
- Recorder-side threading beyond one synchronous caller was not exercised -- consistent with
  `AIBT-015`'s own sequential-per-instance execution decision; `NativeTraceWriterV1`'s own spin-lock
  already handles concurrent writers if a future multi-worker driver needs one, unchanged by this ADR.
- Payload-bearing trace records (`PayloadOffset`/`PayloadLength`, e.g. for blackboard/command detail)
  were not exercised -- the spike used zero-payload records throughout; nothing here contradicts the
  existing payload mechanism, it simply wasn't needed for this card's own scope.
