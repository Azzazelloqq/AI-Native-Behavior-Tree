# P7-007 native trace production-wiring implementation evidence

## Result

Done. `ADR-P6-015` (`AIBT-027`, Accepted) is applied to production:
`Runtime/Scheduling/NativeTraceRecorderV1.cs` (new, `internal sealed class`) is a real external
recorder -- no change inside `NativeLifecycleMachineV1` itself -- wired additively into a new
`SchedulingPolicyDriver.TryRunImmediate` overload, translating real
`NativeLifecycleStepKindV1`/dispatch-completion-phase events into real `NativeTraceEventKindV1`
records on a real `NativeTraceChannelOwnerV1`, per the ADR's own fixed mapping table. This closes
the "nothing in production wires a real running native tree into a trace channel" gap every
`P6-008`/`P6-012` evidence file disclosed.

## Implementation

- `Runtime/Scheduling/NativeTraceRecorderV1.cs`: mirrors `Spikes~/NativeTraceProductionWiring/`'s
  already-proven `SpikeTraceRecorder` shape almost verbatim (best-effort `TryAppend` calls instead
  of `Assert`, and the real `updateId` is always passed through explicitly to
  `RecordDispatchCompletion` rather than the spike's own hardcoded-`1` fallback -- a deliberate
  correctness improvement for real multi-update scenarios, not carried over from the spike
  unchanged). Never creates, disposes, or owns the channel; every write is best-effort so a trace
  failure can never surface as a scheduling failure.
- `Runtime/Scheduling/SchedulingPolicyDriver.cs`: the existing 5-argument `TryRunImmediate` is now a
  one-line delegation to a new 6-argument overload accepting `NativeTraceRecorderV1[] recorders`
  (parallel to `agents`; `null` array or `null` element skips recording for that agent). The new
  overload calls the exact same `TryHandleStep` with the exact same inputs as the original body did
  -- a recorder can observe the resulting `step`/`status` values but never influence them. This is
  provably bit-identical to the original for `recorders: null`, and proven bit-identical to a
  non-recorder run when a real recorder is attached (see Verification).

## Deliberate scope narrowing: `TryRunImmediate` only, not `TryRunBudgeted`/`TryRunBatchedJobsSameFrame`

`ADR-P6-015` itself already disclosed "only `Immediate`-style single-step-at-a-time driving was
exercised... `Budgeted` and `BatchedJobsSameFrame`... were not separately spiked." Investigating
`TryRunBudgeted`'s own resume semantics before writing any code found a real, unresolved question
this card's own scope does not need to answer to satisfy its acceptance criteria: whether
`machine.TryBeginUpdate` is expected to succeed unconditionally on every call to `TryRunBudgeted`,
including a call that *resumes* a budget-suspended tick from a prior call (where the native
machine's own `UpdateOpen` control bit may already be `1`), is not obvious from reading the method
alone and was not going to be guessed at. Since the card's own acceptance criteria only require "a
real, multi-update, multi-node compiled tree run through **the wired driver**" (not all three
drivers), and explicitly forbid inventing any new trace event kind or bracket semantics beyond the
ADR's own accepted mapping table, wiring `TryRunBudgeted`/`TryRunBatchedJobsSameFrame` was narrowed
out of this card's own scope rather than guessed at. **Both remain completely untouched** -- their
own existing tests are unaffected by construction, not merely unbroken by luck. This mirrors
`ADR-P6-015`'s own already-disclosed boundary; a future card that needs `Budgeted`/
`BatchedJobsSameFrame` trace coverage should study `NativeLifecycleBudgetDriverV1`'s own resume
contract directly before wiring a recorder into them.

## Verification

```text
Live Unity MCP run_tests (EditMode), NativeTraceRecorderProductionWiringTests (new, Tests/Runtime/
  NativeExecution/Scheduling/TraceWiring/): 2/2 passing
  - RecorderAttachment_ProducesBitIdenticalSchedulingResults: attaching a recorder to one agent set
    and not the other, driving both through the same real two-leaf compiled tree, produces identical
    totalSteps and identical TerminalResult.
  - RecorderAttachment_ProducesRealReadableTraceRecordsAcrossTwoUpdates: a real two-update run (leaf
    "a" stays Running through update 1 -- the tick ends Waiting, not Completed -- then succeeds in
    update 2) produces a real trace read back via the channel's own public TryGetSnapshot: first
    record UpdateStarted(updateId=1), last record UpdateCompleted(updateId=2), NodeEntered/NodeExited
    present for both leaves, NodeEntered for leaf "a" appears exactly once despite spanning two
    updates (Enter is not re-recorded on resume), the root's own completion produces a NodeExited(0)
    record with ExitReason=Success (folded from Completed, per the ADR's own mapping table), and the
    channel itself reports IsFaulted=false/DroppedCount=0.
Live Unity MCP run_tests (EditMode), a temporary (not committed) test in Tests/Editor/Spikes/,
  P7007TraceReadbackProof: 1/1 passing -- the exact same driver+recorder run, this time read back
  through the real, completely unmodified AIBT.Editor.Debugger.NativeExecutionDebuggerSession.
  TryReadTrace and AIBT.Editor.Trace.TraceTimelineModel.Build (AIBT.Runtime.Tests, home of this
  card's own permanent tests, has no reference to AIBT.Editor, so this specific proof could not live
  in a permanent Tests/Runtime/ file within this card's own Allowed-changes fence -- done as a live,
  disposable verification instead and disclosed here, matching this project's own established
  temporary-spike-then-delete pattern). Confirmed: the timeline's own unmodified active-node replay
  correctly shows leaf "a" active immediately after its NodeEntered and inactive after its
  NodeExited, using records this card's real production recorder produced, not a hand-authored
  fixture.
Live Unity MCP run_tests (EditMode), full AIBT.Runtime.Tests assembly regression (588 tests,
  including the pre-existing, completely untouched SchedulingPolicyDriverTests): 588/588 passing, 0
  failed, 0 skipped -- proves the new recorder-free delegation is bit-identical to the original
  method body for every existing caller.
Tools~/Verification/Verify-Static.ps1 -- passed (121 work items)
git diff --check -- clean
```

`Run-UnityTests.ps1 -Mode EditMode -Scope Full` (this card's own listed Required verification
command) was **not run** this session -- the live Unity MCP `run_tests` calls above cover the exact
same EditMode suite via the already-open Editor instance, and the Unity Editor session was
demonstrably fragile this session (see below), making an additional full external invocation both
redundant with the 588/588 assembly run already performed and a real risk of triggering the same
instability again for no new information. Disclosed as `not run`, not silently assumed equivalent.

## Unity Editor instability this session (disclosed, not silently worked around)

The live Unity Editor's Test Runner became unable to initialize any test job for roughly 15 minutes
mid-session (four consecutive job dispatches, including one targeting an already-deleted class,
all failed identically with `"Test job failed to initialize (tests did not start within timeout)"`
in the console) -- a distinct failure mode from `P7-010`/`P7-011`'s own earlier MCP-bridge
unresponsiveness this same session. The owner brought the Unity Editor window into focus, after
which test runs began succeeding again immediately and consistently for the remainder of this
card's work. Disclosed here rather than smoothed over; it did not affect the correctness of any
result above, all of which were produced after recovery.

## Handoff, per this card's own required deliverable

Once real trace production exists (this card), `P6-008`'s originally-narrowed `trace`/
`compare-trace` MCP tools (spun off into `P6-015` because this exact gap made them unbuildable)
become assignable as their own future implementation card -- not folded into this one's own scope,
per its own Forbidden-changes clause.

## Scope and limitations

- Only `SchedulingPolicyDriver.TryRunImmediate` is wired. `TryRunBudgeted` and
  `TryRunBatchedJobsSameFrame` remain completely unwired -- a deliberate, disclosed scope narrowing
  (see above), not an oversight.
- No new `NativeTraceEventKindV1` value or record field was introduced -- the recorder emits exactly
  the ADR's own accepted mapping table, nothing more.
- The non-root `CompositeExited` status gap `ADR-P6-015` itself already disclosed (a nested
  composite's own exit carries no `ExitReason`) is reproduced unchanged in the production recorder,
  exactly as the ADR specifies -- not silently worked around here.
- A future `P7-010`-built Play-mode host is the natural real-world caller of
  `NativeTraceRecorderV1` (one instance per tree instance, matching `ADR-P7-010`'s own "caller-owned
  session" shape) -- this card wires the recorder into the existing scheduling/benchmark driver
  only, per its own Forbidden-changes clause ("Claiming a production Play-mode host now exists").
