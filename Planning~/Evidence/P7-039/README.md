# P7-039 evidence — external write to a native Tree-scope blackboard slot

Status: `Done` — 2026-09-07

See `Documentation~/decisions/ADR-P7-039-external-tree-blackboard-write.md` for the accepted
design, including the same-day "Correction" section: the originally-approved mechanism (a shared
V2 arena both `ProductionTreeHost` bootstrap overloads use) does not exist. Starting implementation
found the real storage is `GeneratedTreeDispatchAdapterV2`'s own private `_treeValues`/
`_treeVersions`, reachable only through the generated-catalog bootstrap overload — the owner
re-approved the corrected mechanism the same day, before any further code was written.

## Implemented boundary

- `NativeBlackboardCanonicalV1` (`Runtime/Blackboard/Native/Tree/NativeTreeBlackboardV1.cs`) gained
  `internal` built-in-only canonicalization overloads (`IsCanonicalBuiltInOnly`/
  `EqualsCanonicalBuiltInOnly`/`CopyCanonicalBuiltInOnly`) that take no `NativeProgramImageViewV2` —
  safe because the built-in branch they mirror never dereferences it; a registered-type slot is
  refused up front instead of silently mishandled.
- `GeneratedTreeDispatchAdapterV2` gained `internal TryResolveExternalTreeWrite`/
  `TryWriteExternalTreeValue<T>`, scanning its own `_definition.Binding.Slots` for a `Tree`-scope,
  `StableKeyId`-matched, built-in-typed slot with `(AccessFlags & Write) == 0`, then writing through
  the same canonical-encoding/negative-zero-normalization/version-bump body
  `NativeTreeBlackboardV1.TryWrite` already uses for node-originated writes.
- `ProductionTreeHost` gained the only new public surface: `ExternalTreeWriteHandle<T>` (opaque,
  private fields, `internal` constructor, mirroring the ABI's own node-side handle shape),
  `TryResolveExternalTreeWrite<T>` and `TryWriteExternalTreeValue<T>`. Both delegate to
  `_generatedDispatch` and refuse structurally (`NativeCapacityPlanInvalid`) when the host was
  bootstrapped through a delegate-based overload instead. The write method refuses (not faults) a
  mid-round call via the existing `_driving` reentrancy invariant, and refuses a handle resolved
  against a different host via `InstanceId` comparison.
- `TreeValidator` gained `AIBT2043 TreeScopeSlotNeverWritten` (Info severity, mirroring `AIBT2042
  MigrationApplied`'s established override pattern) — raised once per declared Tree-scope key with
  zero Write accesses across the whole tree. `ValidateAccess` now also accumulates the set of
  Tree-scope keys some node writes, reusing its existing per-node access loop rather than a second
  graph walk.
- `Documentation~/specifications/blackboard-v1.md` documents the new external-write semantics and
  both disclosed limitations (determinism, cross-host handle refusal) normatively.
- `Documentation~/generated/api-reference-runtime.md` regenerated via `AIBT/MCP/Regenerate
  Documentation`; the new public API is purely additive.

## Scope actually delivered vs. originally planned

- Only the generated-catalog dispatch path. A `DispatchLifecycle`/`DispatchLeaf`-bootstrapped host
  has no Tree-scope blackboard storage at all (confirmed empirically: `SchedulingPolicyDriver`'s
  `SchedulingAgent`/`NativeLifecycleMachineV1` carry node/frame/control/memory/configuration arrays
  only) — refused structurally by both new `ProductionTreeHost` methods, not silently accepted.
- Only built-in (non-registered) Tree-scope value types — a registered type's canonicalization needs
  the native program's own `NativeArray`-backed registered-type/field tables, which
  `GeneratedTreeDispatchAdapterV2`'s managed `NativeProgramBlackboardBindingV2` binding does not
  carry. Rejected structurally (`BlackboardTypeMismatch`) at resolution, disclosed as a real,
  reasoned scope narrowing in the ADR rather than left silently unhandled. Not live-tested against a
  real registered-type fixture (would need a dedicated `[AibtBurstValue]` fixture disproportionate
  to this card's own scope) — the rejection itself is a trivial, directly-reviewable one-line guard.

## Behavior-first coverage

New fixture, its own dedicated assembly (`Tests/Editor/CodeGen/GenerationP7039/`,
`AIBT.CodeGen.P7039ExternalTreeWrite.Tests`) — a node assembly declares exactly one shard, so this
could not share `GenerationShard`'s own assembly without perturbing its hash-pinned assertions
elsewhere. Two real `[AibtBurstNode]` fixtures: `ExternalTreeReaderNode` (Tree-scope Read-only
`live-target`, Float32 — the eligible case) and `ExternalTreeWriterNode` (Tree-scope Write
`owned-target`, Int32 — the node-owned, ineligible case).

`Tests/Integration/NativeRuntime/ExternalTreeBlackboardWriteTests.cs` (9 tests), driving the real
compiled catalog/definition through a real `ProductionTreeHost`, no dispatch shape hand-authored:

- `ExternalWrite_ResolvedForEligibleKey_ObservedByNodeOnFirstTickAfterWrite` — writes before the
  first tick, the reader node observes it and the root reaches `Success`.
- `WithoutExternalWrite_DefaultZeroValueLeavesReaderFailing` — the control: without a write, the
  declared default leaves the reader `Failure`, proving the positive case genuinely comes from the
  write.
- `ResolveExternalTreeWrite_UnknownKey_RejectsWithNoStateChange`,
  `ResolveExternalTreeWrite_TypeMismatch_Rejects`,
  `ResolveExternalTreeWrite_KeyANodeWrites_RejectsAsNodeOwned` — the three structural resolution
  rejections, each against the real compiled program.
- `ExternalWrite_MidRound_RefusedStructurally_LikeEveryOtherReentrantEntryPoint` — `_driving` forced
  `true` via reflection (mirroring this suite's own established `InvokeUpdate`/`InvokeOnDestroy`
  technique), write refused, then a real tick proves no partial write ever reached memory.
- `ExternalWrite_HandleFromDifferentHost_RefusedStructurally` — a handle resolved against host A is
  refused when used against host B.
- `DelegateBasedHost_ExternalWrite_RefusedStructurally_NoBlackboardStorageExists` — a real
  `DispatchLeaf`-bootstrapped host refuses both new methods structurally.
- `ExternalWrite_RepeatedAfterWarmup_AllocatesNoManagedMemory` — zero GC allocation after warm-up,
  the same `GcAllocIs.Not.AllocatingGCMemory()` idiom this suite already uses elsewhere.

`Tests/Editor/Validation/TreeScopeSlotNeverWrittenValidatorTests.cs` (3 tests): a never-written
Tree-scope key emits exactly one Info `AIBT2043` and the document still validates cleanly; a
node-written Tree-scope key never emits it; Agent/Shared-scope keys are never considered by this
check even when never written.

One existing test needed a real, disclosed update, not a validator behavior change:
`SemanticSliceCompilationTests.CanonicalGoldenTree_ValidatesAndCompilesDeterministically`'s
`enum-snapshot.aibt.json` golden fixture has a genuine Tree-scope key ("state") no node writes —
`AIBT2043` now correctly fires there. The test's own "compiles cleanly" invariant was narrowed from
"zero diagnostics" to "zero non-Info, non-`TreeScopeSlotNeverWritten` diagnostics", preserving its
real intent (no actual Error) without asserting away a genuinely new, correct signal.

## Verification

Confirmed through Unity MCP on the open Unity Editor instance:

- Compilation clean after every change; final Console inspection had zero errors.
- `AIBT.Integration.Tests`: 52/52 passed (includes the 9 new external-write tests and the updated
  `SemanticSliceCompilationTests` case).
- `AIBT.Editor.Tests`: 436/436 passed (includes the 3 new `AIBT2043` tests).
- Full host EditMode regression (all assemblies, no filter): 1800 total, 1797 passed, 3 pre-existing
  unrelated failures matching this project's own established baseline (2 `GeneratedArtifactContractTests`
  `PackageInfo.FindForAssembly` assertions, 1 `LocalSaveSystem` autosave test) — none touch this
  card's own files.
- `Tools~/Verification/Verify-Static.ps1`: passed, 145 work items.
- `git diff --check`: passed.

## Scope notes

- No scheduler, budget, `SchedulingProfile`, or Agent/Shared-scope change.
- `P7-040` (hot-reload/blackboard-defaults gap, found during this card's own planning audit) is
  independent, unblocked, and untouched by this card.
- `P7-034` (Swarm Arena, a separate session) is expected to consume this mechanism; no gameplay or
  sample content was built here.
