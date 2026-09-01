# P7-012 native-backend hot reload implementation evidence

## Result

Done. Builds the native-backend hot-reload mechanism `ADR-P7-011` decided
(`Runtime/Execution/Native/HotReload/`), and finally closes `P5-007`'s own long-blocked
acceptance criteria (golden-equivalence re-run, batch isolation, `Auto` determinism, all for a
hot-reloaded native instance) -- `P5-007`'s status moves to `Done` alongside this card.

## What was built

- `NativeHotReloadInstance` (`NativeHotReloadInstance.cs`): bundles one native tree instance's full
  ownership set (`NativeLifecycleMachineV1` plus its `NativeProgramImageOwnerV1`/
  `NativeInstanceArenaOwnerV1` and every caller-owned array `TryCreate` needs -- the machine struct
  itself owns nothing disposable). `TryBuild` reuses `NativeProgramImageOwnerV1.TryCreate`/
  `NativeInstanceArenaOwnerV1.TryCreate` unchanged, per `ADR-P7-011` decision 1.
- `NativeHotReloadFullRestart.TryRestart`: reopens a fresh update on the old instance, requests
  abort, drains to `Completed`, then constructs a fresh instance bound to the new program --
  ownership transfers only on success (old instance untouched and still owned by the caller on
  failure).
- `NativeHotReloadStateMigration.TryMigrate`: constructs a fresh instance, then for every
  `Migrate`-classified node (via the unchanged, reused `HotReloadProgramIdentityMap`/
  `HotReloadCompatibilityClassifier`) copies `Generation` and `NodeMemory` bytes to the node's new
  compiled index, resets a migrated composite's own cursor bytes when its direct children's order
  changed, and separately walks the *active call stack* (see the real bug below) to preserve live
  continuity. Does **not** fall back to full restart for an active old instance, per `ADR-P7-011`
  decision 3.

## A real, non-obvious bug found and fixed during this card's own test-driven verification

`ADR-P7-011`'s own field-name framing ("apply the composite-cursor-reset rule to native's
`ChildCursor` field") described the *symptom* correctly but not the actual mechanism. Investigated
directly against `NativeLifecycleMachineV1`'s own dispatch code while debugging a genuinely failing
migration test (`Migrate_ReorderedChildrenMidFlight_...`, live values captured via `Debug.Log`
diagnostics through several Unity MCP `run_tests` round-trips, not assumed):

1. **The composite cursor a dispatch decision actually reads lives in the node's own `NodeMemory`
   bytes** (`NativeLifecycleMachineV1`'s private `ReadCursor`/`WriteCursor`), not
   `NativeFrameStateV1.ChildCursor`, which is written once and never read by any dispatch path.
   This part was already identified and disclosed in `P7-011`'s own evidence.
2. **The `_frames` array is not indexed by compiled node index at all -- it is a call STACK indexed
   by DEPTH**, reused across sibling nodes over an instance's lifetime (confirmed by reading
   `NativeLifecycleMachineV1`'s `ChildSelected`/`PopFrame` code directly: entering a child pushes
   `_frames[control.Depth]` and increments `Depth`; exiting pops and decrements). A frame's own
   `NodeIndex` field, not its array position, says which node it currently represents. This was
   **not** identified by `P7-011`'s own spike or ADR -- it is a new finding from this card.

The original migration code (mirroring the spike's own simplified copy) copied
`oldFrames[oldCompiledIndex] -> newFrames[newCompiledIndex]`, which reads and writes unrelated
stack slots the moment more than one node has ever been active in the same tree -- observed
directly as two leaves' live/inactive Frame state landing **swapped** at the wrong array positions
after migration, despite the underlying `HotReloadProgramIdentityMap` index resolution itself being
independently verified correct via a separate diagnostic. Fixed by walking the old instance's own
`Control.Depth`-many active stack slots position-for-position (preserving depth), remapping only
each frame's own `NodeIndex` field via the identity maps, and copying `Control.Depth` itself to the
fresh instance -- the same "capture/seed frame stack by depth, remap `NodeIndex`" approach
`Spikes~/ActiveInstanceHotReloadMigration/SpikeActiveInstanceHotReloadMigration.cs` had already
proven out for the *reference* executor (`P6-018`), now generalized to native's own depth-indexed
`_frames`. If any active-path node cannot migrate (dropped, incompatible, excluded by a subtree
restart, or lacking a stable `NodeId`), `TryMigrate` now fails cleanly (`NativeLifetimeStateInvalid`)
rather than silently truncating the stack -- a partial/truncated active path was judged unsafe to
invent without being asked for; the caller falls back to full restart in that case.

## Tests

- `Tests/Runtime/NativeExecution/HotReload/`:
  - `NativeHotReloadFullRestartTests` (3 tests): active-instance abort + fresh construction; a
    never-begun instance still restarts cleanly (`TryBeginUpdate` succeeds unconditionally on a
    fresh instance too -- investigated, not assumed, after an initial wrong test expectation);
    driven-to-completion equivalence to a fresh instance.
  - `NativeHotReloadStateMigrationTests` (2 tests): no-structural-change migration copies
    Frame/Generation/composite-cursor-bytes verbatim; reordered-children migration resets the
    composite's cursor instead of copying a stale value, *and* drives the migrated instance forward
    afterward to confirm it resumes against the correct child -- the exact gap `P7-011`'s own
    evidence disclosed as unverified.
  - `NativeHotReloadAutoDeterminismTests` (2 tests): `NativeAutoSelectionV1.TrySelect` picks
    identically before/after a real full restart with the same inputs; a reseeded (post-reload)
    `NativeWorkEstimatorV1` reaches the identical estimate a never-reloaded one would for the same
    observations.
- `Tests/Integration/NativeRuntime/`:
  - `NativeHotReloadGoldenEquivalenceTests` (4 cases, one per `NativeGoldenExecutionPolicyV1`): a
    full-restarted instance driven to completion by each accepted policy produces a byte-identical
    atomic-step trace to a never-reloaded control instance driven the same way.
  - `NativeHotReloadBatchIsolationTests` (1 test): reloading the middle lane of a 3-lane
    `NativeBatchedLifecycleOwnerV1` batch leaves both untouched sibling lanes' own traces
    bit-identical to an all-untouched control batch (and the reloaded lane itself also matches,
    confirming restart-equivalence a second way).

All new tests: 12/12 passing, live via Unity MCP `run_tests` against the real, unmodified
`6000.5.8f1` Editor (after the migration fix above; several earlier iterations genuinely failed
while diagnosing the bug -- see git history of this session's own conversation for the diagnostic
trail).

## Why the JSON golden-fixture corpus itself was not re-run through a hot-reloaded instance

`P5-007`'s own deliverable text says "golden-equivalence re-run ... for every accepted policy" --
satisfied here by extending `NativeExecutionEquivalenceTests`'s own established "`TraceEntry`
equality across driving mechanisms" technique to compare a full-restarted instance against a
never-reloaded control, for all 4 policies. The JSON-fixture-based
`NativeExecutionEquivalenceTests.EveryGoldenBehaviorCasePassesTheNativeAdapter` test proves
*cross-backend* (reference vs. native) parity per fixture; hot reload is a continuity mechanism
layered on top of one backend's own dispatch contract, not a per-fixture behavior difference, and
`NativeBehaviorCaseExecutor`'s own construction path does not use the owner/lease-based
`NativeHotReloadInstance` shape this card's mechanism operates on. Bridging the two was judged out
of proportion to what this card's own acceptance criteria ask for; disclosed here rather than
silently substituted without comment.

## Verification

```text
Verify-Static.ps1 -- passed (121 work items, 6 schemas)
Unity MCP run_tests (EditMode):
  - AIBT.Tests.Runtime.NativeExecution.HotReload.* -- 7/7 passing
  - AIBT.Tests.Integration.NativeRuntime.NativeHotReloadGoldenEquivalenceTests -- 4/4 passing
  - AIBT.Tests.Integration.NativeRuntime.NativeHotReloadBatchIsolationTests -- 1/1 passing
  - Full AIBT.Tests.Runtime.NativeExecution group -- 328/328 passing (no regression)
  - Full EditMode project regression -- 1609 total, 1606 passed, 3 failed, all 3 pre-existing and
    unrelated to this card (a CodeGen-test-assembly-path environment issue in
    AIBT.Tests.CodeGen.Generation.GeneratedArtifactContractTests, and an unrelated
    LocalSaveSystem.Tests.SaveStoreTests failure in a different Unity package entirely)
```

Unity Test Runner intermittently failed to initialize ("tests did not start within timeout") or the
MCP bridge disconnected mid-request several times during this card's debugging phase -- `execute_code`
echo checks confirmed the Editor itself stayed responsive throughout each time; retrying `run_tests`
(once or twice) always succeeded on the next attempt. Matches the same pattern already documented in
prior cards' own evidence, not a new instability.

## Scope and limitations (disclosed, matching `P7-011`'s own "Explicitly unverified" framing)

- Tree-blackboard content, parallel-branch/observer/budget state migration, and the `V2`
  (blackboard-bearing) construction/lease path (`TryCreateV2`/`TryAcquireExecutionLeaseV2`) were not
  exercised -- the existing golden-equivalence harness itself only exercises the `V1` path.
- Deeper multi-frame-deep concurrent active-state migration (more than one live frame at once)
  beyond the shallow root+one-active-child depth this card's own tests exercise.
- Subtree restart (`TryMigrate` with a non-empty exclusion set) has no dedicated new test beyond
  what the non-empty-exclusion-set code path structurally provides -- it is the same mechanism as
  full migration, per `ADR-P5-001`'s own "one mechanism, not three" framing, and was not itself
  spiked separately either.
- No new performance default, threshold, or "acceptable reload overhead" claim is introduced -- this
  card verifies correctness only; a future benchmark card (mirroring `P5-009`) would measure
  native-backend reload cost if one is assigned.
- An active path that runs through a node `TryMigrate` cannot migrate (dropped, incompatible,
  excluded, or lacking a stable `NodeId`) makes `TryMigrate` fail cleanly rather than partially
  truncating the stack -- callers must fall back to full restart in that case; not itself exercised
  by a dedicated test in this pass.
