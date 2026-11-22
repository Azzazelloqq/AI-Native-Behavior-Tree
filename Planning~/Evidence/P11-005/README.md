# P11-005 evidence — a truthful AIBT Graph for a native agent

Branch `feature/aibt-consumer-workflow`, base `2fff7e5`. Raw files: `C:\UnityProjects\_aibt_acceptance\P11-005\`.
Unity 6000.6.9f1, the Modules Editor (Pipeline port 7810). Status: accepted by the orchestrator on `bb56c00` ("Acceptance run" at the end); the item is in `Review`.

## Commits

| Commit | What |
| --- | --- |
| `a9334c9` | code: native blackboard read in the live debugger, tooltip in the panel, tree-list status rule |
| `7719d02` | tests: 14 native live-blackboard tests, 5 tree-list status tests, one changed expectation |
| `282c1c9` | docs: graph window page, CHANGELOG |
| this commit | task card, work item, MASTER_PLAN, this evidence |

## What changed (`git diff --numstat 2fff7e5 282c1c9`)

| File | + | − |
| --- | --- | --- |
| `Editor/Graph/GraphLiveDebugger.cs` | 98 | 2 |
| `Editor/Graph/TreeCatalogEntry.cs` | 94 | 4 |
| `Editor/Graph/BehaviorTreeGraphWindow.Inspector.cs` | 10 | 2 |
| `Editor/Graph/BehaviorTreeGraphWindow.Chrome.cs` | 2 | 1 |
| `Tests/Editor/Graph/GraphNativeLiveBlackboardTests.cs` (+ .meta) | 495 (+2) | 0 |
| `Tests/Editor/Graph/TreeCatalogNativeStatusTests.cs` (+ .meta) | 112 (+2) | 0 |
| `Tests/Editor/Validation/TreeScopeSlotNeverWrittenNativeBindingTests.cs` | 6 | 3 |
| `Documentation~/editor-and-layout.md` | 18 | 5 |
| `CHANGELOG.md` | 16 | 0 |

Allowed paths only: `git diff --stat 2fff7e5 HEAD -- . ':!Editor' ':!Tests' ':!Documentation~' ':!Planning~' ':!CHANGELOG.md'`
is empty. Nothing under `Runtime/`, `Authoring/`, `Agents/`, `MCP/`, `CodeGen~`, no ABI file, no
specification, no package version. `README.md` is unchanged (outside the allowed paths; its list of
statuses `Valid, errors, Needs native compile` stays true).

## Part 1 — live blackboard values of a native agent

Cause: `GraphLiveDebugger.TryFormatValue` read only the managed instance, so with a `NativeTreeAgent`
every key showed `live: —`. Now, when a native agent is attached, the value is read through
`NativeAgent.Host.TryReadTreeValue` (ADR-P11-004 D7, internal) on the existing 0.2 s panel refresh;
nothing is read when no agent is attached.

**Which string is the stable key.** The key `Id`. The generated program hashes
`StableHash.Fnv1A64(slot.Key.Id)` (`GeneratedCompiledProgramV2.cs:152`); the compiler never uses
`key.Name`; the JSON parser sets `Name = Id` (`CanonicalTreeJson.cs:288-290`); the compiler refuses a
key whose name differs from its id (`AIBT1007`). There is no rename operation, so a renamed key is a
new id, which the running program does not have: the panel shows `live: —` with the reason "The running
program has no key '<k>'.", and after the tree is prepared again the new key reads its value. The test
`ARenamedKey_IsANewId_WhichTheOldProgramDoesNotHave` proves all three steps. The panel's labels were
already keyed by `key.Id`.

**Rules, as implemented** (`GraphLiveDebugger.ExplainRefusal`, reason shown as the label's tooltip):

| Situation | Panel | Tooltip |
| --- | --- | --- |
| Read succeeds | `live: <value>` | — |
| No run (host is null) | `live: —` | The agent has no run now. |
| Host executes a pipelined job, a value was read before | last text kept | The host is executing a job (PipelinedJobs); this is the last value read. |
| Host executes, nothing read yet | `live: —` | … no value was read yet. |
| Host stopped | `live: —` | The host is stopped. |
| Key not in the running program | `live: —` | The running program has no key '<k>'. |
| Other scope | `live: —` | Only Tree-scope keys are read from a native agent. |
| Registered (non-built-in) type | `live: —` | Keys of a registered type are not shown for a native agent. |
| Host not from a generated catalog | `live: —` | The host was not started from a generated catalog. |
| Any other refusal | `live: —` | The host refused the read: <code>. |

A new run (a different host object) clears the kept texts, so the panel shows the new run's defaults.

**`Format`.** Added Quaternion `(x, y, z, w)`, Enum32 (the integer; the contract has no member names),
AgentId `agent N`, EntityId `entity N`, OperationId `operation a:b:c:d`, AssetId `asset <guid>[:localFileId]`.
Types that cannot occur: an invalid id (the `BlackboardValue` factories refuse one, so there is no
"none" branch); a registered type through a native host (`TryReadTreeValue` decodes built-ins only and
returns `BlackboardTypeMismatch`, shown as `—` with the reason). `UnsupportedScope` cannot come from a
generated host (the generated compiler refuses Agent-scope keys, `AIBT3017`); it is covered through
the pure `ExplainRefusal` function.

**Subtree windows.** `LiveKeyName` maps a subtree's private key to `prefix + id` and an interface key
to the parent key it is bound to. The native program is compiled from the same expanded document, so
the same mapping reads the right slot. Test: a child window shows its private `range` as 3 and its
interface `distance` as the parent's 1.25.

## Part 2 — the "Needs native compile" status

Cause: `TreeCatalogEntry.Read` gave this status to every valid tree with a native user node, checking
nothing. In the window's registry (`ProjectNodeCatalog.Build`) a user node without a reference handler
comes only from generated shards, so the old status fired exactly when the generated code existed.

Rule (written into the task card before the code): "Needs native compile" only when the project
catalog has no generated descriptor for one of the tree's native node types; otherwise "Valid", with a
detail naming the prepared runtime assets compiled from the tree (as source or subtree), or saying none
is prepared yet. A caller without the catalog gets "Valid" with "generated code and prepared runtime
assets are not checked here". Staleness of a prepared asset is not shown in the list.

By construction the new "Needs native compile" branch is unreachable in the window (its registry has
those nodes only because generated code exists); it is reached in tests with an empty catalog.

All uses of the status string: `TreeCatalogEntry.cs` (set and details), the list's status filter in
`BehaviorTreeGraphWindow.Chrome.cs` (unchanged, lists the string as a filter value),
`Documentation~/editor-and-layout.md`, `README.md:230` (unchanged), and the tests below.

**Changed test expectation (one):** `TreeScopeSlotNeverWrittenNativeBindingTests.TheGraphTreeList_ShowsANativeTreeAsNeedingNativeCompile_WithoutNotes`
now passes the generated catalog and expects `Valid` instead of `Needs native compile`. Its name is kept
(the point of the test, no notes, still holds); a comment says why.

**Refresh time** (`RefreshCatalog`, 6 project trees, medians of 15 samples, a loaded machine):

| Measurement | Runs |
| --- | --- |
| Before, whole refresh | 14.65 / 14.22 / 15.14 ms |
| First after, `FindAssets` on every refresh (discarded) | 32.4 / 34.5 / 35.9 ms |
| After, interleaved A/B, the 6 tree reads without / with facts | 6.85/6.96, 6.83/6.92, 7.47/7.71 ms (about +0.1 ms) |
| Index build, once per `.asset` change or domain reload | 11.8 / 13.6 / 17.8 ms |
| After, whole refresh (noisy) | 17.6 / 13.9 / 20.0 ms (minimums 13.3 / 13.6 / 18.7) |

`FindAssets("t:ProductionTreeAsset")` costs about 12 ms, so the prepared-asset index is cached until an
`.asset` file is imported, deleted or moved (a nested `AssetPostprocessor`, the same pattern as
`ProductionTreeAssetStaleness`). Files: `before/refresh-timing.json`, `after/refresh-timing.json`,
`after/refresh-ab-interleaved.json`; probes `time-refresh.cs`, `ab-facts.cs`. The whole-refresh
difference after the cache is within this machine's noise and is not a confirmed change.

## Tests (EditMode, `AIBT.Editor.Tests`, on `282c1c9`; raw `tests/<Class>.txt/.json`)

| Class | Result |
| --- | --- |
| `GraphNativeLiveBlackboardTests` (new) | 14/14 |
| `TreeCatalogNativeStatusTests` (new) | 5/5 |
| `TreeScopeSlotNeverWrittenNativeBindingTests` | 19/19 |
| `GraphEditorWorkflowTests` | 15/15 |
| `SubtreeGraphEditorTests` | 13/13 |
| `SubtreeImportAndBakeTests` | 6/6 |
| `GraphConsumerWorkflowTests` | 4/4 |
| `ProductionTreeAssetStalenessTests` | 6/6 |

The native tests run real generated hosts (the integration fixture catalog found through
`TypeCache`, no asmdef change), cover every built-in key type read from a running host, the
pipelined-jobs freeze, a new run, each refusal, a renamed key and a subtree window.

## Live evidence (Native Guard sample, `Assets/AibtNativeGuardDemo/`)

| File | Shows |
| --- | --- |
| `before/tree-list-status.png`, `before/tree-list-statuses.json` | First Agent, Native Guard, Swarm Arena: "Needs native compile" |
| `before/blackboard-native-live.png` | every key `live: —` while the native agent runs (run 1) |
| `after/tree-list-status.png`, `after/tree-list-statuses.json` | all Valid; Native Guard → `NativeGuard.runtime.asset`, First Agent → `FirstAgent.runtime.asset`, Swarm Arena → "No runtime asset is prepared from this tree yet." |
| `after/blackboard-native-live.png` | intruder-close false, intruder-seen true, gate-open true, weapon-ready-at 12189613, sweep-spot -1 (run 2) |
| `after/graph-native-live.mp4` | 25 s of the window in Play Mode |
| `after/graph-native-live.mp4.stills/t000.0s.png` | weapon-ready-at 18560284, sweep-spot -1, gate-open true (run 3) |
| `…/t008.0s.png` | weapon-ready-at 26460056, sweep-spot 0, gate-open false (run 4) |
| `…/t016.0s.png` | weapon-ready-at 34471620, sweep-spot 2, gate-open false (run 5) |
| `…/t024.0s.png` | sweep-spot back to its default -1 in a new run, gate-open true (run 6) |

Console: after every domain reload the Editor logs pre-existing errors ("Release of invalid GC handle …
previous domain", two "Failed to load VirtualArtifacts"); `Editor.log` shows them at every third reload
since long before this work. No warning or error comes from the changed files.

## Not done or not verified

- The pipelined-jobs freeze is proven by an EditMode test only, not seen live in Play Mode.
- The tooltip text is not captured in a picture.
- `UnsupportedScope` cannot occur through a generated host; only the pure function is tested.
- Keys of a registered type show `—`; they are not decoded.
- The list does not show whether a prepared asset is stale.
- The first list refresh after any `.asset` change pays the 12–18 ms index build.
- `README.md` was not edited (outside the allowed paths).
- No full test suite run; only the classes above.
- In the clip intruder-seen and intruder-close did not change; gate-open, sweep-spot and the run did.
- Refresh time was measured on this project (6 trees) only, not on a large project.
- A native agent in a subtree window is shown by a test only, not live.

## Acceptance run (orchestrator, 2026-11-22)

Read by the orchestrator on `bb56c00` (clean working tree): the whole code diff `2fff7e5..bb56c00`
under `Editor/`, the one changed test, this file and four pictures.

- **Scope.** `git diff --stat 2fff7e5 bb56c00` names only `Editor/Graph/` (4 files), `Tests/Editor/`,
  `Documentation~/editor-and-layout.md`, `CHANGELOG.md` and `Planning~/`. Nothing under `Runtime/`,
  `Authoring/`, `Agents/`, `MCP/`, `CodeGen~`.
- **Callers of the status.** `TreeCatalogEntry.Read` has two callers outside tests: the window's list
  (passes the project facts) and `BehaviorTreeImporter` (built-in registry only, so a tree with a
  project node ends as "Needs registry" before the native branch). No caller reaches the "not
  checked here" detail except tests.
- **Changed expectation.** One, as listed above
  (`TheGraphTreeList_ShowsANativeTreeAsNeedingNativeCompile_WithoutNotes`: `Valid` with the catalog
  passed). It follows from the purpose of the item.
- **Pictures.** `before\blackboard-native-live.png`: run 1, every key `live: —`, First Agent and
  Native Guard "Needs native compile". `after\blackboard-native-live.png`: run 2, `intruder-close`
  false, `intruder-seen` true, `gate-open` true, `weapon-ready-at` 12189613, `sweep-spot` -1, all
  trees Valid. `after\graph-native-live.mp4.stills\t016.0s.png`: run 5, `gate-open` false,
  `weapon-ready-at` 34471620, `sweep-spot` 2. `after\tree-list-status.png`: Edit Mode, all six Valid.
- **Full run of all nine assemblies** on `bb56c00`, 19:00-19:16; Editor-log counters: 1 and 3 at the start and at the end (they did not move). Raw output:
  `C:\UnityProjects\_aibt_acceptance\P11-005\acceptance\full-run-bb56c00.txt`. This run also covers the P11-004 review fix `ac4201c`,
  which had only the Integration assembly run on it.

| Assembly | `1eb2359` | `bb56c00` | New tests |
|---|---|---|---|
| `AIBT.Runtime.Tests` | 815/815 | 815/815 | - |
| `AIBT.Integration.Tests` | 422/422 | 423/423 | `RefusedReads_AfterTheOneLog_AllocateNoManagedMemory` (P11-004 fix) |
| `AIBT.Editor.Tests` | 813/814 | 832/833 | `GraphNativeLiveBlackboardTests` 14, `TreeCatalogNativeStatusTests` 5 |
| `AIBT.BehaviorCases.Tests` | 65/65 | 65/65 | - |
| `AIBT.NativeBurstDispatch.Tests` | 79/79 | 79/79 | - |
| `AIBT.NativeTreeAgentBlackboard.Tests` | 33/33 | 33/33 | - |
| `AIBT.NativeSharedBlackboard.Tests` | 36/36 | 36/36 | - |
| `AIBT.CodeGen.ContractTests` | 6/6 | 6/6 | - |
| `AIBT.PlayMode.Tests` (PlayMode) | 2/2 | 2/2 | - |

The one failure is the known `ZeroCustomNodesReturnsExactlyThePhase1BuiltInCatalog`.

Not done by the orchestrator: the two new test files were not read line by line; the clip was not
watched (three stills were opened); the refresh timing was not measured again; the Play Mode
session was not repeated.

To know: in the window the status "Needs native compile" can no longer appear (its registry holds a
native project node only when generated code exists), so the list says nothing about a prepared
asset that is stale; the first list refresh after an `.asset` change pays a 12-18 ms index build on
this project and loads every `ProductionTreeAsset` once; `README.md:230` still lists the status.
