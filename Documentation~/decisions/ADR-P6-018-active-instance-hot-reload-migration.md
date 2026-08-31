# ADR P6-018: Active-instance hot-reload migration

- Status: Accepted 2026-08-31
- Date: 2026-08-31
- Decision ID: AIBT-030

## Context

`ADR-P5-001`'s own implementation addendum shipped migration only when the old instance is idle,
falling back to full restart otherwise, naming `ReferenceFrame`'s "read-only `NodeIndex` and
extensive per-decorator-type fields" as the blocker for full mid-flight migration, and recording
that scope reduction as disclosed follow-up work. This card decides whether and how to extend
`HotReloadStateMigration` to cover the active-instance case, backed by a disposable spike proving
(or disproving) the mechanism against a real, actively-executing instance.

## Spike evidence (`Spikes~/ActiveInstanceHotReloadMigration/`, 2026-08-31, this workstation)

A disposable NUnit spike (`SpikeActiveInstanceHotReloadMigration`, run live via Unity MCP `run_tests`)
found the original blocking analysis inaccurate and proved the mechanism works end to end.

1. **`ReferenceFrame`'s own fields are not the blocker -- confirmed by reflection over every
   declared property.** `NodeIndex` is the *only* read-only property (set once at construction,
   which migration needs anyway -- a fresh frame always needs the new compiled index); all 30+ other
   fields (decorator/parallel/repeater/cooldown/reactive/abort state) already have normal settable
   properties. **Passed.**
2. **A real, mid-tick active instance (root `Repeater(count: 3)` wrapping a perpetually-`Running`
   leaf -- 2 real nested active frames, genuine decorator-specific state, not a trivial single-leaf
   case) migrated field-for-field across a genuine, already-accepted `Migrate`-category parameter
   edit (`count: 3 -> 5`).** Every `ReferenceFrame` property except `NodeIndex` matched the old
   instance's own value exactly after migration, including the repeater's own configured
   `RepeaterCount` (`3`, the OLD instance's own value, not the new program's `5` default) -- real
   proof of state transfer, not a fresh reactivation. **Passed.**
3. **The migrated instance is not merely superficially seeded -- it keeps running correctly.** A
   subsequent real `Update()` call on the migrated instance did not fault or get rejected (the
   reference executor's own many internal consistency checks would very likely catch incoherently
   seeded state almost immediately) and correctly remained `Waiting`, still mid-repeater-cycle.
   **Passed.**
4. **The actual gap is structural, not a mutability problem.** `ReferenceExecutionMachine` exposes
   no accessor for its own frame stack at all (`_frames` is `private`, unlike per-node state's own
   already-`internal` `CaptureNodeState`/`SeedNodeState`). Per this card's own Forbidden-changes
   clause, the spike used reflection *only* to reach `_frames`, standing in for the future
   implementation card's real accessor methods -- every other type involved (`ReferenceFrame` itself)
   was used with real, already-granted `InternalsVisibleTo` access, no reflection needed.

Full raw output is in `Planning~/Evidence/P6-018/README.md`.

## Decision

1. **Build it: full active-frame-stack migration is worth building, addending `ADR-P5-001` rather
   than replacing its model.** The construct-fresh-and-selectively-copy model stays exactly as
   `ADR-P5-001` defined it; this addendum only widens *what* gets copied (the active frame stack, in
   addition to per-node memory/generation/cooldown state), never the mechanism's own shape.
2. **New accessor pair, mirroring `CaptureNodeState`/`SeedNodeState`'s own already-accepted shape and
   precondition.** `ReferenceExecutionMachine` gains `internal ReferenceFrame[] CaptureFrameStack()`
   (returns the current `_frames` in depth order, plus each suspended `ReferenceParallelBranch`'s own
   `SuspendedFrames` recursively -- not exercised in this card's own spike, since the spiked tree has
   no `Parallel` node, but structurally identical: `ReferenceParallelBranch.SuspendedFrames` is
   itself a `List<ReferenceFrame>`) and `internal void SeedFrameStack(ReferenceFrame[] frames)`,
   valid only before the fresh instance's first accepted update -- the exact same precondition
   `SeedNodeState` already enforces, for the same reason (migration happens once, immediately after
   construction, never mid-execution).
3. **Scope: only when every node on the active path (and inside any suspended parallel branch)
   classifies `Migrate`.** `HotReloadStateMigration.Migrate` gains a check, using the existing
   `HotReloadClassificationResult.NodeVerdicts`, over exactly the set of nodes the old instance's
   captured frame stack references. If any of them is `IncompatibleRestart`/`Dropped`/unresolvable in
   the new program, fall back to the existing full-restart path unchanged -- a coherent active
   traversal path cannot be partially migrated node-by-node the way idle per-node state can, since
   the frame stack itself represents one continuous parent-child chain that must remain structurally
   valid against the new program's own child-index tables.
4. **Everything else about the existing mechanism is untouched.** Per-node memory/generation/cooldown
   state still migrates via the existing, unmodified `CaptureNodeState`/`SeedNodeState`; this
   addendum is purely additive on top.

## Consequences

- A future implementation card adds `CaptureFrameStack`/`SeedFrameStack` to
  `Runtime/Execution/Reference/Core/ReferenceExecutionMachine.cs`, wires the new "is the active path
  entirely `Migrate`" check into `Runtime/HotReload/Migration/HotReloadStateMigration.cs`, and updates
  `Editor`'s `HotReloadWorkflowWindow` (`P5-008`) to surface the newly-possible active-instance
  migration path instead of always reporting a full restart in that case.
- `HotReloadStateMigrationTests.cs`'s own existing idle-instance tests, re-run unmodified, still pass
  -- this decision does not weaken the idle path's own accepted guarantees.
- The native backend still has no hot reload at all (`P5-004`/`P5-007`'s own disclosed gap) --
  unchanged by this decision, which is scoped to the reference (managed) executor only.

## Explicitly unverified (stated, not generalized)

- Recursion into suspended `ReferenceParallelBranch.SuspendedFrames` was reasoned about (same
  `ReferenceFrame` type, same field-copy mechanism) but not separately spiked -- the spiked tree has
  no `Parallel` node. A future implementation card should verify this case explicitly rather than
  assume the non-parallel case generalizes automatically.
- The reflection technique this spike used to reach `_frames` is a decision-only stand-in, not the
  shipped mechanism -- the future implementation card must add the real `internal` accessor methods,
  not ship reflection into production.
- Whether an active path containing a `StructuralChildChangeNodeIds` entry (children reordered but
  still `Migrate`-classified) needs special handling for an in-progress composite's own cursor was
  not investigated here -- `HotReloadClassificationResult`'s own documentation already flags this as
  "a node-type-semantics decision for the caller," inherited unchanged by this addendum, not resolved
  by it.
