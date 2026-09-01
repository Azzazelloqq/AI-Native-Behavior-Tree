# P7-011 native-backend hot reload decision evidence

## Result

Done, accepted. `Documentation~/decisions/ADR-P7-011-native-backend-hot-reload.md` (`AIBT-035`)
decides how `ADR-P5-001`'s construct-fresh-and-selectively-copy hot-reload model applies to the
native execution backend -- the disclosed gap restated unchanged since `P5-004` through
`P5-GATE`/`P6-GATE`/Phase 7's own handoff. No production file under `Runtime/Execution/Native/`,
`Runtime/Compiled/Native/`, `Runtime/State/Native/`, or `Runtime/Scheduling/Native/` was touched,
per this card's own Forbidden-changes clause -- this card decides on paper plus a disposable live
spike only.

## Decision, summarized

1. Fresh program-image and instance-arena construction reuse `NativeProgramImageOwnerV1.TryCreate`/
   `NativeInstanceArenaOwnerV1.TryCreate` unchanged -- both already derive their own required
   capacity from a `CompiledProgram`/leased program view; no new capacity-planning code needed.
2. State capture/seeding needs **zero new internal engine methods** -- a real, positive difference
   from the reference-executor backend, which needed two new `internal` methods
   (`CaptureNodeState`/`SeedNodeState`) before migration was buildable. Native composes entirely from
   `NativeInstanceArenaOwnerV1`'s already-public `TryAcquireExecutionLease`/`TryReleaseExecutionLease`
   plus the View's public `Frames`/`Generations`/`NodeMemory` properties.
3. Native migration is **not** restricted to an idle old instance the way the reference executor's
   own implementation is -- proven live migrating a genuinely active instance, because
   `NativeFrameStateV1` is one uniform blittable struct (no decorator-specific polymorphic fields),
   unlike the reference executor's `ReferenceFrame`.
4. Full restart requires reopening a fresh update on the active old instance before requesting
   abort -- `TryRequestAbort` needs an *open* update, the opposite precondition from the reference
   executor's own `Abort` (which needs *no* open update). Confirmed live by a real failure on first
   attempt, not assumed.
5. The composite-cursor-reset rule (`ADR-P5-001` item 2) applies unchanged to native's own
   `ChildCursor` field -- identified as a real, disclosed gap in this spike's own naive whole-`Frame`
   copy (see below), not a new decision.

See the ADR for full reasoning per point.

## Spike (`Spikes~/NativeHotReloadModel/`, archived from `Tests/Editor/Spikes/NativeHotReloadModel/`, deleted from `Tests/` after verification)

`SpikeNativeHotReloadModel.cs` (2 NUnit tests, `AIBT.Editor.Tests.Spikes` namespace) drove real
`NativeProgramImageOwnerV1`/`NativeInstanceArenaOwnerV1`/`NativeLifecycleMachineV1` instances built
from real `CompiledProgram`s (via the public `CanonicalTreeJson`/`ReferenceCompiler`/
`ReferencePreviewDriver.CreatePreviewNodeRegistry()` -- the same node registry every reference-backend
spike in this project already uses; `CompiledProgram` itself is backend-neutral).

- **`FullRestart_AbortsActiveOldInstance_ConstructsFreshOwnersFromNewProgram`**: drives an old
  instance's first tick to its natural `Waiting` boundary (a still-`Running` leaf, genuinely active,
  not idle), reopens a fresh update, requests abort (`BurstNodeAbortReason.HotReload`, the same
  reason value the reference backend already uses), drains to `Completed`, then constructs a fresh
  instance bound to a new `CompiledProgram` and confirms it starts clean (re-enters its first node
  from scratch). First attempt failed with a real `NativeLifetimeStateInvalid` (aborting a `Waiting`
  instance directly, without reopening an update, is rejected) -- fixed per finding 4 above; second
  attempt passed.
- **`Migration_ReorderedChildren_CopiesActivationByStableNodeIdAcrossShiftedCompiledIndices_UsingOnlyPublicOwnerApi`**:
  builds `sequence(a-running, b-success)` and, separately, `sequence(b-success, a-running)` -- a pure
  reorder of the children array (each node's own `NodeId` keeps its own fixed type, so both classify
  `Migrate` under `ADR-P5-001` even though the active node "a"'s compiled index genuinely shifts).
  Drives the old instance to a genuinely active state, captures its live `Frame`/`Generation` for
  every node (keyed by stable `NodeId`) using only the public execution-lease/View API, releases,
  constructs a fresh instance bound to the reordered program, seeds the captured state at each node's
  **new** compiled index, and confirms it landed correctly (the migrated node's `LifecycleState` is
  `Running` at its new index) -- then confirms the old instance's own state, re-read afterward, is
  unchanged (pure copy, never in-place mutation of shared structure). First attempt's own JSON
  builder had a real bug (it varied leaf *type* at fixed node-ID positions instead of reordering the
  children array itself, so the compiled index never actually shifted -- caught by a real assertion
  failure, not silently passed); fixed by giving each node ("a"/"b") a fixed type and varying only
  the children array order.

Both tests: 2/2 passing, live via Unity MCP `run_tests` against the real, unmodified `6000.5.8f1`
Editor.

## Real finding: this spike's own migration copy does not implement the composite-cursor-reset rule

The reorder test's capture/seed loop copies every node's `NativeFrameStateV1` verbatim, including the
root sequence's own `ChildCursor` -- the direct native analog of the reference executor's positional
cursor `ADR-P5-001` item 2 already governs. In the test's own concrete values, the root's
`ChildCursor` was `0` in both programs, but position `0` held a *different* child in each (the active
node was position 0 in the old tree, position 1 in the new one). A verbatim copy leaves the cursor
pointing at the wrong child in the new tree. The spike's own assertions never drove the migrated
instance further after seeding, so this did not surface as a test failure -- it was identified by
reasoning through the spike's own captured values while writing this evidence, not caught by a
dedicated assertion. This is not a new decision: `ADR-P5-001` item 2 already requires resetting any
composite's cursor whenever its direct children's order changed; it transfers unchanged to native's
`ChildCursor` field. Disclosed as real, load-bearing follow-up scope for `P7-012`, not silently
smoothed over.

## Verification

```text
Compilation: clean (0 errors) after 3 revisions -- missing `using AIBT.Burst;`; a C# CS1655/CS1612
  ref/property-indexer-mutation issue (fixed by capturing View.Frames/View.Generations into local
  NativeArray<T> variables before indexer-assignment, not chaining property access); NativeProgramImageOwnerV1's
  View exposing only NativeArray<T>.ReadOnly (fixed by owning separate writable nodes/children/
  configuration arrays, mirroring SchedulingPolicyDriver's own pattern)
Live Unity MCP run_tests (EditMode), SpikeNativeHotReloadModel: 2/2 passing after 3 live iterations
  (TryAdvance is one atomic step, not "run to the next leaf" -- fixed by advancing past the root's
  own CompositeEntered; TryRequestAbort needs an open update, not idle-Waiting -- fixed per finding 4;
  the reorder test's own JSON builder did not actually reorder the children array -- fixed)
Tools~/Verification/Verify-Static.ps1 -- passed (see command output below)
git diff --check -- clean
```

A full detached EditMode regression was **not attempted** this session (unlike `P7-010`, where an
optional one was attempted and hung) -- given the Unity Editor's demonstrated fragility this session
(see below) and that this card's own Required verification does not list one, it was skipped by
design rather than attempted and abandoned.

## Unity Editor instability this session (disclosed, not silently worked around)

The live Unity Editor session became genuinely unresponsive to the MCP bridge twice during this
card's work -- once for several minutes after the first `run_tests` dispatch (recovered after the
owner manually checked and restarted the Editor), and a second, shorter instance recovered on its
own after a domain reload. Both are disclosed here as real environmental friction encountered while
producing this evidence, not smoothed over; they did not affect the correctness of the spike results
above, all of which were produced against a responsive Editor after each recovery.

## Scope and limitations

- This card decided on paper only -- no production file changed. A future, not-yet-numbered
  implementation card (`P7-012`) builds the real classifier/migration code per the ADR.
- Deeper, multi-frame-deep concurrent active state was not exercised -- only a two-node,
  single-active-frame sequence.
- Tree blackboard content, parallel-branch state, observer state, and budget state were not
  exercised -- the spike's trees used no blackboard slots, `Parallel`, or `Cooldown` nodes.
- The composite-cursor-reset rule's own correctness (finding above) was identified by reasoning, not
  by a dedicated assertion catching a real failure -- `P7-012` should add a test that actually drives
  a migrated composite forward and confirms it resumes against the correct child.
- Subtree restart (a non-empty, non-whole-tree exclusion set) was not itself spiked -- expected to
  transfer for the same structural reasons as full migration, per `ADR-P5-001`'s own "one mechanism,
  not three" framing, but not itself exercised.
- Only `NativeInstanceArenaOwnerV1`'s V1 (non-blackboard) construction path was exercised; V2
  (blackboard-bearing) fresh construction and migration were not exercised.
