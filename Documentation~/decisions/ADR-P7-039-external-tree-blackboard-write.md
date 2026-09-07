# ADR P7-039: External write to a native Tree-scope blackboard slot

- Status: Accepted 2026-09-07
- Date: 2026-09-07
- Decision ID: AIBT-040

## Context

`ProductionTreeHost`/`ProductionTreeScheduler` (the native, generated-Burst-dispatch production
path from P7-027/P7-033/P7-037/P7-038) have no public way for ordinary C# game code to feed live
external data (an agent's own transform, a moving target's position, ...) into an
already-bootstrapped tree instance on a per-frame basis. Confirmed empirically, not from
documentation:

**Correction (2026-09-07, before any code was written):** this decision's first drafted mechanism
assumed `ProductionTreeHost` executes both its bootstrap overloads over a shared
`NativeInstanceArenaViewV2`/`NativeProgramImageViewV2` (the "V2 arena" system `NativeSharedContextOwnerV1`
and `NativeTreeBlackboardV1.TryRead`/`TryWrite` operate on). That is factually wrong, found while
starting implementation, before writing any production code:

- `ProductionTreeHost._agents[0]` is built by `SchedulingPolicyDriver.TryCreateAgents`
  (`Runtime/Scheduling/SchedulingPolicyDriver.cs:296`, `SchedulingAgent`/`NativeLifecycleMachineV1`)
  for **both** bootstrap overloads. That machine owns node/frame/control/memory/configuration
  arrays only -- no blackboard storage of any kind. `NativeTreeBlackboardV1.TryRead`/`TryWrite` are
  not called anywhere in production code today, only in their own unit tests.
- The delegate-based overloads (`DispatchLifecycle`/`DispatchLeaf`) have no blackboard storage
  anywhere: the caller's own dispatch function receives only `DispatchRequest` (node index, phase,
  time, exit/abort reason) and returns a status. There is no AIBT-managed blackboard for this path
  to write into -- not a gap, an absent capability.
- The generated-catalog overload's blackboard is a **third, separate** storage: a private
  `NativeArray<byte> _treeValues` / `NativeArray<ulong> _treeVersions` pair owned directly by
  `GeneratedTreeDispatchAdapterV2` (`Runtime/Integration/GeneratedDispatch/
  GeneratedTreeDispatchAdapterV2.cs:19-20`), addressed through `NativeProgramBlackboardBindingV2
  .Slots` (`definition.Binding`, a managed `IReadOnlyList<NativeBlackboardSlotBindingV2>` -- the
  same slot struct as the V2 arena system, just reached through a different, managed projection).
  `DispatchSlot.SnapshotTreeValues`/`CommitTreeValues` copy between this array and each node's own
  per-request binding buffer immediately around that node's own dispatch call, always inside a
  `_driving == true` window.

The corrected scope: external write is reachable **only for the generated-catalog bootstrap
overload** (`TryBootstrap(GeneratedTreeRuntimeDefinitionV2, GeneratedBurstCatalogV2, ...)`). A host
bootstrapped through `DispatchLifecycle`/`DispatchLeaf` has nothing to write into and is refused
structurally, not silently ignored. This does not change the eligibility rule, the safety gate, or
the disclosed limitations below -- only where the write actually lands.

The original audit that motivated this card (why not just extend an existing primitive) still
holds unchanged:

- `NativeTreeBlackboardV1.TryRead`/`TryWrite` (`Runtime/Blackboard/Native/Tree/
  NativeTreeBlackboardV1.cs:84-144`) resolve a slot only through
  `TryResolve(program, nodeIndex, accessOrdinal, ...)`, which requires a compiled node access
  record (`program.Accesses[range.Offset + accessOrdinal]`) whose `NodeIndex`/`AccessOrdinal`
  match. There is no resolution path that does not originate from a specific compiled node.
- `NativeSharedContextOwnerV1` (`Runtime/Blackboard/Native/Shared/NativeSharedContextOwnerV1.cs`)
  implements a real bind/update/contribute/reduce lifecycle for cross-instance Shared scope, but is
  never constructed or referenced by `ProductionTreeHost` or `ProductionTreeScheduler` anywhere —
  it is unused production machinery, and its whole design (multi-instance contribution + reduction)
  targets a different problem than one instance's own live input.
- `NativeSnapshotBuilderV1.TryFreeze` (`Runtime/Integration/Snapshots/NativeSnapshotBuilderV1.cs:144`)
  produces a `NativeSnapshotOwnerV1` exactly once; neither type has any method to update payload
  bytes after freezing. Snapshot is a primitive for static per-run facts, not a live per-frame feed.

The compile-time signal needed to gate a new write path safely already exists and required no new
machinery to discover: `ReferenceCompiler.BuildBlackboardSlots`
(`Authoring/Compilation/ReferenceCompiler.cs:357-373`, `GatherBlackboardAccessFlags`) aggregates
every node's `Reads`/`Writes`/observer `WatchedKeys` per blackboard key into
`CompiledBlackboardAccessFlags`, stored per slot (`CompiledBlackboardSlotRecord.AccessFlags`,
`Runtime/Compiled/Model/CompiledProgramRecords.cs:266`) and carried through native compilation into
`NativeBlackboardSlotBindingV2.AccessFlags` (`Runtime/Compiled/Native/NativeProgramContracts.cs:354`,
verified by `NativeCompiledProgramV2Verifier.ValidateSlotAccessTable`,
`Runtime/Compiled/Native/NativeProgramImageOwner.cs:593-602`). A Tree-scope slot with
`(AccessFlags & Write) == 0` is therefore already knowable without adding new compiled metadata.

`TreeValidator.ValidateAccess` (`Authoring/Validation/TreeValidator.cs:673-697`) validates that
every node access references a declared key and rejects Shared-scope writes, but never flags a
declared Tree-scope key with zero Write accesses across the whole tree — that state compiles
silently today.

The safety hazard this ADR must close: neither `ProductionBatchedGroupDriverV1` nor
`ProductionPipelinedGroupDriverV1` expose a persisted `JobHandle` a caller could poll — each either
completes its `JobHandle` synchronously within one call (`ProductionBatchedGroupDriverV1.cs:60-64`)
or owns it entirely inside `NativePipelinedPhaseControllerV1`
(`ProductionPipelinedGroupDriverV1.cs:123-124,193-194`). The only real "is this instance's memory
safe to touch" signal is `ProductionTreeHost._driving`
(`Runtime/Integration/ProductionTreeHost.cs:55`), set for the full duration of every drive/round
entry point and cleared only once the host is fully idle between frames — and it is private, with
no public accessor anywhere on the class.

Not reopened by this decision: `ADR-P4-007`'s rejection of runtime scheduling-policy autotuning
(concerns policy selection, not blackboard data flow) and P7-033's global-budget-only rule (concerns
time budget, not data). Neither P7-033, P7-037 nor P7-038's cards or ADRs mention any existing or
deferred mechanism for external per-frame blackboard writes — this is new scope, not a resumption
of prior deferred work.

Found but explicitly out of scope for this decision: `NativeHotReloadStateMigration.TryMigrate`
never touches `TreeBlackboard` at all (no grep match for "Blackboard" anywhere in
`Runtime/Execution/Native/HotReload/`), and the fresh instance hot reload builds uses the V1 arena
create path that skips `TryInitializeTreeDefaults` — so today, hot reload leaves every Tree-scope
blackboard slot (node-written or not) as raw zero bytes, neither migrated nor reset to its declared
default. This is a pre-existing, independent defect unrelated to node/read-write access patterns;
it is tracked separately as `P7-040` and must not be silently fixed or made worse here.

## Decision

### Compile-time eligibility

A Tree-scope blackboard slot is eligible for external write only when no node in the tree declares
Write access to it: `(slot.AccessFlags & CompiledBlackboardAccessFlags.Write) == 0`. This is a hard
runtime precondition on the write path (see below), not merely a warning. `TreeValidator` gains a
new Info-severity diagnostic, `AIBT2043 TreeScopeSlotNeverWritten`, raised once per Tree-scope key
that has zero Write accesses across the whole tree — mirroring `AIBT2042 MigrationApplied`'s
established default-severity-override pattern (`ADR-P7-005`): the document already compiles and
validates successfully; this is a review heads-up, never a blocking failure. Agent- and
Shared-scope keys are not in scope for this diagnostic or for the write path below.

### Resolution and write API

`GeneratedTreeDispatchAdapterV2` (the type that already privately owns `_treeValues`/`_treeVersions`)
gains two new `internal` members:

- `TryResolveExternalTreeWrite(ulong stableKeyId, NativeBlackboardTypeIdV2 expectedType, out uint
  slotIndex, out NativeBlackboardSlotBindingV2 slot, out BurstContextResult failure)` — scans
  `_definition.Binding.Slots` for `Scope == Tree` with a matching `StableKeyId`, the same
  type-identity checks `NativeTreeBlackboardV1.TryResolve` already performs, and requires
  `(slot.AccessFlags & Write) == 0`. A slot whose `RegisteredTypeIndex != CompiledIndex.Invalid`
  (a registered, non-built-in value type) is rejected for this release — its canonical-encoding
  check needs the native program's registered-type/field tables as `NativeArray`s, which this
  managed-list-backed adapter does not carry; only the closed built-in scalar/vector/fixed-value
  set (`Bool`/`Int32`/`Int64`/`Float32`/`Float64`/`Float2`/`Float3`/`Quaternion`/fixed
  strings/opaque IDs) is eligible. This is a disclosed scope narrowing, not silently dropped
  support — the primary use case (a live position, a live target ID) is entirely built-in-typed.
- `TryWriteExternalTreeValue<T>(uint slotIndex, NativeBlackboardSlotBindingV2 slot, NativeArray<T>
  candidate, out bool changed, out BurstContextResult failure)` — reuses `NativeTreeBlackboardV1
  .TryWrite`'s own canonical-encoding/negative-zero-normalization/version-bump body, adapted to
  `_treeValues`/`_treeVersions` and a built-in-only canonical checker added to
  `NativeBlackboardCanonicalV1` (new `*BuiltInOnly` overloads that take no
  `NativeProgramImageViewV2`, since the built-in canonicalization branch never dereferences it).

`ProductionTreeHost` gains the only public entry points, both delegating to
`_generatedDispatch` and refusing structurally (existing `NativeCapacityPlanInvalid`, the same code
`FailUnsupportedForcedPolicy` already uses for "this configuration doesn't support the requested
operation") when the host was bootstrapped through a delegate-based overload instead:

- `TryResolveExternalTreeWrite<T>(string stableKey, NativeBlackboardTypeIdV2 expectedType, out
  ExternalTreeWriteHandle<T> handle, out NativeRuntimeFailureV1 failure)` — resolves once (typically
  at spawn time). The returned handle is an opaque public struct (mirroring the shape of the ABI's
  own node-side handles: private fields, `internal` constructor), carrying the resolved slot
  identity and this host's own `InstanceId` — never a raw ordinal a caller could forge, and never
  usable against a different host.
- `TryWriteExternalTreeValue<T>(ExternalTreeWriteHandle<T> handle, T value, out bool changed, out
  NativeRuntimeFailureV1 failure)` — the per-frame hot path. No string lookup; the one-value wrapper
  it needs to call into the adapter is an `Allocator.Temp` `NativeArray<T>`, matching this
  codebase's own established ephemeral-value pattern, never a managed allocation. Fails
  structurally, touching no memory, when: the host is mid-round (the same reentrancy invariant
  `_driving` already enforces for every other entry point — `DriveOneUpdate`, `TryBeginBatchedRound`,
  `TryAdvanceToNextDispatch` all already fail identically on reentry; here the write is simply
  refused rather than faulting the whole host, since a caller writing at the wrong moment is an
  expected, recoverable situation, not a reentrancy bug), or the handle's captured `InstanceId`
  does not match this host's own `InstanceId` — a handle resolved against a different (or since-
  destroyed) host is refused, never trusted.

Only the generated-catalog dispatch path and only Tree scope are addressed. A
`DispatchLifecycle`/`DispatchLeaf`-bootstrapped host has no blackboard storage to write into at all
(see Context) and is refused, not silently accepted. Agent scope has no real driving use case (a
live external value shared identically across instances is simplest as a plain per-instance write
in the caller's own loop, exactly matching how the tree host population is already iterated);
Shared-scope write stays permanently forbidden at the ABI level (`AIBT5007`,
`burst-node-abi-v1.md`) pending its own reducer decision, unrelated to this card.

### Determinism and hot reload — disclosed limitations

External writes make an instance's deterministic replay dependent on the calling code reproducing
the exact same writes at the exact same logical updates — this is a new way to introduce the same
class of non-determinism already disclosed for a custom `Func<long> clock`, not a new kind of bug.

A resolved `ExternalTreeWriteHandle<T>` is valid only for the exact `ProductionTreeHost` instance it
was resolved against (its `InstanceId`), never for a different host or a stale one. `ProductionTreeHost`
itself has no rebootstrap path today — `TryBootstrap*` refuses once `_bootstrapped` is set
(`ProductionTreeHost.cs:129,165`) — so this guard currently only ever rejects genuine cross-host
misuse, not a same-host program replacement; it stays a real, load-bearing check regardless (a
future capability could add rebootstrap, and the guard makes the write path correct against that
without needing to be revisited). `ProductionTreeHost` is also not itself hot-reloaded by anything
in this codebase today (`NativeHotReloadInstance`/`NativeHotReloadStateMigration` operate on a
`NativeInstanceArenaOwnerV1` directly, a different path `ProductionTreeHost` never constructs) — so
`P7-040`'s independent hot-reload/blackboard-defaults gap does not currently reach a
`ProductionTreeHost`-driven instance at all. This decision does not fix that gap and must not make
it worse if a future card wires hot reload into `ProductionTreeHost`.

## Consequences

- Game code gets a narrow, explicit, allocation-free way to feed one live value per frame into a
  running native tree instance, without reopening scheduler policy, budget, or the Shared-scope
  reducer decision.
- The eligibility gate is structural: a tree cannot have both a node-declared writer and an external
  writer for the same key. Changing a tree to add a node-side writer for a key some game code
  externally writes becomes a compile-time-visible, then runtime-refused, conflict rather than a
  silent race.
- `ProductionTreeHost`'s public surface grows by one handle type and two methods; no existing public
  member's signature or behavior changes.
- P7-034 (Swarm Arena) and any future gameplay showcase can consume this instead of inventing a
  bespoke ad hoc write mechanism.

## Alternatives rejected

- **Snapshot refresh (add an update-after-freeze method to `NativeSnapshotOwnerV1`):** Snapshot's
  registry/lease/dependency shape is designed for coarse, infrequent environment facts read under a
  Job dependency, not a per-frame hot write; adding mutation would change its whole contract for a
  narrower need already served better by extending Blackboard.
- **Wire `NativeSharedContextOwnerV1` into the production host:** Shared scope's entire value is
  cross-instance reduction; a value that is simply written once per instance (an agent's own
  position) needs no combination step, and reusing the bind/update/reduce state machine for a
  single-writer/single-reader case is unused complexity with no matching real case today.
- **Allow external write regardless of node-declared Write access:** would let external code and a
  node race on the same slot with no defined ordering; rejected — the `Write == 0` eligibility gate
  is a hard precondition, not an advisory convention.
- **Per-frame string-keyed write with no resolved handle:** simpler call site, but reintroduces a
  lookup on the hot path and a silent-typo risk; rejected in favor of resolve-once/write-many,
  matching this codebase's established zero-allocation-after-warmup discipline.
- **Fixing `P7-040`'s hot-reload gap inside this card:** unrelated defect, pre-existing for every
  Tree-scope slot regardless of write ownership; folding it in would blur this card's own scope and
  its own required evidence. Tracked and sequenced separately.
- **Supporting registered (non-built-in) blackboard value types now:** would need a managed-list
  equivalent of `NativeBlackboardCanonicalV1`'s registered-type canonicalization (today keyed off
  `NativeArray`-backed `NativeProgramImageViewV2.RegisteredTypes`/`RegisteredFields`, which
  `GeneratedTreeDispatchAdapterV2` does not hold); rejected for this release as real added surface
  with no current use case, not a fundamental blocker — a later card can add it against real demand.
- **Extending `DispatchLifecycle`/`DispatchLeaf` with new blackboard storage instead of narrowing
  scope to generated-catalog only:** would mean designing a whole new per-instance storage feature
  for a path that has never had one, a materially larger card than what was approved; rejected —
  callers needing external write should use the generated-catalog overload, which already exists
  and already owns real per-instance Tree-scope storage.

## Approval

Findings and this direction were presented to the owner 2026-09-07 after auditing the addressing
gap across Blackboard, Shared scope and Snapshot; approved as the recommended design. The
hot-reload/blackboard-defaults gap found during the same audit was, by the owner's own choice,
filed as a separate new card (`P7-040`) rather than folded into this one.

Re-presented and re-approved the same day, before any code was written: starting implementation
found the originally-approved mechanism (a shared V2 arena both bootstrap paths use) does not
exist — see the Context correction above. The owner approved the corrected mechanism (write through
`GeneratedTreeDispatchAdapterV2`'s own private storage, generated-catalog path only) without
reopening the eligibility rule, the safety gate, or the disclosed-limitations framing, all of which
were unaffected by the correction.
