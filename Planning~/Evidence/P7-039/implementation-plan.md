# P7-039 implementation plan

Status: completed 2026-09-07. Implemented and verified within the corrected ADR-P7-039 (see its
same-day "Correction" note). See `Planning~/Evidence/P7-039/README.md` for full evidence.

**Correction (2026-09-07):** this plan originally targeted `NativeTreeBlackboardV1`/
`NativeInstanceArenaViewV2` directly. Starting implementation found `ProductionTreeHost` never
constructs that arena for either bootstrap overload — see `ADR-P7-039`'s own "Correction" note. This
revision targets the real storage owner, `GeneratedTreeDispatchAdapterV2`, and is scoped to the
generated-catalog bootstrap overload only.

## Scope

Add a narrow, allocation-free external write path from ordinary C# code into a running native tree
instance's own Tree-scope blackboard, addressed by stable key and gated on no node ever declaring
Write access to that key. Implements `ADR-P7-039` exactly; only the generated-catalog dispatch path,
only built-in (non-registered) value types, no scheduler/budget/Agent/Shared-scope/hot-reload change.

## 1. `NativeBlackboardCanonicalV1`: built-in-only canonical overloads

1. Add `internal static bool IsCanonicalBuiltInOnly(NativeBlackboardSlotBindingV2 slot,
   NativeArray<byte>.ReadOnly bytes)` — returns `false` immediately if
   `slot.RegisteredTypeIndex != CompiledIndex.Invalid` (registered types unsupported by this
   overload family), otherwise delegates to the existing `IsCanonicalBuiltIn(slot.TypeId,
   slot.EnumContractId, bytes, 0, slot.Size)`.
2. Add `internal static bool EqualsCanonicalBuiltInOnly(NativeBlackboardSlotBindingV2 slot,
   NativeArray<byte> current, NativeArray<byte>.ReadOnly candidate)` and `internal static void
   CopyCanonicalBuiltInOnly(NativeBlackboardSlotBindingV2 slot, NativeArray<byte>.ReadOnly source,
   NativeArray<byte> destination)` — same loop shape as the existing `EqualsCanonical`/
   `CopyCanonical`, but calling a new private `CanonicalByteBuiltInOnly`/
   `IsNegativeZeroComponentBuiltInOnly` pair that mirrors only the non-registered branch of the
   existing `CanonicalByte`/`IsNegativeZeroComponent` (no `NativeProgramImageViewV2` parameter
   anywhere in this family — the built-in branch never needed one).
3. Unit-test directly: a Float3/Bool/Int32 value round-trips through
   `IsCanonicalBuiltInOnly`/`CopyCanonicalBuiltInOnly`/`EqualsCanonicalBuiltInOnly` identically to
   the existing registered-aware methods for the same built-in slot (differential test against the
   existing methods, not just a standalone assertion); a registered-type slot is rejected by
   `IsCanonicalBuiltInOnly` regardless of byte content; a negative-zero float normalizes to +0
   exactly like the existing path.

## 2. `GeneratedTreeDispatchAdapterV2`: stable-key resolve and write

1. Add `internal bool TryResolveExternalTreeWrite(ulong stableKeyId, NativeBlackboardTypeIdV2
   expectedType, out uint slotIndex, out NativeBlackboardSlotBindingV2 slot, out BurstContextResult
   failure)`. Scans `_definition.Binding.Slots` for `Scope == Tree` with matching `StableKeyId`;
   rejects (`TypeMismatch`) a `RegisteredTypeIndex != CompiledIndex.Invalid` slot; validates the same
   type-identity fields `NativeTreeBlackboardV1.TryResolve` already checks
   (`TypeId`/`Version`/`Size`/`Alignment`/`EnumContractId`); rejects (`PhaseViolation`) when
   `(slot.AccessFlags & CompiledBlackboardAccessFlags.Write) != 0`; rejects (`InvalidHandle`) when no
   slot matches the key at all.
2. Add `internal bool TryWriteExternalTreeValue<T>(uint slotIndex, NativeBlackboardSlotBindingV2
   slot, NativeArray<T> candidate, out bool changed, out BurstContextResult failure) where T :
   unmanaged`. Body mirrors `NativeTreeBlackboardV1.TryWrite`'s own tail exactly, adapted to
   `_treeValues`/`_treeVersions`: validate `candidate` shape and `slot.Size`/`slot.Alignment` against
   `UnsafeUtility.SizeOf<T>()`/`AlignOf<T>()`; validate `(ulong)slot.Offset + slot.Size <=
   _treeValues.Length` and `slotIndex < _treeVersions.Length`; reject non-canonical bytes
   (`InvalidEncoding`) via step 1's new overloads; no-op (`Success`, `changed:false`) if the
   candidate already equals the canonical current bytes; reject (`Overflow`) at
   `ulong.MaxValue` version; otherwise copy canonical bytes into `_treeValues` and increment
   `_treeVersions[slotIndex]`.
3. Confirm (read `TreeValueByteCount`/`InitializeTreeDefaults` again while implementing) that a
   Tree-scope slot's own `Offset` is always usable directly against `_treeValues` with no
   `agentBaseOffset` addition — true by construction since Tree-scope offsets are computed before
   Agent-scope's own base offset, but verify live via a test with both Tree- and Agent-scope keys
   present in the same tree.
4. Unit-test directly against a real compiled `GeneratedTreeRuntimeDefinitionV2`/
   `GeneratedBurstCatalogV2` fixture: unknown key, registered-type key (rejected), write-owned-by-
   node key (rejected), zero-Write key (accepted), value round-trip via the adapter's own existing
   `TryReadTreeValueByte`, unchanged-value no-op (`changed:false`, no version bump), and that a
   node's own subsequent `Execute`/`SnapshotTreeValues` call observes the externally-written value.

## 3. `ProductionTreeHost`: public resolve/write API

1. Add a public `readonly struct ExternalTreeWriteHandle<T> where T : unmanaged` (nested, alongside
   `DispatchRequest`) — private fields only (`_instanceId`, `_slotIndex`, `_slot`), `internal`
   constructor, no public accessors, mirroring the ABI's own node-side handle opacity.
2. Add `public bool TryResolveExternalTreeWrite<T>(string stableKey, NativeBlackboardTypeIdV2
   expectedType, out ExternalTreeWriteHandle<T> handle, out NativeRuntimeFailureV1 failure) where T :
   unmanaged`. Throws `ArgumentException` for a null/empty key (matches this class's existing
   argument-validation style for `TryBootstrap`'s null checks). Fails with the existing
   `InvalidLifetime()` helper when not `_bootstrapped` or already `_disposed`. Fails with
   `NativeRuntimeDiagnosticCodeV1.NativeCapacityPlanInvalid` (the same code
   `FailUnsupportedForcedPolicy` already uses for "this configuration doesn't support the requested
   operation") when `_generatedDispatch == null`. Otherwise computes `StableHash.Fnv1A64(stableKey)`
   and calls the new adapter method, mapping `BurstContextResult` to `NativeRuntimeFailureV1` via the
   existing diagnostic codes: `InvalidHandle -> BlackboardUndeclaredAccess`, `TypeMismatch ->
   BlackboardTypeMismatch`, `PhaseViolation -> BlackboardRegistryMismatch`. On success, returns
   `new ExternalTreeWriteHandle<T>(InstanceId, slotIndex, slot)`.
3. Add `public bool TryWriteExternalTreeValue<T>(ExternalTreeWriteHandle<T> handle, T value, out bool
   changed, out NativeRuntimeFailureV1 failure) where T : unmanaged`. Fails with `InvalidLifetime()`
   when the handle is invalid, `handle` was resolved against a different `InstanceId`,
   `_generatedDispatch == null`, `_disposed`, or `_driving` is currently `true` (mid-round refusal --
   do not fault the host, this is an expected caller-timing case, not a reentrancy bug). Otherwise
   wraps `value` in a one-element `Allocator.Temp` `NativeArray<T>`, calls the adapter's new write
   method inside a `try/finally` that disposes the temp array, and maps any `BurstContextResult`
   failure the same way as step 2 (plus `InvalidEncoding -> BlackboardInvalidValue`, `Overflow ->
   BlackboardVersionOverflow`).
4. Unit-test end-to-end through the real `ProductionTreeHost` public surface: generated-catalog
   bootstrap succeeds; delegate-based (`DispatchLifecycle`/`DispatchLeaf`) bootstrap is refused by
   both new methods; mid-round refusal during a real `TryBeginBatchedRound`/`TryAdvanceToNextDispatch`
   window; cross-host handle refusal (resolve against host A, use against host B); zero managed
   allocation after warm-up across repeated `TryWriteExternalTreeValue` calls (profiler-style
   assertion matching this codebase's existing zero-GC test pattern).

## 4. `TreeValidator`: `AIBT2043 TreeScopeSlotNeverWritten`

1. Add `TreeScopeSlotNeverWritten` to `TreeValidationDiagnosticCodes` (`AIBT2043`) and register it in
   `TreeValidationDiagnosticCatalog` with the same Info-severity override pattern already used for
   `MigrationApplied`.
2. In `ValidateNodes`, accumulate a `HashSet<string>` of blackboard keys that receive at least one
   node Write access across the whole tree (extend `ValidateAccess`'s own existing loop to also
   record `access.Key` when `access.Mode == NodeAccessMode.Write`, via a new `ICollection<string>`
   parameter threaded through -- do not re-walk nodes a second time). After node validation, iterate
   the `blackboard` dictionary for `Scope == Tree` entries absent from that set and emit one
   `AIBT2043` per key.
3. Tests: a Tree-scope key with zero Write accesses emits exactly one `AIBT2043` and the document
   still validates with no errors; a Tree-scope key some node writes emits none; Agent/Shared-scope
   keys are never considered by this check either way.

## 5. Documentation

1. Add a short normative subsection to `blackboard-v1.md` (or the confirmed correct spec file)
   describing: eligibility (`Write == 0` across the tree, built-in types only), that it applies only
   to a `ProductionTreeHost` bootstrapped through the generated-catalog overload, the determinism
   disclosure, and the cross-host-handle-refusal rule.
2. Regenerate `Documentation~/generated/api-reference-runtime.md` via the existing generator command
   (`P7-014`'s pattern) once the new public API lands; confirm purely additive via the existing
   baseline-diff tooling.

## Verification

1. Unity compilation and Console inspection through Unity MCP.
2. Focused new tests listed in each step above.
3. `AIBT.Runtime.Tests` + `AIBT.Integration.Tests` and `AIBT.Editor.Tests` full regression.
4. `Verify-Static.ps1`, `git diff --check`, public-API baseline diff (additive only).
5. Live Unity MCP proof: bootstrap a real `ProductionTreeHost` through the generated-catalog
   overload, resolve and write an external value, and confirm a node observes it — not just a
   unit-test assertion.

## Explicit non-goals

- No scheduler, budget, profile, or latency change.
- No Agent/Shared-scope write support; no registered (non-built-in) value type support.
- No new blackboard storage or write support for the delegate-based bootstrap overloads.
- No fix to `P7-040`'s hot-reload/blackboard-defaults gap.
- No gameplay/sample/Swarm Arena content.
