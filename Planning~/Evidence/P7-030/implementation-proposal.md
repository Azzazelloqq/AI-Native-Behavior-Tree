# P7-030 implementation proposal

Status: Accepted by the owner on 2026-09-04 ("Подтверждаю"). This file is a design/implementation plan, not passing
verification evidence or a change to accepted runtime semantics.

## Preparation

Source inspected at `fa14161`. Dependencies P7-010 and P7-027 are done in `work-items.json`.
At proposal time no production source had been changed. The existing lifecycle machine and budget driver will be
reused; this proposal adds the host integration needed to satisfy the four P7-030 findings.

## Proposed behavior

1. **Completion:** retain Success/Failure and stop progressing that instance. Do not implicitly
   restart. Subsequent Unity frames are harmless; no repeated lifetime error. Automatic restart
   and pooling/reinitialization are outside this fix.
2. **Clock:** default to Unity scaled game time, converted to integer microseconds. Allow an
   injected `Func<long>` clock for deterministic tests or a caller-selected clock. Read once per
   new eligible update. Reject invalid/backwards input with the existing failure mechanism rather
   than silently clamping it. Advancing time alone does not disable Tick at timeScale zero.
3. **Budget:** a nullable step limit selects Immediate (null) or Budgeted (including zero, which
   performs no semantic step). Expose a per-frame step-limit setting so a zero-budget instance
   can subsequently resume. Budget exhaustion ends the current frame's segment; the next frame
   resumes the same logical update, with its update ID and time frozen. Begin a new logical update
   only after Waiting; a terminal result stops the instance. No wall-clock budget is introduced.
4. **Lifecycle:** a new dispatch overload receives node index, phase, logical update ID/time and
   the actual Exit/Abort reason. It returns the existing `BurstContextResult` plus Tick's
   `NodeStatus`. All required callbacks are invoked before acknowledging native dispatch.
   Opaque Burst contexts/native ownership tokens are not fabricated or exposed by this API.
   Caller-owned adapters remain responsible for invoking their node implementation; automatic
   construction of arbitrary generated catalog workspaces is not added by this card.
5. **Compatibility:** retain the existing `DispatchLeaf(uint)` overload for explicitly Tick-only
   integrations, forwarding through the same host loop with no-op non-Tick callbacks. Document
   its limitation; do not silently change its callback count or claim it implements a general
   Action lifecycle. Its completion and clock bugs are still fixed.
6. **Disable/destruction:** disabling the component pauses stepping without aborting its activation;
   enabling resumes it. The default game clock continues while this component alone is disabled:
   deadlines may therefore expire at the next eligible update. A budget-suspended update first
   resumes with its frozen time, as required by the executor contract. Normal destruction cancels
   active work through Abort/Exit(Aborted), then releases owned storage. It never starts a fresh
   root merely to cancel an instance that has not run. Do not impose the normal per-frame budget
   on mandatory destruction cleanup.
7. **Failure:** a failed callback/update stops further execution and reports its diagnostic once.
   Do not keep invoking faulted user callbacks or claim successful lifecycle cleanup after a
   callback itself throws. Release owned resources and retain enough failure context to diagnose it.

## Public API proposal

Keep the old TryBootstrap overload and add one accepting a full-lifecycle dispatch delegate,
an optional injected clock and the existing trace capacity. Add a nullable step-budget property;
the default is Immediate. Use one immutable request value containing the fields in item 4 and
a delegate returning `BurstContextResult` with `out NodeStatus` for Tick. No scheduler manager,
new node interface hierarchy, event bus or restart-policy framework is needed.

The old overload uses the default clock. The new overload may select the clock explicitly.
The implementation must keep error reporting compatible with existing diagnostic contracts;
any genuinely missing diagnostic/API contract must be proposed rather than invented silently.

## Narrow additional file scope needed

Inspection found two integration details beyond the host file:

- `NativeLifecycleStepResultV1` currently exposes phase/index/token but not Exit/Abort reasons.
  Extend the internal result in `Runtime/Execution/Native/Core/NativeLifecycleMachineV1.cs`
  to carry the actual reasons already owned by the machine. Do not duplicate the machine's
  active-frame tracking in the host or infer reasons from the last Tick.
- `Runtime/Scheduling/NativeTraceRecorderV1.cs` lacks budget-yield/resume recording and accepts
  a node status where Exit requires a reason. Add the minimal recorder hooks needed for accurate
  host lifecycle/budget traces, reusing existing event kinds. Release the writer lease when a
  frame segment yields and reacquire on resume, without inventing an UpdateCompleted event.

These are owner-approved narrow scope extensions for P7-030, not authorization to refactor either
subsystem or change its semantics. Corresponding focused tests belong with the existing native
lifecycle/trace tests; P7-032's controller recovery remains separate.

## Implementation sequence after agreement

1. Record the accepted choices and narrow scope in P7-030 and its decision documentation.
   Add behavior tests before implementation: terminal Success/Failure followed by more frames;
   Timeout and Cooldown deadline boundaries with controlled time; real Enter/Tick/Exit and
   Abort/Exit effects; budget zero, yield/resume and Immediate equivalence; disable/resume and
   destruction cleanup; callback error and legacy-overload compatibility.
2. Expose native dispatch reasons and add accurate recorder hooks. Preserve existing consumers;
   run focused native lifecycle and trace tests.
3. Implement one host execution loop using the existing native lifecycle and budget drivers.
   Keep logical-update state separate from Unity frame segments; guard against reentrant driving
   and repeated initialization without widening the public lifecycle to support pooling.
4. Run host tests, full EditMode regression and static verification. Record exact counts and
   independently identify unrelated baseline failures. Measure allocations after warmup.
5. Verify actual multi-frame Play-mode execution, debugger attachment across budget yields and
   scene teardown. Do not substitute reflection-pumped Update calls for Player-loop evidence.
   Regenerate affected API documentation and correct capability claims; report Player/build
   paths separately if not exercised. No commit/push is part of this task authorization.

## Decision recorded

The owner approved the proposed behavior, additive API shape and two narrow internal/recorder
scope extensions before implementation. This satisfies P7-030 and Planning~/DECISION_BOUNDARIES.md's
agreement requirement for public API, clock, budget and disable/teardown semantics.
