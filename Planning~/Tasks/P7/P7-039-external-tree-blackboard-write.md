# P7-039 — External write to a native Tree-scope blackboard slot

Status: `Done`

Completed 2026-09-07: `ADR-P7-039` is accepted (including its same-day "Correction" section) in
`Documentation~/decisions/ADR-P7-039-external-tree-blackboard-write.md`; implementation and
verification evidence are recorded in `Planning~/Evidence/P7-039/README.md`.

**Correction (2026-09-07, before any code was written):** the ADR's originally-approved mechanism
(a shared V2 arena both `ProductionTreeHost` bootstrap overloads use) does not exist. The real write
target is `GeneratedTreeDispatchAdapterV2`'s own private `_treeValues`/`_treeVersions` storage,
reachable only through the **generated-catalog** bootstrap overload. The ADR was corrected and
re-approved the same day; this card's sections below already reflect the correction — see the ADR's
own "Correction" note in its Context section for the full explanation.

## Objective

Give ordinary C# game code a narrow, allocation-free, safe way to write one live external value per
frame into an already-bootstrapped native tree instance's own Tree-scope blackboard, addressed by a
stable key rather than a compiled node's own `(nodeIndex, accessOrdinal)` access record. This closes
a real architectural gap found while planning P7-034 (Swarm Arena): `ProductionTreeHost`/
`ProductionTreeScheduler` have no public channel for a moving target's position, an agent's own
transform, or similar live input. It does not add scheduling policy, per-tree budgets, Agent/Shared
scope support, registered (non-built-in) value type support, delegate-based-overload support, or fix
the independent hot-reload/blackboard-defaults gap tracked separately as `P7-040`.

## Depends on

- `P2-007` — native Tree and Agent blackboard storage.
- `P7-030` — the current `ProductionTreeHost` execution contract this card extends.
- `P7-033` — production scheduler and both Jobs group drivers (the reentrancy invariant this card's
  safety gate must respect).
- `P7-037` — production generated-catalog dispatch bootstrap (the only dispatch path this card's
  write API reaches; see the ADR's Context correction for why the delegate-based overloads have no
  blackboard storage to write into).

## Required reading

- `Documentation~/decisions/ADR-P7-039-external-tree-blackboard-write.md` (this card's own accepted
  decision, including its "Correction" note — read first).
- `Runtime/Integration/GeneratedDispatch/GeneratedTreeDispatchAdapterV2.cs` in full, especially
  `_treeValues`/`_treeVersions`, `DispatchSlot.SnapshotTreeValues`/`CommitTreeValues`/
  `TryResolveTreeSlot` and `TreeValueByteCount` — the actual owner of the storage this card writes
  into, and the exact offset/scope math (Tree-scope slots always start at byte 0; Agent-scope starts
  at `agentBaseOffset`) a new write path must respect.
- `Runtime/Blackboard/Native/Tree/NativeTreeBlackboardV1.cs` (`TryWrite`'s own canonical-encoding/
  negative-zero-normalization/version-bump body, and `NativeBlackboardCanonicalV1`'s built-in vs.
  registered canonicalization split) — the body to reuse/adapt, not the class to extend; nothing in
  this file is called from the write path itself.
- `Runtime/Compiled/Native/NativeProgramBlackboardContractsV2.cs` (`NativeBlackboardSlotBindingV2`,
  `NativeProgramBlackboardBindingV2.Slots`) — the exact slot struct shared between the managed
  (`GeneratedTreeDispatchAdapterV2`) and native-view (`NativeProgramImageViewV2`) projections.
- `Runtime/Compiled/Model/CompiledProgramRecords.cs` and `Authoring/Compilation/ReferenceCompiler.cs`
  (`GatherBlackboardAccessFlags`/`BuildBlackboardSlots`) — how `CompiledBlackboardAccessFlags` is
  already aggregated per slot at compile time; do not duplicate this computation.
- `Runtime/Integration/ProductionTreeHost.cs` in full, especially `_driving`, `_generatedDispatch`,
  `DriveOneUpdate`, `TryBeginBatchedRound`, `TryAdvanceToNextDispatch`, `CompletePendingDispatch` and
  `TryResumeBatchedDriveAfterDispatch` — the exact reentrancy invariant a new write entry point must
  respect without weakening it for anything else.
- `Runtime/Scheduling/SchedulingPolicyDriver.cs` (`SchedulingAgent`) — confirm it genuinely carries
  no blackboard storage before relying on that fact again.
- `Authoring/Validation/TreeValidator.cs` (`ValidateAccess`, `ValidateBlackboard`) and
  `Authoring/Validation/TreeValidationDiagnostics.cs` (`AIBT2042 MigrationApplied`'s Info-severity
  override pattern to mirror for the new `AIBT2043` code).
- `Documentation~/specifications/blackboard-v1.md` and `burst-node-abi-v1.md`'s stable-typed-bindings
  section — confirm the new write path stays outside the ABI's own node-side binding contract and
  does not require a spec-version bump there; add the externally-fed-slot semantics to
  `blackboard-v1.md` instead if that spec is the right home (confirm during implementation).

## Mandatory planning gate

Resolved by `ADR-P7-039`, recorded here for traceability:

1. Compile-time eligibility gate: `(slot.AccessFlags & CompiledBlackboardAccessFlags.Write) == 0`,
   using already-computed data — no new compiled metadata.
2. Resolution addressing: by `StableKeyId`, Tree scope only, built-in value types only, with the
   same type-identity checks `NativeTreeBlackboardV1.TryResolve` already performs.
3. Safety against an in-flight Job round: the write API lives on `ProductionTreeHost` itself and
   reuses its existing `_driving` reentrancy invariant; it must fail exactly like every other
   entry point on reentry, never race.
4. Handle lifetime: valid only for the exact `ProductionTreeHost` instance (`InstanceId`) it was
   resolved against; a handle used against a different or destroyed host is refused, not trusted.
5. Explicit disclosed limitations: determinism depends on the caller reproducing the same writes;
   the pre-existing, independent hot-reload/blackboard-defaults gap (`P7-040`) is out of scope, does
   not currently reach `ProductionTreeHost` at all, and must not be made worse.
6. Mechanism (post-correction): the write lands in `GeneratedTreeDispatchAdapterV2`'s own private
   `_treeValues`/`_treeVersions`, reachable only via the generated-catalog bootstrap overload. A
   delegate-based (`DispatchLifecycle`/`DispatchLeaf`) host is refused structurally — it has no
   blackboard storage of any kind.

## Allowed changes

- `Runtime/Integration/GeneratedDispatch/GeneratedTreeDispatchAdapterV2.cs` — new `internal`
  `TryResolveExternalTreeWrite`/`TryWriteExternalTreeValue<T>` members over its own existing
  `_treeValues`/`_treeVersions`; no change to any existing member's signature or behavior.
- `Runtime/Blackboard/Native/Tree/NativeTreeBlackboardV1.cs` (specifically its `NativeBlackboardCanonicalV1`
  nested class) — new `internal` built-in-only canonicalization overloads (`IsCanonicalBuiltInOnly`/
  `EqualsCanonicalBuiltInOnly`/`CopyCanonicalBuiltInOnly`) that take no `NativeProgramImageViewV2`;
  no change to any existing method's signature or behavior.
- `Runtime/Integration/ProductionTreeHost.cs` — new public `ExternalTreeWriteHandle<T>` type,
  `TryResolveExternalTreeWrite<T>` and `TryWriteExternalTreeValue<T>` methods only, delegating to
  `_generatedDispatch`; no change to any existing public member's signature or behavior.
- `Authoring/Validation/TreeValidator.cs` and `TreeValidationDiagnostics.cs` — new
  `AIBT2043 TreeScopeSlotNeverWritten` Info diagnostic and its aggregation pass.
- Focused new tests under `Tests/Runtime/NativeExecution/Blackboard/`,
  `Tests/Runtime/Integration/GeneratedDispatch/` (or the existing `ProductionTreeHostTests.cs`), plus
  `Tests/Editor/Validation/` for the new diagnostic.
- `Documentation~/specifications/blackboard-v1.md` (or the most accurate existing spec, confirmed
  during implementation) — document the new externally-fed-slot semantics normatively.
- Regenerated `Documentation~/generated/api-reference-runtime.md` via the existing generator, since
  this adds public Runtime API.
- `Planning~/Evidence/P7-039/`.

## Forbidden changes

- No scheduling-policy, per-tree-budget, `SchedulingProfile`, or budget-admission change (P7-033's
  own boundary stays untouched).
- No Agent- or Shared-scope write support; no registered (non-built-in) value type support; no
  reopening of the Shared-scope reducer decision (`burst-node-abi-v1.md`'s `AIBT5007`).
- No new blackboard storage for the delegate-based (`DispatchLifecycle`/`DispatchLeaf`) bootstrap
  overloads — a host bootstrapped that way is refused structurally, not given new storage.
- No change to `NativeSharedContextOwnerV1` or `NativeSnapshotOwnerV1`/`NativeSnapshotBuilderV1`.
- No change to any existing `NativeTreeBlackboardV1`/`NativeBlackboardCanonicalV1` method's
  signature or behavior — only new, additive `internal` members.
- No fix to the hot-reload/blackboard-defaults gap — disclose it, do not touch
  `NativeHotReloadStateMigration.cs` or `NativeHotReloadInstance.cs` from this card (`P7-040` owns
  that).
- No weakening of `ProductionTreeHost`'s existing reentrancy (`_driving`) invariant for any other
  entry point.
- No gameplay/sample work (`P7-034` remains a separate session's own scope).

## Deliverables

- `GeneratedTreeDispatchAdapterV2` can resolve and write a Tree-scope, built-in-typed slot by
  `StableKeyId`, gated on `AccessFlags.Write == 0`, reusing `NativeTreeBlackboardV1.TryWrite`'s own
  canonical-encoding/version-bump logic adapted to its own `_treeValues`/`_treeVersions`.
- `ProductionTreeHost.TryResolveExternalTreeWrite<T>`/`TryWriteExternalTreeValue<T>` work end-to-end
  through the generated-catalog dispatch path, and refuse structurally (not silently) on a
  delegate-based host.
- `TreeValidator` raises `AIBT2043` (Info) for a declared Tree-scope key with zero Write accesses,
  never blocking compilation.
- `blackboard-v1.md` (or the confirmed correct spec) documents the new semantics, including the
  disclosed limitations (determinism, generated-catalog-only, built-in-types-only).

## Acceptance criteria

- A live end-to-end test bootstraps a `ProductionTreeHost` through the generated-catalog overload
  with a tree whose declared Tree-scope key has no node writer, resolves a write handle, writes a
  value from outside any node callback, and a node reading that key observes the written value on
  its next tick.
- A `ProductionTreeHost` bootstrapped through `DispatchLifecycle` or `DispatchLeaf` is refused
  structurally (a clear `NativeRuntimeFailureV1`, not an exception or silent no-op) by both new
  methods.
- Attempting to resolve a write handle for a key that some node in the tree does write, or for a
  registered (non-built-in) value type, is refused structurally, with no partial state change.
- Attempting `TryWriteExternalTreeValue` while the host is mid-round (a batched or pipelined group
  wave in flight, or a pending dispatch not yet completed) is refused structurally, exactly matching
  the existing reentrancy failure other entry points already produce; no torn or partial write is
  ever observable.
- A handle resolved against one host is refused, not silently accepted, when used against a
  different host instance.
- `TreeValidator` emits `AIBT2043` for exactly the keys with zero Write accesses and never for a
  key any node writes; the document still compiles and validates successfully either way.
- No managed allocation after warm-up for repeated `TryWriteExternalTreeValue` calls.
- Full regression proves no existing dispatch, scheduling, or validation behavior changed.

## Required verification

```text
Verify-Static.ps1
focused GeneratedTreeDispatchAdapterV2 stable-key resolve/write tests (success, wrong-key, write-owned-by-node, type mismatch, registered-type rejection)
focused ProductionTreeHost external-write tests (generated-catalog success, delegate-based rejection, mid-round rejection, cross-host handle rejection, zero allocation after warm-up)
focused TreeValidator AIBT2043 tests (never-written key, node-written key, Agent/Shared keys unaffected)
AIBT.Runtime.Tests + AIBT.Integration.Tests
AIBT.Editor.Tests
generated documentation/API-diff checks (new public API is additive)
live Unity MCP proof: compile, run_tests, and a live bootstrapped host round-trip
git diff --check
```

## Handoff

Creating this card and accepting its ADR authorizes no implementation by itself; the owner must
still say to proceed before any production code is written. Once Done, this becomes the mechanism
P7-034 (a separate session, Swarm Arena) is expected to consume for feeding live agent/target
positions into native tree instances — this card does not build or touch any Swarm Arena content
itself. `P7-040` (hot-reload/blackboard-defaults gap) is independent and unblocked by this card.

## Outcome

Done, 2026-09-07. `ADR-P7-039` implemented as corrected (see its own "Correction" note); full
defect history and verification are in `Planning~/Evidence/P7-039/README.md`.

Acceptance criteria:

- **Live end-to-end proof through the generated-catalog path** -- met: a real compiled tree, a real
  `ProductionTreeHost`, external write before the first tick, the reader node observes it on that
  tick; a control host without the write stays `Failure`, proving the effect genuinely comes from
  the write.
- **A delegate-based host is refused structurally** -- met: `DispatchLifecycle`/`DispatchLeaf`
  bootstrap has no Tree-scope blackboard storage at all (confirmed empirically, not assumed); both
  new methods refuse with `NativeCapacityPlanInvalid`, never an exception or silent no-op.
- **A key some node writes, an unknown key, or a registered value type are refused structurally,
  with no partial state change** -- met for the first two with live tests; the registered-type
  rejection is a reviewable one-line guard, disclosed as not separately live-tested (see evidence).
- **Mid-round rejection matches the existing reentrancy failure, no torn write ever observable** --
  met: `_driving` forced via reflection, write refused, a subsequent real tick proves no partial
  write reached memory.
- **A handle resolved against one host is refused against a different host** -- met.
- **`TreeValidator` emits `AIBT2043` for exactly the keys with zero Write accesses** -- met, plus a
  real existing golden fixture (`enum-snapshot.aibt.json`) genuinely triggered it, handled as a
  disclosed, correct new signal rather than suppressed.
- **No managed allocation after warm-up** -- met, `GcAllocIs.Not.AllocatingGCMemory()`.
- **Full regression proves no existing behavior changed** -- met: 1800 total, 1797 passed, the same
  3 pre-existing unrelated baseline failures this project has documented since P7-003.

Scope actually delivered is narrower than the ADR's first draft, in a way approved the same day:
only the generated-catalog dispatch path, only built-in (non-registered) Tree-scope value types. See
`Planning~/Evidence/P7-039/README.md`'s own "Scope actually delivered vs. originally planned"
section.
