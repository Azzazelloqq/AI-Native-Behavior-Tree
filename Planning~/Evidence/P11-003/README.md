# P11-003 — AIBT2043 counts writes through native `bindings`

Branch `feature/aibt-consumer-workflow`, base `360538b`. Status: implemented and accepted by the
orchestrator on `5094631` ("Acceptance run" at the end); the item is in `Review`. Raw runs, probes, the two stop notes and the screenshots are in
`C:\UnityProjects\_aibt_acceptance\P11-003\`. All paths below are relative to the submodule unless they
start with `C:\`.

## Step 0 facts (360538b, read only)

The full note is `C:\UnityProjects\_aibt_acceptance\P11-003\P11-003-step0-blocker-binding-access.md`.

1. The validator receives only a `NodeRegistry` (`Authoring/Validation/TreeValidator.cs:8-18`).
   `NodeRegistryEntry` holds `Manifest`, `NumericTypeId`, `Source` and `HandlerBinding`, but no generated
   descriptor (`Authoring/Registry/NodeRegistry.cs:59-82`).
2. Native manifests have empty `reads`/`writes`:
   - CodeGen writes them empty (`CodeGen~/AIBT.CodeGen/GeneratedMetadataEmitter.cs:171`);
   - the materializer copies the empty arrays (`Authoring/Registry/Generated/GeneratedShardMetadataMaterializer.cs:301,311`);
   - `NodeConfigurationField` has no access kind (`Authoring/Model/Nodes/NodeManifestContracts.cs:118-163`).
3. The access exists only in `GeneratedBindingDescriptor.Kind`: `BlackboardRead=0`, `BlackboardWrite=1`,
   `BlackboardReadWrite=2` (`Authoring/Registry/Generated/GeneratedNodeContracts.cs:15-19, 214-245`).
   Only the compiler reads it (`Authoring/Compilation/Generated/GeneratedScopeCompiler.cs:288-330`).
4. Descriptors exist on some paths but never reach the validator:
   - the generated compiler (`GeneratedCompiledProgramV2.cs:368-371`);
   - `ProjectNodeCatalog.ValidateAuthoring` (`Editor/Registry/ProjectNodeCatalog.cs:39`);
   - MCP `validate` (`MCP/Verification/McpVerificationToolDispatcher.cs:29,47,62`);
   - the subtree expander (`Authoring/Compilation/Subtrees/SubtreeExpander.cs:112,325`; it renames binding
     values to `r:key` at `:506`).
5. A registry entry's manifest is the descriptor's own instance:
   - `NodeRegistryBuilder.AddUserExtension` keeps the reference (`Authoring/Registry/NodeRegistryBuilder.cs:46,142`);
   - it is called with `descriptor.Manifest` (`Editor/Registry/ProjectNodeCatalog.cs:157`, `Authoring/Registry/Generated/GeneratedNodeRegistry.cs:25`).
6. The host rule: `TryResolveExternalTreeWrite` refuses a key when `(AccessFlags & Write) != 0`
   (`Runtime/Integration/GeneratedDispatch/GeneratedTreeDispatchAdapterV2.cs:249-291`, `:280`). That flag is set
   when an access record has `Mode != Read` (`GeneratedCompiledProgramV2.cs:544`), and the records come
   from bindings (`GeneratedScopeCompiler.cs:327`). So `Write` and `ReadWrite` are refused and `Read` is accepted.

## Deviation from the brief

The brief allowed code only in `Authoring/Validation/**`, tests and the recipe generator. Two lines
outside those areas were needed. The owner allowed each one; nothing else changed outside them.

### Deviation 1 — one registration line in `GeneratedNodeDescriptor`

- **Line.** `Authoring/Registry/Generated/GeneratedNodeContracts.cs:317`, the last statement of the long
  constructor, after all of its checks: `GeneratedBindingWriteIndex.Register(manifest, _bindings);`. No other
  member or signature in the file changed. `ExpectedPublicAbiV2.txt` is untouched, and
  `AIBT.CodeGen.ContractTests` passes 6/6 (run 2).
- **Permission.** The owner, 2026-11-20: «Да, вариант B» (relayed by the orchestrator).
- **Facts.** Step 0 facts 1–5 above.
- **Why option A was rejected.** Option A passed the descriptors to the validator. It needed about five
  caller edits outside `Authoring/Validation`, two of them in `Authoring/Compilation`, which was forbidden.
  Option B touches one line, and every path that builds its registry from descriptors gets the rule
  through fact 5.
- **Risks.**
  - *Hidden static state.* `GeneratedBindingWriteIndex` (`Authoring/Validation/GeneratedBindingWriteIndex.cs`) is
    an internal static `ConditionalWeakTable<NodeManifest, …>`.
    - It holds no manifest alive, and it goes away with the domain.
    - It stores only the Tree-scope binding IDs of `Write`/`ReadWrite` members. Lookup is by reference.
  - *Dependence on instance identity.* A manifest that is not the descriptor's instance is not found, and
    such a manifest keeps the earlier behaviour (the note stays):
    - a hand-built fixture;
    - a manifest read back from JSON;
    - a project node whose identical reference manifest wins (`ProjectNodeCatalog.cs:154-156`). That node
      runs in the managed agent, so this is correct.
  - So the failure mode is "note as before", never a missing note. Tests:
    - `AManifestNoGeneratedDescriptorRegistered_KeepsTheEarlierBehaviour`;
    - `EveryRegistryANativeTreeIsValidatedWith_HoldsTheDescriptorsOwnManifest`.
  - *Duplicate registration.* Two descriptors built over one manifest instance do not throw: `GetValue`
    creates the entry atomically, and the merge runs under the entry lock.
    - If they report different sets, the index keeps their **intersection**: a key counts as written only
      if every descriptor writes it.
    - This is safe because the worst case is the old behaviour (a note), never a missing note for a key
      game code must feed.
    - Tests: `ASecondDescriptorForTheSameManifest_DoesNotThrow_AndKeepsTheWrites` and
      `DescriptorsWithDifferentWritesForOneManifest_KeepOnlyTheMembersEveryOneWrites`.
- **Instance identity, path by path.** All of these are tested in `TreeScopeSlotNeverWrittenNativeBindingTests`.
  No path copies the manifest.

  | Path | Registry source | Test |
  | --- | --- | --- |
  | import (subtree expansion) | `ProjectNodeCatalog.Discover()` → `Build` (`ProjectNodeCatalog.cs:76-83`) | `EveryRegistry…` (Build case) |
  | graph Problems panel | `ProjectNodeCatalogCache.Current` → `ValidateAuthoring` | `EveryRegistry…` (Current), `TheGraphPath_ValidateAuthoring_…` |
  | MCP validate | `ProjectNodeCatalogCache.Current.Registry` | `McpValidateAndMcpEdits_…` |
  | MCP edit | `ValidateAuthoring` | `McpValidateAndMcpEdits_…` (`create_tree`, `add_node` with bindings) |
  | prepare_runtime / baker | `ProjectNodeCatalog.ForCatalog` + `GeneratedNodeRegistry.Build` | `EveryRegistry…` (ForCatalog, GeneratedNodeRegistry), `ThePreparePath_TheBaker_…` |
  | subtree expander (`r:key`) | registry from the catalog; expanded binding values | `ASubtree_NativeWritesOfAPrivateKeyAndOfAnInterfaceKey_AreNotInputs` |

  The eval probe confirms the same thing in the live Editor: `entryManifestIsDescriptorManifest: true`
  (`after/compile-path-probe.txt`).

### Deviation 2 — the semantic projection's note in `CompileDocument`

- **Line.** `Authoring/Compilation/Generated/GeneratedCompiledProgramV2.cs:383`. The old line was
  `Add(diagnostics, semantic.Diagnostics);`. It is now a loop that adds every diagnostic of the projection
  except those for which `TreeValidator.IsTreeScopeSlotNeverWrittenNote` is true.
  - That predicate (`TreeValidator.cs:543`) matches code `TreeScopeSlotNeverWritten` **and** severity `Info`.
  - `Add` (`:785`) was the same loop, so order and everything else stay as they were.
  - Nothing else in `Authoring/Compilation` changed.
- **Permission.** The owner, 2026-11-20: «Да, разрешаю» (relayed by the orchestrator). The note with the facts is
  `C:\UnityProjects\_aibt_acceptance\P11-003\P11-003-blocker2-semantic-projection.md`.
- **Facts.**
  - `CompileDocument` validates the real document at `:371`, and that pass is now correct.
  - It then builds `CreateSemanticProjection` (`:570-598`): native nodes become placeholders, bindings
    become `null`, and every key becomes a Tree `Int32` key.
  - It compiles the projection with `ReferenceCompiler.Compile` (`:380`), which validates again.
  - Its diagnostics were added at `:383`, so the note came back for every key that no managed node writes.
  - `DiagnosticCollection` merges equal duplicates, which hid the doubling.
- **Why the goal needs it.** Without this line, these paths keep the false note because they return the
  compiler's diagnostics:
  - the graph Problems panel (`ProjectNodeCatalog.ValidateAuthoring:43-45` for native-only trees);
  - MCP/CLI edits;
  - `prepare_runtime` and the baker.

  The "after" screenshot would then be unchanged.
- **Rejected option 2.** Option 2 fixed only the direct validator paths (MCP/CLI `validate`). The note
  would stay on the panel, on edits and on prepare, and the documentation would have to say so.
- **The compiler error AIBT2043 is not filtered.**
  - `ReferenceCompilerDiagnosticCodes.ScopeContractMismatch` (Error, also `AIBT2043`) is raised only by
    `GeneratedScopeCompiler.cs:138,283`, never by `ReferenceCompiler`. So it cannot come from the
    projection.
  - The predicate is tested directly in `TheFilterMatchesOnlyTheValidatorsInfoNote`:
    - the validator's note → true;
    - `ReferenceCompilerDiagnostics.Create(ScopeContractMismatch, …)` (Error) → false;
    - an `AIBT2043` Warning → false;
    - another Info → false;
    - `null` → false.
- **The `SubtreeExpansion` branch (`nodeSourceIds != null`)** is covered by
  `ASubtree_NativeWritesOfAPrivateKeyAndOfAnInterfaceKey_AreNotInputs`, which compiles a parent through
  the generated compiler with a resolver. The probe shows the same branch in the live Editor (see the
  counts below).
- **Managed and direct ReferenceCompiler paths are untouched.**
  - `TheReferenceCompilerDirectly_ReportsTheSameNotesAsTheValidator` covers the direct call.
  - In the live probe on First Agent (managed), `validateAuthoring` = `[AIBT2043]` and
    `ReferenceCompiler.Compile` = `[AIBT3012, AIBT2043]`. Both are identical in `intermediate/` and `after/`
    `full-diagnostics.json`, and content hash `e74591b6…` is the same.

## The change

| File:line | What |
| --- | --- |
| `Authoring/Validation/GeneratedBindingWriteIndex.cs` (new, 74 lines) | The internal index: `Register` (`:30`; intersection at `:49`) and `TryGetWrittenBindings` (`:57`, a snapshot copy; false when unknown) |
| `Authoring/Validation/TreeValidator.cs:508` | `ValidateNodes` also calls `CollectGeneratedBindingWrites` next to `ValidateAccess` and `CollectKeyParameterTargets` |
| `Authoring/Validation/TreeValidator.cs:543` | `IsTreeScopeSlotNeverWrittenNote` (code and Info) |
| `Authoring/Validation/TreeValidator.cs:555` | `CollectGeneratedBindingWrites`: for each binding of the node whose member the index lists, it adds the bound key id to the written set |
| `Authoring/Registry/Generated/GeneratedNodeContracts.cs:317` | Deviation 1 |
| `Authoring/Compilation/Generated/GeneratedCompiledProgramV2.cs:383` | Deviation 2 |

What the validator does now:
- A Tree key is "written" if one of these holds:
  - a manifest declares a write (unchanged);
  - a managed `BlackboardKey` parameter targets it (unchanged);
  - a generated node binds it through a `Write` or `ReadWrite` member (new).
- The access comes from `GeneratedBindingDescriptor.Kind`, through the index keyed by the manifest
  instance.
- After subtree expansion, the binding values are the expanded ids (`r:key`), so private keys and
  interface keys are handled the same way.
- The wording of the note is unchanged. It is still true: no node writes such a key.

## Tests

New classes:
- `Tests/Editor/Validation/TreeScopeSlotNeverWrittenNativeBindingTests.cs`, 19 tests:
  - access cases: `Write`, `ReadWrite`, only `Read`, one node writes and another reads;
  - a project Burst node (`aibt.tests.external-tree-write-writer`/`-reader`);
  - a managed key parameter in a native tree;
  - an unregistered manifest;
  - the index itself, duplicate registration, different sets;
  - the filter predicate;
  - the compiler reports each input once;
  - the direct ReferenceCompiler path;
  - a subtree with `r:key` and an interface key;
  - registry identity on every path;
  - the graph, the tree list, the baker, and MCP validate/edits.
- `Tests/Integration/NativeRuntime/NativeBindingInputNoteHostRuleTests.cs`, 1 test. On a tree with
  `Read`, `Write`, `ReadWrite`, mixed and project-node keys, the keys without the note are exactly the keys
  `TryResolveExternalTreeWrite` refuses (`count`, `flag`, `owned`, `ready-at`, `target`). The noted keys are
  exactly the accepted ones.

| Run | Content | Result |
| --- | --- | --- |
| 1 (`tests/run1-new-classes.txt`) | the new classes and `TreeScopeSlotNeverWrittenValidatorTests` | 18/19, 1/1, 5/5. The tree-list test assumed notes in `Details`, but the list replaces them for a native tree; the test was changed to check the status "Needs native compile" and no note |
| 2 (`tests/run2-affected-full.txt`) | after recompile: `AIBT.CodeGen.ContractTests`, the validation namespace and 31 affected classes ( MCP authoring/verification/subtrees/documentation, graph, subtree native/import/bake, native library, consumer workflow, external writes, agents) | all green except the 2 known `GeneratedArtifactContractTests` host-layout failures ("The CodeGen test assembly must belong to the AIBT package", pre-existing, see P11-001 evidence); `AIBT.CodeGen.ContractTests` 6/6 |
| 3 (`tests/run3-docs-after-regeneration.txt`) | after the recipes were regenerated: `McpDocumentationGeneratorsTests`, `UserGuideSnippetTests`, `TreeScopeSlotNeverWrittenNativeBindingTests`, `TreeScopeSlotNeverWrittenValidatorTests` | 14/14, 5/5, 19/19, 5/5 |

The known `McpToolDispatcherTests.ZeroCustomNodesReturnsExactlyThePhase1BuiltInCatalog` (105 vs 100) was
not in these runs.

## Changed expectations of existing tests

None. No existing test pinned `AIBT2043` on a key that a native node writes.

## Counts and diagnostic lists (deviation 2, conditions 2 and 3)

Probe `scripts/full-diagnostics-probe.cs`, live Editor. The `intermediate/` state has deviation 1 only;
the `after/` state has both deviations.

| Tree | 360538b final | intermediate: real / projection / final | after: real / projection (dropped) / final |
| --- | --- | --- | --- |
| Native Guard | 5 | 3 / 5 / 5 | 3 / 5 / 3 |
| native tree with a subtree (`scratch-trees/`) | — | compiler 5 | compiler 2 (`idle-flag`, `intruder-seen`) |
| recipe tree (CLI, step 6 `validate` and later) | 8 | — | 4 |

- Before the fix, every fed key of Native Guard got the note twice (real document and projection),
  merged into one by `DiagnosticCollection`. After the fix, each fed key comes exactly once, from the real
  document.
- In both states, the full lists of the compiler, the bake and `ValidateAuthoring` contain only
  `AIBT2043|Info` lines for Native Guard and the subtree tree. The only difference is the removed lines,
  and the sorted order is kept.
- On the recipe tree, `tree compile` returns `AIBT3012` (error) before and after.
- Subtree tree:
  - removed notes: `alarm-raised` (written by `set-bool` in the parent), `strike:spot` (a private key
    written by `random-int`) and `weapon-ready-at` (an interface key written by `set-deadline`);
  - kept notes: `idle-flag` and `intruder-seen`, which only `Read` members bind.

## Hashes before and after

| Tree | Hash | 360538b | after |
| --- | --- | --- | --- |
| Native Guard | content | `916b044d…a512` | `916b044d…a512` |
| Native Guard | prepared-asset file (`native-guard-probe.txt`) | `b87f591a…f901` | `b87f591a…f901` |
| Native Guard | existing demo asset | `3ac1aaba…03d8` | `3ac1aaba…03d8` |
| Native Guard | prepared-asset JSON (`full-diagnostics.json`) | — | `70e9e580…` (intermediate and after) |
| recipe tree | content (steps 1–5) | `283f9726…`, `ac7d9ebe…`, `8a9b2a21…`, `769c7500…`, `d7582722…` | the same |
| recipe tree | prepared asset | `599843b5…` | `599843b5…` |
| recipe tree after `configure_node` | content | — | `aabccaf5…` (= P11-002 rework run) |
| subtree tree | content / expanded / asset JSON | — | `0fd34bb0…` / `f34151ab…` / `6a3443d7…` (intermediate and after) |

Two things changed nowhere: no compiled program code and no catalog fingerprint input. The descriptor
line only fills a side table; the filter only drops a note.

The reprepared recipe asset (`8e85720d…`) differs from P11-002's (`d92b8813…`). Its first prepare
already differed at `360538b` (`1ffd8e5e…` in P11-002 vs `599843b5…` here, before this change), so prepared
asset hashes compare only within one Editor environment.

## Corrected documentation (old → new)

`Documentation~/generated/recipes.md` was regenerated by `McpRecipesDocumentGenerator.Generate`
(`scripts/RegenerateRecipes.cs`, `runs/regenerate-recipes.txt`), not edited by hand.

| Place | Old | New |
| --- | --- | --- |
| recipe "build a native tree", step 1 | `[<one AIBT2043 info per key>]` | `[<one AIBT2043 info per key: no node binds a key yet>]` |
| same, step 6 `validate` | `[<one AIBT2043 info per key>]` | `[<four AIBT2043 infos: gate_open, self_position, target_position, target_seen>]` |
| same, "Diagnostics" bullet | "… is an info note. It also appears for keys that native nodes write through `bindings` (here `ready_at`, `shots`, `spot`, `fatigue`)." | "… is an info note on an input key, a key the game feeds: here `target_seen`, `gate_open`, `self_position` and `target_position`. A key a native node writes through a Write or ReadWrite member of `bindings` (here `ready_at`, `shots`, `spot`, `fatigue`) has no note; `NativeTreeAgent.SetFact` refuses exactly those keys." |

These files were checked and needed no change:
- `README.md:223-225` and `Documentation~/editor-and-layout.md:126`: their wording ("no node writes or
  binds", "no node declares a write for") is accurate now.
- `Documentation~/native-node-library.md`, `ai-and-mcp.md`, `cli.md` and `Samples~/NativeGuard/README.md`
  have no sentence about the note.

`CHANGELOG.md` has a new entry.

**Specifications.**
- `Documentation~/specifications/blackboard-v1.md:80` calls `AIBT2043` the compile-time signal of
  `(AccessFlags & Write) == 0`. That was false for native nodes before this item and is true after it.
  Not edited.
- `agent-shared-blackboard-v1.md:420` describes the compiler's error `AIBT2043`, the duplicate code, which
  is out of scope.

## Live runs

- **Recipe "build a native tree" through the CLI.**
  - Before: `before/recipe-cli.txt`. After: `after/recipe-cli.txt`. Script `scripts/run-cli-recipe.sh`;
    summary from `scripts/summarize_recipe.py`.
  - Steps 1–4 add no writing node, so before and after both show 8 notes.
  - From step 5 (`apply_domain_patch`) on, which covers `validate`, `compile`, `prepare --dry-run` and
    `prepare`: 8 notes before, 4 after (`gate_open`, `self_position`, `target_position`, `target_seen`).
  - `tree compile` exits 1 with `AIBT3012`, as the recipe says.
- **Recipe "change a native tree and prepare it again".**
  - After: `after/recipe-reprepare-cli.txt`.
  - `configure_node` and both prepares show 4 notes.
  - The prepare without overwrite is refused with `AIBT9018`, as the recipe says.
  - The asset GUID is kept (`5c20f5ce…`).
- The scratch folder `Assets/AibtRecipeScratch` (and its `.meta`) was deleted with
  `AssetDatabase.DeleteAsset` (`runs/delete-recipe-scratch.txt`: `folderExists false`, `metaExists false`).
  The subtree probe read its trees from outside `Assets/`.

## Screenshots

- `screens/before-native-guard-graph.png` (360538b): the Problems panel shows 5 notes, on `gate-open`,
  `intruder-close`, `intruder-seen`, `sweep-spot` and `weapon-ready-at`.
- `screens/after-native-guard-graph.png`: "Problems: 3 notes".
  - The note stays, correctly, on:
    - `gate-open`: only `wait-until-true` binds it, a `Read` member;
    - `intruder-close`: `bool-is-true`, Read;
    - `intruder-seen`: two `bool-is-true` nodes, Read.

    The game feeds all three with `SetFact`, which the host accepts.
  - The note is gone from:
    - `weapon-ready-at`: `set-deadline` writes it; the `deadline-passed` nodes read it;
    - `sweep-spot`: `random-int` writes it; the `int-equals` nodes read it.

    The host refuses an external write to both.

## Not covered

- Import never shows notes. `TreeCatalogEntry.Read(path)` uses the built-in registry, so native trees
  are "Needs registry", and import surfaces only errors from subtree expansion. The graph's tree list
  shows only the status "Needs native compile" for a valid native tree. So the rule reaches import only
  through the registry identity, which is tested; it produces no visible output there.
- A project node with an identical reference manifest keeps the reference manifest (old behaviour,
  correct for the managed agent). It has no dedicated live tree.
- `AIBT2043` is always Info; no severity override exists, so no other severity was tested beyond the
  predicate.
- Agent- or Shared-scope keys of a native tree: the projection turns them into Tree keys, and its notes
  are now dropped. The real document's validator reports only Tree keys, so this is not tested
  separately.
- The MCP recipe through `mcp serve` was not rerun. The CLI run uses the same bridge tools.

## Open questions

1. `blackboard-v1.md:80` is now accurate. Should the specification say that native `Write`/`ReadWrite`
   bindings count? It is not edited; that is the owner's call.
2. The compiler's duplicate definition of `AIBT2043` (`ScopeContractMismatch`, Error) still shares the
   code with the note. This is out of scope here.
3. The node manifest's `reads`/`writes` stay empty for generated nodes, so MCP `get_node_contract` shows
   no access in the manifest (P11-002 stage 4 tool finding). This is out of scope here.

## Acceptance run (orchestrator, 2026-11-20)

Read by the orchestrator on `5094631` (clean working tree): the whole code diff `360538b..5094631`
under `Authoring/`, this file, the "after" screenshot and the raw recipe transcripts.

- **Scope.** Code outside tests and the recipe generator: `Authoring/Validation/TreeValidator.cs` (+32),
  the new `Authoring/Validation/GeneratedBindingWriteIndex.cs`, one added line in
  `Authoring/Registry/Generated/GeneratedNodeContracts.cs` (deviation 1) and one replaced line in
  `Authoring/Compilation/Generated/GeneratedCompiledProgramV2.cs` (deviation 2). Each deviation was
  allowed by the owner in the orchestrator's session before the session went on («Да, вариант B»,
  «Да, разрешаю»). Nothing changed under `Runtime/`, `CodeGen~`, `Schemas~`, `Agents`, `Samples~`,
  `Documentation~/specifications/`, and not `package.json` or `ExpectedPublicAbiV2.txt`.
- **The compiler line.** The helper `Add` it replaces is the same loop
  (`GeneratedCompiledProgramV2.cs:785`), so only the dropped notes differ. The predicate needs the
  validator's code and severity Info, so the compiler error that shares the code AIBT2043 passes.
- **Recount from the raw transcripts** (`C:\UnityProjects\_aibt_acceptance\P11-003\before\recipe-cli.txt`, `after\recipe-cli.txt`, the
  orchestrator's own script): steps 1–4 report 8 notes before and after; from `apply_domain_patch`
  on (`validate`, `compile`, `prepare --dry-run`, `prepare`) 8 notes before and 4 after
  (`gate_open`, `self_position`, `target_position`, `target_seen`). The content hash of every step
  and the prepared-asset hash (`599843b5…`) are equal in both files; `tree compile` reports AIBT3012
  in both. The three hash values of `before\native-guard-probe.txt` equal those of
  `after\native-guard-probe.txt`.
- **Screenshot.** `screens\after-native-guard-graph.png`: "Problems: 3 notes", on `gate-open`,
  `intruder-close` and `intruder-seen`.
- **Parent project.** `Assets/AibtRecipeScratch` does not exist.
- **Full run of all nine assemblies** on `5094631`, 19:47–20:05; Editor-log counters did not move
  during the run. Raw output: `C:\UnityProjects\_aibt_acceptance\P11-003\acceptance\full-run-5094631.txt`.

| Assembly | `0485779` | `5094631` | New tests |
|---|---|---|---|
| `AIBT.Runtime.Tests` | 815/815 | 815/815 | — |
| `AIBT.Integration.Tests` | 403/403 | 404/404 | `NativeBindingInputNoteHostRuleTests` 1 |
| `AIBT.Editor.Tests` | 793/794 | 812/813 | `TreeScopeSlotNeverWrittenNativeBindingTests` 19 |
| `AIBT.BehaviorCases.Tests` | 65/65 | 65/65 | — |
| `AIBT.NativeBurstDispatch.Tests` | 79/79 | 79/79 | — |
| `AIBT.NativeTreeAgentBlackboard.Tests` | 33/33 | 33/33 | — |
| `AIBT.NativeSharedBlackboard.Tests` | 36/36 | 36/36 | — |
| `AIBT.CodeGen.ContractTests` | 6/6 | 6/6 | — |
| `AIBT.PlayMode.Tests` (PlayMode) | 2/2 | 2/2 | — |

The one failure is the known `ZeroCustomNodesReturnsExactlyThePhase1BuiltInCatalog` (105 found,
100 expected).

Not done by the orchestrator: the new test file was not read line by line; no recipe step and no
probe was run again; the "before" screenshot was not opened; the two `GeneratedArtifactContractTests`
failures named in run 2 are outside the nine assemblies of the full run and were not looked into.

To know: the rule depends on a registry holding the descriptor's own manifest instance (an internal
static side table). A manifest that is not registered keeps the note, so the failure mode is the old
behaviour, never a missing note.
