# P11-007 evidence — review fixes, batch 1 (F1, F2, F3, F4, F5, F7, F12, W5)

Branch `feature/aibt-consumer-workflow`, base `4be2983`. Raw files: `C:\UnityProjects\_aibt_acceptance\P11-007\`.
Unity 6000.6.9f1, the Modules Editor (Pipeline port 7810). Findings from `Planning~/Evidence/P11-REVIEW/README.md`.
Status: `In Progress`; the item awaits the orchestrator's acceptance.

## Commits

| Commit | Finding | Result |
| --- | --- | --- |
| `02cf4ec` | W5 | fixed |
| `c2510cf` | F1 | fixed |
| `7be4d4b` | F12 | partly: 2 of the 3 tests pass; the third now runs and fails on a real assertion (see F12) |
| `aa3bf3d` | F2 | fixed |
| `7fac1a9` | F7 | fixed |
| `c43a1f5` | F4 | fixed |
| `ff023cd` | F3 | fixed |
| `06fd964` | F5 | fixed |
| this commit | — | task card, work item, MASTER_PLAN, this evidence |

Every fix commit holds its test. "Before" runs were made with the test in place and the fix absent;
"after" runs with both.

## W5 — `NativeTreeAgent.TryGetValue` true for a faulted run

- **Cause verified:** `TryGetValue` checked only `Host != null`; the agent clears `Host` in `LateUpdate`, so
  between the scheduler's `Update` that faulted the host and the agent's `LateUpdate` it read the faulted
  run's values and returned true.
- **Fix** (`Runtime/Integration/NativeTreeAgent.cs`, +2 −1): also false when `Host.LastFailure.Code != None`.
- **Test:** `NativeTreeAgentValueReadTests.AFaultedRun_ReturnsFalse_InTheFrameOfTheFault_BeforeLateUpdate`
  (forced `BatchedJobsSameFrame` without job capabilities faults the first update; only the scheduler's
  `Update` runs; the host is still set and failed; `TryGetValue` and `TryGetVector3` return false, value 0).
- **Before:** `W5\before-integration-and-class.txt`: Integration 423/424, the new test fails at line 138
  ("Expected: False But was: True"). **After:** `F1\before-and-W5-after.txt`: class 10/10.

## F1 — one key on two handles of one native node

- **Cause verified:** `GeneratedTreeDispatchAdapterV2.DispatchSlot.TryCreate` gave each blackboard binding its
  own live offset. `NativeBurstDispatchBindingValidationV2` requires live bindings of one request with the
  same target (scope and ordinal) to share offset and layout, so a node with two handles on one key failed
  validation: `NativeLifetimeStateInvalid` at start.
- **Fix** (`GeneratedTreeDispatchAdapterV2.cs`, +11 −0): a blackboard binding whose target slot an earlier
  binding of the same node already uses takes that binding's live offset. The live region keeps one block
  per binding: the batched workspace (`GeneratedDispatchGroupWorkspaceV2`) checks that a participant's region
  is the sum of its case's binding sizes. A first attempt that also shrank the region worked for immediate
  and agent-frame scheduling and failed batched-jobs and pipelined-jobs with the same code
  (`F1\after-attempt1.txt`, Integration 438/440); the committed version keeps the size.
- **Read-then-write with `center == result`:** the Burst bridge stages writes and publishes them after the
  tick (`PublishFrame`); every read in the tick sees the value from before it. `random-float3-in-radius`
  with center and result on one key therefore steps from the previous point: the test checks each step equals
  previous + (expected offset), 200 steps, flat and 3D.
- **Tests** (`Tests/Integration/NativeRuntime/NativeLibraryAliasedKeyTests.cs`, new):
  - `TheCasesCoverEveryLibraryNodeWithTwoHandlesOfOneValueType`: reflection over the standard shard finds the
    nine nodes whose configuration has two blackboard bindings of one value type: copy-bool, copy-int,
    copy-float, copy-float3, int-keys-at-least, float-keys-at-least, float3-within-distance, float3-distance,
    random-float3-in-radius; the cases must list exactly these.
  - `OneKeyOnBothHandles_GivesTheContractResult_OnProductionTreeHost` (9 cases): one update, Success, values
    unchanged, distance 0.
  - `EveryAliasedNode_RunsUnderEveryPolicy_OnNativeTreeAgents` (immediate, agent-frame, batched-jobs,
    pipelined-jobs): 3 agents of one tree holding all nine aliased nodes; values checked through `TryGetValue`.
  - `NativeLibraryRandomTests.RandomFloat3InRadius_CenterAndResultOnOneKey_StepsFromTheValueBeforeTheTick` (flat, 3D).
- **Before:** `F1\before-and-W5-after.txt`: `NativeLibraryAliasedKeyTests` 1/14 (13 fail with
  `NativeLifetimeStateInvalid`, all four policies and all nine host cases), the random case 0/2.
  **After:** `F1\after.txt`: `NativeLibraryAliasedKeyTests` 14/14, `NativeLibraryRandomTests` 36/36,
  `NativeTreeAgentValueReadTests` 10/10. The whole `AIBT.Integration.Tests` was run once on the first attempt
  (438/440, only the two job policies of the new test failing) and `AIBT.NativeTreeAgentBlackboard.Tests`
  33/33; after the final change only the three classes were rerun (the change only acts when two bindings
  of one node name one slot).

## F12 — three stale tests

- **Cause verified:** as the review says. `ZeroCustomNodesReturnsExactlyThePhase1BuiltInCatalog` counted the
  host project's sample nodes; `GeneratedArtifactContractTests.FixturePath` required
  `PackageInfo.FindForAssembly`, which is null when AIBT is embedded under `Assets/`.
- **Fix** (tests only, `7be4d4b`):
  - `ZeroCustomNodes…` sets aside only type IDs declared by non-test assemblies outside the package (asmdef
    path not under the package root): `[BtNode]` classes and native structs (`[AibtBurstNode]` and the short
    forms `[AibtAction]`, `[AibtCondition]`). It also asserts that none of them reuses a package ID and that
    the IDs are unique; the rest must equal the built-in set exactly. **Still means its name:** a package
    node added, removed or renamed without updating the expected set, or a duplicate, still fails; in a
    consumer project without custom nodes nothing is set aside. A first version missed the short forms
    (`aibt.samples.first-agent.move` is an `[AibtAction]`): `after-F12-F2-F7-F4-F3.txt`.
  - `FixturePath`: the Package Manager's path, else the folder holding `package.json` above the test
    assembly's asmdef; asserts that this `package.json` names `com.azzazello.aibt`. **Still means its
    name:** the fixtures are read from the package itself in both layouts; a wrong or missing fixture fails.
- **Assembly:** `GeneratedArtifactContractTests` is in `AIBT.CodeGen.GenerationTests`.
  **Before** (`F12\before-codegen-generation.txt`): 12/14, failing
  `EmittedMetadata_MaterializesConsumerDescriptorAndCompilesCanonicalV2Artifact` and
  `RegisteredCatalog_DrivesJsonValidationAndCompiledDefaultCodec`, both in `FixturePath`.
  **After** (`F12\after-codegen-generation.txt`): 13/14. `RegisteredCatalog_…` passes.
  `EmittedMetadata_…` now passes every assertion up to line 199 (the Agent-scope fixture compiles, header
  and stream checks pass) and fails on the pinned content hash of `Tests/Fixtures/P2/CodeGen/generated-compiled-v2.json`:
  expected `60dc5c12…3712`, got `426504d3…6595`. The pin dates from `567a29c` (2026-11-02); the fixture is
  format 2, so the F2 change does not reach it. **Not fixed, cause not investigated:** a stale pin or a real
  change of the compiled bytes; refreshing the pin would change a pinned compiled-format fixture, which
  needs the owner. This disagrees with the review's "product correct in all three cases" for that test.
- `McpToolDispatcherTests` before 7/8, after 8/8 (`after-F12-F3-corrected-tests.txt`).

## F2 — format 3 without subtree features refused by the native compiler

- **Cause verified:** `GeneratedCompiledProgramV2Compiler` compiles a document that does not need expansion
  as it is; `CompileDocument` refused `FormatVersion != 2` (AIBT2042), and so did `GeneratedScopeCompiler`
  for bindings. A tree that became format 3 when a reference was added stays format 3 after Unpack or after
  its last reference is deleted.
- **Fix** (`Authoring/Compilation/Generated/GeneratedCompiledProgramV2.cs`, +8 −0): at the start of
  `CompileDocument`, a format-3 document with no reference node and no interface is replaced by the same
  document with format 2 (a copy; the caller's document is unchanged). ADR-P11-001 rules relied on: §4.4
  identity rule (such a document is its own expansion), §4.4 step 5 (an expansion is a format-2 document),
  §4.5 (the program's semantic hash is the expansion's). Fixed in the compiler, not by lowering the file's
  format on Unpack: the compiler is the one place every path goes through (prepared assets, MCP
  `prepare_runtime`, the graph, files already saved as format 3); §4.1 keeps the file's format.
- **Tests** (`Tests/Editor/Consumer/SubtreeFormat3WithoutFeaturesTests.cs`):
  format 3 compiles without and with a resolver and through `SubtreeExpander.Expand`, with
  `CanonicalSemanticHash` and `CompiledContentHash` equal to the same tree in format 2; add a reference
  (format becomes 3), Unpack, prepare: the unpacked tree prepares, runs `TryCreateProgram`, and its semantic
  hash equals the hash of the tree with the reference.
- **Before** (`F2\before-sourceid-corrected.txt`): 0/2, both refused with "AIBT2042: … require tree format
  version 2" (the control, the tree with its reference, prepares). The first before-run
  (`before-F12-F2-F7-F4-F3.txt`) had an empty source ID in the test itself; corrected before the fix.
  **After** (`after-F12-F2-F7-F4-F3.txt`): 2/2; `Subtree*` 168/168, `GeneratedScopeCompilerTests` 14/14.
- **Docs:** one sentence added to `Documentation~/subtrees.md` (format section).

## F7 — MCP `runtime_hosts` and `read_trace` do not see native agents

- **Cause verified:** `McpRuntimeTools.FindHosts` used `FindObjectsByType<ProductionTreeHost>`, which skips
  objects with `DontSave`; `NativeTreeAgent` creates its host with `HideInInspector | DontSave`.
- **Fix** (`MCP/McpRuntimeTools.cs`, +8 −1): also the `Host` of every `NativeTreeAgent` found by
  `FindObjectsByType` (loaded scenes only: no prefab assets), non-null, distinct. Output fields unchanged.
- **Test** (`Tests/Editor/Mcp/McpRuntimeNativeAgentTests.cs`): a real agent with a prepared asset after one
  scheduler update: its host has the flags, is listed once with object name, scene path, updates > 0 and
  program hash; `Trace` by its `instanceId` answers; a stopped agent lists no host.
- **Before:** 1/2 (`hosts` empty). **After:** 2/2; `AgentConsumerToolsTests` 14/14, `McpDocumentationGeneratorsTests` 14/14.
- **Docs corrected:** `Documentation~/ai-and-mcp.md`, `MCP/Documentation/McpRecipesDocumentGenerator.cs`,
  `Documentation~/generated/recipes.md` said these hosts are not listed.

## F4 — one project scan per subtree-using tree in a tree-list refresh

- **Cause verified:** `BehaviorTreeGraphWindow.RefreshCatalog` called `TreeCatalogEntry.Read` per tree, and
  each tree that needs expansion called `ProjectTreeIndex.Scan()`.
- **Fix** (`TreeCatalogEntry.cs` +18 −1, `BehaviorTreeGraphWindow.Chrome.cs` +4 −5, `ProjectTreeIndex.cs` +4 for
  the internal `ScanCount`): `TreeCatalogEntry.ReadAll` shares one snapshot, taken the first time a tree
  needs it; the window calls it.
- **Test** (`Tests/Editor/Consumer/TreeListScanTests.cs`): three parents using one child and a plain tree:
  one scan, each parent expanded (3 nodes); no subtree users: no scan.
  **Before:** 1/2, "Expected: 1 But was: 3". **After:** 2/2.
- **Measurement** (`F4\probe-tree-list.cs`, `F4\measure.txt`): the window's own list (12 trees, 5 of them
  with subtree features, 4 of those in the scratch folder `Assets/AibtP11007Measure`), one refresh per
  block, the two variants interleaved, 6 invocations × 2 rounds. Per entry (the code before the fix):
  5 scans per refresh, 317, 483, 373, 423, 457, 447 ms (invocation means). `ReadAll`: 1 scan, 57, 95, 56,
  81, 88, 96 ms. CPU before each invocation 1–15 %, 7 Unity processes running. Folder deleted afterwards
  (`probe-cleanup-result.txt`).

## F3 — inspector staleness per repaint

- **Cause verified:** both inspectors called `ProductionTreeAssetStaleness.Evaluate` in `OnInspectorGUI`; for
  an asset prepared from subtrees it hashes the source with `ProductionTreeAssetBaker.CurrentSourceHash`,
  which scans the project's tree files.
- **Fix** (`ProductionTreeAssetStaleness.cs` +58, both inspectors +1 −1): `EvaluateForInspector` keeps each
  asset's state (weak table) until a tree file is imported, moved or deleted (`SourcePostprocessor`), the
  node catalog is rediscovered (`ProjectNodeCatalogCache.Changed`), or the asset's own source GUID, catalog
  type, catalog fingerprint, source hash or subtree GUIDs change (prepared again, reimported); a domain
  reload starts empty. `Evaluate` itself is unchanged (`SubtreeImportAndBakeTests` relies on it seeing an
  edit on disk before import). Choice: an edit on disk that Unity has not imported shows once it is imported.
- **Test** (`Tests/Editor/Consumer/ProductionTreeAssetInspectorStateTests.cs`): a subtree-prepared asset in a
  temporary folder; 20 repaints → 0 scans; edit the subtree and import → re-prepared and Current; catalog
  refresh → evaluated once; delete the subtree → `SourceChanged`, equal to a fresh `Evaluate`.
  **Before:** 0/1, "20 repaints … Expected: 0 But was: 20". **After:** 1/1 (`after-F12-F3-corrected-tests.txt`).
  The first after-run counted one more scan from an open AIBT Graph window that also listens to catalog
  changes; the count now starts after the catalog refresh.
- **Measurement** (`F3\probe-inspector-state.cs`, `F3\measure.txt`; the probe's eval budget is 5 s, so 6
  invocations × 4 interleaved rounds × 2 calls): `Evaluate` 78.9, 71.9, 84.5, 87.2, 73.7, 91.2 ms per call and
  one scan per call; `EvaluateForInspector` 6.7–8.2 µs per call (reflection call included) and no scan. 42 tree
  files in the project; CPU 6–15 % before each invocation.

## F5 — `AssetDatabase.Refresh()` after every recompile

- **Cause verified:** `ProjectTreeIndex` kept its registered values in a static dictionary; after every domain
  reload `RegisterAfterReload` scanned, `Register` found no previous value for every tree ID, called
  `RegisterCustomDependency` for each and `ScheduleRefresh` requested `AssetDatabase.Refresh()`. It
  registered every tree ID of the project even when no tree uses subtrees.
- **Fix** (`ProjectTreeIndex.cs`): the registered values and whether a tree uses subtrees are saved in
  `SessionState` whenever they change; after a reload they are restored instead of scanning; a new Editor
  session (no saved values) registers them all once as before. Nothing is registered while no tree uses
  subtrees and nothing was registered; the first tree that uses them registers all when it is imported.
  `TreeFilePostprocessor` and the registry-change path are unchanged, so real changes still re-import
  dependants. `RefreshRequests` (internal) counts refresh requests.
- **Test** (`Tests/Editor/Consumer/ProjectTreeIndexReloadTests.cs`): a simulated reload (static state cleared,
  `RegisterAfterReload` run) after the index is up to date: no scan, no refresh request, the same registered
  values; a scan root with one plain tree and nothing registered: nothing registered. The index's state is
  restored afterwards. **Before** (`F5\before-tests.txt`): 0/2 ("no walk … But was: 1"; a registered
  `tests.p11007.reload.plain`). **After** (`F5fter-tests.txt`): 2/2.
- **Real recompile** (`F5\probe-refresh-requests.cs`): before the fix, after `Invoke-UnityRecompile.ps1`:
  `refreshRequests 1, scans 1, registered 35` (`F5\before-real-recompile.txt`). With the fix
  (`F5\after-real-recompile.txt`): the first reload after the change (no saved values yet) 1, 1, 35; the next
  domain reload with nothing changed (`EditorUtility.RequestScriptReload`) `refreshRequests 0, scans 0,
  registered 35`.

## Protected paths not touched

`git diff --stat 4be2983 HEAD -- Tests/Editor/CodeGen/Contracts/ExpectedPublicAbiV2.txt CodeGen~ Documentation~/specifications package.json`
prints nothing. No existing public API changed; additions are internal (`ScanCount`, `RefreshRequests`,
`ReadAll`, `EvaluateForInspector`).

## Tests run

All runs EditMode through `Invoke-UnityTests.ps1`, classes by `-Filter` (one class per line), raw output in the files named.

| Run (file) | Result |
| --- | --- |
| `W5efore-integration-and-class.txt` (before W5) | `AIBT.Integration.Tests` 423/424; `NativeTreeAgentValueReadTests` 9/10 |
| `F1efore-and-W5-after.txt` (before F1) | `NativeTreeAgentValueReadTests` 10/10; `NativeLibraryAliasedKeyTests` 1/14; random case 0/2 |
| `F1fter-attempt1.txt` (first F1 attempt) | `AIBT.Integration.Tests` 438/440 (2 job-policy cases of the new test); `AIBT.NativeTreeAgentBlackboard.Tests` 33/33 |
| `F1fter.txt` | `NativeLibraryAliasedKeyTests` 14/14, `NativeLibraryRandomTests` 36/36, `NativeTreeAgentValueReadTests` 10/10 |
| `F12efore-codegen-generation.txt` | `AIBT.CodeGen.GenerationTests` 12/14 |
| `before-F12-F2-F7-F4-F3.txt` | `McpToolDispatcherTests` 7/8, `SubtreeFormat3WithoutFeaturesTests` 0/2, `McpRuntimeNativeAgentTests` 1/2, `TreeListScanTests` 1/2, `ProductionTreeAssetInspectorStateTests` 0/1 |
| `F2efore-sourceid-corrected.txt` | `SubtreeFormat3WithoutFeaturesTests` 0/2 (AIBT2042) |
| `F12fter-codegen-generation.txt` | `AIBT.CodeGen.GenerationTests` 13/14 (failure: `EmittedMetadata_…`, pinned hash) |
| `after-F12-F2-F7-F4-F3.txt` | `SubtreeFormat3WithoutFeaturesTests` 2/2, `McpRuntimeNativeAgentTests` 2/2, `TreeListScanTests` 2/2, `AgentConsumerToolsTests` 14/14, `GeneratedScopeCompilerTests` 14/14, `GraphEditorWorkflowTests` 15/15, `ProductionConsumerWorkflowTests` 12/12, `ProductionTreeAssetStalenessTests` 6/6, `Subtree*` 168/168, `TreeCatalogNativeStatusTests` 5/5, `TreeScopeSlotNeverWrittenNativeBindingTests` 19/19, `McpDocumentationGeneratorsTests` 14/14, `ProjectTreeIndexPackageTests` 4/4; `McpToolDispatcherTests` 7/8 and `ProductionTreeAssetInspectorStateTests` 0/1 (test corrections below) |
| `after-F12-F3-corrected-tests.txt` | `McpToolDispatcherTests` 8/8, `ProductionTreeAssetInspectorStateTests` 1/1 |
| `F5efore-tests.txt` | `ProjectTreeIndexReloadTests` 0/2 |
| `F5fter-tests.txt` (with every fix) | `ProjectTreeIndexReloadTests` 2/2, `Subtree*` 169/169 (includes `SubtreeImportAndBakeTests`, the dependency tests, unchanged), `ProjectTreeIndexPackageTests` 4/4, `TreeListScanTests` 2/2, `ProductionTreeAssetInspectorStateTests` 1/1, `ProductionTreeAssetStalenessTests` 6/6, `GraphEditorWorkflowTests` 15/15 |

Test corrections made after a before-run, before the fix was judged: F2 (empty source ID in the test), F12 (short-form
native node attributes), F3 (scan count started before a catalog change that an open graph window also answers).

## Console

After each recompile the Console's new entries were Unity's own "Release of invalid GC handle. The handle is
from a previous domain" (from `AssetPostprocessingInternal`) and two "Failed to load 'VirtualArtifacts/Primary/…'"
errors (`console-after-cycleC-recompile.txt`); none from AIBT code. The error entries naming AIBT are the
expected logs of tests (`LogAssert.Expect`).

## Not done / not verified

- F12: `EmittedMetadata_MaterializesConsumerDescriptorAndCompilesCanonicalV2Artifact` fails on its pinned hash;
  not investigated, not fixed (owner decision for the pin). The suite is not green in this assembly.
- F1: after the final adapter change only the three affected classes were rerun, not all of
  `AIBT.Integration.Tests`; `AIBT.NativeBurstDispatch.Tests` was not run.
- F3: an edit on disk that Unity has not imported is shown after the import (previously at once).
- F5: the custom dependency values are assumed to stay registered in the AssetDatabase across domain reloads
  within a session; checked indirectly (dependency tests green, no re-import needed), not by reading them back.
- Measurements are from one busy machine; the F3/F4 differences are 10× and more, not small.
- None of the nine full assemblies was run here (the orchestrator runs them).
