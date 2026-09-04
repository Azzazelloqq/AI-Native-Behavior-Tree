# P7-029 implementation proposal

Status: Accepted by the owner on 2026-09-04 ("приступай" following the proposed callback order).
Prepared 2026-09-04 after the owner's instruction to start P7-029. No implementation changes
had been made at proposal time. Existing uncommitted P7-030 work is preserved.

## Verified preparation

- P7-006, P7-012 and P7-018 are Done in work-items.json.
- DocumentMigrator still selects constructors that omit blackboard, description, revision,
  Agent/Shared contracts and node bindings. The full constructors already support these fields.
- Native migration copies per-node memory/generations and active frames independently, resets
  structural cursors, and copies only Depth from the old control structure. The native composite
  accepts a completed child's result by incrementing the memory cursor; it does not reconstruct
  the cursor from the child's stable identity. Resetting only the cursor is therefore insufficient.
- CooldownInitialized is a separate array and is still not copied.
- The reference migration path falls back to restart for a non-idle machine, so it does not
  provide an active-stack reconciliation implementation to reuse.
- ADR-P5-001 defines the structural cursor reset. ADR-P7-011 retains native active-instance
  migration. Neither settles the callback sequence for a still-active child after that reset.
  P7-029 explicitly requires owner agreement on that sequence before leaving Draft.

## Recommended behavior requiring agreement

On a structural child change, retain the affected composite's activation and unaffected ancestors,
but cancel its previously active descendant path before evaluating children in the new order.
Use the existing HotReload abort reason and the normative deepest-first Abort/Exit order.
Do not treat cancellation as a normal child Success/Failure or advance the reset cursor with it.

For Sequence(a,b), with a Running, changed to Sequence(b,a), the observable continuation is:

1. a.Abort(HotReload), a.Exit(Aborted).
2. b.Enter, b.Tick, b.Exit(Success), assuming b returns Success.
3. a.Enter, a.Tick, a.Exit(Success), assuming this new activation returns Success.
4. Root Success.

This explicitly permits the new activation of a; it is a consequence of the accepted cursor reset.
If b was Running instead, cancel/exit b, then execute the complete new order b,a. If the order did
not change, preserve the active activation: next Tick has no extra Enter/Abort/Exit.

For nested changes on one active path, reconcile at the outermost affected composite once;
do not cancel the same descendant twice. A terminal-pending child finishes its terminal Exit
with its original reason; do not fabricate Abort after a completed Tick. Then clear stale child
result/cursor state before traversal of the new order.

Keep the existing fresh-instance ownership model. TryMigrate must not mutate/dispose the old
instance or invoke application callbacks itself. Seed the fresh instance with the cancellation
continuation and let the existing native dispatch/completion protocol deliver its callbacks before
any new-order Tick. Ownership IDs/leases must remain those of the fresh instance.

Compatible instance-lifetime memory, including cooldown state, survives this reconciliation.
Excluded/incompatible nodes retain freshly initialized memory and flags. Do not broaden the
classifier or replace active-instance migration with a blanket full restart.

## Implementation plan after agreement

1. Add behavior regressions for complete document-field preservation, v2 scope contracts,
   bindings, revision, no-rule/no-change paths and source immutability. Pass every existing
   semantic field through the full constructors; only rule-defined fields/version change.
2. Add Cooldown regressions with deadline 110: blocked at 20, allowed at 110, unchanged compatible
   state retained and excluded/incompatible state reset. Copy the flag by stable NodeId in the
   same eligibility branch as its deadline memory.
3. Add full callback-order regressions for both active-child positions, unchanged order, nested
   composites and terminal-pending Exit. Implement coherent cursor/frame reconciliation using
   the existing cancellation path. Keep this concentrated in NativeHotReloadStateMigration;
   allow a narrow internal helper in NativeLifecycleMachineV1 if needed to queue/reset a child
   path without duplicating its lifecycle rules. No new public API or node ABI is proposed.
4. Run focused tests first, then full EditMode regression in the open Unity Editor. Reproduce
   document/v2 preservation, active Sequence reorder and Cooldown boundaries live. Record exact
   counts and current baseline failures, run static verification and git diff --check, then
   update evidence, the accepted decision addendum, changelog and card state. No commit/push.

## Additional gap disclosed, not silently included

NativeHotReloadInstance also owns ParallelBranches, which the migrator does not copy. Parallel
suspended frames lie outside the copied Depth prefix, and the corresponding control bookkeeping
is not transferred either. ADR-P7-011 already lists parallel/budget/observer state as unverified.
This is a separate state-migration gap found during the card's required inventory, not live proof
of a new repro in this preparation. Do not claim general Parallel/budget migration correctness
from the three P7-029 fixes; track that scope separately rather than silently implementing it here.

## Decision needed

The owner approved the cancellation-before-new-order sequence above and the narrow internal
lifecycle-helper scope. P7-029's explicit semantic gate is resolved; ordinary implementation
choices and the preservation fixes need no further confirmation.
