# P7-029 — Fix real data-loss/state bugs in document migration and native hot reload

Status: `Done`

Verified 2026-09-04: 90/90 focused tests, full EditMode 1685/1688 with the same three unrelated
baseline failures, and separate live document/Sequence/Cooldown probes.
See [evidence and limits](../../Evidence/P7-029/README.md). No Player build was run.

Owner approved the cancellation-before-new-order lifecycle on 2026-09-04:
[accepted implementation plan](../../Evidence/P7-029/implementation-proposal.md).
The narrow internal lifecycle helper in `NativeLifecycleMachineV1.cs` is included in this scope.

## Objective

Preserve document data and consistent native execution state during migration. This card groups
three review findings in existing Phase 7 functionality. Revalidated against `66fa058` on
2026-09-04; evidence and remaining semantic decisions are distinguished below.

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
   an MCP-driving agent. The omitted document `Revision` also falls back to 1; preserve it unless
   an accepted migration contract explicitly calls for changing it.
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
- `P7-018` (current v2 Agent/Shared document contracts that must survive migration).

## Revalidation and formulation corrections

- Findings 1 and 2 remain P1; finding 3 remains P2. In this re-review, the canonical migration
  probe again changed one blackboard key to zero and still compiled, and the active reorder probe
  again produced `a:Tick, a:Exit, a:Enter, a:Tick, a:Exit, Completed`, skipping `b`.
- The earlier cooldown probe reported initialized flag 1 -> 0, retained deadline 110, and Success
  at time 20 where the configured blocked result was Failure. This probe was not rerun in the
  re-review; current code inspection again confirms that the separate flag array is not copied.
- Data preservation means semantic/model-field preservation, not byte-for-byte retention of JSON
  whitespace/property order: this migrator operates on a document model, not source text.
- ADR-P5-001 explicitly resets a reordered Memory composite's cursor to not-yet-started.
  Consequently, an unconditional "no child may ever repeat" criterion is incorrect: a reset may
  legitimately re-enter a child. Require a coherent stack/cursor and the accepted reset semantics.
- Before implementation, agree the precise callback sequence for reconciling a still-active child
  with that reset, including any required Abort/Exit. Do not silently invent lifecycle behavior or
  remove active-instance migration support. This gate was resolved by the owner-approved plan above.

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

- Migrated documents/nodes preserve all semantic fields except those explicitly changed by the
  rule chain: include blackboard, description, revision, Agent/Shared contracts and node bindings.
- A reordered, actively-running `Sequence` resumes with a stack/cursor consistent with the accepted
  reset policy; no child is skipped or duplicated as an artifact of conflicting state. Legitimate
  re-entry caused by the agreed reset is tested explicitly rather than prohibited.
- A migrated Cooldown node's `CooldownInitialized` state survives native hot reload correctly.

## Acceptance criteria

- Live proof of each reported scenario after the fix: a document with a blackboard key and a real
  Agent/Shared v2 document retain their data through migration. Also cover bindings, non-default
  revision, no-rule/no-change paths and source-document immutability.
- For `Sequence(a,b)` with `a` Running reordered to `(b,a)`, verify the full agreed callback order
  through root completion, including `b` at the correct position, not just a DispatchRequired flag.
  Include the complementary active-child position and unchanged-order preservation.
- With the configured Failure blocked result and deadline 110, the migrated Cooldown blocks at
  time 20 and permits execution at the deadline. Incompatible/reset nodes must not inherit stale
  cooldown flags; an unchanged compatible node must preserve them.
- Full regression with exact counts; identify baseline failures from the actual run rather than
  treating a historical number of unrelated failures as an allowed failure budget.

## Required verification

From the package root, with verification environment variables set:

```powershell
& './Tools~/Verification/Verify-Static.ps1'
& './Tools~/Verification/Run-UnityTests.ps1' -UnityPath $UnityPath -ProjectPath $ProjectPath -OutputPath $OutputPath -Mode EditMode -Scope Full
git diff --check
```

Run focused migration/hot-reload tests first and live proof of the three reported scenarios.
Use Unity MCP tests for an already-open project rather than launching a second Editor.

## Handoff notes

- Reported in the code review with live Unity probes and exact file/line references, then checked
  against the source when this card was created. The revalidation above distinguishes fresh probes
  from earlier evidence. All three remain actionable:
  #1 and #2 are silent data loss / silently wrong execution, #3 is a silent wrong-status regression.
  None of the three were introduced by `P7-018`; #1 does directly affect `P7-018`'s own new v2/
  Agent-Shared documents once a version-migrated node is involved, worth keeping in mind when
  scheduling. P7-030/P7-031/P7-032 track the separate host, MCP workflow and scheduler recovery
  scopes. Final sequencing and implementation authorization remain the owner's decision.
