# P11-009 evidence — review fixes, batch 3 (F9, F10 native half, F8 write side, F6)

Branch `feature/aibt-consumer-workflow`, base `1d081c7`. Raw files: `C:\UnityProjects\_aibt_acceptance\P11-009\`.
Unity 6000.6.9f1, the Modules Editor (Pipeline port 7810). Findings from `Planning~/Evidence/P11-REVIEW/README.md`
and the stops of `Planning~/Evidence/P11-008/README.md`. Status: accepted by the orchestrator on `3d16480`
("Acceptance run" at the end); the item is in `Review`.

## Commits

| Commit | Finding | Result |
| --- | --- | --- |
| `c38e5bf` | F9 | fixed: Tree plus Agent/Shared keys compile and run; existing bytes unchanged |
| `2700107` | F10 native half | fixed: warning `AIBT2054` in graph, MCP edits, tree list, MCP `validate`, bake |
| `1e54b2f` | F8 write side | fixed: the write resolve and the adapter write check `T` |
| `33debb8` | F6 | fixed: MCP resolves subtrees against the Editor's file set |
| this commit | — | task card, work item, MASTER_PLAN, CHANGELOG, this evidence |

Every fix commit holds its test. "Before" runs were made with the test in place and the fix absent;
"after" runs with both. Only the tests that cover each change were run (the test script runs the whole
named assembly and reports the filtered classes too; both counts are given).

## F9 — a native tree with Tree plus Agent/Shared keys ended in AIBT3017

- **Cause (confirmed by probe):** `GeneratedScopeCompiler.BuildSlots` gives each scope its own zero-based
  offsets; `GeneratedCompiledProgramV2.BuildFinalSemanticProgram` labelled every semantic slot record
  `BlackboardScope.Tree` and kept the scope-local offset, so `CompiledProgram`'s per-scope overlap check saw
  two Tree ranges at offset 0. `F9\probe-bytes-before.txt` (probe `F9\probe-f9-bytes.cs`, Native Guard through
  `GeneratedCompiledProgramV2Compiler` with the project's generated descriptors): `tree+agent` and
  `tree+shared` → `AIBT3017 … Tree-scope blackboard memory ranges overlap`.
- **Fix** (`GeneratedCompiledProgramV2.cs` +45 −5): the semantic records keep the `Tree` label (Phase 1
  observers may watch only Tree slots, and the Agent-only fixture's observer watches an Agent key), but
  their offsets are placed in one combined space: Tree from 0, Agent past the end of Tree, Shared past
  the end of Agent, each start aligned to the scope's largest alignment; the semantic default blob is laid
  out in the same scope order. For a single-scope program every offset and default is what it was. The v2
  byte stream does not contain these records (it serialises the scope-grouped `GeneratedScopeSlot` list),
  so no format, ABI or existing byte changes.
- **Existing bytes unchanged:** `F9\probe-bytes-before.txt` vs `F9\probe-bytes-after.txt`, section
  `tree-only` (Native Guard, Tree keys only) is identical, including the full byte stream
  (`F9\native-guard-bytes-compare.txt`): content hash `1546cb72…f826`, length 5451, semantic hash
  `e2afaafe…0946`, the same semantic slots and defaults. `AIBT.CodeGen.GenerationTests` 14/14 with
  `generated-compiled-v2.json` and `minimal-compiled-v1.golden.json` untouched (`F9\after-neighbours.txt`).
- **After:** `tree+agent` compiles (`fc38a9d9…0b00`, 5782 bytes), `tree+shared` compiles (`2a9671ab…819d`,
  5786 bytes).
- **The review's other variant is a different cause:** binding an Agent key to a library node
  (`agent-bound-to-library-node` in the probe) gives `AIBT3015` + `AIBT3018` before and after. Library nodes
  declare Tree-scope bindings, so the binding's scope does not match the key; that is the intended
  refusal, not the overlap. `MixedScopeNativeTreeTests.ReviewVariant_AgentKeyBoundToALibraryNode_IsRefusedAsABindingScopeMismatch`
  pins it (AIBT3018 present, AIBT3017 absent).
- **Run with every scope's values checked:** a fixture node assembly
  (`Tests/Editor/CodeGen/GenerationP11009/`, shard `aibt.tests.p11009.mixed-scope`) with `agent-bump`
  (reads and increments an Agent Int32) and `mixed-report` (reads a Tree Bool, a Tree Int32 and the Agent
  Int32, writes a Tree Int32 = agent × 1000 + tree). Tree: keys `agent-counter` (Agent, 40), `ready`
  (Tree, true), `tree-in` (Tree, 7), `tree-out` (Tree, 3); root memory-sequence [bump, report].
  - `ProductionTreeHost`: defaults read back (40, 7, 3, true); external write `tree-in` = 9; an external
    read of the Agent key is refused (Tree-only API); after one update Success, `agent-counter` = 41,
    `tree-out` = 41009, `tree-in` = 9, `ready` = true.
  - `NativeTreeAgent`, three agents with `SetFact("tree-in", 5 + i)`, in seven profiles: interactive,
    background, forced Immediate, Budgeted, BatchedJobsSameFrame, PipelinedJobs and JobsAgentFrame; every
    completed run after the first reports `Success`, `ready=True`, `tree-in=5+i`, `tree-out=41005+i`,
    `agent-counter=41` (Agent values start from their default in every run: each restart is a fresh host).
  - **Before:** `F9\before.txt`: `MixedScopeNativeTreeTests` 1/11 (the only pass is the library-node
    variant). **After:** `F9\after-class.txt` 11/11; `F9\after-neighbours.txt`: `AIBT.Integration.Tests`
    452/452, `AIBT.CodeGen.GenerationTests` 14/14, `ReferenceCompilerTests` 18/18,
    `GeneratedScopeCompilerTests` 14/14, `NativeBlackboardProgramBindingTests` 11/11.
- **Shared scope:** a tree with Shared keys now compiles, but no host run was made with a Shared binding:
  the host adapter accepts only Tree and Agent accesses (`GeneratedTreeDispatchAdapterV2`, the scope check in
  the access path), so a native node with a Shared binding is refused at run time as before. Not changed;
  outside this brief.
- **For a user:** a native tree may now declare Tree keys next to Agent or Shared keys (for example an
  Agent contract added to Native Guard); before, it failed with an internal compiler error.

## F10 — native half: AIBT2054 warns about observer blocks in native trees

- **Code:** `AIBT2054` `TreeValidationDiagnosticCodes.ObserverNotEvaluatedByNativeHost`, default severity
  Warning, registered in the tree-validation catalog (free before: no use anywhere).
- **Where it is raised** (one helper, `ProjectNodeCatalog.NativeObserverWarnings`, for every tree without
  managed nodes; a managed tree keeps AIBT2051): `ProjectNodeCatalog.ValidateAuthoring` (graph and MCP
  edits, both overloads; with subtrees on the expanded tree), `TreeCatalogEntry` (tree list), MCP `validate`
  (with and without subtree references), `ProductionTreeAssetBaker` (prepare). The compiler is not touched,
  so the program bytes are the same; the warning comes from the Editor and MCP.
- **Message:** "Node 'guard' has an 'observer' block, but native hosts (NativeTreeAgent, ProductionTreeHost,
  ProductionTreeScheduler, TreeSimulation) do not re-evaluate observers: nothing re-checks the condition when
  a watched key changes, so the block changes no outcome. To re-check the condition on every update, make it
  an earlier child of 'aibt.core.reactive-sequence' or 'aibt.core.reactive-selector' (or their -retaining
  variants above a wait or a multi-step branch) and remove the block." Location `/nodes/guard/observer`.
- **Test:** `Tests/Editor/Validation/NativeObserverWarningTests.cs` (6): graph path warns once naming the
  node and no AIBT2051; same tree without the block: no warning; the compiler's own output has no AIBT2054
  and succeeds; tree list status `Warnings` with the message (without the block `Valid`); the baker warns
  and still bakes; MCP `validate` stays `valid: true` with one `warning` at `/nodes/guard/observer`.
- **Before:** `F10\before.txt`: 2/6 (the two "unchanged" tests pass). **After:** `F10\after-class.txt` 6/6;
  `F10\after-editor.txt`: `AIBT.Editor.Tests` 852/852 (`NativeObserverWarningTests` 6/6,
  `McpDocumentationGeneratorsTests` 14/14, `DiagnosticCodeUniquenessTests` 1/1, `ManagedObserverRefusalTests`
  3/3, `TreeScopeSlotNeverWrittenNativeBindingTests` 19/19); `F10\after-integration.txt`:
  `AIBT.Integration.Tests` 452/452, `NativeLibraryReactiveGuardTests` 9/9. The first after run had 850/852:
  the generated API reference lacked the new public field; `api-reference-authoring.md` got its line.
- **No test asserts "no diagnostics" for a tree with the block on these paths.** The test P11-008 stopped
  on (`NativeLibraryReactiveGuardTests`, the block "compiles, runs and changes nothing") compiles through the
  Authoring compiler, which is unchanged, and stays 9/9.
- **Seen where a user sees it** (probe tree `Assets/_P11009Probe/native-observer.aibt.json`, created for the
  capture and deleted afterwards): `F10\graph-native-observer.png` (graph: "1 warning" badge, the node's
  warning marker, the AIBT2054 line in Problems), `F10\tree-list-native-observer.png` (tree list: the tree is
  `Warnings`), `F10\mcp-validate-native-observer.json` (MCP `validate` on the real project: the AIBT2054
  warning; the AIBT2038 errors there come from this project's policy, which requires descriptions).
- **Docs:** README, `native-node-library.md` and the implementation-status note of
  `execution-semantics-v1.md` said the block "changes nothing"; each now names the warning. No rule changed.
- **For a user:** an observer block in a native tree shows a warning in the graph, the tree list, MCP
  `validate` and MCP edits; the tree runs as before.

## F8 twin — the host write let `T` and `expectedType` disagree

- **Cause verified:** `GeneratedTreeDispatchAdapterV2.TryResolveExternalTreeWrite` (non-generic) compared
  `expectedType` with the slot and nothing about `T`; `TryWriteExternalTreeValue<T>` checked only the size
  and alignment of `T`.
- **Fix** (adapter +38 −1, host +4 −1): a generic internal adapter overload `TryResolveExternalTreeWrite<T>`
  adds `BuiltInManagedType<T>.TypeId == expectedType.TypeId` and the size/alignment of `T` to the type check
  (the same place and order as the read resolve of P11-008, so a mismatched `T` is `TypeMismatch` →
  `BlackboardTypeMismatch`); `ProductionTreeHost.TryResolveExternalTreeWrite<T>` calls it. The adapter's
  `TryWriteExternalTreeValue<T>` refuses a `T` whose managed type id differs from the slot's
  (`TypeMismatch`). The non-generic adapter method is kept (internal). Public signatures unchanged; no ABI
  member; `production-host.md` and the method's doc comment say so.
- **Callers (none refused):** `Samples~/SwarmArena/Runtime/Scene/SwarmArenaAgent.cs` (and its imported copy
  in `Assets/SwarmArena`), `Benchmarks~/Phase7/SwarmArena/…/BenchmarkAgent.cs`: `Float3Value`/Float3 ×4;
  `Spikes~/PatrolPursuit/Runtime/ResearchActor.cs`: `Float3Value`/Float3, `float`/Float32, `bool`/Bool,
  `int`/Int32; `NativeTreeAgent.FactSlot<T>` derives `expectedType` from `T` (its fact types are a subset of
  the mapping); tests: `float`/Float32, `int`/Int32, `bool`/Bool, `long`/Int64 and the generic kits
  (`NativeLibraryTestKit`, `StandardNativeShardTests`) pass `T` with its own built-in. The existing
  `ResolveExternalTreeWrite_TypeMismatch_Rejects` (`int` with Int32 on a Float32 key) was already a refusal.
- **Tests:** `ExternalTreeBlackboardWriteTests.ResolveExternalTreeWrite_TIsNotTheManagedTypeOfTheDeclaredType_Rejects`
  (`int`, `uint` and a struct with one `float` refused for a Float32 key; `float` accepted and written) and
  `WriteExternalTreeValue_AtTheAdapter_RefusesATThatIsNotTheSlotsManagedType` (`uint` refused, `float` written).
- **Before:** `F8\before.txt`: `ExternalTreeBlackboardWriteTests` 9/11 (both new tests: "Expected: False But
  was: True"); assembly 452/454. **After:** `F8\after-and-F6-before.txt`: `AIBT.Integration.Tests` 454/454:
  `ExternalTreeBlackboardWriteTests` 11/11, `ExternalTreeBlackboardReadTests` 11/11,
  `NativeTreeAgentValueReadTests` 10/10, `NativeTreeAgentTests` 16/16, `ProductionTreeWorldEffectTests` 6/6,
  `StandardNativeShardTests` 3/3, `NativeBindingInputNoteHostRuleTests` 1/1, `MixedScopeNativeTreeTests` 11/11;
  `ProductionConsumerWorkflowTests` 12/12 (Editor).
- **Allocation-free write:** `ExternalTreeBlackboardWriteTests.ExternalWrite_RepeatedAfterWarmup_AllocatesNoManagedMemory`
  (`Is.Not.AllocatingGCMemory()`, a warmed handle) passes in the after run. The new check reads a static
  readonly per-`T` value; its static initializer runs once per `T`, outside the per-frame path.
- **For a user:** `TryResolveExternalTreeWrite<T>` with a `T` that is not the built-in's own managed type
  (`uint` for Int32, `Unity.Mathematics.float3` or an own struct for Float3) now fails with
  `BlackboardTypeMismatch` instead of writing reinterpreted bits.

## F6 — MCP and the Editor resolved subtrees against different file sets

- **Probe first** (`F6\probe-f6.cs`, before the change → `F6\probe-f6-before.txt`):
  - synthetic layout (a temporary `Assets` plus a package root): a copy of a subtree in `Old~/`,
    `.backup/` or `CVS/` → Editor `Found Assets/Trees/…`, MCP `Ambiguous` (AIBT3021); a tree only in
    `Samples~/` or a file named `.dotfile.aibt.json` → Editor `NotFound`, MCP `Found`; a subtree only in the
    package → Editor `Found Packages/com.example.trees/…`, MCP `NotFound` (AIBT3020);
  - this project: the Editor sees 39 tree files, MCP 120; **35 of 58** tree IDs resolve differently, among
    them the review's case `samples.native-guard` (Editor `Assets/Samples/AIBT/Native Guard/…`, MCP
    ambiguous with `Assets/AIBT/Samples~/NativeGuard/…`), the other installed samples, every golden fixture
    tree (ambiguous with copies under `Tools~/Verification/TestResults/`), and trees only in `Benchmarks~/`,
    `Spikes~/`, `Samples~/` and `Planning~/` (MCP `Found`, Editor `NotFound`).
- **Fix** (Editor rule as the reference): `ProjectTreeIndex.RootsFor(assetsDirectory)` gives MCP the Editor's
  roots (`Roots()`: `Assets` and every registered package) when the folder is this project's `Assets`, and
  the folder alone otherwise (a temporary or another project's folder, whose packages this Editor does not
  know); `ProjectTreeIndex.TreeFiles` is the Editor's own walk with its skip rule (folders starting with '.',
  ending with '~', `cvs`; files starting with '.'). `McpSubtrees.SubtreeFiles` parses those files; a tree in
  a package gets its asset path (`Packages/<name>/…`) as source ID, a tree under `Assets` keeps each tool's
  existing source ID. Used by MCP `validate`, `compile`, edits (`McpSubtrees.Resolver`), `prepare_runtime`,
  `extract_to_subtree` (resolver and the tree-ID uniqueness check) and `find_subtree_usages`.
- **Not changed on purpose:** looking up the tree a tool works on by its ID (`LoadTreeOrThrow`, the project
  manifest) still scans `Assets` as before, so MCP finds, validates and edits every tree file it found before.
- **After:** `F6\probe-f6-after.txt`: 39/39 files, **0 of 35** tree IDs resolved differently; the `~`/`.`/`CVS`
  copies no longer make references ambiguous. (The synthetic package case is still `NotFound` there because
  the probe calls MCP on a temporary folder, which by design gets no package roots; the package path is
  covered by the test with explicit roots.)
- **Tests:** `McpSubtreeToolsTests.ACopyInAFolderUnityDoesNotImport_IsIgnoredByMcp_AsByTheEditor` (3 cases:
  resolver, `validate`, `compile`, `find_subtree_usages`), `ASubtreeInAPackage_ResolvesForMcp_AsForTheEditor`,
  `McpRoots_AreTheEditorsRoots_ForThisProjectsAssetsFolder_AndTheFolderAloneElsewhere`.
  **Before:** `F8\after-and-F6-before.txt`: the 3 cases fail ("Expected: Found But was: Ambiguous"),
  `McpSubtreeToolsTests` 24/27 (the other two tests use the new seam and were added with the fix).
  **After:** `F6\after-editor.txt`: `AIBT.Editor.Tests` 857/857 (`McpSubtreeToolsTests` 29/29,
  `ProjectTreeIndexPackageTests` 4/4, `AibtTreeDiscoveryTests` 4/4, `McpDocumentationGeneratorsTests` 14/14,
  `SubtreeGraphEditorTests` 13/13).
- **Does MCP lose trees it finds today in a normal project? Judged no; stated for the orchestrator:** only as
  subtree targets, and only files in folders Unity does not import (`.`/`~`/`cvs` under `Assets`) or
  dot-files. Those are not assets: the Editor and the import already report a reference to them as AIBT3020,
  so MCP accepted references the Editor then refused. In a consumer project the package sits in `Packages/`
  and its `Samples~` was never seen by MCP. Every loss in the probe is in this development project's `~`
  folders (`Samples~`, `Benchmarks~`, `Spikes~`, `Planning~`, `Tools~`). If the owner wants MCP to keep
  resolving such files, the alternative is a second rule in MCP, which is the disagreement the review asked
  to remove.
- **For a user:** MCP tools resolve a subtree reference to the same file as the graph and the import: no
  false AIBT3021 because of a copy in a hidden folder, and subtrees in packages resolve.

## Not verified

- The ten assemblies were not run (as the brief says); only the classes listed above, each with its whole
  assembly reported by the script: `AIBT.Integration.Tests` 454/454, `AIBT.Editor.Tests` 857/857,
  `AIBT.CodeGen.GenerationTests` 14/14 (last runs).
- F9: no host run with a Shared-scope binding (the host adapter refuses Shared accesses; unchanged).
  `NativeCompiledProgramV2Verifier` (the NativeProgramImageOwner V2 path, not used by the production host)
  compares semantic and binding slots one by one; it was not run on a mixed tree.
- F9 follow-ups found, not fixed: (1) a node that uses one ReadWrite binding for both a read and a write
  opens two value sessions; in a catalog whose largest case has no spare bindings the per-frame session
  capacity (`GeneratedBurstCatalogV2.CreateWorkspaceCapacity`) is exceeded and the host stops with
  `NativeLifetimeStateInvalid` ("Callback rejected: CapacityExceeded"); the fixture was split into two nodes.
  (2) Agent-scope values start from their default in every `NativeTreeAgent` run, because each restart
  builds a fresh host; whether Agent memory should survive a restart is not specified here.
- F10: the bake returns the warning with its diagnostics, but the graph's "Prepare runtime asset" shows bake
  diagnostics only on failure, so a user sees AIBT2054 there through the graph's own validation. For a tree
  with subtrees the bake warns only for the parent's own nodes (the graph's validation uses the expanded
  tree). No screenshot of an MCP edit; that path is the graph's `ValidateAuthoring`, covered by the test.
- F6: MCP on a folder that is not this project's `Assets` gets no package roots (temporary test projects,
  another project's path); `LoadTreeOrThrow` and the manifest still list trees in hidden folders (unchanged).
- Console after the runs: the tests' expected errors plus Editor messages ("Release of invalid GC handle",
  "Failed to load 'VirtualArtifacts/…'") not from AIBT code; their origin was not traced.
- Not checked in a Player build.

## Acceptance run (orchestrator, 2026-11-23)

Read by the orchestrator on `3d16480` (clean working tree): this file, the session's report, the
whole diff `1d081c7..3d16480` under `Authoring/`, `Runtime/`, `Editor/`, `MCP/`,
`Documentation~/specifications` and the two changed asmdef files; two screenshots.

- **Protected paths.** `git diff --stat 1d081c7..3d16480` is empty for
  `Tests/Editor/CodeGen/Contracts`, `Tests/Fixtures`, `CodeGen~` and `package.json`. Under
  `Documentation~/specifications` one line: the implementation-status note of P11-008 names
  `AIBT2054`; no rule changed.
- **F9, existing bytes.** Checked by reading, not only by the probe: `GeneratedScopeCompiler.BuildSlots`
  emits the slots scope by scope (Tree, Agent, Shared), each scope sorted by key ID with the same
  comparer the semantic projection sorts with. For a tree with keys of one scope the new default
  blob order therefore equals the old one and every scope base is 0, so offsets and defaults are
  what they were. `F9\native-guard-bytes-compare.txt`: the Tree-only section is identical including
  `bytesHex`. Both pins pass untouched in the full run.
- **F8 twin.** One added condition in the write resolve and one in `TryWriteExternalTreeValue<T>`;
  public signatures unchanged.
- **F6.** `F6\probe-f6-before.txt` / `probe-f6-after.txt`: in this project MCP saw 120 tree files
  and the Editor 39 before, 39 and 39 after; the synthetic layout's six cases agree after the fix
  except a package outside this project, which a foreign folder does not get by design.
- **F10.** `F10\graph-native-observer.png` and `F10\tree-list-native-observer.png` opened: the
  problem list shows `AIBT2054` on node `guard`, the header "1 warning", the tree list "Warnings".
  The probe folder `Assets/_P11009Probe` of the parent project is gone.
- **Full run of ten assemblies** on `3d16480`, 2026-11-23 00:21-00:34; Editor-log counters did not
  move (unknown-command 1, main-thread-timeout 3 before and after). Raw output:
  `C:\UnityProjects\_aibt_acceptance\P11-009\acceptance\full-run-3d16480.txt`.

| Assembly | `7773059` | `3d16480` | New tests |
|---|---|---|---|
| `AIBT.Runtime.Tests` | 815/815 | 815/815 | - |
| `AIBT.Integration.Tests` | 441/441 | 454/454 | `MixedScopeNativeTreeTests` 11, `ExternalTreeBlackboardWriteTests` +2 |
| `AIBT.Editor.Tests` | 846/846 | 857/857 | `NativeObserverWarningTests`, new cases in `McpSubtreeToolsTests` (11 in all) |
| `AIBT.BehaviorCases.Tests` | 65/65 | 65/65 | - |
| `AIBT.NativeBurstDispatch.Tests` | 79/79 | 79/79 | - |
| `AIBT.NativeTreeAgentBlackboard.Tests` | 33/33 | 33/33 | - |
| `AIBT.NativeSharedBlackboard.Tests` | 36/36 | 36/36 | - |
| `AIBT.CodeGen.ContractTests` | 6/6 | 6/6 | - |
| `AIBT.CodeGen.GenerationTests` | 14/14 | 14/14 | - |
| `AIBT.PlayMode.Tests` (PlayMode) | 2/2 | 2/2 | - |

Not done by the orchestrator: the new test files and the documentation pages were not read line by
line; no probe was run again; the MCP `validate` response was not opened.

To know (found by the session, not fixed, reported to the owner):
- A native node that reads and writes through one ReadWrite binding opens two value sessions; in a
  catalog whose largest case has no spare bindings the per-frame session capacity is exceeded and
  the host stops with `NativeLifetimeStateInvalid`. The test fixture was split into two nodes to
  avoid it. This is a defect of its own, of the same class as F1.
- Agent-scope values start from their default in every `NativeTreeAgent` run (each restart builds a
  fresh host); whether they should survive a restart is not specified.
- A tree with Shared keys compiles now, but a native node with a Shared binding is still refused
  by the host at run time, as before.
- `NativeCompiledProgramV2Verifier` (not used by the production host) was not run on a mixed tree.
- MCP's tree lookup by ID and its manifest still list trees in folders Unity does not import; only
  subtree resolution follows the Editor's rule.
