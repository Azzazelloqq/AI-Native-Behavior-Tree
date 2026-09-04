# ADR P7-011: Native-backend hot reload

- Status: Accepted 2026-09-01
- Date: 2026-09-01
- Decision ID: AIBT-035

## Context

### P7-029 owner-approved active-child reconciliation (2026-09-04)

When a structural child change resets an active Memory composite's cursor, cancel its old active
descendant path before traversing the new order. The fresh instance dispatches
`Abort(HotReload) -> Exit(Aborted)` deepest-first, then starts at child zero. Preserve the
composite and unaffected ancestors. A child whose Tick already terminated receives its pending
terminal Exit, without an additional Abort. Clear stale pending child results before new traversal.
Nested changes on the same path reconcile once at the outermost affected composite.

Thus `Sequence(a,b)` with a Running, reordered to `(b,a)`, cancels/exits a and then executes b,a;
a's new activation is intentional. Unchanged order preserves active callbacks without extra Enter.
The migrator remains read-only toward the old instance and never acknowledges callbacks on the
application's behalf. A narrow internal machine helper queues the existing descendant-cancellation
and cursor-reset transition on the freshly seeded instance. No public API or node ABI changes.
Compatible instance memory and CooldownInitialized survive together; excluded/incompatible nodes
retain fresh defaults. See `Planning~/Evidence/P7-029/implementation-proposal.md`.

`ADR-P5-001` (`AIBT-023`) decided hot reload is never in-place mutation: it is always construct a
fresh instance bound to the new `CompiledProgram`, then selectively copy surviving live state,
keyed by stable authoring `NodeId`, never by compiled index. `P5-004`/`P5-005`/`P5-006` implemented
that model for the reference-executor backend only, explicitly disclosing "the native backend's own
fresh-instance construction, a separate capacity-plan/lease subsystem, remains open follow-up work"
-- restated unchanged through `P5-GATE`, `P6-GATE`, and this project's Phase 7 handoff. This card
decides, on paper plus a disposable live spike, how that same model applies to
`NativeProgramImageOwnerV1`/`NativeInstanceArenaOwnerV1`/`NativeLifecycleMachineV1` -- the native
backend's fixed-capacity, pre-planned-and-leased equivalent of the reference executor's managed
allocation.

`P7-010`'s own ADR (`AIBT-034`, production Play-mode host) informs this card as context: a future
host built inside `AIBT.Runtime` will have full internal access to every type this ADR discusses, so
native fresh-instance construction does not itself need a new public-facade crossing (unlike the
reference-executor's `HotReloadPreviewDriver`/`ReferencePreviewDriver` pattern, built for
`AIBT.Editor` consumers with no internals visibility).

## Spike evidence (`Spikes~/NativeHotReloadModel/`, 2026-09-01, this workstation)

A disposable NUnit spike (`SpikeNativeHotReloadModel`, run live via Unity MCP `run_tests` against
the real, unmodified `6000.5.8f1` Editor) drove real `NativeProgramImageOwnerV1`/
`NativeInstanceArenaOwnerV1`/`NativeLifecycleMachineV1` instances end to end. 2/2 tests passing.

1. **Fresh program-image and instance-arena construction are already complete, existing primitives
   -- no new engine code needed.** `NativeProgramImageOwnerV1.TryCreate(newProgram,
   NativeProgramImageCapacityV1.Exact(newProgram), Allocator.Persistent, ...)` builds a fresh,
   capacity-planned, leased program image directly from a `CompiledProgram`; `capacity` needs no
   manual sizing (`Exact` derives it). `NativeInstanceArenaOwnerV1.TryCreate(programLease,
   NativeInstanceArenaCapacityV1.TryDerive(programLease.View, ...), Allocator.Persistent, ...)`
   likewise derives its own required capacity automatically from the leased program view. Both were
   exercised live, repeatedly, for both the "old" and "new" program in every spike test.
2. **A real, disclosed seam: neither Owner supplies `NativeLifecycleMachineV1.TryCreate`'s own
   parameter shapes directly.** `NativeProgramImageOwnerV1`'s View exposes node/child/config data as
   `NativeArray<T>.ReadOnly` (by design -- shared, leased, never mutated during execution), but
   `TryCreate` wants plain mutable `NativeArray<T>` for `nodes`/`children`/`configuration`; `.AsArray()`
   does not exist on `.ReadOnly`. Confirmed live by a real compile error, not assumed. A caller must
   still separately own writable copies of those three arrays -- mirroring
   `Runtime/Scheduling/SchedulingPolicyDriver.cs`'s own existing pattern exactly. Likewise, neither
   Owner supplies `NativeLifecycleNodeBindingV1[]` (the lifecycle-kind classification, computed by the
   caller from each node's own type ID) or `NativeLifecycleControlV1` (owned by the machine alone, no
   Arena-side equivalent) -- both are always caller-allocated, regardless of which construction path
   is used. `NativeProgramImageOwnerV1`'s real role is the safety-checked, leased, generation-bound
   READ access proven in point 1, not literally supplying the machine's constructor arrays.
3. **State capture/seeding needs zero new internal engine methods -- a real, positive difference
   from the reference-executor backend.** Unlike `ReferenceExecutionMachine`, which needed two new
   `internal` methods (`CaptureNodeState`/`SeedNodeState`) added by `P5-006` before migration was
   buildable at all, `NativeInstanceArenaOwnerV1`'s existing **public** API
   (`TryAcquireExecutionLease`/`TryReleaseExecutionLease` plus the View's public `Frames`/
   `Generations`/`NodeMemory` `NativeArray<T>` properties) already exposes everything needed to read
   and write per-node activation state. The spike's `Migration_ReorderedChildren...` test composed
   only this already-public surface to capture the old instance's live `Frame`/`Generation` for
   every node (keyed by stable `NodeId` via `CompiledProgram.DebugMap`), then seed them into a freshly
   constructed new instance at each node's **new** compiled index -- proven live across a genuine
   compiled-index shift (see point 4), including confirming the old instance's own state was
   untouched by the whole operation (read again afterward, unchanged).
4. **Migrating a genuinely active (non-idle) instance is proven, not merely idle-to-idle --
   confirmed by reading real code, not assumed identical to the reference-executor's own
   restriction.** `ADR-P5-001`'s implementation addendum restricted reference-executor migration to
   an idle old instance only, because `ReferenceFrame` has "an extensive set of decorator/parallel/
   repeater-specific mutable fields," making field-by-field reconstruction of an active frame stack
   large and failure-prone. **Native's `NativeFrameStateV1` is a single, uniform, blittable struct
   for every node kind** (composite, decorator, leaf alike -- confirmed by reading
   `Runtime/State/Native/NativeInstanceArenaContracts.cs` directly; even `Cooldown`/`Parallel`-specific
   fields live inline in the same fixed-shape struct, not a polymorphic hierarchy), so there is no
   analogous field-by-field reconstruction problem. The spike's reorder test drove the old instance to
   a genuinely active state (one live `Waiting` frame, not idle) before capturing -- proving this
   empirically, not just arguing it structurally. **Scope actually exercised**: one active frame
   (a two-node sequence, depth 2). Deeper, multi-frame-deep concurrent activity was not itself
   exercised; the mechanism (a per-index array copy) does not structurally depend on depth, but this
   was not empirically re-verified at greater depth.
5. **Real, disclosed finding: native's abort mechanism has the opposite precondition from the
   reference executor's own `Abort`.** `HotReloadFullRestart` (`P5-004`) uses
   `ReferenceExecutionMachine.Abort`, which requires **no open update** (works between ticks).
   Native's `NativeLifecycleMachineV1.TryRequestAbort` requires the **opposite**: `control.UpdateOpen
   != 0` (an open update), confirmed live by a real `NativeLifetimeStateInvalid` failure on first
   attempt when the spike tried to abort a `Waiting` (between-ticks) instance directly -- `Waiting`
   itself sets `UpdateOpen = 0` (confirmed by reading `NativeLifecycleMachineV1.cs` line 801). The
   working sequence, proven live: reopen a fresh update (`TryBeginUpdate` on an already-active
   instance -- safe, since it only (re)initializes frame 0 when `Depth == 0`, confirmed by reading the
   method, so resuming an active instance never re-enters it from scratch), *then* request the abort
   within that reopened update. `FullRestart_AbortsActiveOldInstance...` proved the full sequence:
   abort an active old instance this way, drain it to `Completed`, and confirm a freshly constructed
   instance bound to the new program starts clean (re-enters its first node from scratch), unaffected.
6. **Real, disclosed gap: this spike's own migration copy does not implement `ADR-P5-001`'s
   composite-cursor-reset rule (item 2) -- identified by reasoning through the spike's own captured
   values, not caught by a dedicated assertion.** The reorder test's capture/seed loop copies every
   node's `NativeFrameStateV1` verbatim, including the root sequence's own `ChildCursor` field (the
   direct native analog of the reference executor's positional cursor `ADR-P5-001` item 2 already
   governs). In that test, the root's `ChildCursor` was `0` (pointing at "child in position 0") in
   both the old and new program -- but position 0 held a *different* child in each (the active node,
   "a", was position 0 in the old tree and position 1 in the new, reordered tree). A verbatim copy
   therefore leaves the root's cursor pointing at the *wrong* child in the new tree; the spike's own
   assertions never drove the migrated instance further after seeding, so this did not surface as a
   test failure, but it would be a real, silent correctness bug (the sequence resuming against the
   wrong child) if this spike's naive whole-`Frame` copy were used unmodified in production. **This is
   not a new decision** -- `ADR-P5-001`'s own item 2 already requires resetting any composite's cursor
   whenever its direct children's order changed, keyed the same way (by comparing each composite's own
   children under the old vs. new program). It applies unchanged to native's `ChildCursor` field. The
   future implementation card (`P7-012`) must apply this rule explicitly, not reuse this spike's own
   simplified whole-copy approach for composite nodes.

## Decision

1. **Fresh-instance construction reuses `NativeProgramImageOwnerV1.TryCreate`/
   `NativeInstanceArenaOwnerV1.TryCreate` unchanged.** No new capacity-planning or lease-management
   code is needed -- both already derive their own required capacity from a `CompiledProgram`/leased
   program view. A production implementation additionally owns writable `nodes`/`children`/
   `configuration` copies and the `NativeLifecycleNodeBindingV1[]`/`NativeLifecycleControlV1`
   `TryCreate` itself still needs directly (point 2 above), mirroring `SchedulingPolicyDriver`'s
   already-accepted pattern -- not a gap to close, a real shape to reuse as-is.
2. **State capture/seeding is built entirely from `NativeInstanceArenaOwnerV1`'s already-public API
   -- no new internal engine method, unlike the reference-executor backend.** A per-node classifier
   (mirroring `P5-002`/`P5-003`'s own `HotReloadProgramIdentityMap`/`HotReloadCompatibilityClassifier`,
   applied against native's own `CompiledProgram.DebugMap`) drives which nodes' `Frame`/`Generation`/
   `NodeMemory` bytes to copy, keyed by stable `NodeId`, resolved to each program's own compiled index
   -- exactly `ADR-P5-001`'s model, now proven buildable for native with zero production-file changes
   to `NativeInstanceArenaOwnerV1` itself.
3. **Native migration is not restricted to an idle old instance the way the reference executor's own
   implementation is.** The reference-executor restriction was a real, specific finding about
   `ReferenceFrame`'s polymorphic field shape, not a general hot-reload principle -- it does not
   transfer to native's uniform `NativeFrameStateV1`. A future implementation may migrate an active
   native instance directly, subject to point 6 below (the composite-cursor-reset rule) and to the
   depth-scope caveat in finding 4 (only a shallow active scenario was itself exercised; the
   implementation card should extend verification to deeper frame stacks before relying on this for
   arbitrarily deep trees).
4. **Full restart requires reopening a fresh update on the active old instance before requesting
   abort.** `TryRequestAbort` needs `UpdateOpen != 0`; a `Waiting` (between-ticks but active) instance
   has `UpdateOpen == 0`. The sequence is: `TryBeginUpdate` (safe on an already-active instance --
   resumes, does not re-enter) → `TryRequestAbort(BurstNodeAbortReason.HotReload)` → drain to
   `Completed` → discard the old owners (`TryDispose`) → construct fresh owners bound to the new
   program. This differs structurally from `HotReloadFullRestart`'s own reference-executor sequence
   (which aborts *without* reopening an update) -- a real backend difference, not a defect in either.
5. **The composite-cursor-reset rule (`ADR-P5-001` item 2) applies unchanged to native's own
   `ChildCursor` field.** Any composite whose direct children's order changed between the old and new
   program resets its own `ChildCursor` during migration, exactly as already decided for the
   reference executor -- this ADR does not reopen or restate that decision differently, it confirms
   it transfers directly (same positional-cursor concept, same native field). The future
   implementation card must not silently reuse a naive whole-`Frame` copy for composite nodes, per
   finding 6.
6. **Random streams (V2) need no migration at all -- they are deterministically re-derivable.**
   `NativeRandomStreamDerivationV1.TryDerive(rootSeed, semanticHash, treeInstanceId, nodeIndex, ...)`
   already reconstructs a node's random stream purely from caller-supplied identity plus its own
   (new) compiled index; a fresh instance's own `TryInitializeRandomStreams` call reproduces the
   correct state without any byte-level copy. This is a structural reading of already-existing code
   (`NativeInstanceArenaOwnerV1.TryInitializeRandomStreams`/`TryResetAll`), not separately spiked.
7. **`P5-007`'s scheduler-contract decision (estimator reset, never carried over across a reload) is
   inherited unchanged.** This card's spike touched no scheduling or estimator code and found no
   concrete reason the native backend's own reload would differ from that already-accepted decision.

## Acceptance criteria mapped

- The spike proves at least full restart for a native-backend instance, live, against a real
  `NativeLifecycleMachineV1` pair: confirmed -- `FullRestart_AbortsActiveOldInstance...` passing,
  live via Unity MCP `run_tests`.
- The ADR states plainly which of full restart / subtree restart / compatible migration are proven
  for the native backend by this card's own spike, and which remain follow-up: **full restart** and
  **compatible migration of an active instance's per-node `Frame`/`Generation` state across a
  compiled-index-shifting reorder** are proven live. **Subtree restart** (a parameterized exclusion
  set, per `ADR-P5-001`'s own "one mechanism, not three" framing) was not separately spiked -- it is
  the same mechanism as migration with a non-empty exclusion set, expected to transfer for the same
  structural reasons as point 3, but not itself exercised. **Tree-blackboard content**, **parallel-
  branch/observer/budget state**, and **the composite-cursor-reset rule's own correctness** (finding
  6 identifies the gap; fixing it is not this card's own scope) are explicit, disclosed follow-up for
  the implementation card.
- No accepted Phase 4 scheduler contract is reopened: confirmed, per decision item 7.

## Consequences

- `P7-012` (native-backend hot reload implementation) builds the real classifier/migration code
  inside `Runtime/HotReload/` (or wherever the existing reference-executor hot-reload code's own
  sibling location is), applying this ADR's decisions -- critically, the composite-cursor-reset rule
  from finding 6/decision 5, which this spike's own simplified copy did not implement.
- `P5-007`'s remaining blocked acceptance criteria (golden-equivalence re-run, batch isolation, `Auto`
  determinism, all for a hot-reloaded native instance) become buildable once `P7-012` lands.
- `P7-010`'s future Play-mode host, once implemented inside `AIBT.Runtime`, is the natural caller of
  this mechanism (full internal access, no facade needed) -- consistent with `P7-010`'s own ADR.

## Explicitly unverified (stated, not generalized)

- Deeper, multi-frame-deep concurrent active state (more than one live frame at once) was not
  exercised -- only a two-node, single-active-frame sequence.
- Tree blackboard content, parallel-branch state, observer state, and budget state were not
  exercised -- the spike's trees used no blackboard slots, `Parallel`, or `Cooldown` nodes. The same
  public-API-composition pattern (View properties + stable-key remapping) is expected to apply, per
  `TryResetTreeBlackboard`'s own existing slot-keyed byte-copy precedent for blackboard content, but
  this is a structural expectation, not an empirical proof.
- The composite-cursor-reset rule's own correctness was identified by reasoning through this spike's
  captured values, not by a dedicated assertion catching a real failure -- the implementation card
  should add a test that actually drives a migrated composite forward and confirms it resumes against
  the correct (not stale-cursor) child.
- Subtree restart (a non-empty, non-whole-tree exclusion set) was not itself spiked, per the
  acceptance-criteria mapping above.
- Only `NativeInstanceArenaOwnerV1`'s V1 (non-blackboard) construction path was exercised; V2
  (`TryCreateV2`, blackboard-bearing) fresh construction and migration were not exercised.
