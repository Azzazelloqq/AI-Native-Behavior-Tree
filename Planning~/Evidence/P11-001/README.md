# P11-001 evidence: subtrees

Decision: `Documentation~/decisions/ADR-P11-001-subtrees.md` (accepted 2026-11-19, AIBT-049, all §8
recommendations). Stage 1 of ADR §7 is accepted; stage 2 is implemented (section "Stage 2" below)
and awaits acceptance; stages 3–4 are not started.

## Stage 1 — format v3, validator, expander, managed path (2026-11-19)

Base: `feature/aibt-consumer-workflow` at `f02ad04`. Editor 6000.6.9f1. Tests run with the async
`Tools/Unity/Invoke-UnityTests.ps1` after `Invoke-UnityRecompile.ps1`, touched classes only (one
runner on this Editor). Raw outputs, the baseline and the harness project are in
`C:\UnityProjects\_aibt_acceptance\P11-001\` (outside the repository).

### What was done

- **Model and format.** `SubtreeInterface`, `SubtreeReference`, `SubtreeKeyBinding`
  (`Authoring/Model/Tree/`); `NodeDocument.Subtree`, `TreeDocument.Interface`. The reader accepts
  `formatVersion` 3 with `interface` and `subtree`; versions 1 and 2 reject both exactly as before
  (`AIBT1004`). The writer emits `interface` after `root` (immediately before `blackboardContracts`
  / `blackboard`) and `subtree` after `description`. `Schemas~/tree.schema.json` allows version 3:
  version 1 and 2 node shapes are unchanged; version 3 nodes are an ordinary node or a reference
  node; `interface` is forbidden below version 3.
- **Validator.** `AIBT2047` SubtreeReferenceShape, `AIBT2048` SubtreeKeyTarget, `AIBT2049`
  SubtreeReferenceRoot, `AIBT2050` SubtreeInterface, in `TreeValidator` and its catalog.
- **Expander** (`Authoring/Compilation/Subtrees/`): `ISubtreeResolver`, `DirectorySubtreeResolver`
  (indexes `*.aibt` / `*.aibt.json` under a directory by tree ID), `InMemorySubtreeResolver`,
  `SubtreeExpander.Expand` per ADR §4.3–4.5, `AIBT3020`–`AIBT3028` in the compiler catalog.
  Cycle detection runs before the depth check (depth ≤ 8, a constant). Diagnostics raised inside a
  subtree carry its document ID and a related location at the reference node.
- **Identity rule.** A document with no reference node and no `interface` is returned as the same
  object (`SubtreeExpansion.IsIdentity`), and the old `ReferenceCompiler.Compile` overload is now a
  call to the new one with a null resolver.
- **Compilation.** `ReferenceCompiler.Compile(document, registry, options, resolver)` expands then
  compiles; `Compile(SubtreeExpansion, registry, options)` compiles an existing expansion. The debug
  map's `SourcePath` of every inner node is the subtree's source ID.
- **Managed path.** Internal `ManagedTreeProgram.TryCompile` overloads with a resolver, and
  `TryCompileExpanded` for stored expanded JSON plus per-node sources.
  `BehaviorTreeSource.FromJson(json, name, resolver)` and an 8-argument `SetContent` that stores
  the per-node subtree sources in two hidden serialized arrays (they survive serialization; the
  7-argument `SetContent` clears them). `BehaviorTreeImporter` is **not** changed (stage 2 wires it
  to this `SetContent`).
- **Behaviour cases.** Both case runners (`AuthoringBehaviorCaseExecutorFactory`,
  `ReferenceBehaviorCaseAdapter`) expand a case tree that uses subtrees with a
  `DirectorySubtreeResolver` over the case tree's directory.
- **Specifications** per ADR §4.8: `canonical-json-v1.md` (new section "Format version 3", plus one
  sentence giving a reference node's member order, which the draft leaves open),
  `identity-and-hashing-v1.md` ("Expanded identities"), `compiled-program-v1.md` (header bullet,
  debug-map sentence), `blackboard-v1.md` ("Subtree interface", §4.3 bullets 1–4),
  `execution-semantics-v1.md` ("Subtree references"), `diagnostics-v1.md` (table of the 13 codes),
  `data-formats.md` (pointer to the ADR), `editor-layout-v1.md:145` (subtree-reference edge
  removed).

### Hashes before and after

A temporary dumper (two test classes, category `P11BaselineDump`, deleted before commit; copies in
the acceptance folder) wrote, for every `*.aibt` / `*.aibt.json` in the package (except `Tools~`,
`Results/`, `.aibt/`, layouts), the canonical bytes hash, semantic hash and, for each of three
registries × two policies, the compiled-content and canonical semantic hash or the failing code;
and, for every semantic-slice tree, the compiled hashes with the semantic-slice registry. The
baseline was taken on `f02ad04` before any change; the second run is after all stage 1 changes.

| Dump | Baseline lines (`f02ad04`) | Lines changed or removed after | Lines added after |
|---|---|---|---|
| Package trees × registries × policies | 409 (144 OK compiled) | **0** | 42 (the 6 new fixture trees) |
| Semantic-slice trees | 57 (18 OK compiled) | **0** | 6 (the 6 new fixture trees) |

The new parent fixtures report `AIBT3020` in the dump because it calls the old overload without a
resolver, as ADR decision 5 requires. Files: `baseline-f02ad04-hashes-*.txt`,
`after-hashes-*.txt`, `diff-*.txt`.

In addition, `SubtreeExpanderTests.IdentityRule_EveryExistingTreeIsReturnedAsTheSameObjectAndCompilesToTheSameHash`
checks the same object comes back for every package tree and compiles each through the old and the
new overload under two registries, comparing the content hashes.

### Changed expectations of existing tests

| Test | Before | After | Reason |
|---|---|---|---|
| `CanonicalTreeJsonTests` (unsupported-version `TestCase`, line 50) | JSON with `formatVersion: 3` → `AIBT1005` | JSON with `formatVersion: 4` → `AIBT1005` | 3 is now a supported version; the test still checks that the first unsupported version fails with the same code. |

No other expectation changed. `SemanticSliceBehaviorTests` only gained two `TestCase` entries for
the new golden cases. `Tests/PlayMode/AIBT.PlayMode.Tests.asmdef` gained a reference to
`AIBT.Authoring` (the PlayMode test parses the subtree document).

### Added public API (additions only; no existing signature changed)

- `AIBT.Authoring.SubtreeInterface`, `SubtreeReference`, `SubtreeKeyBinding` (new types).
- `NodeDocument`: constructor with a trailing `SubtreeReference subtree`; `CreateSubtreeReference`;
  `Subtree`; `IsSubtreeReference`; `WithSubtree`.
- `TreeDocument`: `SubtreeFormatVersion = 3`; constructor with a trailing `SubtreeInterface`;
  `Interface`; `SetInterface`.
- `TreeValidationDiagnosticCodes`: `SubtreeReferenceShape`, `SubtreeKeyTarget`,
  `SubtreeReferenceRoot`, `SubtreeInterface`.
- `ISubtreeResolver`, `SubtreeResolution`, `SubtreeResolutionStatus`, `DirectorySubtreeResolver`,
  `InMemorySubtreeResolver`, `SubtreeExpander` (`MaximumNestingDepth`, `RequiresExpansion`,
  `Expand`), `SubtreeExpansion`, `SubtreeExpansionDiagnosticCodes`.
- `ReferenceCompiler.Compile(TreeDocument, NodeRegistry, ReferenceCompilerOptions, ISubtreeResolver)`
  and `Compile(SubtreeExpansion, NodeRegistry, ReferenceCompilerOptions)`.
- `BehaviorTreeSource.FromJson(string, string, ISubtreeResolver)`,
  `BehaviorTreeSource.SetContent(..., IReadOnlyDictionary<NodeId, string> subtreeNodeSourceIds)`.

`ExpectedPublicAbiV2.txt` is unchanged and `AIBT.CodeGen.ContractTests` passes (the node ABI is not
touched).

### Tests

New classes:

| Class | What it proves |
|---|---|
| `SubtreeFormatTests` | v3 fixture round-trip; every v1/v2 fixture still round-trips byte-identically; exact member order; `interface`/`subtree` rejected below v3; malformed shapes → `AIBT1004`. |
| `SubtreeValidatorTests` | One negative per `AIBT2047`–`AIBT2050` (plus variants) and catalog registration. |
| `SubtreeExpanderTests` | Identity over all package trees (≥ 40); renaming, constants, nesting, twice-used subtree; hand-copy equivalence (same source → identical content hash; separate source → only `SourcePath` differs); one negative per `AIBT3020`–`AIBT3028`; related locations; `DirectorySubtreeResolver`. |
| `SubtreeHotReloadTests` | A parameter edit inside the subtree migrates both instances (5 nodes, state kept); retargeting one reference restarts only `b:root`. |
| `SubtreeManagedTests` | Managed program with per-instance private keys written through a `BlackboardKey` parameter; missing resolver → `AIBT3020`; `BehaviorTreeAgent` with `FromJson` + resolver; stored expanded content + sources compiles like the resolver path. |
| `SubtreeGoldenEquivalenceTests` | Both golden cases give the same result and trace on the subtree tree and on its hand-written expansion; the expansion is byte-equal to the hand copy. |
| `SubtreePlayModeTests` | A `BehaviorTreeAgent` runs a tree with the same subtree twice in Play Mode. |

Golden cases: `subtree-reactive-observer` (an observer outside the subtree aborts the running
subtree branch) and `subtree-twice-private-keys` (two instances keep separate private keys).

Runs (raw output in `runs/` of the acceptance folder):

| Run | Result |
|---|---|
| New classes, EditMode (`new-tests-final.txt`) | `SubtreeFormatTests` 25/25, `SubtreeValidatorTests` 11/11, `SubtreeExpanderTests` 26/26, `SubtreeHotReloadTests` 2/2, `SubtreeManagedTests` 4/4, `CanonicalTreeJsonTests` 20/20, `SubtreeGoldenEquivalenceTests` 3/3, `SemanticSliceBehaviorTests` 16/16 |
| PlayMode (`playmode-final.txt`) | `SubtreePlayModeTests` 1/1 |
| Regression of touched areas (`regression-1.txt`) | `ReferenceCompilerTests` 18/18, `TreeValidatorTests` 39/39, `DiagnosticCodeAndCatalogTests` 17/17, `ManagedAgentLayerTests` 24/24, `ManagedParameterValidationTests` 17/17, `BehaviorTreeAgentComponentTests` 5/5, `SemanticSliceCompilationTests` 15/15, `SemanticSliceBehaviorTests` 16/16, `McpTestingToolDispatcherTests` 8/8, `McpVerificationToolDispatcherTests` 23/23, `DocumentMigratorTests` 7/7, `BlackboardSchemaTests` 13/13, `TreeScopeSlotNeverWrittenValidatorTests` 5/5, `HotReloadStateMigrationTests` 21/21, `GeneratedArtifactContractTests` 4/6 |
| ABI (`abi-1.txt`) | `AIBT.CodeGen.ContractTests` 6/6 |
| dotnet harness outside Unity (`dotnet-harness/dotnet-test.txt`) | 64/64 (`SubtreeTestKit` + Format, Validator, Expander, HotReload tests compiled without `UNITY_EDITOR` against the Unity-built `AIBT.Runtime`/`AIBT.Authoring`, NUnit 3.14, .NET 8 roll-forward, `AIBT_PACKAGE_ROOT` set) |
| `Tools~/Verification/Verify-Static.ps1` (`verify-static-1.txt`) | passed (7 schemas, 181 work items) |

The two `GeneratedArtifactContractTests` failures are the known pre-existing host-layout failures
(`PackageInfo.FindForAssembly` returns null because AIBT lives under `Assets/`; recorded in P7-018,
P7-033, P7-041); the file is not touched by this change.

### Not run / not covered

- Full run of all assemblies (left to acceptance, as in previous tasks).
- Native executor, `NativeTreeAgent`, `TreeSimulation`, importer, production assets, graph editor,
  MCP/CLI: stages 2–4.
- The reference executor's golden case for private keys shows separation through
  `initialBlackboard` and the snapshot, because reference fixture nodes only use fixed-name keys;
  writes through renamed `BlackboardKey` parameters are proven in the managed and PlayMode tests.

### Deviations from the ADR and open questions

1. `TreeDocument.LatestFormatVersion` stays 2 (callers use it as "what the writer produces for new
   trees"); version 3 is `TreeDocument.SubtreeFormatVersion`.
2. Generated `bindings` values cannot contain `:` (binding-value grammar), so a subtree whose
   generated binding names a key that the expansion would rename to `r:key` reports `AIBT3026`
   instead of being renamed. Stage 2 should decide whether to widen the grammar.
3. The expansion is always format 2 (ADR §4.4's "format 1 if every input is format 1" cannot occur:
   a tree with references is format 3, and the expansion keeps v2-only members).
4. Constants of registered (unmanaged) blackboard types report `AIBT3026`.
5. `canonical-json-v1.md` got one sentence beyond the draft: a reference node's member order.
6. The behaviour-case runner indexes the whole directory of the case tree, so the subtree golden
   trees live in their own `Trees/Subtrees/` directory.
7. `BehaviorTreeImporter` is not wired (stage 2).

### Rework after the acceptance run (2026-11-19)

The orchestrator's full run on `d1e76ff` (raw output:
`C:\UnityProjects\_aibt_acceptance\P11-001\stage1-acceptance\full-run-d1e76ff.txt`) results:

- Runtime 815/815, Integration 229/229, BehaviorCases 65/65, NativeBurstDispatch 79/79,
  NativeTreeAgentBlackboard 33/33, NativeSharedBlackboard 36/36, CodeGen.ContractTests 6/6,
  PlayMode 2/2.
- Editor 715/718. Besides the known `ZeroCustomNodesReturnsExactlyThePhase1BuiltInCatalog`, two
  new failures:
  - `McpDocumentationGeneratorsTests.CommittedGeneratedFilesMatchAFreshRegeneration`:
    `Documentation~/generated/api-reference-authoring.md` was stale (118 public types; a fresh
    generation gives 129).
  - `McpDocumentationGeneratorsTests.GeneratedApiReferenceCoversEveryPublicMemberInAllFourAssemblies`:
    57 new public members of `AIBT.Authoring` were missing.

Stage 1 should have regenerated the API reference; the touched-classes runs did not include
`McpDocumentationGeneratorsTests`.

Fixes:

1. **Generated documentation.**
   - Regenerated with the package's own `AIBT/MCP/Regenerate Documentation` menu command, invoked
     through `EditorApplication.ExecuteMenuItem`; it writes to `Assets/AIBT-Documentation`. The
     generator and MCP code are unchanged.
   - Copied only the files that differed from `Documentation~/generated`, as in P10-011:
     - `api-reference-authoring.md` (118 → 129 public types; additions only, plus the header
       count);
     - `api-reference-agents.md` (two lines: `BehaviorTreeSource.FromJson(…, ISubtreeResolver)`
       and the 8-argument `SetContent`).
   - Identical, not touched: `api-reference-runtime/editor/mcp.md`, `anti-patterns.md`,
     `migrations.md`, `recipes.md`, `workflow-guide.md`.
   - Not copied: `node-catalog.md`. It differs only by the host project's sample nodes, since the
     menu command uses the project registry and the test uses the package registry; no node type
     was added.
   - The temporary `Assets/AIBT-Documentation` folder was removed.
2. **Expansion format version.** `SubtreeExpander.Expand` now builds the expanded document with a
   new `internal const int ExpandedFormatVersion = 2` instead of
   `TreeDocument.LatestFormatVersion`. The expansion stays format 2 even if the latest version for
   new trees changes. The value is the same (2), so bytes and hashes do not change:
   `SubtreeGoldenEquivalenceTests` still finds the expansion byte-equal to the hand-written
   format-2 copies.

Runs after the fixes (`stage1-acceptance/fix-tests.txt`):

| Class | Result |
|---|---|
| `McpDocumentationGeneratorsTests` | 13/13 |
| `SubtreeExpanderTests` | 26/26 |
| `SubtreeGoldenEquivalenceTests` | 3/3 |

### Acceptance run (orchestrator, 2026-11-19)

Full run of all nine assemblies on `7e614b8`, clean working tree, 14:29–14:42; no `No command
named` error in the Editor log before or after. Raw output:
`C:\UnityProjects\_aibt_acceptance\P11-001\stage1-acceptance\full-run-7e614b8.txt` (the first run,
on `d1e76ff`, is `full-run-d1e76ff.txt`).

| Assembly | Before stage 1 (`c68df77`) | `7e614b8` |
|---|---|---|
| `AIBT.Runtime.Tests` | 815/815 | 815/815 |
| `AIBT.Integration.Tests` | 224/224 | 229/229 |
| `AIBT.Editor.Tests` | 649/650 | 717/718 |
| `AIBT.BehaviorCases.Tests` | 65/65 | 65/65 |
| `AIBT.NativeBurstDispatch.Tests` | 79/79 | 79/79 |
| `AIBT.NativeTreeAgentBlackboard.Tests` | 33/33 | 33/33 |
| `AIBT.NativeSharedBlackboard.Tests` | 36/36 | 36/36 |
| `AIBT.CodeGen.ContractTests` | 6/6 | 6/6 |
| `AIBT.PlayMode.Tests` (PlayMode) | 1/1 | 2/2 |

The one failure is `McpToolDispatcherTests.ZeroCustomNodesReturnsExactlyThePhase1BuiltInCatalog`,
which predates this task. The hash comparison was recomputed from the raw dumps: all 409 + 57
baseline lines are present unchanged after stage 1; the 42 + 6 new lines belong to the new subtree
fixtures. Protected paths (`ExpectedPublicAbiV2.txt`, `package.json`, `Runtime/Execution`,
`Runtime/Compiled`, `Runtime/HotReload`, `Runtime/Simulation`, `Runtime/Integration`,
`Runtime/Trace`, `CodeGen~`, `Editor`, `MCP`, `MCP~`) have no change from `f02ad04` to `7e614b8`.

Stage 1 is accepted. Deviation 2 above (generated `bindings` values cannot contain `:`) was
decided by the owner the same day: see ADR "Owner decisions", item 9; it is done in stage 2.

## Stage 2 — native path, import, baking (2026-11-19)

Base: `feature/aibt-consumer-workflow` at `1073d07`, clean working tree. Editor 6000.6.9f1 (port
7810). Tests run with the async `Tools/Unity/Invoke-UnityTests.ps1` after
`Invoke-UnityRecompile.ps1`, touched classes only. Raw outputs:
`C:\UnityProjects\_aibt_acceptance\P11-001\stage2\` (hash dumps, `runs/`, the temporary dumpers).

### Step 0 — owner decision 9 (`bindings` values)

Checked before editing that nothing downstream relies on the narrow grammar of binding values:

- `GeneratedScopeCompiler.CompileBindings` uses a value only as a lookup key into the document's
  blackboard (`keyById`); access records store slot indices, never the string.
- `GeneratedCompiledProgramV2`, prebinding and `GenericNativeDispatchTranslatorV1` read the
  generated descriptor's binding IDs, not document values; `StableKeyId` comes from key
  definitions.
- Canonical JSON and the semantic hash write the value as a JSON string; `:` and capital letters
  need no escaping.
- `CodeGen~` emits descriptor binding IDs only; MCP `McpAuthoringJson` and `DocumentMigrator`
  round-trip through `NodeBindingMap`.
- The JSON reader already read values with the key ID grammar (`IdValue`) before `NodeBindingMap`
  rejected them.

Result: no dependency; no change to the compiled program format, ABI v2 or the code generator.
Changes: `NodeBindingMap` accepts a value that matches the blackboard key ID grammar or the member
grammar (a strict union: everything valid before stays valid); binding IDs keep the member grammar;
`tree.schema.json` (`bindings.additionalProperties` = `id` or `bindingId`) and one sentence of
`agent-shared-blackboard-v1.md`; the expander's `AIBT3026` check for generated bindings is removed,
so `RenameBindings` renames them like any other key use. No existing test referenced that check.

### What was done (stage 2)

1. **Generated V2 compiler** (`GeneratedCompiledProgramV2.cs`): two additive overloads,
   `Compile(document, descriptors, registeredTypes, options, ISubtreeResolver)` and
   `Compile(SubtreeExpansion, descriptors, registeredTypes, options)`. The resolver overload
   validates the parent and every subtree against the generated registry and the registered types
   (new `SubtreeExpander.Expand(..., RegisteredBlackboardTypeCatalog)`), then compiles the expansion
   with each inner node's debug-map source path set to its subtree file. A document without
   references and interface takes exactly the old path. The old overload sends a document with
   references or an interface through the expander without a resolver, so it reports `AIBT3020`
   (ADR §6).
2. **Import** (`BehaviorTreeImporter.cs`, `[ScriptedImporter(3, "aibt")]`, bumped from 2): a tree
   with references or an interface is expanded against the project's trees
   (`Editor/Graph/ProjectTreeIndex.cs`, new) and the project node catalog
   (`ProjectNodeCatalog.Discover()`), and stored with the 8-argument `BehaviorTreeSource.SetContent`
   (canonical JSON of the expansion plus per-node subtree sources). The import declares
   `DependsOnSourceAsset` on every subtree file it read (found or ambiguous candidates),
   `DependsOnCustomDependency("AIBT/Subtrees/TreeId/<id>")` for every tree ID it looked up, and
   `"AIBT/Subtrees/NodeRegistry"`. `ProjectTreeIndex` registers each tree-ID dependency as a hash of
   the paths that declare it, re-registers when tree files change (postprocessor, after assembly
   reload), and only then schedules one `AssetDatabase.Refresh`. If the expansion fails, the source
   keeps the raw JSON and the status is `Errors` with the expansion diagnostics.
   `TreeCatalogEntry` describes the expanded tree (status, node count).
3. **Baking** (`ProductionTreeAssetBaker.cs`): new `Create(document, sourceId, catalogType,
   ISubtreeResolver, out diagnostics)`; the existing overload passes the project index when the
   tree uses subtrees (before, a reference node has no node type and the bake's catalog check
   threw `NullReferenceException`). The asset records
   the GUIDs of its subtree files (`ProductionTreeAsset._subtreeSourceGuids`, ordered by tree ID)
   and, per ADR §4.5, its source hash is the plain SHA-256 of the source bytes for a tree without
   subtrees (as before) and otherwise SHA-256 of
   `"aibt.production-tree-sources.v1\n" + parentGuid + " " + sha(parent) + "\n"` followed by one
   line `treeId guid sha|missing` per subtree.
4. **Staleness** (`ProductionTreeAssetStaleness.cs`): an asset with subtree sources compares that
   hash as the project resolves the subtrees now (an unresolvable subtree counts as a change); the
   postprocessor also runs when a tree file is deleted or moved and re-evaluates assets prepared from
   subtrees. Assets without subtrees behave exactly as before.
5. **Native golden harness** (`Tests/Integration/NativeRuntime/NativeBehaviorCaseAdapter.cs`, test
   code only): the factory expands subtrees as the reference adapter does; the adapter traces
   composite results and composite aborts from the machine for every program (before: only for
   retaining programs; others traced the last leaf status as the composite's result and no composite
   abort). The two stage-1 subtree cases need both (an inverter over a failing check; an observer
   aborting the subtree's sequence). The ten existing cases pass unchanged.

### New tests (stage 2 acceptance items of ADR §7)

| ADR §7 stage 2 item | Test |
|---|---|
| Step 0: native node bound to an instance's private key and to an interface key given a constant | `SubtreeNativeTests.ResolverOverload_ExpandsBothInstances_AndBindsNativeNodesToRenamedKeys` (access records target `a:armed`, `b:armed`, `live-target`, `b:gate`; the constant is `b:gate`'s default), `NativeTreeAgent_RunsASubtreeUsedTwice_WithPrivateAndConstantKeys` |
| Step 0: negative test for an invalid value | `SubtreeBindingGrammarTests` (`NodeBindingMap` rejects `""`, `a b`, `-x`, `:x`, `a/b`, `a#b`, an overlong non-member ID; binding IDs keep the member grammar; JSON with `a b` / `-x` gives `AIBT1004` (`SchemaViolation`) at `/nodes/emit/bindings/live-target`; a binding to `a:armed` round-trips byte-identically) |
| Generated V2 overload | `SubtreeNativeTests`: resolver overload = `Compile(SubtreeExpansion)` (same content hash); the saved expansion compiled without a resolver has the same canonical semantic hash, config and default blobs (only debug-map source paths differ); the old overload gives `AIBT3020`; a tree without subtrees gives identical bytes through either overload |
| Native vs reference executor on the stage-1 trees | `NativeExecutionEquivalenceTests.EveryGoldenBehaviorCasePassesTheNativeAdapter` + `subtree-reactive-observer`, `subtree-twice-private-keys` (all four golden policies) |
| `NativeTreeAgent` with a subtree used twice | `SubtreeNativeTests.NativeTreeAgent_RunsASubtreeUsedTwice_WithPrivateAndConstantKeys` (only instance b emits while the parent key is 0; both emit at 1), `NativeTreeAgent_PrivateKeysBelongToEachInstance` |
| `TreeSimulation` group: inputs and an external action inside a subtree used twice | `TreeSimulationActionTests.SubtreeUsedTwice_SharesTheGroupsChannels_AndCompletesPerInstance` (with and without jobs: both instances read the one input row and start actions on one channel; completing b then a gives one `JobDone` each, the root succeeds only after both) |
| Editing a subtree re-imports the parent and re-bakes the asset | `SubtreeImportAndBakeTests.ImportedParent_StoresTheExpandedTree_AndIsReimportedWhenTheSubtreeChanges`, `BakedAsset_RecordsTheSubtreeFile_AndIsRePreparedWhenItChanges` |
| A second file with the same tree ID gives `AIBT3021` in dependants | `SubtreeImportAndBakeTests.SecondFileWithTheSameTreeId_GivesAIBT3021_InDependants_UntilItIsRemoved` (imported source `Errors` with `AIBT3021`; `TryPrepareAgain` fails with `AIBT3021`; removing the file repairs both) |
| Native hot reload of an edited subtree | `SubtreeNativeHotReloadTests` (native lifecycle machine: a parameter edit migrates both instances, 5 nodes, and the running leaf resumes with `Tick`, no `Enter`; retargeting one reference migrates 3 nodes, resets the 2 nodes of that branch, and the other instance resumes) |
| Same subtree ×16 (deterministic numbers only) | `SubtreeNativeTests.SubtreeUsedOnceAndSixteenTimes_ProgramGrowsLinearly` (numbers below) |

The import tests work in `Assets/AibtP11SubtreeImportTest/` and delete it.

### Same subtree ×1 and ×16 (deterministic numbers, no timing)

Parent: a memory sequence with N references to one subtree (a memory sequence over a native
condition bound to a private key and a native action bound to an interface key that the parent
binds to its own key). Compiled with the generated V2 resolver overload
(`SubtreeNativeTests.SubtreeUsedOnceAndSixteenTimes_ProgramGrowsLinearly`; raw line:
`stage2/subtree-scale.txt`).

| Uses | Program nodes | Blackboard slots | Instance node memory | Default-value bytes |
|---|---|---|---|---|
| 1 | 4 | 2 | 12 B | 8 B |
| 2 | 7 | 3 | 20 B | 12 B |
| 16 | 49 | 17 | 132 B | 68 B |

Each use adds the subtree's 3 nodes, 1 private-key slot, 8 B of node memory and 4 B of defaults;
the test asserts this linearity. The expansion duplicates nodes per use (ADR risk "same subtree
×16"); nothing is shared between instances, and nothing else grows.

### Changed expectations of existing tests

No assertion of an existing test changed. Test-code changes besides new files:

| File | Change | Reason |
|---|---|---|
| `NativeExecutionEquivalenceTests.EveryGoldenBehaviorCasePassesTheNativeAdapter` | Two new `TestCase`s (`subtree-reactive-observer`, `subtree-twice-private-keys`) | ADR §7 stage 2: native/reference equivalence on the stage-1 trees. |
| `NativeBehaviorCaseAdapter.cs` (native golden harness) | The factory expands subtrees; composite results and composite aborts are read from the machine for every program, not only retaining ones | The previous approximation (last leaf status as the composite's result; no composite abort) gave wrong traces for an inverter over a failing check and for an observer aborting a sequence. The machine was right (the root reached the second instance); only the harness traced it wrongly. All ten existing cases still pass under all four policies. |

`BehaviorTreeImporter` moved from `[ScriptedImporter(2, "aibt")]` to version 3, so every `.aibt`
file re-imports once (the three sample trees were re-imported without errors). The generated V2
dump (below) shows four format-3 fixture files whose failure codes changed; no test asserts them.

### Added public API (additions only; no existing signature changed)

- `SubtreeExpander.Expand(TreeDocument, ISubtreeResolver, NodeRegistry, ValidationOptions,
  RegisteredBlackboardTypeCatalog)`.
- `GeneratedCompiledProgramV2Compiler.Compile(TreeDocument, IEnumerable<GeneratedNodeDescriptor>,
  RegisteredBlackboardTypeCatalog, ReferenceCompilerOptions, ISubtreeResolver)` and
  `Compile(SubtreeExpansion, IEnumerable<GeneratedNodeDescriptor>, RegisteredBlackboardTypeCatalog,
  ReferenceCompilerOptions)`.
- `ProductionTreeAssetBaker.Create(TreeDocument, string, Type, ISubtreeResolver, out
  DiagnosticCollection)`.

Behaviour of existing public members that changed for trees with subtrees only:
`GeneratedCompiledProgramV2Compiler.Compile` without a resolver reports `AIBT3020` (before:
unrelated validation errors such as `AIBT2042`); `ProductionTreeAssetBaker.Create` without a
resolver resolves through the project's trees (before: `NullReferenceException`);
`ProductionTreeAssetStaleness.RefreshAll` also matches assets by their subtree files. Internal
additions: `ProductionTreeAsset._subtreeSourceGuids` (serialized, hidden) with
`SubtreeSourceGuids`/`RecordSubtreeSources`; `ProjectTreeIndex`, `RecordingSubtreeResolver`;
`TreeCatalogEntry.Expansion` and a `Read` overload; `ProductionTreeAssetBaker.CurrentSourceHash`;
`ProductionTreeAssetStaleness.RefreshAll(…, bool)`.

Generated reference regenerated with `AIBT/MCP/Regenerate Documentation` (through
`EditorApplication.ExecuteMenuItem`, output `Assets/AIBT-Documentation`, removed afterwards); copied
only the two files that differed: `api-reference-authoring.md` (3 new methods) and
`api-reference-editor.md` (the new `Create` overload and the importer's summary). `node-catalog.md`
was not copied (it differs only by the host project's sample nodes, as in stage 1).
`ExpectedPublicAbiV2.txt` is unchanged.

### Hashes before and after (stage 2)

The stage-1 dumpers (category `P11BaselineDump`) were restored temporarily, together with a third
one for the generated V2 compiler (every package tree × the generated registry of the project's
nodes × two policies, through the old overload) and a trace dumper used to diagnose the native
golden harness; all four were deleted before commit (copies in `stage2/`). Runs: before any change
(`1073d07`), after step 0, and after all of stage 2.

| Dump | Lines | Changed after step 0 | Changed after stage 2 |
|---|---|---|---|
| Package trees × registries × policies (`hashes-editor-*.txt`) | 451 | **0** | **0** |
| Semantic-slice trees (`hashes-semanticslice-*.txt`) | 63 | **0** | **0** |
| Generated V2, package trees × 2 policies (`hashes-v2-*.txt`) | 126 (14 OK compiled) | 8 | the same 8 (after-step-0 and after-stage-2 files are identical) |

The 8 V2 lines are failure codes of four format-3 subtree fixtures that the old overload cannot
compile: `alert-raise`, `inverted-check`, `reactive-parent` `AIBT2021,AIBT2042` → `AIBT2021` (binding
values in the key ID grammar are no longer rejected, step 0; the fixture nodes are not generated
nodes); `twice-parent` `AIBT2042` → `AIBT3020` (once its binding values pass, the unresolved
reference is reported, ADR decision 5). Every OK
line (content and semantic hashes) is unchanged; no test asserts these codes.

### Runs (stage 2)

Raw output in `stage2/runs/` of the acceptance folder.

| Run | Result |
|---|---|
| New classes (`new-tests-1.txt`, then `regression-1.txt`) | `SubtreeNativeTests` 7/7, `SubtreeNativeHotReloadTests` 2/2, `SubtreeBindingGrammarTests` 19/19, `SubtreeImportAndBakeTests` 3/3 |
| Regression of touched areas (`regression-1.txt`) | `SubtreeExpanderTests` 26/26, `SubtreeFormatTests` 25/25, `SubtreeValidatorTests` 11/11, `SubtreeHotReloadTests` 2/2, `SubtreeManagedTests` 4/4, `ProductionTreeAssetStalenessTests` 6/6, `GraphConsumerWorkflowTests` 4/4, `ProductionConsumerWorkflowTests` 12/12, `GraphEditorWorkflowTests` 15/15, `AgentConsumerToolsTests` 14/14, `McpSimulationToolsTests` 3/3, `GeneratedScopeCompilerTests` 14/14, `McpAuthoringJsonTests` 2/2, `DocumentMigratorTests` 7/7, `NativeExecutionEquivalenceTests` 14/14, `NativeTreeAgentTests` 16/16 |
| Regression, second batch (`regression-2.txt`) | `AIBT.CodeGen.ContractTests` 6/6, `McpDocumentationGeneratorsTests` 13/13, `SubtreeGoldenEquivalenceTests` 3/3, `SemanticSliceBehaviorTests` 16/16, `BehaviorTreeAgentComponentTests` 5/5, `TreeSimulationActionTests` 46/46 (with the new subtree case) |
| PlayMode (`playmode-1.txt`) | `SubtreePlayModeTests` 1/1 |
| Hash dumps (`hash-dump-final.txt`) | category `P11BaselineDump` 5/5 |
| `Tools~/Verification/Verify-Static.ps1` (`verify-static-1.txt`, `verify-static-2.txt`) | passed (7 schemas, 181 work items) |

`new-tests-2.txt` is an earlier failing run (import tests with test-assembly nodes, see open
question 1; native golden cases before the harness fix); both were fixed before `regression-1.txt`.
After deleting the dumpers the project recompiled without errors; the Console shows only the
pre-existing URP errors (open question 5). The parent repository's `git status --short` is the
same before and after the tests (`parent-status-before.txt`, `parent-status-after.txt`: no
difference).

### Not run / not covered (stage 2)

- Full run of all assemblies (left to acceptance, as in previous tasks).
- The dotnet harness outside Unity was not re-run (the stage-2 tests use Unity's native containers
  and the asset database).
- Graph editor (palette, reference node view, inspector, windows, `GraphLiveDebugger`,
  `MatchesProgram`), MCP and CLI: stages 3–4.
- Timing: only the deterministic ×1/×16 numbers above.

### Deviations and open questions

1. **Import with nodes from test or Editor assemblies.** The importer expands against
   `ProjectNodeCatalog.Discover()`, which leaves out test assemblies (as the rest of the Editor
   tooling does). A tree with subtrees whose node types are unknown there imports with status
   `Errors` (`AIBT2021`) and its raw JSON, while a tree without subtrees shows `Needs registry`. Shipped
   catalogs (player assemblies) are not affected; the import tests use built-in and standard native
   nodes for this reason, and the bake test uses the test consumer catalog through
   `ProductionTreeAssetBaker`, which uses the catalog it is given.
2. **The tree-ID index covers `Assets/` only** (not `Packages/`), skipping hidden folders, `~`
   folders and `cvs`. A subtree that lives only in a package is not found (`AIBT3020`).
3. **`ProductionTreeAsset.cs`**: besides the serialized field, two internal members read and write
   it; no public member. An asset saved again after this change gets the (empty) field in its YAML.
4. **Import-time policy.** The importer expands with a policy that admits every node domain and scope
   (structural validation only); the managed agent and the native bake compile the expansion under
   their own policies, so policy errors are reported there, as for a tree without subtrees.
5. The Editor console shows `Release of invalid GC handle` / `Failed to load VirtualArtifacts`
   errors from the URP global settings re-import; they predate this work (1520 earlier occurrences
   in `Logs/Editor.log`) and are not from AIBT.

### Acceptance run (orchestrator, 2026-11-19, stage 2)

Full run of all nine assemblies on `c02b42b`, clean working tree, 15:45–15:58; the Editor-log
counters did not move during the run (`No command named` 0; main-thread timeouts 9 before and
after). Raw output:
`C:\UnityProjects\_aibt_acceptance\P11-001\stage2-acceptance\full-run-c02b42b.txt`.

| Assembly | Stage 1 (`7e614b8`) | `c02b42b` | New tests |
|---|---|---|---|
| `AIBT.Runtime.Tests` | 815/815 | 815/815 | — |
| `AIBT.Integration.Tests` | 229/229 | 240/240 | `SubtreeNativeTests` 7, two native golden cases, two `TreeSimulationActionTests` subtree cases |
| `AIBT.Editor.Tests` | 717/718 | 741/742 | `SubtreeBindingGrammarTests` 19, `SubtreeNativeHotReloadTests` 2, `SubtreeImportAndBakeTests` 3 |
| `AIBT.BehaviorCases.Tests` | 65/65 | 65/65 | — |
| `AIBT.NativeBurstDispatch.Tests` | 79/79 | 79/79 | — |
| `AIBT.NativeTreeAgentBlackboard.Tests` | 33/33 | 33/33 | — |
| `AIBT.NativeSharedBlackboard.Tests` | 36/36 | 36/36 | — |
| `AIBT.CodeGen.ContractTests` | 6/6 | 6/6 | — |
| `AIBT.PlayMode.Tests` (PlayMode) | 2/2 | 2/2 | — |

The one failure is `McpToolDispatcherTests.ZeroCustomNodesReturnsExactlyThePhase1BuiltInCatalog`,
which predates this task. Every added test is accounted for (11 in Integration, 24 in Editor).

The hash comparison was recomputed from the raw dumps in `stage2/`: the 451-line and 63-line dumps
are byte-identical before the stage, after step 0, after stage 2, and to the stage-1 "after"
dumps; the generated-V2 dump (126 lines) differs in exactly the 8 failure-code lines listed above,
and its 14 OK lines are unchanged. Protected paths (`ExpectedPublicAbiV2.txt`, `package.json`,
`Runtime/Execution`, `Runtime/Compiled`, `Runtime/HotReload`, `Runtime/Simulation`, `Runtime/Trace`,
`CodeGen~`, `MCP`, `MCP~`, `ProductionTreeHost.cs`, `NativeTreeAgent.cs`,
`ProductionTreeScheduler.cs`) have no change from `1073d07` to `c02b42b`. The diff was read in
full; the changes to existing code are additive overloads and one hidden serialized field.

Stage 2 is accepted. Open questions 1 (import status of a subtree parent whose node types are not
in the project catalog) and 2 (tree-ID index covers `Assets/` only) go to the owner with the
stage-3 go-ahead; stages 3 and 4 are not started.
