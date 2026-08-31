# P6-015 Native trace production-wiring decision evidence

## Result

Done, accepted. `ADR-P6-015` (`AIBT-027`) decides the real-lifecycle-step-to-trace-record
translation lives as an external recorder co-located with whatever already drives
`NativeLifecycleMachineV1` (e.g. `SchedulingPolicyDriver`), hooking the exact same
`TryAdvance`/`TryCompleteDispatch` call sites the driver already has -- never a change inside the
machine itself. A fixed mapping table translates `NativeLifecycleStepKindV1` values (plus dispatch
completion phase) into `NativeTraceEventKindV1` values.

## Real finding: `CompositeExited` carries no exit status for non-root composites

Unlike `Completed` (`HasRootStatus`/`RootStatus`), the `CompositeExited` step result exposes only a
`NodeIndex` -- confirmed by reading every one of its four call sites in
`Runtime/Execution/Native/Core/NativeLifecycleMachineV1.cs` (`AdvanceComposite`, `AdvanceDecorator`,
`AdvanceParallel`, `AdvanceAbort`). The root's own exit is unaffected (the recorder defers and folds
it into the following `Completed` step, which does carry `RootStatus`), but a **nested** composite's
own exit has no such fallback. A second spike test drives a 3-level tree (root sequence → child
sequence → leaf) to the child sequence's own `CompositeExited` and shows, by constructing the record
an external recorder could actually produce, that no `ExitReason` can be populated from that step
alone. Left open for a future implementation card (either a small additive widening of
`NativeLifecycleStepResultV1`, mirroring `Completed`'s own optional-status shape, or driver-side
per-frame status tracking) -- not attempted here per this card's own Forbidden-changes clause.

## Verification

```text
Disposable spike (SpikeNativeTraceProductionWiring, Tests/Editor/NativeTraceProductionWiringSpike/
  during this session, archived afterward): 2/2 tests passing, live via Unity MCP run_tests --
  SimpleSequence_RealExecutionProducesReadableTraceViaUnmodifiedReadSide,
  NestedComposite_CompositeExitedStepAloneCannotSupplyExitStatus_RealGapNotAssumed
Regression (both required by this card's own acceptance criteria, unmodified, live via Unity MCP):
  AIBT.Tests.Editor.Debugger.NativeExecutionDebuggerSessionTests -- 5/5 passing
  AIBT.Tests.Editor.Trace.TraceTimelineModelTests -- 4/4 passing
Full EditMode regression (host project): see verification-results.json for the exact count --
  run with the spike present, zero new failures beyond any pre-existing unrelated ones
Verify-Static.ps1 -- passed
git diff --check -- clean
```

No production file (`Runtime/Trace/Native/`, `Runtime/Scheduling/`,
`Editor/Debugger/NativeExecutionDebuggerSession.cs`, `Editor/Trace/TraceTimelineModel.cs`) was
touched, per this card's own Forbidden-changes clause -- the spike lived temporarily in
`Tests/Editor/NativeTraceProductionWiringSpike/`, then archived to
`Spikes~/NativeTraceProductionWiring/` and deleted from `Tests/`, mirroring `P6-013`'s own
`SpikeReferencePreviewSimulationCapability` precedent exactly.

## Handoff

A future, not-yet-numbered implementation card builds the real recorder type per `ADR-P6-015`'s
mapping table, wires it additively into `SchedulingPolicyDriver`'s drive methods (or their eventual
Play-mode-host equivalent), and implements `P6-008`'s originally-scoped `trace`/`compare-trace` MCP
tools on top of it -- either accepting the disclosed non-root `CompositeExited` status gap or
separately escalating the small machine widening that would close it.
