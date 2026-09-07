# P7-040 — Native hot reload never migrates or defaults Tree-scope blackboard

Status: `draft`

Creating this Draft card authorizes no implementation. Filed 2026-09-07, found while auditing the
addressing gap that became `P7-039`; independent of it.

## Objective

`NativeHotReloadStateMigration.TryMigrate` copies `NodeMemory`, `Generations`, Cooldown flags and
the active call-stack `Frames`, but never reads or writes `TreeBlackboard` anywhere — confirmed by
grep, zero matches for "Blackboard" in the whole `Runtime/Execution/Native/HotReload/` folder. The
fresh instance a hot reload builds is created through `NativeHotReloadInstance.TryBuild` ->
`NativeInstanceArenaOwnerV1.TryCreate` (the plain V1 path, not `TryCreateV2`), which allocates the
`TreeBlackboard` array with `NativeArrayOptions.ClearMemory` and never calls
`TryInitializeTreeDefaults` (only the V2 create path does that).

**Empirical conclusion**: after a native hot reload, every Tree-scope blackboard slot of the
migrated instance is raw zero bytes — neither the old instance's live values nor the tree's own
declared per-key defaults. This affects every Tree-scope slot regardless of whether any node writes
it; it is not specific to `P7-039`'s externally-written-slot case, though an externally-fed slot
happens to self-heal on its very next external write in the typical continuous-feed pattern, while a
node-only-written slot does not self-heal until whatever node logic would naturally rewrite it (if
any ever does).

This card investigates and fixes that gap. It does not reopen `ADR-P7-011`'s own accepted
active-instance migration decision, and it is not a prerequisite for `P7-039`.

## Depends on

- `P7-012` — native-backend hot reload implementation (the card that shipped
  `NativeHotReloadStateMigration`).
- `P2-007` — native Tree and Agent blackboard storage.

## Required reading

- `Runtime/Execution/Native/HotReload/NativeHotReloadStateMigration.cs` in full — confirm no other
  migrated-but-uncopied array exists alongside this one (the same discipline `P7-029` already
  applied to `CooldownInitialized`).
- `Runtime/Execution/Native/HotReload/NativeHotReloadInstance.cs` (`TryBuild`) — confirm exactly why
  it uses the V1 arena create path rather than V2, and whether that choice was deliberate or an
  oversight.
- `Runtime/Compiled/Native/NativeInstanceArenaOwner.cs` (`TryCreate` vs `TryCreateV2`,
  `TryInitializeTreeDefaults`) — confirm the exact default-population contract a migrated instance
  should honor for an unmigrated (or newly-declared) Tree-scope key.
- `Documentation~/decisions/ADR-P7-011-native-backend-hot-reload.md` and `ADR-P5-001` — the accepted
  active-instance migration and reorder-reset decisions this fix must stay consistent with.
- `Planning~/Evidence/P7-029/README.md` — the most recent precedent for finding and fixing a
  migrated-but-uncopied array (`CooldownInitialized`); mirror its investigation discipline.

## Scope (to be finalized in its own planning gate before implementation)

- Likely `Runtime/Execution/Native/HotReload/NativeHotReloadStateMigration.cs` and possibly
  `NativeHotReloadInstance.cs` (if the V1-vs-V2 arena create choice itself needs to change).
- New tests reproducing the exact zero-byte-after-reload scenario for both a node-written and an
  externally-written (post-`P7-039`) Tree-scope key.
- `Planning~/Evidence/P7-040/`.

## Open questions for the owner-approved plan

1. Should a migrated instance carry forward the OLD instance's live Tree-scope values (like
   `NodeMemory`), or reset to declared defaults (like a fresh bootstrap), or something in between?
   This is a real semantic decision, not just a copy-the-missing-array fix — unlike `CooldownInitialized`
   (P7-029 finding 3), where "carry it forward" was the obviously correct fix.
2. Does switching the hot-reload arena creation to the V2 path (which already calls
   `TryInitializeTreeDefaults`) have any other observable effect that needs to be understood before
   committing to it as the fix?

## Handoff notes

Found and disclosed during `P7-039`'s planning audit; the owner chose to file it as this separate
card rather than fold it into `P7-039`. Not yet sequenced ahead of other Draft Phase 7 cards.
