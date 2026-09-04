# P7-032 — Preserve native scheduler ownership after rejected completion

Status: `Done`

Owner authorized autonomous continuation on 2026-09-04. See Evidence/P7-032/implementation-plan.md.

## Objective

Keep SameFrame and Pipelined controllers consistent with their execution owner when a completion
call is rejected, so callers retain a supported recovery and disposal path.

## Revalidated finding

**P2 — Wrong-sized completion buffers strand the controller/owner pair in incompatible states.**
Reviewed 2026-09-04 against `66fa058`. `NativeBatchedLifecycleOwnerV1.TryComplete` checks buffer
creation and lengths before changing its scheduled state. Both phase controllers, however, clear
their dependency and transition to ExecuteReady on every false return from that call.

Earlier in this review, a real scheduled job was probed in Unity for both controllers with a
zero-length results buffer for one lane: completion failed; phase became ExecuteReady; retry with
correct buffers and rescheduling failed; abort reported success but disposal still failed. Cleanup
required completing through the underlying owner, which is not a controller API recovery path.
The same control flow remains in the current source; the scheduled-job probe was not rerun in
this re-review. This is a rejected-input recovery defect, not evidence that valid scheduling fails.

The owner's false return has two meanings: precondition rejection while still scheduled, or an
executed lane failure after it has completed and become ready. A fix must distinguish these;
blindly retaining ExecuteScheduled on every false return creates the opposite inconsistency.

## Depends on

- P2-019 (same-frame scheduling).
- P4-003 (pipelined scheduling).

## Required reading

- `Documentation~/architecture.md`, `Documentation~/execution-and-scheduling.md`.
- `Planning~/DECISION_BOUNDARIES.md`, P2-019 and P4-003 cards/evidence.
- `Runtime/Scheduling/Native/NativeBatchedLifecycleOwnerV1.cs`.
- `Runtime/Scheduling/Native/NativeSameFramePhaseControllerV1.cs`.
- `Runtime/Scheduling/Native/NativePipelinedPhaseControllerV1.cs` and their tests.

## Scope

- The three native scheduling files above, only completion ownership/state handling.
- Focused regressions in `Tests/Runtime/NativeExecution/Scheduling/`.
- `Planning~/Evidence/P7-032/`.

## Implementation plan

1. Identify precondition rejection versus completed lane failure using existing owner contracts.
   Agree any cross-task/internal contract change before implementing it.
2. Add the same behavioral recovery cases for both controllers, then correct the smallest
   state transition. Preserve pipelined stage restrictions and truthful job ownership.
3. Verify retry, subsequent valid rounds, metrics and disposal through supported APIs only.

## Forbidden changes

- No force-dispose of live native storage, reflection-based production recovery, ignored failures,
  scheduler policy change, batching optimization, or population-host work.
- Do not relax Pipelined's requirement to advance a stage before completing a scheduled round.

## Deliverables and acceptance criteria

- For each controller, schedule a real job and reject missing/wrong-length result and failure
  buffers. A retry with correct buffers completes that same round without executing it again.
- Metrics advance only for the accepted completed round; invalid arguments do not publish results
  or falsely release the execution owner's outstanding operation.
- After recovery, another legal round/update succeeds and normal abort/disposal succeeds without
  accessing private owners or leaking native allocations.
- A real lane failure with valid buffers preserves its diagnostic and the completed owner's
  correct state, permitting the documented cleanup path. Test this separately from bad arguments.
- Existing successful execution and Pipelined stage-order behavior remain unchanged.

## Required verification

From the package root, with verification environment variables set:

```powershell
& './Tools~/Verification/Verify-Static.ps1'
& './Tools~/Verification/Run-UnityTests.ps1' -UnityPath $UnityPath -ProjectPath $ProjectPath -OutputPath $OutputPath -Mode EditMode -Scope Full
git diff --check
```

Run focused native scheduling tests first; use Unity MCP tests for an already-open project.
Record exact test counts and baseline failures. If normal scheduling/per-round work changes,
run the relevant existing benchmark; otherwise document why an error-path/state-only correction
does not support or require a new performance claim.

## Handoff notes

Owner authorized continuation and commits on 2026-09-04. Completed with 14 added real-job
regressions; 126/126 scheduling tests passed. Full EditMode: 1726/1729, the same three baseline
failures. See Evidence/P7-032/README.md. No policy, latency or normal-round work changed.
