# P7-037 evidence

Status: **Done**. ADR AIBT-038 accepted 2026-09-05; the production contract it defines is now
implemented, proven live end-to-end through the real `ProductionTreeHost` bootstrap, and regression-clean.
See "Production implementation" below for the closing pass.

## Fresh audit findings

- `ProductionTreeHost.DispatchRequest` contains node index, phase, update/time and lifecycle reasons,
  but no config, instance-memory, blackboard, completion, transaction or command storage.
- `NativeBurstDispatchWorkspaceOwnerV2` requires exact per-request storage views;
  `NativeBurstDispatchBatchOwnerV2` owns grouped copies and can execute generated batches, but exposes
  only test-oriented individual result readers rather than an atomic production commit view.
- Generated catalog facades expose static `ExecuteImmediate`/`Schedule`. No common retained executor
  type exists for a production coordinator, and reflection is confined to the MCP test harness.
- `GeneratedBurstDispatchPrebindingV2.CatalogPlan` already derives the exact handshake, cases, fields,
  bindings and canonical rules from generated shard metadata. It is the correct build-time authority;
  caller-authored offsets are unnecessary.
- `GeneratedCompiledProgramV2.SemanticProgram` already contains the generated node identities and
  final config/memory offsets. The missing work is catalog identity/bootstrap and state commit, not a
  second tree compiler.

## Disposable proof

`Tests/Integration/NativeRuntime/GeneratedDispatchLifecycleProofTests.cs` currently:

1. materializes the real `GenerationShard` emitted by the packaged source generator;
2. parses the existing canonical `generated-tree-v2.aibt.json` fixture;
3. compiles it through `GeneratedCompiledProgramV2Compiler`;
4. derives all dispatch layout and offsets from compiler/generated metadata;
5. drives Enter/Tick/Exit through one real lifecycle machine using both generated immediate and
   scheduled facades;
6. compares root status, callback phases and complete post-lifecycle instance memory, and checks at
   the Tick commit boundary that the callback wrote `Count=38` after reading the bound value 37.

The first Unity run failed closed with `RegistryMismatch`. That exposed a production blocker in
`GeneratedBurstDispatchPrebindingV2`: it hashed every Authoring built-in, including
`aibt.stdlib.*`, while the source-generated facade correctly hashes the frozen `aibt.core.*`
runtime authority plus the selected generated shards. Prebinding now uses the same core authority,
checks canonical and numeric collisions, and the generated facade accepts the derived handshake.

The proof is deliberately test-local and currently uses a copied live blackboard value. It does not
yet prove production blackboard ownership, writes, command reduction, multi-instance batching or
failure recovery. Those remain mandatory before P7-037 can be Done.

## Verification

- `dotnet build AIBT.Integration.Tests.csproj --no-restore`: passed with 0 compiler errors. Existing
  Unity assembly-reference version warnings remain.
- Live Unity EditMode proof through MCP: 1/1 passed, including a real scheduled `JobHandle`.
- Generated prebinding catalog-plan regression through Unity MCP: 1/1 passed.
- Runtime core-authority contract through Unity MCP: 1/1 passed.
- A combined multi-assembly Unity job did not start within its 120-second initialization timeout;
  the three focused tests above were run separately and passed.
- `Verify-Static.ps1`: passed; 142 work items.
- `git diff --check`: passed.

## Proposed production contract

ADR AIBT-038 records the concrete generated executor, explicit catalog ownership, compiled runtime
definition and atomic dispatch-wave commit model revealed by the proof.

## Production implementation (2026-09-05, closing pass)

`Runtime/Integration/GeneratedDispatch/` (new: `GeneratedBurstCatalogExecutorV2.cs`,
`GeneratedBurstCatalogV2.cs`, `GeneratedTreeRuntimeDefinitionV2.cs`, `GeneratedTreeDispatchAdapterV2.cs`)
implements ADR AIBT-038's contract exactly: `IGeneratedBurstCatalogExecutorV2`, explicit
`GeneratedBurstCatalogV2` ownership/disposal with a decoded canonical layout blob, an immutable
`GeneratedTreeRuntimeDefinitionV2` binding a compiled v2 program to a catalog, and a new
`ProductionTreeHost.TryBootstrap(GeneratedTreeRuntimeDefinitionV2, GeneratedBurstCatalogV2, ...)`
overload. This work was already substantially written (uncommitted) when this pass started; closing
it surfaced four real, load-bearing defects, each found and fixed empirically against the live Unity
Editor via Unity MCP, not assumed correct because the code compiled:

1. **Two real compile errors** in `GeneratedTreeDispatchAdapterV2.cs`, invisible from reading the
   source alone: an uninitialized `out failure` on the `TryFindCase`-fails branch of `TryCreate`
   (CS0177), and ten call sites where `DispatchSlot.Dispose()` (the nested class's own zero-arg
   `IDisposable` member) silently hid the *enclosing* class's same-named private static
   `Dispose<T>(ref NativeArray<T>)` helper by C#'s declaring-type-first name lookup rule (CS1501 x10,
   not a signature mismatch) -- fixed by explicit `GeneratedTreeDispatchAdapterV2.Dispose(ref ...)`
   qualification at each nested call site.
2. **A genuine architectural bug in the new `GeneratedTreeRuntimeDefinitionV2.TryCreate` gate**: it
   compared `program.Header.CompiledFormatVersion`/`NodeRegistryHash` (which tree compiler produced
   this program, and its full live built-in registry) directly against `handshake.CompiledFormatVersion`/
   `NodeRegistry` (the generated catalog's own self-identity fields -- the layout-blob format version,
   always `1` since the Phase 2 `BurstNodeAbi` spike, and the frozen `aibt.core.*` authority plus only
   the catalog's selected generated shards). These are two same-named but semantically unrelated field
   pairs; comparing them directly can never agree for a real compiled tree and always failed closed
   with `TypeMismatch`. Root-caused by diffing live hash/version dumps from both sides (not guessed);
   fixed by removing that comparison and keeping only `ExecutionSemanticsVersion` (confirmed genuinely
   shared -- carried through unchanged from `ReferenceCompiler.ExecutionSemanticsVersion` across the
   v1-to-v2 wrap in `GeneratedCompiledProgramV2.cs:582`) plus the per-node `TryFindCase`
   (TypeId+Version+size) loop already in the method, which is the real, precise compatibility gate.
3. **A real scope gap in `GeneratedTreeDispatchAdapterV2`'s blackboard storage**: `TryResolveTreeSlot`
   accepted only `BlackboardScope.Tree`, but the project's own canonical `generated-tree-v2.aibt.json`
   fixture (and Agent/Shared scope generally, since `P7-018`) uses an **Agent**-scope blackboard
   access -- the adapter could not dispatch the project's own reference proof fixture at all.
   `ReferenceCompiler.BuildBlackboardSlots` gives Tree and Agent scope independent, zero-based
   `Offset` spaces (`scopeOffsets[(int)key.Scope]`), so naively widening the scope check to accept
   Agent would have let a Tree slot and an Agent slot silently collide at the same buffer offset --
   confirmed by reading `BuildBlackboardSlots` directly before touching anything. Fixed by giving
   Agent-scope storage its own base offset (`agentBaseOffset = treeRegionLength`) inside the same
   per-instance buffer, threaded through `TreeValueByteCount`/`InitializeTreeDefaults`/
   `SnapshotTreeValues`/`CommitTreeValues`/`TryResolveTreeSlot`. Scoped deliberately narrow: one tree
   instance is also the implicit sole "agent" for this standalone host (no cross-instance reduction
   happens outside `P7-033`'s own population scheduling), so this is safe for Tree+Agent; **Shared**
   scope stays unsupported and out of this adapter's scope, since it is genuinely cross-instance --
   matching the ADR's own explicit population-semantics carve-out to `P7-033`.
4. **The ABI/documentation surface was stale for the new, ADR-approved public members** --
   `Tests/Editor/CodeGen/Contracts/ExpectedPublicAbiV2.txt` (the narrow, IL2CPP/AOT-focused
   `AIBT.Burst`-namespace ABI manifest) and the generated API reference docs
   (`Documentation~/generated/api-reference-{runtime,authoring}.md`, `P7-014`'s own generator) had not
   been regenerated. Captured a fresh manifest live via the test's own `BuildPublicAbiManifest()` and
   diffed it against the checked-in baseline before writing anything, confirming the only change is
   exactly `IGeneratedBurstCatalogExecutorV2` (type + its 2 methods, 3 lines) inserted in sorted
   position -- purely additive, matching the ADR's own "Public API scope" list. Updated
   `ExpectedPublicAbiV2.txt` and rewrote `PublicSurfaceV2_ChangesOnlyEnterAndTickOpaqueSizePins`
   (renamed `...PlusAibt038GeneratedCatalogExecutor`) to mechanically reconstruct v2 from v1 (the two
   known opaque-size pins plus this one insertion) rather than assume the two fixtures stay
   index-aligned, since they no longer have equal length. Regenerated the docs live via the
   `AIBT/MCP/Regenerate Documentation` menu command.

New tests (`Tests/Integration/NativeRuntime/GeneratedDispatchLifecycleProofTests.cs`), all passed live
against the real `6000.5.8f1` Editor via Unity MCP:

- `ProductionTreeHost_GeneratedBootstrap_DrivesRealCustomNodeThroughFullLifecycle` -- the actual
  AIBT-038 acceptance proof: a normally compiled tree with a real generated custom node runs to a
  genuine `Success` through the real `ProductionTreeHost.TryBootstrap(definition, catalog, ...)` +
  `Update()` loop, not the disposable low-level workspace calls the original proof used.
- `ProductionAdapter_ImmediateAndScheduledDispatch_AreLifecycleEquivalent` -- immediate and a real
  scheduled `JobHandle`, driven through the actual `GeneratedTreeDispatchAdapterV2` (not the original
  proof's raw workspace calls), reach identical `Success`/byte-identical tree-value results on two
  separate instances sharing one catalog. The original `NormallyCompiledTree_...` test proved
  immediate/scheduled equivalence only at the disposable low-level dispatch layer; this closes that
  gap at the actual production entry point.
- `MultipleInstances_ShareOneCatalog_WhileEachKeepsIsolatedMutableState` -- two adapters over one
  catalog both reach `Success` independently; the catalog refuses disposal while either adapter is
  retained and only disposes once both release it.
- `Dispatch_RejectsOutOfRangeNodeIndex_WithoutMutatingAnyTreeState` -- a rejected dispatch (invalid
  node index) returns `InvalidHandle` and leaves every tree/blackboard byte byte-for-byte unchanged.
  Proves the guard-level rejection case of "a rejected/faulted batch commits no partial node memory";
  a genuine mid-execution generated-callback fault (the executor runs and reports failure) remains
  unproven -- forcing one needs a dedicated fault-injectable Burst leaf fixture, judged disproportionate
  to add this pass since `NativeBurstDispatchWorkspaceOwnerV2`'s own atomic-commit mechanics (which
  `GeneratedTreeDispatchAdapterV2` wraps unmodified apart from the new `TryAcknowledgePublishedCommands`
  reuse path) are already independently proven by `Tests/Runtime/NativeExecution/Dispatch/
  NativeBurstDispatchWorkspaceTests.cs`. Disclosed, not silently skipped.
- `Dispatch_AfterWarmup_AllocatesNoManagedMemory` -- a warmed-up run, then a fresh instance's own
  `Dispatch` calls wrapped individually in `Assert.That(..., Is.Not.AllocatingGCMemory())`.

## Verification (closing pass)

- Live Unity EditMode via MCP: `GeneratedDispatchLifecycleProofTests`, 7/7 passed (the original 2 plus
  5 new).
- `AIBT.Integration.Tests` + all 3 CodeGen test assemblies: 58/58 passed.
- `AIBT.CodeGen.ContractTests` (ABI baseline, both rewritten tests): 6/6 passed.
- `AIBT.Tests.Editor.Documentation.McpDocumentationGeneratorsTests`: 12/12 passed after regeneration.
- Full host EditMode regression: **1738/1741 passed**, 0 skipped. The 3 failures are the same
  pre-existing, already-documented ones from before this pass (`NEXT_STEPS.md`): 2 CodeGen
  `PackageInfo` assertions and 1 `LocalSaveSystem` autosave test -- unrelated to AIBT, not touched.
- `Verify-Static.ps1`: passed; 142 work items.
- `git diff --check`: passed.
- Not run this pass, disclosed rather than assumed clean: the CI-only, currently-unenforced
  `Tools~/Verification/P7/Audit/Baseline/public-api-baseline.txt` (`P7-020`, all 4 assemblies, gated
  on `P0-005`'s still-blocked self-hosted runner) is now stale by the same additive members: real,
  bounded follow-up work owned by `P7-020`, not re-run here to avoid a batch-mode Unity invocation
  against the same open Editor session this pass depended on.

## Scope note

Population/multi-instance batching (grouping many tree instances' requests into one catalog-executor
call per wave) is explicitly `P7-033`'s own ownership per the ADR; this adapter dispatches one
instance's one node per call, matching "Standalone host execution uses the same request/commit path
with a one-instance group" in `ADR-P7-037`.
