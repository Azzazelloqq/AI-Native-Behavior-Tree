# P11-001 evidence: subtrees

Decision: `Documentation~/decisions/ADR-P11-001-subtrees.md` (accepted 2026-11-19, AIBT-049, all §8
recommendations). All four stages of ADR §7 and the naming-policy fix are accepted; the owner closed
the item on 2026-11-20 (section "Closing" at the end).

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

## Stage 3 — graph editor (2026-11-19)

Base: `feature/aibt-consumer-workflow` at `d8b34a8`, clean working tree. Editor 6000.6.9f1 (port
7810). Tests run with the async `Tools/Unity/Invoke-UnityTests.ps1` after
`Invoke-UnityRecompile.ps1`, touched classes only. Raw outputs and visual evidence:
`C:\UnityProjects\_aibt_acceptance\P11-001\stage3\` (`runs/` for test output).

### Step 0 — the two approved stage-2 follow-ups (`8fce1aa`)

1. **Import status `Needs registry`** (open question 1 of stage 2). A tree with subtrees whose only
   expansion errors are unknown node types (`AIBT2021`) now imports with status `Needs registry`,
   like a tree without subtrees; any other expansion error keeps `Errors`
   (`TreeCatalogEntry.OnlyUnknownNodeTypes`, internal). The source still keeps the raw JSON in both
   cases.
2. **Tree-ID index covers `Packages/`** (open question 2). `ProjectTreeIndex.Roots()` is `Assets/`
   plus every registered package with a folder on disk (`Packages/<name>/...` asset paths, read from
   the package's resolved folder); the same skips apply (hidden, `~` and `cvs` folders). Files of
   immutable packages (registry, git, built-in) are listed once per resolved folder; embedded and
   local packages are read on every scan. A tree ID declared in `Assets/` and in a package gives
   `AIBT3021` like any other duplicate.

### What was done (stage 3, ADR §4.7 and §7)

All of it is Editor code (`Editor/Graph/**`, `Editor/Trace/TraceTimelineWindow.cs`, two style
sheets); nothing outside the Editor assembly changed. Every edit goes through the window's one edit
history (`ApplyEdit`, one Unity undo step each).

1. **Palette and picker.** The node palette (`NodeSearchProvider`) lists a "Subtree…" entry first
   when the window asks for it (not when inserting into an edge). Choosing it opens a tree picker
   (`SubtreePickerProvider`) over the project's trees from the tree catalog, without the open tree.
   A canvas context-menu item "Add Subtree…" does the same.
2. **Reference node.** A reference is one card (`BehaviorTreeNode`) with a SUBTREE badge, the
   subtree's tree ID and name, and one row per interface key (`key → parent key`, `key = constant`
   or `key: not connected`). It accepts no children. Double-click opens the subtree.
3. **New references** get a short ID from the subtree's name (no `:`), and same-named parent keys of
   the same type are connected at once. Adding a reference to a format-1/2 tree raises it to format 3.
4. **Inspector for a reference**: subtree path, interface version, the expansion's problems for this
   reference, the key map (per interface key: a parent Tree key of the same type, a constant for
   Bool/Int32/Int64/Float32/Float64, or not connected), "Create parent key" for a missing key
   (adds the key and connects it in one undo step), "Connect same-named keys", Open Subtree, Unpack.
5. **Diagnostics on the reference card.** The open tree is validated through the expansion (as at
   import). A problem inside a subtree, or an `r:n` node, is shown on the outermost reference that
   uses it; the problems list appends "(in <document>)" for problems located in a subtree file.
6. **Open Subtree** opens the subtree in its own graph window (reused for the same file and
   instance) with a breadcrumb back to the parent (`Boss Fight › Strike (instance left)`). The
   breadcrumb and the instance prefix are local view state of the window; the layout format is
   unchanged and the reference is an ordinary node in the parent's layout.
7. **Extract to Subtree…** (context menu on a non-root node): asks for a new file, moves the node and
   its descendants into a new tree (tree ID `<parent tree ID>.<slug>`, interface = the Tree keys
   they use) and puts a reference with the node's label in their place. The new file is kept pending
   in the window and written by the next Save, before the parent; Undo therefore leaves no file
   behind. An undone extraction's file name can be used again.
8. **Unpack Subtree** replaces a reference by its expansion (nodes `r:n` with labels
   "reference / inner", private keys `r:key` and constants added as Tree keys); one undo step. A
   name collision with an existing node or key is refused with `AIBT3027`.
9. **Live debugging.** `MatchesProgram` compares the running program with the graph's expansion
   (the window's own subtree resolver). In the parent, every `r:n` program node lights the reference
   card `r` (running wins, otherwise the strongest flash). A subtree window opened from a reference
   gets that instance's prefix, attaches to the parent's agent and lights only its own instance's
   nodes; its blackboard panel shows the instance's live values (interface keys through the
   binding, private keys as `r:key`).
10. **Trace timeline**: runtime nodes map to cards the same way, and node labels come from the
    program's debug map, so a subtree node reads "Left strike / Hit". A line "Active: …" lists the
    active nodes at the scrubbed step; the diagnostics list names the node by label.
11. **Edits keep the subtree interface** (`d6f073f`): `GraphDocumentEdits.Rebuild` and
    `SemanticEditOperations` rebuilt a document without its `interface`, and `Copy` dropped a
    reference's `subtree`; both are kept now (see deviation 1).

Demo trees for the evidence: `Tests/Editor/Graph/Fixtures/SubtreeDemo/` (`boss-fight.aibt` uses
`strike.aibt` twice; checked by `DemoFixtures_BossFightExpandsToTwoStrikeInstances`). The Play Mode
agent was created by an Editor eval in Play Mode only; no scene or other project file was changed.

### New tests (stage 3 acceptance items of ADR §7)

| ADR §7 stage 3 item | Test |
|---|---|
| Step 0: `Needs registry` | `SubtreeImportAndBakeTests.SubtreeWithNodeTypesTheProjectCatalogDoesNotKnow_ImportsAsNeedsRegistry`, `RealExpansionErrorsNextToUnknownNodeTypes_StayErrors`, `OnlyUnknownNodeTypes_IsTrueOnlyWhenEveryErrorIsAIBT2021` |
| Step 0: `Packages/` in the index | `ProjectTreeIndexPackageTests` (4: package asset paths and skips, `AIBT3021` across `Assets/` and a package, immutable folders listed once, default roots) |
| Palette entry and picker | `SubtreeGraphEditorTests.Palette_ListsSubtreeFirstOnlyWhenAsked`, `Picker_ListsProjectTreesWithoutTheOpenTree` |
| Reference card | `ReferenceCard_ShowsSubtreeNameBadgeAndOneRowPerInterfaceKey` |
| Add reference, key map, create parent key (with undo/redo) | `AddReference_UsesAShortIdConnectsSameNamedKeysAndUndoes`, `KeyMap_ConstantAndDisconnectEditsAreUndoable`, `CreateMissingParentKey_AddsTheKeyAndConnectsItInOneStep` |
| Extract and Unpack (with undo/redo) | `Extract_ReplacesTheNodesByAReference_UndoLeavesNoFile_SaveWritesTheSubtree`, `Unpack_ReplacesTheReferenceByItsExpansion_AndUndoes` |
| Diagnostics on the reference | `ReferenceProblems_AreBadgedOnTheReferenceCard` |
| Open Subtree and breadcrumb | `OpenSubtree_OpensItsOwnWindowWithABreadcrumbBack` |
| Live debugger: reference in the parent, one instance per subtree window | `LiveDebugger_HighlightsTheReferenceInTheParentAndOneInstanceInEachSubtreeWindow` |
| Trace timeline labels | `TraceTimeline_MapsExpandedNodesToTheReferenceAndShowsTwoPartLabels` |
| Demo fixtures | `DemoFixtures_BossFightExpandsToTwoStrikeInstances` |

### Visual evidence

In `stage3/` of the acceptance folder; every image and still was looked at.

| File | Shows |
|---|---|
| `01-parent-subtree-used-twice.png` | Boss Fight with two references to Strike (Left strike, Right strike), each with its key row `hits → hits` |
| `02-inspector-key-map.png` | Left strike selected: subtree path, interface version, key map `hits` → "Parent key hits", Open Subtree / Unpack |
| `03-subtree-window-breadcrumb.png` | Strike opened from Left strike in its own window; breadcrumb `Boss Fight › Strike (instance left)` |
| `04-reference-diagnostic.png` | `hits` of Right strike disconnected: `AIBT3025` badge on the card, in the inspector and in the problems list (undone afterwards) |
| `05a-before-extract.png`, `05b-after-extract.png` | Finisher and its two children replaced by the reference "Finisher" to the pending `finisher.aibt` ("written on Save"); undone, never saved |
| `06a-before-unpack.png`, `06b-after-unpack.png` | Right strike unpacked into `Right strike / Combo` and its four children, private key `right:swings` added; undone |
| `07-live-parent.mp4` (+ `.stills/`, every 0.5 s) | Play Mode, parent window: the reference card lights while its instance runs (e.g. stills `t004.5s`: Right strike), the live `hits` value grows |
| `08-live-left-instance.mp4`, `09-live-right-instance.mp4` (+ stills) | The two Strike windows: each lights only its own instance's nodes; `swings` shows each instance's own value |
| `10-live-simultaneous-samples.txt` | 24 samples of the running nodes of all three windows at the same instant: while the parent runs `left`, only the left window has running nodes, and the same for `right` |

The clips are separate recordings (one window each, no video tool to compose them); the samples
file is the simultaneous evidence.

### Changed expectations of existing tests

None. The test diff from `d8b34a8` only adds files and test methods (`SubtreeImportAndBakeTests`
gains three tests and two constants).

### Added public API

None. Every new member is internal (`SubtreeGraphEdits`, the window's subtree commands,
`TreeCatalogEntry.OnlyUnknownNodeTypes`, `ProjectTreeIndex.ScanRoot`, …). The generated
`api-reference-editor.md` was regenerated: only two summaries changed (the importer's, for
`Needs registry`, and `BehaviorTreeNode`'s, for the reference card). `ExpectedPublicAbiV2.txt`,
`package.json`, the specifications and the protected runtime folders are unchanged.

### Runs (stage 3)

Raw output in `stage3/runs/`.

| Run | Result |
|---|---|
| Step 0 (`step0-1.txt`) | `SubtreeImportAndBakeTests` 6/6, `ProjectTreeIndexPackageTests` 4/4 |
| Development runs (`dev-01` … `dev-03`) | failing early runs of `SubtreeGraphEditorTests` (fixture JSON, absolute subtree source IDs `AIBT3010`), fixed; `dev-03` 12/12 |
| Affected classes (`dev-04-affected.txt`) | 29 classes, all passed except `McpDocumentationGeneratorsTests` 11/13 (stale generated reference, a new public property), fixed in `dev-05` |
| After regenerating the reference (`dev-05-docs-regen.txt`) | `McpDocumentationGeneratorsTests` 13/13, `SubtreeGraphEditorTests` 12/12, `BehaviorTreeGraphAdapterTests` 15/15 |
| Extract label, demo fixtures, path reuse (`dev-06`, `dev-07`) | `SubtreeGraphEditorTests` 13/13, `BehaviorTreeGraphAdapterTests` 15/15, `GraphEditorWorkflowTests` 15/15 |
| Final, code at `c40a6ab` (`final-1.txt`) | `SubtreeGraphEditorTests` 13/13, `GraphEditorWorkflowTests` 15/15, `GraphConsumerWorkflowTests` 4/4, `SubtreeImportAndBakeTests` 6/6, `ProductionTreeAssetStalenessTests` 6/6, `McpDocumentationGeneratorsTests` 13/13, `ProjectTreeIndexPackageTests` 4/4, `BehaviorTreeGraphAdapterTests` 15/15, `GraphEditSessionTests` 6/6, `SemanticEditOperationsTests` 4/4, `TraceTimelineWindowTests` 4/4, `SubtreeExpanderTests` 26/26, `McpAuthoringToolDispatcherTests` 20/20 |
| `Tools~/Verification/Verify-Static.ps1` (`verify-static-1.txt`; `verify-static-2.txt` after these docs) | passed (7 schemas, 181 work items) |

`dev-04-affected.txt` in full: `SubtreeGraphEditorTests` 12/12, `GraphEditorWorkflowTests` 15/15,
`GraphConsumerWorkflowTests` 4/4, `SubtreeImportAndBakeTests` 6/6,
`ProductionTreeAssetStalenessTests` 6/6, `ProjectTreeIndexPackageTests` 4/4,
`BehaviorTreeGraphAdapterTests` 15/15, `GraphEditSessionTests` 6/6, `SemanticEditOperationsTests`
4/4, `SemanticEditTransactionTests` 3/3, `SemanticPatchTransactionTests` 4/4,
`TraceTimelineWindowTests` 4/4, `BehaviorTreePreviewWindowTests` 2/2,
`DiagnosticGraphPresentationTests` 3/3, `LayoutSemanticIsolationTests` 2/2,
`LargeGraphPerformanceTests` 1/1, `ReferencePreviewParityTests` 2/2, `McpAuthoringJsonTests` 2/2,
`McpAuthoringToolDispatcherTests` 20/20, `McpMigrationToolDispatcherTests` 4/4,
`McpVerificationToolDispatcherTests` 23/23, `McpPermissionEnforcerTests` 17/17,
`McpCustomToolsToolDispatcherTests` 7/7, `CustomMcpToolProviderDiscoveryTests` 4/4,
`AgentConsumerToolsTests` 14/14, `ProjectManifestQueryTests` 3/3, `SubtreeManagedTests` 4/4,
`SubtreeExpanderTests` 26/26, `McpDocumentationGeneratorsTests` 11/13.

The Editor recompiled without errors after every change; the parent repository's `git status --short`
is the same before and after (`runs/parent_status_before.txt`, `runs/parent_status_after.txt`); the Console has no compiler warning from
the touched files and no new error (only the pre-existing `Release of invalid GC handle` /
`Failed to load VirtualArtifacts` errors, stage-2 open question 5).

### Not run / not covered (stage 3)

- Full run of all nine assemblies (left to acceptance) and PlayMode tests (no PlayMode code changed).
- Native agents in a subtree window: the code path is shared (`ShowLive(NativeTreeAgent)` uses the
  same mapping), but only the managed agent was exercised (test and clip).
- Real packages: the `Packages/` index was tested with package roots mapped to temporary folders
  (adding a package would change the project's manifest and lock file); no installed package with
  trees was used, and a parent re-import after a package update was not tried.
- MCP and CLI (stage 4, not started).

### Deviations and open questions

1. **Interface kept by edits, also for MCP.** Before, any graph or MCP authoring edit of a format-3
   tree (`SemanticEditOperations`, `GraphDocumentEdits.Rebuild`) dropped its `interface`, and
   copying a reference dropped its `subtree`. The fix is in shared Editor code, so MCP authoring edits
   of format-3 trees now keep the interface too (no `MCP/**` file changed; MCP tests pass).
2. **Extract writes the new file on Save.** The subtree file is pending in the window until Save
   (written first, then imported, then the parent), so Undo/Redo never leave or need a file on disk;
   closing the window without saving discards it. The alternative (write at once) would leave an
   orphan file after Undo. Owner's choice if the other behaviour is wanted.
3. **Unpack keeps the format version** (a tree stays format 3 after its last reference is unpacked).
4. **An unknown node type in the parent masks reference errors at import**: parent validation fails
   first, so the import shows `Needs registry` with `AIBT2021` only (found in step 0; left as is).
5. **Tree picker and `Tests/`.** The picker lists the tree catalog, which leaves out `Tests/`, so the
   demo trees under `Tests/` cannot be picked (they resolve and open normally).
6. **Layout after Undo** (pre-existing, not changed): in a tree with a saved layout, Undo of an edit
   that placed new nodes leaves the layout marked changed (the window offers to save positions only).
   This happens for any added node (checked with a plain node), not only for Unpack/Extract.
7. **Running highlight on a reference card** is the same glow as on other cards, but the card's teal
   subtree border makes it less prominent; the active edge to it is clear.
8. **Test windows leak when a coroutine test fails** before its `finally` (the pattern of
   `GraphEditorWorkflowTests`); nine leaked windows from the failing development runs were closed by
   hand. Passing runs leave none.

### Acceptance run (orchestrator, 2026-11-19, stage 3)

Full run of all nine assemblies on `7794cb0`, clean working tree, 17:13–17:22; the Editor-log
counters did not move during the run (`No command named` 1, main-thread timeouts 13, before and
after; the one `No command named 'help'` entry is a single CLI call made before the run). Raw
output: `C:\UnityProjects\_aibt_acceptance\P11-001\stage3-acceptance\full-run-7794cb0.txt`.

| Assembly | Stage 2 (`c02b42b`) | `7794cb0` | New tests |
|---|---|---|---|
| `AIBT.Runtime.Tests` | 815/815 | 815/815 | — |
| `AIBT.Integration.Tests` | 240/240 | 240/240 | — |
| `AIBT.Editor.Tests` | 741/742 | 761/762 | `SubtreeGraphEditorTests` 13, `ProjectTreeIndexPackageTests` 4, three in `SubtreeImportAndBakeTests` |
| `AIBT.BehaviorCases.Tests` | 65/65 | 65/65 | — |
| `AIBT.NativeBurstDispatch.Tests` | 79/79 | 79/79 | — |
| `AIBT.NativeTreeAgentBlackboard.Tests` | 33/33 | 33/33 | — |
| `AIBT.NativeSharedBlackboard.Tests` | 36/36 | 36/36 | — |
| `AIBT.CodeGen.ContractTests` | 6/6 | 6/6 | — |
| `AIBT.PlayMode.Tests` (PlayMode) | 2/2 | 2/2 | — |

The one failure is `McpToolDispatcherTests.ZeroCustomNodesReturnsExactlyThePhase1BuiltInCatalog`,
which predates this task. Every added test is accounted for (20 in Editor).

Protected paths (`ExpectedPublicAbiV2.txt`, `package.json`, `Documentation~/specifications`,
`Runtime/Execution`, `Runtime/Compiled`, `Runtime/HotReload`, `Runtime/Simulation`,
`Runtime/Integration`, `Runtime/Trace`, `CodeGen~`, `MCP`, `MCP~`) have no change from `d8b34a8`
to `7794cb0`. The diff of the shared Editor code was read (`SubtreeGraphEdits.cs`,
`ProjectTreeIndex.cs`, `TreeCatalogEntry.cs`, `GraphLiveDebugger.cs`,
`BehaviorTreeGraphExecution.cs`, `GraphDocumentEdits.cs`, `SemanticEditOperations.cs`, the window
partials' changes to existing code); `BehaviorTreeGraphWindow.Subtrees.cs` (new, 873 lines) was
accepted on its tests and the visual evidence, not read line by line. The eight screenshots and
stills of the three clips were looked at and show what the table above says; the per-instance
`swings` values in the two subtree windows agree with the shared `hits` value at their ticks.

Stage 3 is accepted. Deviations 2 (Extract writes the new file on Save), 5 (the tree picker does
not list trees under `Tests/`) and 7 (the running glow is less prominent on a reference card) go
to the owner with the stage-4 go-ahead; stage 4 is not started.

## Stage 4 — MCP, CLI, user guide (2026-11-19)

Start: `6caf728`, clean submodule. Evidence outside the repository:
`C:\UnityProjects\_aibt_acceptance\P11-001\stage4\`.

### What was done (stage 4, ADR §4.7 and §7)

1. **`add_node` with a `subtree` object** (`MCP/Authoring/McpAuthoringJson.cs`,
   `McpAuthoringToolDispatcher.Subtrees.cs`): a node `{id?, subtree: {treeId, interfaceVersion?,
   keys?}, displayName?, description?}` adds a reference. Same rules as the graph editor: a missing
   `id` is the short ID made from the tree ID (`SubtreeGraphEdits.NewReferenceId`), a missing
   `interfaceVersion` is the subtree's current one, interface keys the node does not list are
   connected to same-named parent keys of the same type (`connectSameNamedKeys`, default true), a key
   given as `null` stays not connected, the tree is raised to format 3. A node with both `subtree`
   and `typeId`/`parameters`/`children`/… is `AIBT9018`. The `add` operation of
   `apply_domain_patch` accepts the same node.
2. **New tools**: `set_subtree_keys` (SemanticEdit; `keys` map of `"parentKey"` / `{"value": c}` /
   `null`, optional `interfaceVersion`, `connectSameNamedKeys`), `extract_to_subtree` (SemanticEdit;
   `nodeId`, `newTreeId`, `path`, optional `name`, `description`; `SubtreeGraphEdits.Extract`; both
   documents validated before anything is written; the new file is written first and deleted again
   if the parent cannot be written; an existing tree ID or file is `AIBT9017`; the reference keeps the
   moved node's description), `unpack_subtree` (SemanticEdit; `SubtreeGraphEdits.Unpack`; returns
   `innerRootId`; a collision is `AIBT3027`, nothing written), `find_subtree_usages` (Read; `status`
   `Found`/`NotFound`/`Ambiguous`, `declaredIn`, `usages` with tree, file, node ID, display name and
   key map, `skippedTreeFiles`). Each has a catalog entry, a dispatcher case, a server tool
   (`aibt_set_subtree_keys`, `aibt_extract_to_subtree`, `aibt_unpack_subtree`,
   `aibt_find_subtree_usages`) and a CLI command.
3. **`inspect_tree` `expanded`** adds `expansion` (`success`, `identity`, `referencedTreeIds`,
   `diagnostics`, `tree` (the format-2 expansion), its `contentHash`, `nodeSources`,
   `referenceAvailable`); without the argument the result is unchanged. **`get_project_manifest`**
   lists `interface` (`{version, keys}` or null) and `referencedTreeIds` for every tree.
4. **Resolution in validate / compile / prepare_runtime** through `AibtTreeDiscovery`
   (`MCP/McpSubtrees.cs`: `McpSubtreeResolver`, logical source IDs for validate/compile, asset paths
   for prepare). A tree without references takes exactly the old code path. `validate` of a tree with
   references validates the expansion and adds `subtrees: {expanded, referencedTreeIds}`. Missing,
   duplicate and recursive subtrees are `AIBT3020`/`3021`/`3022` diagnostics, never exceptions.
5. **Every MCP edit of a tree with references** is validated with the references resolved
   (`ProjectNodeCatalog.ValidateAuthoring(document, options, resolver)`, new internal overload). Before
   this, any MCP edit of a format-3 tree with a reference was rejected with `AIBT3020`.
6. **CLI** (`MCP~/Cli/Program.cs`): `tree expand <treeId>`, `tree usages <treeId>`,
   `tree extract <treeId> <nodeId> <newTreeId> <path>`, `tree unpack <treeId> <nodeId>`,
   `tree keys <treeId> <nodeId> key=parent|key:=<json>|key=`. Both .NET projects build with 0
   warnings, 0 errors.
7. **Documentation**: new `Documentation~/subtrees.md` (linked from the README's documentation list,
   `cli.md` and `ai-and-mcp.md`; text only, the guides have no image convention), a "Subtrees"
   section in `cli.md`, `ai-and-mcp.md` authoring and verification lists, a "Subtrees" paragraph in
   the workflow guide generator; `Documentation~/generated/workflow-guide.md` regenerated with
   **AIBT > MCP > Regenerate Documentation** (the other generated files are unchanged; the
   host-project `node-catalog.md` differs only by the host's own nodes and was not copied).
8. **Reference-card running glow** (stage 3 deviation 7, `Editor/Graph/Styles/AibtGraph.uss` only):
   on a reference card the glow ring is 5 px instead of 3 px and drawn 11 px outside the card instead
   of 6 px, so a dark gap separates it from the teal border.
9. **`describe_agent`** needed no change: a test shows the two-part labels.

### New tests

| ADR §7 stage 4 item | Test |
|---|---|
| Dispatcher tests per tool | `McpSubtreeToolsTests` (23): add_node (format raise, same-named keys, explicit `null` → `AIBT3025`, generated ID, domain patch, missing subtree → `AIBT3020`, malformed node → `AIBT9018`), set_subtree_keys (constant, `AIBT3026`, `AIBT3025`, back to a parent key, not a reference, unknown node), extract_to_subtree (dry run, success and expansion, policy rejection without file, existing tree ID or file, root, read-only parent → no orphan file), unpack (labels, private keys, `AIBT3027`), find_subtree_usages (Found/NotFound/Ambiguous), manifest fields, inspect expanded vs. `SubtreeExpander` and identity, validate/compile `AIBT3020/3021/3022`, prepare_runtime dry run with references and `AIBT3020`, plain tree vs. direct `ReferenceCompiler` hash, `extract_subtree`/`inline_subtree` round trip of a reference |
| Permission negatives | `EachNewToolIsCatalogued_AndDeniedWithoutItsCategory` (4 cases: no grant and a wrong grant → `AIBT9012`, no file changed), `TheExistingToolsKeepTheirPermissions_AndOnlyGainOptionalArguments` |
| `describe_agent` inside a subtree | `McpSimulationToolsTests.AnAgentInsideASubtree_IsDescribedWithTwoPartLabels` (active path `Main`, `Go / Within radius`; debug map `go:check` from `Assets/probe.aibt`) |
| Live CLI session | below |

No existing test expectation was changed.

### Runs (stage 4)

Raw output in `stage4\runs\`. Final run of every touched or covering class
(`touched-classes-1.txt`):

| Run | Result |
|---|---|
| `AIBT.CodeGen.ContractTests` | 6/6 |
| `McpAuthoringToolDispatcherTests` | 20/20 |
| `McpAuthoringJsonTests` | 2/2 |
| `McpToolDispatcherTests` | 7/8, `ZeroCustomNodesReturnsExactlyThePhase1BuiltInCatalog` is known (below) |
| `McpVerificationToolDispatcherTests` | 23/23 |
| `McpPermissionEnforcerTests` | 17/17 |
| `AgentConsumerToolsTests` | 14/14 |
| `ProjectManifestQueryTests` | 3/3 |
| `AibtTreeDiscoveryTests` | 4/4 |
| `SubtreeGraphEditorTests` | 13/13 |
| `McpDocumentationGeneratorsTests` | 13/13 (after regeneration) |
| `McpSubtreeToolsTests` (new) | 23/23 |
| `McpSimulationToolsTests` | 4/4 (one new) |

`ZeroCustomNodesReturnsExactlyThePhase1BuiltInCatalog` fails because the host project Modules
registers five sample nodes (`aibt.samples.first-agent.move`, three `aibt.samples.swarm.*`,
`samples.simple.next-waypoint`) from samples imported into the parent project; the test expects the
package's built-in set only. It is unrelated to the tool catalog; the expectation was not changed.
`Tools~/Verification/Verify-Static.ps1` passes (`verify-static.txt`).

### Trees without references: hash comparison

`stage4\hashes-mcp-{before,after-core,after}.txt`: SHA-256 of the normalized responses of
`inspect_tree`, `validate`, `compile` and `prepare_runtime` (dry run, each of the three Player
catalogs) for every one of the 65 tree files of the package, each alone in a temporary project
(390 lines). `before` is `6caf728`; `after` is the final code (identical to `after-core`). All lines
of the files without references are byte-identical, including the interface-only subtree files. The
13 differing lines belong to the three parents with references (`boss-fight`, `reactive-parent`,
`twice-parent`), whose subtrees are not in the single-file temporary project: they now report
`AIBT3020`, where before validate checked the parent alone and prepare stopped at the parent's own
errors. The dump tool is kept as `stage4\P11Stage4BaselineDump.cs` (not committed).

### Live CLI session

`stage4\live\`: the package CLI (`MCP~/Cli`, built DLL) through the Editor bridge (`mcp ensure`)
against the running Editor "Modules", on copies in `Assets/P11Stage4Scratch/` (deleted afterwards,
Editor refreshed). Three sessions; transcripts and resulting files kept:

- **C (`transcript-c.txt`, `result-c/`, all green)**: usages; extract with the default `Read`
  permission → `AIBT9012`; extract Finisher into `finisher.aibt` (interface `hits`); disconnecting
  `hits` of the right strike → rejected with `AIBT3025`, nothing written; rebind the right strike
  `hits=bonus_hits`; expand (`right:windup` …, node sources); usages of both subtrees;
  `tree validate` → valid, `tree compile` → success; `orphan.aibt` → `AIBT3020` from validate and
  compile.
- **A (`transcript-a-managed-demo.txt`)**: the stage-3 demo shape (managed `wait-seconds`,
  `increment-int`) with the constant rebinding `hits:=5` (private key `right:hits` in the
  expansion); extract, expand and usages succeed; validate/compile report `AIBT2040` because this
  project's policy forbids nondeterministic nodes (correct).
- **B (`transcript-b-naming-policy-AIBT2038.txt`)**: deterministic `aibt.stdlib.wait` leaves and a
  subtree with a private key `swings`: validate, compile and the edits are rejected with `AIBT2038`
  for `left:swings` / `right:swings` (open question 1).

`describe_agent` output with two-part labels: `stage4\describe-agent-subtree.json`.

### Visual evidence (reference-card glow)

`stage4\glow\`: the stage-3 demo `boss-fight.aibt` in Play Mode (agent created by an Editor eval in
Play Mode only, `create-agent.cs`), recorded from the AIBT Graph window with stills every 0.5 s.
`before-live-parent.mp4` and `before-peak-left-strike-running.png`: the yellow ring lies on the teal
border. `after-live-parent.mp4`, `after-peak-right-strike-running.png` and
`after-low-pulse-right-strike-running.png`: the thicker ring outside the border, visible also at the
low point of the pulse. `before-window.png`, `after-window.png`: window screenshots.

### Deviations and open questions

1. **Naming policy and private keys (found, not fixed).** With `blackboardNaming: snake_case` (the
   Modules project), the expanded private keys `r:key` fail `AIBT2038` when a tree is validated or
   compiled under the project policy (MCP `validate`, `compile`, and every MCP edit of a native or
   reference tree). The check is in the existing validator run on the expansion (stages 1–2), not in
   stage-4 code; changing it changes validation semantics, so it is left to the owner (for example,
   apply the naming rule to the part after the last `:`). Documented as a limit in `subtrees.md`.
   MCP edits of managed trees are validated under the managed policy (unchanged behaviour), so they
   pass, but `validate`/`compile` of such trees report it.
2. **Two additive changes outside `MCP/**`**: the internal overload
   `ProjectNodeCatalog.ValidateAuthoring(TreeDocument, ReferenceCompilerOptions, ISubtreeResolver)`
   and an optional `documentId` parameter of the internal `SubtreeGraphEdits.Unpack`, so MCP shares
   the graph editor's logic and reports its own document IDs.
3. **MCP edits are stricter than the graph editor**: an edit that leaves a not connected key or a
   missing subtree is rejected (the graph editor allows such states while editing). This follows
   the MCP rule that invalid partial trees are not persisted.
4. **Two tree indexes**: MCP resolves through `AibtTreeDiscovery` (trees under `Assets/`), import and
   the graph editor through `ProjectTreeIndex` (`Assets/` and `Packages/`). A reference to a subtree
   under `Packages/` imports but is `AIBT3020` in MCP.
5. **`extract_subtree` payload** now includes the `subtree` object of a reference node (additive; the
   round trip through `inline_subtree` restores the reference exactly).
6. **`AIBT3020` messages** from MCP list the tree files `AibtTreeDiscovery` could not read; in Modules
   these are three invalid fixtures (under `Spikes~`, `Tests/Fixtures`, `Tools~/Verification`), so
   the message is long.
7. Seen, not changed (pre-existing): `describe_agent` names a node type from a test-only catalog
   `type 0x…`; `inspect_tree`'s `sourcePath` mixes `/` and `\`; extracting a node whose label equals
   the new tree's root label gives labels like `Finisher / Finisher` in the expansion.

### Not run / not covered (stage 4)

- No full nine-assembly run (only the touched classes, as the brief requires).
- The MCP server tools were built, not called through an MCP client; the CLI exercises the same
  bridge operations.
- `prepare_runtime` with references was tested as a dry run (test), not persisted to an asset in the
  live session.

### Acceptance run (orchestrator, 2026-11-19, stage 4)

Full run of all nine assemblies on `6b7e141`, clean working tree, 19:57–20:09; the Editor-log
counters did not move during the run (`No command named` 0, main-thread timeouts 1, before and
after). Raw output:
`C:\UnityProjects\_aibt_acceptance\P11-001\stage4-acceptance\full-run-6b7e141.txt`.

| Assembly | Stage 3 (`7794cb0`) | `6b7e141` | New tests |
|---|---|---|---|
| `AIBT.Runtime.Tests` | 815/815 | 815/815 | — |
| `AIBT.Integration.Tests` | 240/240 | 240/240 | — |
| `AIBT.Editor.Tests` | 761/762 | 785/786 | `McpSubtreeToolsTests` 23, one in `McpSimulationToolsTests` |
| `AIBT.BehaviorCases.Tests` | 65/65 | 65/65 | — |
| `AIBT.NativeBurstDispatch.Tests` | 79/79 | 79/79 | — |
| `AIBT.NativeTreeAgentBlackboard.Tests` | 33/33 | 33/33 | — |
| `AIBT.NativeSharedBlackboard.Tests` | 36/36 | 36/36 | — |
| `AIBT.CodeGen.ContractTests` | 6/6 | 6/6 | — |
| `AIBT.PlayMode.Tests` (PlayMode) | 2/2 | 2/2 | — |

The one failure is `McpToolDispatcherTests.ZeroCustomNodesReturnsExactlyThePhase1BuiltInCatalog`
(the host project registers five sample nodes; 76 instead of 71). Every added test is accounted
for (24 in Editor).

Protected paths (`ExpectedPublicAbiV2.txt`, `package.json`, `Documentation~/specifications`,
`Schemas~`, `Runtime/**`, `Authoring/**`, `Agents/**`, `CodeGen~`) have no change from `6caf728` to
`6b7e141`. Outside `MCP/**` and `MCP~/**` the code diff is `AibtGraph.uss` (12 lines), the optional
`documentId` of `SubtreeGraphEdits.Unpack` and the internal resolver overload of
`ProjectNodeCatalog.ValidateAuthoring`. Read in full: `McpAuthoringToolDispatcher.Subtrees.cs`,
`McpSubtrees.cs`, and the diffs of the catalog, dispatcher, authoring, verification and runtime
tools and of the CLI; `McpAuthoringJson.cs`, the server tool wrappers and the test file were not
read line by line. The hash comparison was recomputed from `hashes-mcp-{before,after}.txt`: 390
lines each, 13 differ, all of them `validate`/`compile`/`prepare_runtime` of the three parents with
references. The glow stills and transcript C were looked at and show what the section above says.

Open question 1 was confirmed in code: `SubtreeExpander` gives an expanded private key the name
`r:key` (name = ID), and `TreeValidator.ValidateNames` checks that name against the project
`blackboardNaming`; only the MCP tools apply `.aibt/policy.json`, so import, the graph editor,
baking and execution are not affected. Transcript C passes because its subtree has no private key;
the stage-3 demo (`strike` with the private key `swings`) cannot be validated or compiled through
MCP/CLI in a project with a naming policy.

Stage 4 is accepted. Open question 1 (naming policy against expanded private keys, `AIBT2038`) goes
to the owner; it needs a change in `Authoring/Validation/TreeValidator.cs` and is not started.

## Naming policy against expanded private keys (2026-11-19, after stage 4)

The owner approved the fix of stage 4's open question 1 on 2026-11-19. Done by the orchestrator in
`34e80f0`.

- **Change** (`Authoring/Validation/TreeValidator.cs`, `ValidateNames`): the project
  `blackboardNaming` policy checks the part of a blackboard key name after the last `:`. The
  qualifier of an expanded private key `r:key` is made of reference node IDs, which the key naming
  policy does not govern. A name without `:` is checked whole, as before. Nothing else changed: the
  policy file format, `ReferenceCompilationPolicy` and its hash, the expander, both executors.
- **Side effect to know**: a hand-written key named `a:b` (the authoring ID grammar always allowed
  `:`) is now checked by its part `b` only; before, any policy except `any` rejected it.
- **Tests**: `TreeValidatorTests.Validate_NamingPolicy_AppliesToThePartOfAKeyNameAfterTheLastColon`
  (three cases: `snake_case`, `camelCase`, `PascalCase`; plain, `r:key` and `r1:r2:key` names, good
  and bad) and
  `McpSubtreeToolsTests.ValidateCompileAndEdits_UnderANamingPolicy_AcceptExpandedPrivateKeys_AndStillCheckTheKeysOwnName`
  (validate, compile and a constant rebinding pass under `snake_case`; a subtree key named
  `SwingCount` still reports `AIBT2038`). No existing test expectation changed.
- **Touched classes** (`C:\UnityProjects\_aibt_acceptance\P11-001\naming-fix\`): `TreeValidatorTests` 42/42, `McpSubtreeToolsTests` 24/24,
  `McpVerificationToolDispatcherTests` 23/23, `ReferenceCompilerTests` 18/18,
  `McpAuthoringToolDispatcherTests` 20/20.
- **Live CLI** (`C:\UnityProjects\_aibt_acceptance\P11-001\naming-fix\live`, `transcript-b-after-fix.txt`): stage 4's session B (the one that
  failed with `AIBT2038`; a subtree with the private key `swings`, project policy `snake_case`) run
  again against the Editor "Modules": extract accepted, `hits:=5` accepted (private key
  `right:hits`), `tree validate` valid with `policyApplied: true`, `tree compile` success; the
  permission negative (`AIBT9012`), `AIBT3025` and `AIBT3020` as before. The scratch folder under
  `Assets/` was removed.
- **Documentation**: the limit in `Documentation~/subtrees.md` is replaced by the rule; changelog
  entry.

Full run of all nine assemblies on `34e80f0`, clean working tree, 20:18–20:29; Editor-log counters
did not move (`No command named` 0, main-thread timeouts 1). Raw output:
`C:\UnityProjects\_aibt_acceptance\P11-001\naming-fix\full-run-34e80f0.txt`.

| Assembly | `6b7e141` | `34e80f0` | New tests |
|---|---|---|---|
| `AIBT.Runtime.Tests` | 815/815 | 815/815 | — |
| `AIBT.Integration.Tests` | 240/240 | 240/240 | — |
| `AIBT.Editor.Tests` | 785/786 | 789/790 | three cases in `TreeValidatorTests`, one in `McpSubtreeToolsTests` |
| `AIBT.BehaviorCases.Tests` | 65/65 | 65/65 | — |
| `AIBT.NativeBurstDispatch.Tests` | 79/79 | 79/79 | — |
| `AIBT.NativeTreeAgentBlackboard.Tests` | 33/33 | 33/33 | — |
| `AIBT.NativeSharedBlackboard.Tests` | 36/36 | 36/36 | — |
| `AIBT.CodeGen.ContractTests` | 6/6 | 6/6 | — |
| `AIBT.PlayMode.Tests` (PlayMode) | 2/2 | 2/2 | — |

The one failure is the known `ZeroCustomNodesReturnsExactlyThePhase1BuiltInCatalog`.

Not done: the new tests were not run against the old validator (the failing state is stage 4's
transcript B and the test of the old behaviour is the live session above); the fix was written and
accepted by the same session, without a second reader.

All four stages of ADR §7 are accepted and the open question is closed. Still open from the stage
notes, none blocking: MCP resolves subtrees under `Assets/` only (import and the graph editor also
under `Packages/`); the MCP server tools were not called from an MCP client; `prepare_runtime` with
references was run only as a dry run; native agents in a subtree window were not exercised.

## Closing (2026-11-20)

The owner closed P11-001 on 2026-11-20 on the condition that the orchestrator sees no problem.
Checked before closing, by reading (no new run, the Editor was in use by P11-002 stage 4):

- Every acceptance criterion of the task card has a test or a recorded live session in the stage
  sections above.
- The four stages and the naming-policy fix each passed a full run of all nine assemblies
  (`7e614b8`, `c02b42b`, `7794cb0`, `6b7e141`, `34e80f0`).
- The subtree test classes stayed green in the three later full runs made for P11-002
  (`4af5571`, `bad2892`, `e944c5c`); the only failure in each is the known
  `ZeroCustomNodesReturnsExactlyThePhase1BuiltInCatalog`.
- No defect is open against the feature.

Not covered at closing (never run; none is a known defect):

- A native agent shown in a subtree window (the code path is shared with the managed agent, which
  was exercised).
- `prepare_runtime` of a tree with references persisted to an asset in a live session (a dry run is
  tested; baking through `ProductionTreeAssetBaker` is tested).
- The MCP server tools called from an MCP client (the CLI and the tool dispatcher tests use the same
  bridge operations).
- An installed package that holds trees (the `Packages/` index is tested with mapped temporary
  folders).

Known limits that stay: MCP resolves subtrees under `Assets/` only, while import and the graph
editor also look in `Packages/`; MCP edits reject a tree that does not expand and compile, the graph
editor allows such states while editing.

The three findings of ADR §9 (duplicate `AIBT2042`/`AIBT2043` definitions, the last-wins identity
map, the stale importer sentence of ADR-P7-042) are not part of this item and have no task yet.
