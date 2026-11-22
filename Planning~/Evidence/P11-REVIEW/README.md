# P11-REVIEW — production-readiness review of P11-001 … P11-005 (2026-11-22)

**Verdict: `ready with conditions`.** No blocker was found: no memory-unsafe access, no crash, no
data loss on a normal path, no silent wrong result on a documented path. The P11 work is careful and
honestly documented. But a first production user will hit real defects. Fix these before a release:

1. A tree that binds one key to two handles of one native node (for example `copy-int` from `k` to `k`,
   or `random-float3-in-radius` with `center == result`) is valid, then its host faults on every
   update (F1, run).
2. A format-3 tree that no longer has a reference cannot be compiled natively. This happens after
   Unpack or after deleting the last reference (F2, run).
3. The subtree feature makes Editor paths walk the whole project tree on disk. A selected
   `NativeTreeAgent` in Play Mode, every tree-list refresh, and every domain reload pay for it (F3–F5,
   measured).
4. MCP and the Editor resolve subtrees against different file sets (F6). MCP `runtime_hosts` and
   `read_trace` cannot see a `NativeTreeAgent` (F7, run).
5. Some API and contract decisions are expensive to change after release: the public host read (F8),
   the Agent-scope refusal (F9), observers on managed agents (F10), the duplicate codes AIBT2042 and
   AIBT2043 (F11), and the random-seed derivation (F13).
6. Make the test suite green, honestly (F12): the three "known" failures are stale tests, not product
   defects.
7. Make one Player build (IL2CPP), and import the Native Guard sample once through the Package Manager
   (F14).

**Counts.** Blocker 0. Fix before release 14. Can wait 15. Note 8. Unconfirmed 10.

| | |
|---|---|
| Reviewed range | `git diff 2e6b9c7..ad1e581` in `Assets/AIBT`: 270 files, +28,058/−277. |
| Production files | 101 production, sample, schema and package files: +8,493 lines (including `CHANGELOG.md` +283 and `README.md` +25). |
| Coverage | Every one of those files was read by at least one reader, in full or as its diff with surrounding code. |
| Readers | Four parallel read-only readers (subtrees; native library; runtime host; Editor and MCP), plus my own reading. |
| Readers' totals | About 105 files and about 12,000 lines, surrounding code and tests included. |
| My own reading | About 30 files and about 2,500 lines: hot-path runtime diffs, the compiler, inspectors, importer, MCP discovery, evidence files, docs and tests. |
| Findings | Every finding in the table was re-checked by me, by reading or by a probe. |
| Probes | 5 probes, 12 runs, all through `unity command eval_file` against the open Modules Editor. No file was added to either checkout, and the open scene was not dirtied. |
| Probe files | Sources and raw output are in `C:\UnityProjects\_aibt_acceptance\P11-review\probes\`. |
| Tests | No test was run. The last full run (orchestrator, `bb56c00`) was used as given. |
| Product changes | None. |

## Findings, by severity

Each finding gives where it is, a scenario, how it was verified, and a fix. "Read" means verified by reading the cited lines. "Run" means a probe output proves it. "Measured" means timed in the Editor.

### Fix before release

**F1. One key bound to two handles of one native node.** P11-002, runtime and compiler.
- **Where:** `Runtime/Integration/GeneratedDispatch/GeneratedTreeDispatchAdapterV2.cs:962`, about `:1019` (each blackboard binding gets its own live region); `Runtime/Execution/Burst/Dispatch/NativeBurstDispatchBindingValidationV2.cs:297-313` (refuses two bindings of one slot at different offsets); no compile rule in `GeneratedScopeCompiler.cs:312-327`.
- **Scenario:** `copy-int {from: i, to: i}` and `int-keys-at-least {left: i, right: i}` both compile `success=True`. Then every `DriveOneUpdate` ends with `failure=NativeLifetimeStateInvalid`, and the root never reports a result. The control trees with two different keys succeed. The same applies to copy-*, *-keys-at-least, float3-distance, float3-within-distance and random-float3-in-radius. The realistic case is "wander from my point", `center == result`.
- **Verified:** run, probe C. The diagnostic code names the wrong cause. The runtime rule dates from `360bbe7`, but P11-002 added nine nodes where aliasing is natural.
- **Fix:** in `DispatchSlot.TryCreate`, reuse the live offset of an earlier blackboard binding with the same slot, as the snapshot path already does (`:940-942`). As a stopgap, add a compile error.

**F2. A format-3 tree without references or an interface fails native compilation.** P11-001, compiler.
- **Where:** `GeneratedCompiledProgramV2.cs:295` sends a document that needs no expansion to `CompileDocument`. `:366-367` and `GeneratedScopeCompiler.cs:170` refuse format ≠ 2. `SubtreeGraphEdits.cs:112-114` raises the format to 3. Unpack (`:349`) and node deletion keep it.
- **Scenario:** a native tree with one reference is unpacked (graph or MCP `unpack_subtree`), or the reference is deleted, and the tree is then prepared. Result: `AIBT2042 Generated bindings and Agent/Shared scopes require tree format version 2. | AIBT2042 Generated compiled format v2 requires tree format version 2.` The same tree as format 2 compiles. The managed path compiles both.
- **Verified:** run, probe E, with the Native Guard tree relabelled as format 3.
- **Fix:** treat a format-3 document without subtree features as format 2 in the identity path (its semantic hash must match the v2 tree, ADR §4.5). Or lower the file to format 2 when its last reference or interface is removed. Add a test.

**F3. The agent inspector runs a full project scan and hashing on every frame in Play Mode.** P11-001, Editor performance.
- **Where:** `Editor/Graph/NativeTreeAgentInspector.cs:18` calls `ProductionTreeAssetStaleness.Evaluate` on every `OnInspectorGUI`, and `:46` calls `Repaint()` unconditionally in Play Mode. `ProductionTreeAssetStaleness.cs:90-92` calls `ProductionTreeAssetBaker.CurrentSourceHash` for an asset prepared from subtrees. That hash runs `ProjectTreeIndex.Scan()` plus SHA-256 of every subtree file (`ProductionTreeAssetBaker.cs:104-130`). `ProductionTreeAssetInspector.cs:14` does the same on every repaint.
- **Scenario:** select a `NativeTreeAgent` whose asset was prepared from a tree with subtrees, in Play Mode. Every Editor frame then pays the cost at least twice (Layout and Repaint events).
- **Measured** (probe G, this project, 40 tree files):

  | Operation | Median | Min–max |
  |---|---|---|
  | `CurrentSourceHash` | 85 ms | 49–118 ms |
  | Plain SHA-256 of the same file | 0.2 ms | |

  That is about 6 fps or less for the whole Editor while the agent is selected. This was not observed live: no subtree-prepared agent exists in this project.
- **Fix:** cache the state per asset, invalidated by the tree-file postprocessor and catalog changes.

**F4. Every tree-list refresh walks the disk once per subtree-using tree.** P11-001, Editor performance.
- **Where:** `Editor/Graph/TreeCatalogEntry.cs:61` runs `ProjectTreeIndex.Expand(..., ProjectTreeIndex.Scan())` for each tree that requires expansion. `BehaviorTreeGraphWindow.Chrome.cs:449-454` calls it for every listed tree. `BehaviorTreeGraphWindow.cs:303-307` refreshes on every `ProjectChanged`, in every open graph window.
- **Measured:** a warm `Scan()` takes 82–85 ms median (48–116 ms) in this project. With M subtree users, each asset save costs about M × 85 ms per window.
- **Fix:** scan once per refresh and pass that snapshot as the resolver.

**F5. Every domain reload re-registers all tree dependencies and forces `AssetDatabase.Refresh()`.** P11-001, Editor.
- **Where:** `ProjectTreeIndex.cs:42-43`: `s_registered` and `s_files` are static, so they are empty after a reload. `:115-121`: `Register` then returns true for every tree ID. `:123-127` and `:148-157`: `ScheduleRefresh()` calls `AssetDatabase.Refresh()` on `delayCall`.
- **Scenario:** any project with at least one tree, on every recompile and every Enter Play Mode with domain reload. This happens even when no tree uses subtrees. It also overrides a user's "Auto Refresh: off".
- **Verified:** read. A cold scan was measured at 66–136 ms (probe G). The `Refresh` cost was not measured; it grows with project size.
- **Fix:** keep the registered values in `SessionState`, or compare against `AssetDatabase` before scheduling. Skip registration when no tree uses subtrees.

**F6. MCP and the Editor resolve subtrees against different file sets.** P11-001, MCP.
- **Where:** MCP uses `MCP/McpSubtrees.cs:68-69` → `AibtTreeDiscovery.Scan` → `Authoring/Model/Tree/TreeSourcePaths.cs:25-28`, which searches `AllDirectories` under `Assets/` only, including `~` and dot folders. The Editor and import use `ProjectTreeIndex`, which skips `.`, `~` and `cvs` folders and includes local packages.
- **Scenario:** in Modules, `samples.native-guard` exists in `Assets/AIBT/Samples~/NativeGuard/` and in `Assets/Samples/AIBT/Native Guard/`. MCP validate, compile and prepare report AIBT3021 (ambiguous) for a reference to it, while the Editor resolves it. A subtree inside a package imports in the Editor but is AIBT3020 in MCP. The `extract_to_subtree` uniqueness check also misses package trees. This is recorded as a deviation in the P11-001 stage 4 evidence; it is unresolved.
- **Verified:** read.
- **Fix:** back `McpSubtreeResolver` with `ProjectTreeIndex.Roots()` and its skip rules.

**F7. `runtime_hosts` and `read_trace` cannot see the host of a `NativeTreeAgent`.** MCP. Known risk 5.
- **Where:** `MCP/McpRuntimeTools.cs:206` uses `FindObjectsByType<ProductionTreeHost>`. `Runtime/Integration/NativeTreeAgent.cs:310-311` creates the host with `HideFlags.HideInInspector | HideFlags.DontSave`.
- **Verified:** run, probe H. A plain host is found; a host with those flags is not (`plainHostFound: true, hiddenHostFound: false`). Native agents are the main P11 runtime, so the AI tools are blind to them.
- **Fix:** enumerate `NativeTreeAgent` instances and their `Host`, or use `Resources.FindObjectsOfTypeAll` filtered to scene objects.

**F8. The public host read API lets `T` and `expectedType` disagree.** P11-004, API.
- **Where:** `GeneratedTreeDispatchAdapterV2.cs:378-381` checks `expectedType` against the slot, but checks only the size and alignment of `T`. The public entry point is `ProductionTreeHost.cs:455`.
- **Scenario:** `TryResolveExternalTreeRead<int>("speed", BuiltIn(Float32), …)` succeeds, and every read returns the float's bits as an `int`, silently. `NativeTreeAgent.TryGetValue` is safe because it derives the type from `T`. The P7-039 write API has the same shape.
- **Verified:** read. No test covers it.
- **Fix:** derive `expectedType` from `T` (as `FactSlot<T>` does) or check `T` against the built-in descriptor. Decide before release, because it is a public signature.

**F9. A native tree with an Agent-scope key fails with an internal error.** Compiler. Known risk 3.
- **Where:** `GeneratedCompiledProgramV2.cs:396-401`: a caught `ArgumentException` from `CompiledProgram` validation (`Runtime/Compiled/Model/CompiledProgram.cs:281`) is reported as AIBT3017.
- **Scenario:** add one unbound Agent-scope key (with a `blackboardContracts.agent`) to the Native Guard tree. The result is `AIBT3017 Generated compiled v2 construction failed: Tree-scope blackboard memory ranges overlap.` If the key is bound to a library node, the result is AIBT3015 plus AIBT3018. The library nodes are documented as Tree-scope only.
- **Verified:** run, probe B. Policies that report `supportsAgentScope: true` (`ProjectNodeCatalog.cs:71`, `ProjectTreeIndex.cs:65`, `McpSubtrees.cs:61`) admit what native compilation cannot build. The only test of an Agent-scope generated compile (`GeneratedArtifactContractTests`, fixture `generated-tree-v2.aibt.json`, Agent key only) never runs in this layout (F12). Probable cause, not proven: the semantic projection turns every key into a Tree `Int32` key (`:570-598`). This is pre-existing, not caused by P11.
- **Fix:** find the cause and fix it. If Agent scope stays unsupported, refuse it in validation with its own diagnostic, and make the policies say so.

**F10. Observers.** Validation versus runtime. Known risk 1.
- **Managed agents:** `ManagedTreeInstance.cs:127` passes `ReferenceObserverConditionRegistry.Empty`. When a watched key changes, `ReferenceExecutionMachine.cs:2543-2548` faults with AIBT4002 "No explicit observer condition evaluator is bound". A tree that validated cleanly therefore stops at run time. No document says so: `README.md:81` and `:332` describe `interrupt` only.
- **Native hosts:** the block compiles and does nothing. This is documented (`production-host.md:46-53`, `native-node-library.md:172-175`, `README.md:293`).
- **Specification:** still describes observers (`execution-semantics-v1.md:225`, `update-phases-v1.md:8`).
- **Verified:** read (managed fault). No probe was run.
- **Fix (cheapest sound):** make validation refuse an `observer` block for the managed policy, and warn on native. Mark the specification section "reference executor only" until the queue is implemented on hosts.

**F11. AIBT2042 and AIBT2043 each have two meanings.** Diagnostics contract. Known risk 5.
- **AIBT2042:** means `MigrationApplied` (Info, `TreeValidationDiagnostics.cs:47`) and `MissingScopeContract` (Error, `ReferenceCompilerDiagnostics.cs:17`).
- **AIBT2043:** means `TreeScopeSlotNeverWritten` (Info, `:55`) and `ScopeContractMismatch` (Error, `:18`).
- **Why it matters:** P11-003 made AIBT2043 a visible note on every native tree. The recipes tell users to filter on these codes. Probe E shows AIBT2042 reported twice for one cause.
- **Verified:** read. This is pre-existing.
- **Fix:** renumber the compiler codes before release. Diagnostic codes are a public contract, and changing them later breaks users' filters.

**F12. Three stale tests hide the real state of the suite.** Tests. Task question 5.
- **`ZeroCustomNodesReturnsExactlyThePhase1BuiltInCatalog`:** fails because the host project contains sample nodes (`aibt.samples.first-agent.move`, `aibt.samples.swarm.{attack-with-cooldown,steer,within-radius}`, `samples.simple.next-waypoint`, from `Assets/Samples/AIBT/…` and `Assets/SwarmArena/`). The failure message is in the stored full run `_aibt_acceptance/P11-005/acceptance/full-run-bb56c00.txt`.
- **The two `GeneratedArtifactContractTests` failures:** come from `FixturePath` (`Tests/Editor/CodeGen/Generation/GeneratedArtifactContractTests.cs:479-485`). It asserts `PackageInfo.FindForAssembly != null`, which is null when AIBT is embedded under `Assets/`.
- **Product:** correct in all three cases.
- **Cost:** the suite is never green, so a new failure in the same class is easy to dismiss. The assembly `AIBT.CodeGen.GenerationTests` is also not one of the nine assemblies run, so its Agent-scope compile test never runs (see F9).
- **Fix:**
  - Build the catalog expectation from package assemblies only: exclude shards and `[BtNode]` types from assemblies outside the package.
  - In `FixturePath`, use the `Application.dataPath/AIBT` fallback that `EditorTestPackagePaths.Resolve` already uses.
  - Add `AIBT.CodeGen.GenerationTests` to the full run.

**F13. The random seed is not reproducible across sessions.** Determinism. Known risk 2.
- **Where:** the stream is f(seed, semantic hash, tree instance id, node), and the id is `Interlocked.Increment(ref s_nextTreeInstanceId)` (`ProductionTreeHost.cs:43, 271, 274-275`). `NativeTreeAgent` makes a new host per run (`NativeTreeAgent.cs:309-312`).
- **Scenario:** with Enter Play Mode Options (no domain reload), a different enable order, or a run that ends at a different frame, the same `RandomSeed` draws other sequences. Edit-mode hosts created in the same domain shift the ids too.
- **Verified:** read. This is documented honestly (`native-node-library.md:221-229`). The tests prove the derivation only, because they reset the counter by reflection.
- **Fix:** decide now. Changing the derivation later changes every recorded seeded behaviour. The cheapest sound fix is to derive the stream id from an agent-owned value (a serialized agent salt or stable id plus the agent's run index), passed into `TryBootstrap`. Do not reset the global counter.

**F14. No Player build and no Package Manager import was ever exercised for P11.** Process. Known risk 7 and risk 5.
- **Not built in a Player:** the 33 Burst nodes, the `random-float3-in-radius` series math, generic `TryGetValue<T>` under IL2CPP, and subtrees in a built player.
- **Sample import:** the Native Guard sample was copied into the project by hand. The `package.json` entry was never used.
- **Fix:** one IL2CPP Player smoke run of Native Guard plus one subtree tree, and one PM import of the sample into a clean project, before release.

### Can wait

**W1. Expansion has no size budget and repeats errors per reference.**
- **Where:** `SubtreeExpander.cs:215-220`, `:294-297`, `:304`.
- **Run (probe E):** trees whose root holds 4 references to the next tree grow ×4 per level.

  | Depth | Expanded nodes | Time |
  |---|---|---|
  | 6 | 5,461 | 179 ms |
  | 7 | 21,845 | 808 ms |

  Depth 8 (the maximum) would be about 87,000 nodes. With a broken leaf type, the error is repeated per copy: the time grows ×5 per level, to 475 ms at depth 7.
- **Impact:** import runs this on every re-import.
- **Fix:** an expanded-node budget diagnostic, and report each subtree's own errors once.

**W2. `float3-distance` and `float3-within-distance` overflow in `float`.**
- **Where:** `StandardNativeConditions.cs:251-260`, `StandardNativeActions.cs:375-377`.
- **Effect:** a distance of 2e19 makes `float3-distance` fail; the docs list only unstorable results as failures. `distance ≥ 1.85e19` makes `inf <= inf` pass for any far pair.
- **Verified:** read.
- **Fix:** square and compare in `double`.

**W3. A failed import expansion shows the wrong error at run time.**
- **Where:** `Editor/Graph/BehaviorTreeImporter.cs:39-48` stores the raw v3 JSON. `Agents/BehaviorTreeSource.cs:136-138` then compiles without a resolver and reports AIBT3020 "no subtree resolver was given" instead of the real AIBT3026 or similar error. The import status itself is right.
- **Verified:** read.
- **Fix:** store the expansion diagnostics in the source and return them.

**W4. Errors found only after expansion point to nodes that exist in neither file.**
- **Where:** `ReferenceCompiler.cs:117-121` validates the expanded document under the parent's source ID, with pointers such as `/nodes/use:leaf`.
- **Verified:** read.
- **Fix:** map the diagnostics through `NodeSourceIds`.

**W5. `TryGetValue` returns true for a faulted run until the agent's `LateUpdate`.**
- **Where:** `NativeTreeAgent.cs:166-167` checks only `Host == null`. The host is cleared only in `ProcessRootResult`, called from `LateUpdate` (`:252`, `:283-287`). The doc (`:150-158`) says the method returns false when the run is faulted.
- **Verified:** read. The values are memory-safe.
- **Fix:** also refuse when `host.LastFailure.Code != None`.

**W6. Host read refusals cannot be told apart.**
- **Where:** `ProductionTreeHost.cs:500-504`: "executing", "stopped", "default handle" and "another host's handle" all return `NativeLifetimeStateInvalid`, and `IsExecuting` is internal (`:422`).
- **Effect:** a caller cannot tell "retry next frame" from "the handle is dead".
- **Verified:** read.
- **Fix:** add a distinct code or make `IsExecuting` public. This is an API decision.

**W7. A failed group commit can leave a node half-written.**
- **Where:** `TryCommitFromGroup` (adapter `:1268-1273`) copies memory, binding values and now the random state before `CommitTreeValues`, which can fail with `Overflow`. This affects the legacy executor path only; the workspace path pre-validates.
- **Verified:** read. This is pre-existing.
- **Fix:** check capacity first.

**W8. Graph Save with a pending extract target that now exists shows a misleading banner.**
- **Where:** `BehaviorTreeGraphWindow.Subtrees.cs:207-217` writes pending subtree files before the tree. `GraphEditSession.cs:116` throws "The file changed outside the editor". `BehaviorTreeGraphWindow.cs:459-463` then offers "Reload from disk", which discards all edits. A file already written for an earlier extract stays on disk.
- **Verified:** read. The "Keep editing" button avoids the loss.
- **Fix:** check every target before writing, and ask for a new path on a collision.

**W9. Wrong-typed MCP arguments return a generic error.**
- **Where:** `McpAuthoringToolDispatcher.Subtrees.cs:89`, `:93`, `:162-164`: `Value<long>()`, `Value<bool>()`, `new TreeId(…)`. `WithPermission` catches only `McpToolException` (`MCP/McpToolDispatcher.cs:169-183`).
- **Verified:** read. No path traversal was found.
- **Fix:** map conversion exceptions to `MalformedArguments`.

**W10. `create_tree` defaults to format 1, so a later bound node gets AIBT2042 twice.**
- **Where:** `McpAuthoringToolDispatcher.cs:49-73`. Known risk 5.
- **Verified:** read. This is documented in the recipes (`generated/recipes.md:185`).
- **Fix:** default to format 2, and stop after the first format error.

**W11. Import runs a full catalog discovery and a disk scan per subtree-using tree.**
- **Where:** `BehaviorTreeImporter.cs:66-67`: `ProjectNodeCatalog.Discover()` plus `ProjectTreeIndex.Scan()`. A registry change re-imports every subtree user.
- **Verified:** read. Not measured.
- **Fix:** reuse the cached catalog and one snapshot per import batch.

**W12. MCP `validate` and `compile` disagree on when to expand.**
- **Where:** `McpVerificationToolDispatcher.cs:44-45` (and `McpRuntimeTools.cs:111`) expand only when there are references. `compile` uses `RequiresExpansion` (references or an interface).
- **Unconfirmed:** whether an interface-only tree then misses an AIBT2047–2050 error.
- **Fix:** use `RequiresExpansion` everywhere.

**W13. `PipelinedJobs` refuses reads while a job is in flight.** Known risk 6.
- **Where:** `CanReadTreeValues` (`ProductionTreeHost.cs:418-419`).
- **Effect:** the refusal is broader than memory safety needs, because pipelined group work commits on the main thread. A fact key reads its default for up to one frame after an automatic restart.
- **Verified:** read. Both limits are documented.
- **Fix:** decide whether to read the last committed values, and document the decision.

**W14. Node-written keys reset at the end of every `NativeTreeAgent` run.** Known risk 4.
- **Where:** `NativeTreeAgent.cs:296-301`: `StopHost(); StartHost();` on restart.
- **Effect:** cooldowns and counters reset when a run ends. This is documented (`native-node-library.md:128-138`). In the Native Guard layout, the zapped intruder needs at least 3.4 s to return, while the run ends at least about 1.6 s after the zap, after the 1.5 s cooldown has expired. So the sample cannot show the loss.
- **Verified:** read. The geometry comes from a reader's reading of `NativeGuard.cs` and the tree JSON; I did not re-derive it.
- **Fix:** accept for now. Later, an opt-in "keep Tree values across runs" option.

**W15. The node manifest's `reads` and `writes` are empty for generated nodes.**
- **Effect:** `get_node_contract` shows no access. Known risk 5.
- **Where:** `CodeGen~/AIBT.CodeGen/GeneratedMetadataEmitter.cs:171` (per the P11-003 evidence and note).
- **Fix:** emit the binding access into the manifest.

### Notes

- **N1. Random nodes as guards.** `random-chance` as a reactive guard re-rolls every update. `wait-random-microseconds` under a v1 reactive composite re-draws every update. The guide (`native-node-library.md:179-181`, `:234-237`) does not warn about either.
- **N2. Timing nodes assume a well-behaved clock.** They need a non-negative, monotonic clock (`StandardNativeTimingNodes.cs:121`, `:163`). A custom clock that starts negative makes an unset `deadline-passed` never pass. This is not documented.
- **N3. The naming policy is weaker for keys with `:`.** It strips the qualifier from every key that contains one, including author-written keys (`TreeValidator.cs:1255`, `:1345`).
- **N4. `DirectorySubtreeResolver` serves one snapshot forever.** It also drops the parse diagnostics of an unreadable file (`:48-50`, `:73-77`).
- **N5. Validating a file alone differs from validating the expansion.** A subtree validated on its own gives AIBT2043 noise for keys written inside it, and a subtree whose root has an observer is rejected alone (`TreeValidator.cs:874-895`).
- **N6. Two editable windows can open on one file** (`BehaviorTreeGraphWindow.Subtrees.cs:513-515`). `WriteIfUnchanged` prevents a silent overwrite.
- **N7. The legacy group executor allocates two more arrays per wave** (`GeneratedDispatchGroupExecutorV2.cs:243-244`). That path already allocates `byte[][]`. The workspace path adds nothing.
- **N8. The sample README states the cooldown without saying it lasts one run only.** See W14. One sentence pointing to the guide would do.

### Unconfirmed

Each line says what would confirm it.

- **U1.** Hot-reload migration (`NativeHotReloadStateMigration.cs:80-82`, `:241-243`) may lose an in-flight dispatch id. There is no runtime caller. Confirm with a migration test that runs with `ActiveDispatchId != 0`.
- **U2.** The Editor read (`TryFindBuiltInDescriptor`, adapter `:461`) ignores `EnumContractId`. Confirm with an `Enum32` key in the graph.
- **U3.** `random-float3-in-radius` may not be bit-identical between Burst and Mono: the tests compare within 1e-4, with Burst on both sides. Confirm by comparing bit for bit with Burst off.
- **U4.** A `PipelinedJobs` agent may never be readable from `Update`/`LateUpdate`. Confirm by counting readable frames over 100 frames.
- **U5.** It is unknown whether any test drives the legacy executor (`workspace == null`) with a random node. Confirm by counting calls in `ProductionDispatchResolverV1.cs:50-54`.
- **U6.** In `BatchedJobsSameFrame`, one F1 request may fail the whole wave for every agent of that catalog. Confirm with a two-agent run.
- **U7.** A Float32 interface constant written as an integer (`7`) may be rejected. Confirm with one test.
- **U8.** A managed `BbKey` parameter inside a subtree is renamed (`SubtreeExpander.cs:656-672`), but its runtime lookup is not shown.
- **U9.** The tree list passes `registeredTypes: null` to `Expand` (`TreeCatalogEntry.cs:61`). Subtrees with registered types may show "Errors".
- **U10.** `BehaviorTreeAgent.Restart(true)` on a running `idle` reports AIBT4001 (P11-002 evidence). Not re-run here.

## 1. Code correctness

**Subtrees (P11-001).** Cycles, depth (8 accepted, 9 refused), a reference as root, ID and key
collisions (AIBT3027 including `a:b`+`c` against `a`+`b:c`), binding and parameter renaming,
determinism (ordinal sorting), and v1/v2/v3 reading, writing and schema agreement all check out. The
directory resolver looks files up by tree ID only, so there is no path traversal.

The defects are at the seams:
- the native compiler's format gate (F2);
- an unbounded expansion (W1);
- diagnostics after expansion (W3, W4);
- two resolvers with different file sets (F6).

**Native library (P11-002).** Contract, implementation and documentation agree for all 33 nodes,
except F1, W2 and N1. Integer and range edges are handled correctly: wraps, `long.MaxValue` deadlines,
`random-int` up to 2³¹, min == max, and invalid configurations that neither write nor draw. Two
pre-existing runtime bugs were found and fixed during P11-002 itself:
- the configuration field ordinal (adapter `:968`);
- the group random commit.

The random write-back now reads the committed state for every member before any member commits (read
and correct).

**Runtime (P11-004).** The read path is memory-safe:
- after dispose, `CanReadTreeValues` checks `Disposed` and the adapter checks `_disposed`;
- old handles are refused by instance ID;
- `JobsAgentFrame` jobs are completed inside the scheduler's `Update`.

The problems are API shape and edge windows: F8, W5, W6, W13.

**Validator, compiler and runtime.** The same rule is implemented in different places, and they
disagree in four cases:
- Agent scope: the policy admits it, native compilation fails (F9);
- observers: they validate, then fault on managed agents and do nothing natively (F10);
- two handles on one slot: they compile, then the runtime refuses them (F1);
- format 3: the managed compiler accepts it, the native one does not (F2).

**Static state.** `ProjectTreeIndex` statics and reload (F5). `s_nextTreeInstanceId` (F13).
`DirectorySubtreeResolver` caches forever (N4). The prepared-asset index in `TreeCatalogEntry` is
invalidated on `.asset` changes and domain reload (read, correct).

**API shapes that are expensive to change later:** F8, F11, F13, W6. Also the `ProductionTreeHost`
`TryBootstrap(..., ulong randomSeed, ...)` overload set, if F13 changes what identifies a stream.

## 2. Performance

**Read, runtime per tick.** Nothing new runs per tick on the drive path except:
- a bool compare and one `ulong` write per group commit;
- two small arrays per wave on the legacy executor path, which already allocates (N7).

Seeding is O(slots) once per run, and reads are off the drive path. The node code has no managed
access or allocation. No doubt was left that needed an Editor measurement, so none was made, in line
with the busy-machine rules.

**Read and measured, authoring.** Subtree expansion is linear in the size of the expanded tree, but
the expanded tree is exponential in references per level. Probe E: ×4 nodes per level, 21,845 nodes
in 808 ms at depth 7, single runs. The time is an order of magnitude only. The growth ratio is
structural and does not depend on machine load.

**Measured, Editor** (probe G):
- 2 runs × 9 interleaved rounds (warm scan, source hash, plain hash per round).
- CPU load 17–27 % around the runs, with 7 Unity processes on the machine.
- The two runs agree: warm scan medians 84.5 and 82.3 ms.

| Operation | Run 1 median | Run 2 median | Range (both runs) |
|---|---|---|---|
| Warm `ProjectTreeIndex.Scan()` | 84.5 ms | 82.3 ms | 48–116 ms |
| Cold scan (parse cache cleared, as after a reload) | 136 ms | 66 ms | |
| `CurrentSourceHash` (tree with references) | 85 ms | 85 ms | |
| Plain SHA-256 of the same file | 0.2 ms | 0.2 ms | |

What these numbers prove: one scan costs tens of milliseconds even in this small project (40 tree
files). Paths that run it per tree or per frame (F3, F4, F5, W11) are therefore real costs. The
absolute values are not portable: they depend on disk, antivirus and project size, and the machine was
loaded.

**Not measured:**
- the cost of `AssetDatabase.Refresh()` after a reload;
- import with many subtree users;
- the graph and live debugger with large trees (the live debugger is throttled to 0.2 s, read).

**Only a Player build can answer:**
- the Burst cost per tick of the 33 library nodes against hand-written project nodes;
- build size growth of the `aibt.native.standard` shard;
- IL2CPP code for the generic reads;
- whether random results are bit-identical between Editor (Mono, Burst on or off) and Player (U3).

## 3. Completeness

**P11-001.** The ADR's format, expander, validator, native path, import and bake, graph windows and
MCP/CLI tools are delivered. Gaps that matter for production:
- the two indexes (F6);
- format 3 after Unpack (F2);
- the Editor costs (F3–F5);
- no total-size limit (W1);
- real packages were never tested: the `Packages/` index was tested with mapped temporary folders.

The other items of the evidence "not done" lists do not matter for production: the window leak in
failing tests, the layout-after-Undo issue, the `Tests/` picker.

**P11-002.** 33 nodes, opt-in shard, host seeding, sample, recipes and quickstart are delivered.
Gaps:
- F1 (new);
- F14 (PM import and Player);
- F13 (seed reproducibility, a disclosed open question).

The open questions "AIBT2043 on native writers" and "no tree read" were closed by P11-003 and P11-004.
"`live: —`" and "Needs native compile" were closed by P11-005.

**P11-003.** Delivered as specified, and the validator now counts native binding writes. The known
residue is the projection's AIBT2043 for Agent and Shared keys (now dropped) and F11.

**P11-004.** Delivered. Gaps:
- `BlackboardUnsupportedScope` is untested because of F9;
- reads from a worker thread are not guarded (documented);
- F8 and W5.

**P11-005.** Delivered. Remaining:
- staleness is not shown in the tree list;
- registered types show `—`;
- a native agent in a subtree window was seen only in a test.

None of these blocks a release.

**Test quality.** For the main rules, the existing tests would fail if the rule broke:
- each AIBT2047–2050 and AIBT3020–3028 code;
- identity of trees without references;
- byte equality with hand-written copies;
- host read refusals after stop, another host's handle, wrong size, executing;
- stream determinism under all four policies;
- the node edge tables.

Important behaviour with no test:
- F1, F2 and F8;
- the reproducibility of F13 in a real session (the tests reset the counter by reflection);
- W5's window;
- a diamond reference and a two-level interface binding;
- a subtree whose own root is a reference;
- malformed MCP arguments;
- `runtime_hosts` with a `NativeTreeAgent`;
- Editor costs with many trees.

**Documentation.** Honest about the native observer limit, run-scoped keys and seed limits. It is
wrong or silent in these places:
- `TryGetValue` on a faulted run (W5);
- managed observers fault (F10);
- random guards (N1);
- clock assumptions (N2);
- one-run cooldown in the sample README (N8);
- Agent scope is admitted by policy but refused by native compilation (F9).

## 4. The seven known risks

| # | Risk | Facts established here | Cost to a user | Likelihood | Recommendation |
|---|---|---|---|---|---|
| 1 | Observers | Native: silently ignored, documented. Managed: validates, then AIBT4002 fault when a watched key changes (F10). Specification unchanged. | A managed agent stops mid-game; a native guard never fires early. | Medium (observers are in the specification and schema) | **Fix (cheap):** refuse in validation (managed) or warn (native), and mark the specification. Implementing the queue on hosts is a separate item. |
| 2 | Random reproducibility | Confirmed, documented (F13). | A seed does not replay in Play Mode without domain reload, or after a timing change. | High for anyone who uses seeds for replay | **Decide before release:** derive from an agent-owned id plus run index. Changing it later changes every seeded result. |
| 3 | Agent scope AIBT3017 | Confirmed by run: an internal "ranges overlap" error, not a policy refusal (F9). The only test never runs (F12). | A user adding an Agent key gets an unexplained failure. | Medium | **Fix**, or refuse with a clear diagnostic. |
| 4 | Keys reset per run | Confirmed, documented (W14). The sample cannot show it. | Cooldowns and counters reset when a run ends. | Medium | **Accept and document** (done). Later, an opt-in to keep values. |
| 5 | Tools and contract gaps | `create_tree` v1: documented (W10). `runtime_hosts`: confirmed by run (F7). Manifest access: W15. `Restart(true)`: not re-run (U10). PM import: never exercised (F14). Duplicate codes (F11). Contract tests: stale (F12). | AI tools blind to native agents; confusing codes. | High (tools are a headline feature) | **Fix:** F7, F11, F12 and F14 before release. W10 and W15 can wait. |
| 6 | P11-004 limits | Confirmed, documented (W13). | A pipelined agent's values freeze in UI. | Low (only forced pipelined profiles) | **Accept**, and decide the read semantics before the API freezes. |
| 7 | No Player build | True for every P11 item. | Unknown: IL2CPP or Burst surprises. | Unknown | **One IL2CPP smoke run** before release (F14). |

## 5. The three "known" failing tests

All three are stale tests; the product is right in each case. Details and fixes are in F12:
- `ZeroCustomNodes…` depends on the host project's samples.
- `GeneratedArtifactContractTests` ×2 assume a UPM layout.

The suite has not been green since before `2e6b9c7`. That is a release-process risk in itself.

## 6. What to do

**Before a release, in order:**
1. F1 (shared live offset, plus a test).
2. F2 (format-3 identity, plus a test).
3. F3, F4 and F5 (scan once and cache).
4. F7 (discovery of agent hosts).
5. F12 (make the three tests hermetic, add the CodeGen assembly to the full run).
6. F8 and F11 (API and code decisions while nothing is released).
7. F10 and F9 (refuse unsupported constructs in validation).
8. F13 (stream identity decision).
9. F6 (one resolver for MCP and the Editor).
10. F14 (one IL2CPP Player smoke run and one PM sample import).

**After:** W1–W15, N1–N8, and the unconfirmed list (U3 and U6 first).

## 7. Not reviewed / not verified

- **No test runs, Player or package.** No test was run; the orchestrator's full run on `bb56c00` was used as given. No Player was built (forbidden). No package installed under `Packages/` was used.
- **Read only from evidence:** `CodeGen~` (only the line about empty manifest access) and `Benchmarks~`.
- **Not exercised live:**
  - F3: no subtree-prepared agent exists in this project; the cost was measured on its components;
  - F5's `AssetDatabase.Refresh` cost;
  - the graph window with large trees;
  - MCP server tools through an MCP client.
- **Specifications** (`Documentation~/specifications/`) were read only where cited (observer lines).
- **Generated references** (`Documentation~/generated/`) were spot-checked (recipes, migrations), not compared line by line.
- **Commits outside `2e6b9c7..ad1e581`** were not reviewed, except where a P11 finding traced back to them (F1's rule, F9, F11).

## Probes

All five probes ran on 2026-11-22 against the Modules Editor (6000.6.9f1, Pipeline port 7810) through
`unity command eval_file`, with internals reached by reflection. Files are in
`C:\UnityProjects\_aibt_acceptance\P11-review\probes\`.

| Probe | Proves | Source | Output |
|---|---|---|---|
| B | F9: Agent-scope key, `AIBT3017 … Tree-scope blackboard memory ranges overlap`; control compiles | `probe-b-agent-scope.cs` | `probe-b-output.txt` |
| C | F1: `copy-int i→i` and `int-keys-at-least i,i` compile, then `NativeLifetimeStateInvalid` on each update; controls succeed | `probe-c-same-key-two-handles.cs` | `probe-c-output.txt` |
| E | F2: format 3 without references fails, format 2 compiles. W1: expansion growth to 21,845 nodes; error repetition with a broken leaf | `probe-e-v3-and-expansion.cs` | `probe-e-output.txt`, `probe-e-output-unknown-leaf.txt` |
| G | F3, F4, F5: scan, source-hash and plain-hash costs, 2 × 9 interleaved rounds, CPU load noted | `probe-g-editor-scan-cost.cs` | `probe-g-output.txt` |
| H | F7: `FindObjectsByType` misses a host with `HideInInspector \| DontSave`; temporary additive scene, closed | `probe-h-hidden-host.cs` | `probe-h-output.txt` |

## Orchestrator's check of this report (2026-11-22)

The orchestrator read the whole report on `88473d9` and checked the findings below against the code
and the raw probe outputs. Nothing was run again.

| Finding | Checked how | Result |
|---|---|---|
| F1 | `probe-c-output.txt`: both controls `root=Success`; `copy-int i to i` and `int-keys-at-least i >= i` compile, then `NativeLifetimeStateInvalid` on updates 1-3 | confirmed |
| F2 | `probe-e-output.txt`; `TreeDocument.LatestFormatVersion = 2` and the gate in `GeneratedCompiledProgramV2.CompileDocument` | confirmed |
| F3 | `NativeTreeAgentInspector.cs`: `Evaluate` on every `OnInspectorGUI`, unconditional `Repaint()` in Play Mode; `ProductionTreeAssetStaleness.Evaluate` calls `CurrentSourceHash` when the asset has subtree sources | confirmed by reading; the frame rate was not observed (no such agent in this project) |
| F4 | `TreeCatalogEntry.cs:61`: `ProjectTreeIndex.Scan()` per tree that requires expansion | confirmed |
| F5 | `ProjectTreeIndex.cs`: static `s_registered`, `RegisterAfterReload` -> `Update(Scan())` -> `Register` true for every tree ID -> `AssetDatabase.Refresh()` on `delayCall` | confirmed by reading; the refresh cost is not measured |
| F6 | `TreeSourcePaths.FindFiles` (`AllDirectories` under the root, no skip rule) behind `McpSubtrees.Resolver` | confirmed |
| F7 | `probe-h-output.txt` (`plainHostFound: true, hiddenHostFound: false`); `McpRuntimeTools.FindHosts` | confirmed |
| F8 | the adapter's `TryResolveExternalTreeRead<T>` checks `expectedType` against the slot and only size and alignment of `T` | confirmed; missed in the orchestrator's own review of P11-004 |
| F9 | `probe-b-output.txt` (`AIBT3017 ... Tree-scope blackboard memory ranges overlap`) | confirmed |
| F12 | the stored failure message of `ZeroCustomNodes...` names five sample node types of the host project; `GeneratedArtifactContractTests.FixturePath` asserts a package for the assembly | confirmed |
| W5 | `NativeTreeAgent.TryGetValue` returns false only for `Host == null`; a faulted host is cleared in `LateUpdate` | confirmed; missed in the orchestrator's own review of P11-004 |
| F10, F11, F13, F14 | established earlier in this project (the same facts were reported to the owner before the review) | agree |

Not checked by the orchestrator: W1-W4 and W6-W15, the notes, the unconfirmed list, the Native Guard
geometry of W14, the count of files and lines read by the reviewers.

The verdict "ready with conditions" is taken as it stands: nothing found is memory-unsafe or loses
data, and F1 and F2 are defects a first user of the native library or of Unpack will hit. No fix is
started; the owner decides the order and the open contract questions (F8, F9, F10, F11, F13, F14).
