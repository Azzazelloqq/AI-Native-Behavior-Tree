# P5-005 affected-subtree restart evidence

## Result

Per `ADR-P5-001`'s correction (restated in its own text and in this session's implementation of
`P5-006`), affected-subtree restart is **not a separate mechanism** from compatible migration --
both are `Runtime/HotReload/Migration/HotReloadStateMigration.Migrate`, differing only in the
exclusion set: `P5-006` passes an empty exclusion set (nothing classified `IncompatibleRestart`
anywhere); this card's own case is the localized one -- `P5-003`'s classifier already computes
`HotReloadClassificationResult.RestartSubtreeRootNodeIds` (the topmost incompatible nodes, or
escalated to the whole tree when localization cannot be proven safe), and `HotReloadStateMigration`
expands those roots to their full descendant `NodeId` sets internally (`ExpandRestartSubtrees`) and
excludes exactly those nodes from the copy -- they keep the freshly constructed instance's default
state, which *is* "that subtree restarted," without a second implementation.

`HotReloadStateMigrationTests.Migrate_DoesNotMigrateStateForIncompatibleTypeChange`
(`Planning~/Evidence/P5-006/`) is this card's own direct proof: an `IncompatibleRestart` leaf is
excluded from the copy (`ResetNodeCount == 1`, `MigratedNodeCount == 0` for a single-leaf tree),
while `HotReloadCompatibilityClassifierTests.TypeChange_NodeIsIncompatible_DescendantSweptIntoRestartRegion_RootIsJustTheChangedNode`
(`Planning~/Evidence/P5-003/`) already proved the localization itself: an unrelated root outside
the incompatible node's subtree keeps classifying `Migrate` and is never included in the exclusion
set.

## Decision this card inherits from `P5-006`

Per `ADR-P5-001`'s implementation addendum, localized restart -- like full migration -- only runs
when the old instance is idle; otherwise it falls back to `HotReloadFullRestart` for the whole
instance, the same as an unsafe localization already does per the ADR's own text. This is not a
new decision specific to this card; it is inherited directly from the shared mechanism.

## Verification

Covered entirely by `Planning~/Evidence/P5-003/` (classifier localization) and
`Planning~/Evidence/P5-006/` (the shared copy mechanism's exclusion-set handling) -- see those
files for the live Unity MCP test results and full-suite regression runs. No additional
implementation or tests exist under this card's own name; there is nothing left to build once
`P5-003` and `P5-006` are both done, per `ADR-P5-001`'s own "not two independent implementations"
finding.

## Scope and limitations

- Same idle-only scope as `P5-006` -- see that card's README for the full rationale
  (`ReferenceFrame`'s read-only `NodeIndex` and extensive per-decorator-type field set made full
  frame-stack migration substantially larger than originally anticipated; escalated to and decided
  by the owner before implementation).
- The shared-blackboard-write escalation rule that can force localization to widen to a full-tree
  restart is `P5-003`'s own conservative over-approximation (any Shared-scope write anywhere in
  the candidate region disqualifies localization), not re-verified here.

## Card-split correction record

This is the card `ADR-P5-001` flagged as needing to be "merged or resequenced" with `P5-006` before
either started implementation. Both are recorded as `Done` against the same commit, with `P5-006`
holding the shared implementation and evidence, and this file recording the correction and
cross-referencing where this card's own specific claims (subtree exclusion, localization) are
actually proven.
