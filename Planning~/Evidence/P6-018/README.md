# P6-018 Active-instance hot-reload migration decision evidence

## Result

Done, accepted: **build it**. `ADR-P6-018` (`AIBT-030`) decides full active-frame-stack migration is
worth building as an addendum to `ADR-P5-001` (never a replacement of its own model), via a new
`CaptureFrameStack`/`SeedFrameStack` accessor pair mirroring the existing idle-instance
`CaptureNodeState`/`SeedNodeState`'s own shape, gated on every active-path node classifying `Migrate`.

## Real finding: the original blocking analysis was inaccurate

`ADR-P5-001`'s own implementation addendum named `ReferenceFrame`'s "read-only `NodeIndex` and
extensive per-decorator-type fields" as what made active-instance migration bigger in scope than
anticipated. Reading `Runtime/State/Reference/ReferenceFrame.cs` directly and enumerating every
declared property via reflection shows this is not accurate: `NodeIndex` is the *only* read-only
property (set once at construction -- exactly what migration needs anyway, since a fresh frame
always needs the new compiled index), and all 30+ other fields already have normal settable
properties. The real gap is structural, not a mutability problem:
`ReferenceExecutionMachine`'s own frame stack (`_frames`) is `private`, with no accessor at all --
unlike per-node state, which already has `internal CaptureNodeState`/`SeedNodeState`.

## Verification

```text
Disposable spike (SpikeActiveInstanceHotReloadMigration, Tests/Editor/
  ActiveInstanceHotReloadMigrationSpike/ during this session, archived afterward): 2/2 tests
  passing, live via Unity MCP run_tests --
  FrameStackFieldsAreAllSettable_ExceptNodeIndex_WhichMigrationNeedsFreshAnyway,
  ActiveMidTickFrameStack_MigratesFieldForField_AndTheMigratedInstanceKeepsRunningCorrectly
  (root Repeater(count: 3) wrapping a perpetually-Running leaf, a real mid-tick active instance with
  2 nested active frames and genuine decorator-specific state, migrated field-for-field across a
  real Migrate-category parameter edit, then resumed under a real subsequent Update without fault)
Regression (required by this card's own acceptance criteria, unmodified, live via Unity MCP):
  AIBT.Tests.Editor.HotReload.Migration.HotReloadStateMigrationTests -- 4/4 passing
Verify-Static.ps1 -- passed
git diff --check -- clean
```

No production file (`Runtime/State/Reference/ReferenceFrame.cs`,
`Authoring/HotReload/HotReloadStateMigration.cs`) was touched, per this card's own Forbidden-changes
clause. Since no existing accessor reaches the machine's private frame stack at all, the spike used
reflection *only* for that one field, standing in for the future implementation card's real
`CaptureFrameStack`/`SeedFrameStack` methods -- every other type involved (`ReferenceFrame` itself)
was used with real, already-granted `InternalsVisibleTo` access. The spike lived temporarily in
`Tests/Editor/ActiveInstanceHotReloadMigrationSpike/`, then archived to
`Spikes~/ActiveInstanceHotReloadMigration/` and deleted from `Tests/`, mirroring this session's own
established precedent.

## Handoff

A future implementation card adds `CaptureFrameStack`/`SeedFrameStack` to the real
`ReferenceExecutionMachine`, wires the "is the active path entirely `Migrate`" check into
`HotReloadStateMigration.Migrate`, and updates `HotReloadWorkflowWindow` (`P5-008`) to surface the
newly-possible active-instance migration path instead of always reporting a full restart in that
case. Recursion into suspended `ReferenceParallelBranch.SuspendedFrames` was reasoned about (same
`ReferenceFrame` type) but not separately spiked -- explicitly disclosed as unverified in the ADR.
