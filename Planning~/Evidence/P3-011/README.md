# P3-011 trace views evidence

## Result

- `Editor/Trace/TraceTimelineModel.cs` (new, public): a pure, allocating (UI-facing) replay over
  one `NativeDebuggerTraceView` snapshot (P3-010's read-only channel view). `Build(view)` walks
  `StepHistory` once, tracking `NodeEntered`/`NodeExited` events to compute the active runtime-node
  set *after every individual step*, not just the final snapshot state -- this is what makes
  scrubbing to a past step show that step's actual graph state rather than only the latest one.
  Diagnostic events are correlated to their step index and the active-node set at that point.
  `HasDroppedEvents`/`DroppedCount`/`IsFaulted` are carried straight through from the channel
  snapshot P3-010 already exposes. This type never reads the trace channel itself and never calls
  anything in `Editor/Debugger/` beyond the public `NativeDebuggerTraceView` it's handed -- a pure
  consumer, per this card's Forbidden-changes clause.
- `Editor/Trace/TraceTimelineWindow.cs` (new): an `EditorWindow` (menu `AIBT/Trace Timeline`) with
  a step-scrub slider, a private `BehaviorTreeGraphView` instance (P3-003's adapter, consumed as-is,
  not modified) highlighting the active nodes *at the scrubbed step*, a diagnostic-event list, and
  an explicit red "channel full / events dropped" banner shown whenever
  `TraceTimelineModel.HasDroppedEvents` (or `IsFaulted`) is true. `AttachSession` takes a
  caller-owned `NativeExecutionDebuggerSession` (P3-010); `LoadGraphContext` takes the
  `TreeDocument`/`NodeRegistry`/`CompiledProgram` needed to translate the channel's raw
  `RuntimeNodeIndex` values into `NodeId`s for highlighting (the same `CompiledProgram.DebugMap`
  translation pattern P3-009's driver already established).
- 5 automated tests, all passing:
  - `ScrubbingEveryStepReproducesTheActiveSetTheRawChannelRecordsActuallyProduced` -- a real
    `[BurstCompile]` job writes a known Enter(A)/Enter(B)/Exit(B)/Enter(C)/Exit(C)/Exit(A) sequence
    into a real bounded channel; at *every* step index, `TraceTimelineModel`'s active set is
    asserted equal to an independently hand-replayed active set computed directly from the same raw
    records the test itself constructed -- not by trusting the model's own logic circularly.
  - `DiagnosticEventsAreCorrelatedToTheStepAndActiveNodesThatProducedThem`.
  - `OverflowingTheBoundedChannelIsReportedAsDroppedRatherThanATruncatedCompleteTrace` -- a
    small-capacity channel (`recordCapacity=4`, so `ordinaryCapacity=3`) is overflowed with 6 real
    records via the channel's own unmodified bounded-capacity/eviction logic (no synthetic
    verification-only hook); asserts `HasDroppedEvents`, `DroppedCount > 0`, and that the view's
    step count is honestly smaller than what was written (not silently presented as complete).
  - `EmptyModelHasNoStepsAndNoDroppedEvents`.
  - `AttachingASessionAndScrubbingHighlightsTheCorrespondingCompiledNodesWithoutThrowing` -- a real
    compiled tree (via `ReferencePreviewDriver.CreatePreviewNodeRegistry()` and
    `ReferenceCompiler.Compile`, matching `P3-009`'s pattern), a real bounded channel, and the
    actual `TraceTimelineWindow` class end-to-end.
- Full EditMode suite: 1310 tests (1305 + these 5 new), only the same 3 pre-existing failures
  already recorded in `Planning~/Evidence/P3-009/`, `P3-010/`, and `P3-012/` (unrelated to this
  card).
- Live interactive verification in the running `6000.5.8f1` Editor via `unityMCP.execute_code`:
  compiled the `success-then-running.aibt.json` fixture, wrote two real trace records through a
  real writer lease, opened `TraceTimelineWindow`, attached the session, and loaded the graph
  context -- `steps=2; scrubIndex=1; dropped=False`; no console errors.

## Decision

No new decision. Consistent with `P3-009`, `Editor/Trace/` translates the channel's raw
`RuntimeNodeIndex` values to `NodeId` via `CompiledProgram.DebugMap` -- the same small,
already-established pattern, not a new mechanism.

## Scope and limitations

- **Self-driven channels only**, inherited from `P3-010`'s own accepted scope narrowing: this card
  is a pure consumer of whatever `NativeExecutionDebuggerSession` is attached to, and no production
  Play-mode host exists yet to attach to a *real* running game (see `Planning~/Evidence/P3-010/README.md`).
  Nothing here changes that; `TraceTimelineWindow.AttachSession` accepts any caller-owned session,
  so it will work unchanged once a future card supplies a real one.
- **No `Editor/Graph/` live wiring** -- `TraceTimelineWindow` hosts its own private `BehaviorTreeGraphView`
  instance rather than attaching to an already-open `BehaviorTreeGraphWindow`, the same disclosed
  pattern as every `P3-004`--`P3-010`/`P3-012` evidence file.
- **Diagnostic-event node correlation uses the record's own `RuntimeNodeIndex`** (when the channel
  sets it) plus the active-node set at that step; it does not attempt to guess a node for
  diagnostics the writer chose to leave node-less (`RuntimeNodeIndex == CompiledIndex.Invalid`).
- Pre-existing, unrelated test failures observed in the same full-suite run, not touched or caused
  by this card: `AIBT.Tests.CodeGen.Generation.GeneratedArtifactContractTests` (2 tests) and
  `LocalSaveSystem.Tests.SaveStoreTests.SaveStore_AutoSave_WritesToDisk` -- identical to the
  failures already recorded in the three preceding P3 evidence files.

See `verification-results.json` for exact commands and results.
