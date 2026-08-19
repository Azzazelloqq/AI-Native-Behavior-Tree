# P3-010 native execution debugger attachment evidence

## Result

- `Editor/Debugger/NativeExecutionDebuggerSession.cs` (new, `AIBT.Editor.Debugger`): a strictly
  read-only attach/detach session over a caller-owned `NativeTraceChannelOwnerV1`. `Attach(owner)`
  stores the reference only; `Detach()` clears it only -- neither call creates, disposes, resets, or
  acquires a writer lease on the channel. `TryReadTrace` calls only the channel's existing public
  `TryGetSnapshot`, which itself only succeeds when the owner is `NativeOwnerStateV1.Initialized`
  (no writer lease/job in flight), so there is no code path in this session that can stall or
  perturb a live native pass. Unlike P3-009, no assembly-boundary facade was needed: every native
  trace type (`NativeTraceChannelOwnerV1`, `NativeTraceRecordV1`, `NativeTraceChannelSnapshotV1`,
  etc.) is already `public` in `AIBT.Runtime`.
- `NativeDebuggerTraceView` (same file): a UI-facing (allocating, not a native hot path) projection
  of one snapshot into active-node indices (nodes with an unmatched `NodeEntered` in the snapshot),
  ordered step history, and diagnostic events (`Kind == DiagnosticRaised`).
- `Editor/AIBT.Editor.asmdef` and `Tests/Editor/AIBT.Editor.Tests.asmdef` gained a `Unity.Collections`
  reference (plus `Unity.Burst` for the test assembly, which also schedules a real Burst job); both
  asmdefs use `overrideReferences: true`/explicit references, so nothing was implicitly available
  before this addition. Integration-owned file update per `AGENT_WORKFLOW.md`, required by this
  card's own result.
- 5 automated tests, all passing (`Tests/Editor/Debugger/NativeExecutionDebuggerSessionTests.cs`):
  - `AttachReadsStepHistoryAndDiagnosticsFromTheBoundedChannel` -- a real `[BurstCompile] IJob`
    writes a 6-record pass (UpdateStarted/NodeEntered/NodeTicked/DiagnosticRaised/NodeExited/
    UpdateCompleted) into a real bounded channel; the session reads it back with the right counts.
  - `ANodeEnteredWithoutAMatchingExitIsReportedActive` -- omitting the exit record leaves the node
    reported active.
  - `TryReadTraceFailsCleanlyWhenNotAttached`.
  - `AttachingAndReadingBetweenPassesAddsNoManagedAllocationToNativeExecution` -- record
    construction (managed, necessarily allocating) happens before the measured block; only the
    acquire/schedule/complete/release sequence is measured via
    `UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory()`, mirroring how the existing
    `NativeExecutionAllocationTests` isolates its own measured calls.
  - `DetachingMidRunLeavesNativeOutputIdenticalToRunningWithoutADebugger` -- a pass run with the
    debugger attached-and-reading between calls produces a byte-for-byte identical trace record
    sequence (kind/sequence/node index) to the same pass run with no debugger at all.
- Live interactive verification in the running `6000.5.8f1` Editor via `unityMCP.execute_code`:
  created a real `NativeTraceChannelOwnerV1`, wrote one record through a real writer lease,
  attached `NativeExecutionDebuggerSession`, and read it back (`attached=True; readOk=True;
  stepHistory=1; active=[3]`); no console errors.
- Full EditMode suite: 1304 tests (1299 + these 5 new), only the same 3 pre-existing failures
  unrelated to this card (see "Scope and limitations").

## Decision

Scope of "attach to a running native executor" was escalated to the owner (`AskUserQuestion`,
2026-08-19) before implementation, per `DECISION_BOUNDARIES.md`'s "a dependency is missing" stop
condition (`AGENT_WORKFLOW.md`). Finding: **no production Play-mode host component exists anywhere
in AIBT** -- nothing instantiates or drives a `NativeLifecycleMachineV1`/
`NativeBatchedLifecycleOwnerV1` during Play mode; native execution today is driven only from
tests/benchmarks, and no production code ever wires a `NativeTraceChannelOwnerV1` to a live pass
(the only non-test caller of `NativeTraceWriterV1.TryAppend` is
`Runtime/Diagnostics/Native/NativeChannelsBurstProbeV1.cs`, a synthetic single-record Burst-compile
proof, not a real execution pipeline). The card's premise -- "how the editor locates a running
native executor instance (in-Editor Play mode first)" -- has nothing to locate today.

Accepted answer: narrow this card's scope to defining and proving the **attach/detach and
read protocol** against a self-driven native pass, exactly mirroring how P3-009's Preview owns its
own reference-executor instance rather than attaching to something external. The attach protocol
itself is intentionally minimal and honest about what exists: whatever owns a running native pass
hands this session its `NativeTraceChannelOwnerV1` reference directly via `Attach(owner)` -- there
is no discovery/registry mechanism, because nothing in production code would populate one yet.
Building a real Play-mode host component was explicitly declined for this card (it is new
production architecture -- a new public API, lifecycle/ordering decisions -- well outside
`Editor/Debugger/`'s allowed-changes scope and this card's Forbidden-changes clause, which
restricts this card to being strictly a reader).

## Scope and limitations

- **No real Play-mode attach target exists yet.** This card proves the read/attach/detach
  mechanics against a self-driven, test-owned native pass (a real Burst job writing through the
  real, unmodified `NativeTraceWriterV1`/`NativeTraceChannelOwnerV1` API). A future card that adds
  a production Play-mode host component would need its own accepted decision (per `AGENT_WORKFLOW.md`,
  not a silent extension of this one), after which this session's `Attach`/`TryReadTrace` API should
  work unchanged against a real owner reference -- the read API is intentionally decoupled from how
  the owner came to exist.
- **Standalone-Player attachment** remains explicitly out of scope, per the card's handoff notes.
- **The debugger's own read-side projection (`NativeDebuggerTraceView.Build`) allocates** (managed
  `List<T>`s) -- this is UI-facing display code, not part of the native hot path, and is not
  claimed to be zero-GC. The acceptance criterion under test is that the *native execution itself*
  (acquire/schedule/complete/release) shows no allocation delta from the debugger's presence, which
  is what `AttachingAndReadingBetweenPassesAddsNoManagedAllocationToNativeExecution` measures.
- **Pre-existing, unrelated test failures** in the same full-suite run, not touched or caused by
  this card: `AIBT.Tests.CodeGen.Generation.GeneratedArtifactContractTests` (2 tests, "the CodeGen
  test assembly must belong to the AIBT package") and
  `LocalSaveSystem.Tests.SaveStoreTests.SaveStore_AutoSave_WritesToDisk` -- identical to the
  failures already recorded in `Planning~/Evidence/P3-009/README.md`.

See `verification-results.json` for exact commands and results.
