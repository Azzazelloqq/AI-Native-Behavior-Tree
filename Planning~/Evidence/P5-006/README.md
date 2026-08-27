# P5-006 compatible active-state migration evidence

## Result

- `Runtime/HotReload/Migration/HotReloadStateMigration.cs`, `HotReloadMigrationReport.cs` (new,
  internal): implements `ADR-P5-001`'s shared reload mechanism at the **empty exclusion set** --
  when `P5-003`'s classifier finds no node anywhere `IncompatibleRestart`, every `Migrate`-verdict
  node's persisted instance state (memory, activation generation, cooldown-init flag) copies from
  the old `ReferenceExecutionMachine` into a freshly constructed one bound to the new
  `CompiledProgram`, keyed by stable `NodeId`. Blackboard values migrate too, via the existing
  `initialBlackboard` constructor parameter (already keyed by `StableKeyId` -- no new blackboard
  plumbing needed), filtered to keys the new program still declares with the same type (a key
  removed or retyped between programs is dropped from migration, not force-fed into a constructor
  that would otherwise reject it and fault the whole instance).
- Two new `internal` methods on the accepted `ReferenceExecutionMachine`
  (`Runtime/Execution/Reference/Core/ReferenceExecutionMachine.cs`), added with explicit owner
  approval (`AskUserQuestion`) since this is the first Phase 5 card to modify an already
  gate-accepted Phase 1/2 file rather than only add new ones: `CaptureNodeState`/`SeedNodeState`,
  each guarded (`SeedNodeState` throws if the destination has already accepted an update; both
  throw on an out-of-range index) and validated (memory-size mismatch is a hard `ArgumentException`,
  never a silent truncation).
- `Tests/Editor/HotReload/Migration/HotReloadStateMigrationTests.cs` (new, 4 tests, all passing):
  null-rejection, falling back to full restart when the old instance is active, a real
  per-node-state round-trip proof (a `Repeater`'s activation generation and memory bytes survive a
  parameter edit, verified by direct snapshot comparison, not by inferring correctness from
  behavior alone), and confirming an `IncompatibleRestart` node's state is never migrated.

## The real architectural finding that reshaped this card's scope

`ADR-P5-001`'s own text assumed migration meant copying "memory, activation generation" -- but
`ReferenceFrame` (the active-traversal-stack element `_frames` holds) turned out to have a
**read-only `NodeIndex`** fixed at construction, plus an extensive set of decorator/parallel/
repeater/cooldown-specific mutable fields (repeater counters, parallel branch arrays, cooldown
deadlines, abort-resume state, and more). Remapping a live frame to a new compiled index is not a
field update -- it means reconstructing the frame and copying roughly two dozen fields correctly,
a substantially larger and more failure-prone undertaking than the ADR's own text anticipated.

This was escalated to the owner directly (twice: first for permission to modify
`ReferenceExecutionMachine.cs` at all, then again once the frame-stack complexity became concrete)
rather than silently scoped down or silently attempted. Decision: **migration runs only when the
old instance is idle** (`CaptureInspection().ActiveNodeCount == 0` -- no active frames). Whenever
it is not, this card's own mechanism falls back to `HotReloadFullRestart` entirely, exactly the
same way an unsafe subtree localization already falls back. See `ADR-P5-001`'s own addendum for
the full record of this decision.

## Verification

Live Unity MCP test run: 4/4 passed. Modifying `ReferenceExecutionMachine.cs` (adding two new
methods, changing no existing logic) was verified to introduce zero regressions by running the
**entire** existing accepted suite immediately after the change, before building anything on top
of it: 1436/1436 (excluding the same 3 pre-existing unrelated failures) passed unchanged. Full
suite after this card's own tests were added: 1440 tests, same 3 pre-existing failures. A 4th,
environment-looking `LocalSaveSystem` failure observed once during `P5-004`'s own verification did
not reproduce here, consistent with it having been transient session state rather than a
regression. `Verify-Static.ps1`: 83 work items, unchanged. Full detail in
`verification-results.json`.

## Scope and limitations

- Idle-only, per the addendum above. Mid-flight frame-stack migration is real, disclosed future
  work -- not attempted, not guessed at.
- Async-operation state (`_operationLedger`/`_completionInbox`/`_commandBuffer`) is not migrated.
  This card's idle check only inspects `ActiveNodeCount`; whether an idle (zero active frames)
  instance can still have a nonzero `ActiveOperationCount` was not separately proven against a
  genuine async command type -- no such fixture exists in the currently available test registries.
  If that combination is possible, an in-flight operation on an idle instance would be silently
  dropped by migration today rather than cancelled per `async-and-commands-v1.md`'s rule -- a real,
  disclosed gap for `P5-007`/`P5-009` to close, not verified safe here.
- Reference-executor backend only, matching every other Phase 5 restart/migration card so far; the
  native backend's own equivalent remains the disclosed gap `P5-004` already recorded.
- A blackboard key whose type changes between programs is dropped from migration (falls back to
  the new program's default value for that key), not forced through -- disclosed, not silently
  corrupted or faulted.
