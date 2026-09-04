# P7-029 — Fix real data-loss/state bugs in document migration and native hot reload

Status: `Draft`

## Objective

Three real, independently confirmed bugs (live-reproduced by the owner, then re-confirmed here by
direct code reading, not assumption) in already-shipped Phase 7 functionality:

1. **`DocumentMigrator.TryMigrate` silently drops document- and node-level data.**
   `Authoring/Migration/DocumentMigrator.cs:59-61` reconstructs the migrated `TreeDocument` with
   only `tags`/`metadata` named explicitly; `blackboard` and `description` both default to `null` in
   the constructor it calls, so a real document's blackboard declarations and description are
   silently discarded on every migration that changes anything. The short `TreeDocument` constructor
   this call resolves to also always passes `agentContract: null, sharedContract: null` — so a real
   v2 document (Agent/Shared scope, `P7-018`) migrating any node loses its scope contracts too, and
   would then fail `CanonicalTreeJson`'s own `ValidateScopeContractPresence` check or silently drop
   Agent/Shared entries. Separately, `:110-112`'s `NodeDocument` reconstruction uses the 9-parameter
   overload with no `bindings` argument, silently dropping every migrated node's generated-binding
   map. Reproduced live: a valid document with one blackboard key has zero keys after migration and
   still compiles "successfully" — a false-positive that hides real data loss from both a human and
   an MCP-driving agent.
2. **Native hot reload after a structural child reorder can corrupt an active `Sequence`'s
   execution**, running the wrong child or skipping one entirely. `Runtime/Execution/Native/
   HotReload/NativeHotReloadStateMigration.cs:154-158` resets a reordered composite's structural
   cursor (`NodeMemory`) when `StructuralChildChangeNodeIds` contains it, but the *active call
   stack* (`:163-209`, the `_frames` array walked by `Depth`) is migrated completely independently
   — it preserves "we are currently, actively, positioned inside child X" unchanged (only remapping
   `NodeIndex` for renumbering), with no reconciliation against the freshly-reset cursor. Reproduced
   live: `Sequence(a, b)` with `a` `Running`, reordered to `(b, a)` — after migration, `a` runs again
   (instead of the frame stack correctly handing control to whichever child the reset cursor now
   names), and `b` is skipped entirely. `Runtime/HotReload/Migration/HotReloadStateMigration.cs`
   (the reference-executor equivalent) has no analogous bug because `ADR-P7-011`'s own decision 3
   deliberately widened *only the native backend* to migrate an active instance directly — the
   reference executor still always falls back to a full restart when the old instance isn't idle,
   so it never faces this reconciliation problem at all. There is no existing correct pattern to
   mirror; this needs real design.
3. **Native hot reload never copies `CooldownInitialized`.** `NativeHotReloadInstance
   .CooldownInitialized` is its own separate `NativeArray<byte>`, distinct from `NodeMemory`/
   `Frames`/`Generations` — confirmed by grep that `NativeHotReloadStateMigration.cs` never
   references it anywhere. A migrated Cooldown node's deadline (`NodeMemory`) is copied correctly,
   but the fresh instance's `CooldownInitialized` stays at its zero-initialized default (`false`),
   so the node behaves as if never triggered. Reproduced live: deadline stays `110`, but at time
   `20` (well before the deadline) the action incorrectly returns `Success` instead of the expected
   `Failure`.

## Depends on

- `P7-006` (migration tooling implementation — the card that shipped `DocumentMigrator`).
- `P7-012` (native-backend hot reload implementation — the card that shipped
  `NativeHotReloadStateMigration`).

## Required reading

- `Authoring/Migration/DocumentMigrator.cs` in full (both bugs: document-level fields at `TryMigrate`,
  node-level `bindings` at `TryMigrateNode`) and `Authoring/Model/Tree/TreeDocument.cs`'s/
  `Authoring/Model/Tree/NodeDocument.cs`'s real constructor parameter lists (confirm every field a
  faithful reconstruction must carry forward, not just the three named here).
- `Runtime/Execution/Native/HotReload/NativeHotReloadStateMigration.cs` in full, plus
  `NativeLifecycleMachineV1`'s own `ReadCursor`/`WriteCursor`/child-dispatch logic (confirm exactly
  how a composite decides its next child on resume — cursor-relative vs. re-scanning the frame
  stack — before designing the reconciliation fix; the bug report's own root-cause theory needs
  live confirmation against the real dispatch code, not just this migration file).
- `Runtime/HotReload/Migration/HotReloadStateMigration.cs` (the reference-executor equivalent —
  confirm it truly has no analogous bug because of its idle-only scope, not by coincidence).
- `Runtime/Execution/Native/HotReload/NativeHotReloadInstance.cs` (the `CooldownInitialized` array's
  own lifecycle — confirm whether any other migrated-but-uncopied array exists alongside it,
  disclosed if found rather than silently expanding scope).
- `ADR-P7-011`/`ADR-P5-001` (the accepted decisions this card's fixes must stay consistent with —
  active-instance migration support, the "one mechanism, not three" subtree-restart framing).

## Allowed changes

- `Authoring/Migration/DocumentMigrator.cs` — carry every real field forward on both the document-
  and node-level reconstruction.
- `Runtime/Execution/Native/HotReload/NativeHotReloadStateMigration.cs` — reconcile the active frame
  stack with a reset structural cursor on reorder, and copy `CooldownInitialized` (and any other
  real gap found during the required reading above, disclosed).
- New/updated tests proving each of the three bugs is fixed, reproducing the exact scenarios above.
- `Planning~/Evidence/P7-029/`.

## Forbidden changes

- No change to `ADR-P7-011`'s own accepted decisions (active-instance migration stays supported,
  not walked back to idle-only) — this card fixes a real implementation bug in that decision's own
  implementation, it does not reopen the decision itself.
- No change to `HotReloadCompatibilityClassifier`'s own classification rules (what counts as
  `Migrate`/`Dropped`/`IncompatibleRestart`) unless a real gap is found there during investigation —
  disclose, don't silently expand.

## Deliverables

- `DocumentMigrator`-migrated documents/nodes are byte-for-byte faithful except for the fields a
  rule chain actually changed.
- A reordered, actively-running `Sequence` (or other composite) resumes correctly after native hot
  reload — the exact right child runs next, none skipped or repeated.
- A migrated Cooldown node's `CooldownInitialized` state survives native hot reload correctly.

## Acceptance criteria

- Live proof, each bug's own exact reported scenario, now passing: a document with a blackboard key
  (and, separately, a real Agent/Shared v2 document) keeps its data through migration; `Sequence(a,b)`
  with `a` `Running` reordered to `(b,a)` resumes correctly; a Cooldown with deadline `110` correctly
  still reports `Failure` at time `20` after hot reload.
- Full regression, zero new failures beyond the two already-disclosed pre-existing host-layout ones.

## Required verification

```text
Verify-Static.ps1
Run-UnityTests.ps1 -Mode EditMode -Scope Full
live proof of each of the three exact reported scenarios via Unity MCP
```

## Handoff notes

- Found by the owner (live Unity reproduction) during the same session as `P7-018`, immediately
  after it landed — reported with exact file/line references, independently re-confirmed here by
  direct code reading before this card was written (not taken on faith). All three are severity-real:
  #1 and #2 are silent data loss / silently wrong execution, #3 is a silent wrong-status regression.
  None of the three were introduced by `P7-018`; #1 does directly affect `P7-018`'s own new v2/
  Agent-Shared documents once a version-migrated node is involved, worth keeping in mind when
  scheduling. Recommended for prioritization alongside/before the owner's own "functionality before
  polish" queue (`P7-027` next) given these are correctness regressions in already-shipped
  functionality, not new capability — final sequencing is the owner's call.
