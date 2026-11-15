# P9-006 — User guide and consumer acceptance

Status: Review, verified on Unity 6000.7.0b1 (Windows 11) on 2026-11-15, in the `Modules` host
project and in two clean consumer projects. Committed locally only; not pushed.

## Documentation

- `README.md` is now a user guide. It covers what AIBT is and a comparison of the two layers,
  installation, and a five-minute start for each layer: the managed layer (`BehaviorTreeAgent`, a
  `BtAction` and a `BtCondition`) and the native layer (`NativeTreeAgent`, a node with the short
  `AIBT.Native` attributes, catalog, Prepare Runtime Asset, facts and effects). It also explains how
  to exchange data with game code, how to work in the graph editor (search, shortcuts, inspector,
  live debugging, automatic Prepare and stale states), and has a standard node table by category,
  advice on which layer to choose, and the known limits. The phase history and design goals moved
  to `Documentation~/roadmap.md` ("Phase history").
- Snippets compile. The managed snippets are copied verbatim from the `guide:` regions of
  `Tests/Editor/Documentation/UserGuideSnippetTests.cs`, which compiles them and runs them: a heal
  tree with `game.is-low-health`, `game.heal` and `aibt.unity.raise-event`; the `Fighter`
  component writes and reads the blackboard and receives the event. The native snippets are quoted
  from `Samples~/FirstAgent` (`MoveNode.cs`, `FirstAgentCatalog.cs`, `FirstAgent.cs`).
  `UserGuideSnippetTests` (3 tests) checks the README against both sources and checks that it has
  exactly these 5 C# blocks.
- Also updated: `Documentation~/consumer-quickstart.md` (scope note, the current toolbar names
  and a new "Player builds" section), `Documentation~/editor-and-layout.md` (node search by
  category, AIBT2043 note, native live view and its limits, runtime asset states),
  `Samples~/README.md` (a table of the samples), the descriptions of the First Agent and Swarm Arena
  samples in `package.json`, `Samples~/FirstAgent/README.md` and `CHANGELOG.md`.
- Generated documentation: `McpApiReferenceGenerator` now covers `AIBT.Agents`, which adds
  `Documentation~/generated/api-reference-agents.md` (70 public types). The drift test and the
  member-coverage test include it. The other generated files were regenerated and are unchanged.
  `NativeTreeAgent`, `NativeTreeRestartMode` and the `AIBT.Native` attributes were already in
  `api-reference-runtime.md` from P9-004 and P9-005.

## Carried-over items

1. **`Get-FullPublicApi.ps1`** now copies `Agents/` and `Analyzers/` when they exist, adds the AI,
   Animation, Audio and Physics modules to the isolated manifest, and dumps `AIBT.Agents` and
   `AIBT.StandardNodes` as well (`PublicApiDump.cs.txt`). Assemblies that are absent are skipped.
   The pre-P9 baseline was dumped with the same script from `git archive 2612389`:
   4 assemblies, 458 types, 2311 members. The final working tree has 6 assemblies, 549 types and
   2520 members, with sha256 `0f845c8a…69096`. Result: **0 lines removed or renamed, 302 added**
   (2 assemblies, 91 types, the rest members). The check passed. Diff: host
   `Temp/stage5/public-api.diff`.
2. **AIBT2043** is produced in `TreeValidator.ValidateBlackboardWriteCoverage`. Investigation found
   a real false positive, not only wording. Managed nodes bind keys through `blackboard-key`
   parameters (ADR-P9-001), which declare no access, so every key in a managed tree was reported as
   "never written". That included keys that nodes do write, for example `waypoint` written by
   `samples.simple.next-waypoint` and `destination` written by `random-point`. Change:
   - a key named by a `blackboard-key` parameter counts as possibly node-written (a new
     `CollectKeyParameterTargets`); manifest access declarations, the native external-write
     eligibility and the Info severity are unchanged;
   - the message names the key and reads as an input note: "Input key 'k': no node in this tree
     writes it, so it keeps its default until game code sets it (BehaviorTreeAgent.Blackboard.Set
     or NativeTreeAgent.SetFact). Expected for keys gameplay feeds." Before, identical messages for
     several keys collapsed into one entry in the graph's Problems list.

   Tests: `TreeScopeSlotNeverWrittenValidatorTests` gained 2 tests, 5/5 in total. No ADR: the
   behavior defined by ADR-P7-039 for declared access is unchanged.
3. **NavMesh path following**: added a new Play Mode test assembly `Tests/PlayMode/AIBT.PlayMode.Tests`
   (with platforms, `UNITY_INCLUDE_TESTS`, and nodes only with `com.unity.modules.ai`).
   `NavMoveToPlayModeTests` uses only the public API: a 20×20 NavMesh with a non-walkable wall,
   a tree loaded from JSON, and the goal written into the blackboard. The agent routes around the
   wall end (min z −7.64 against a straight line at z = −5), arrives within stop-distance, the
   node's speed is applied, the NavMeshAgent's own speed is restored and `nav-stop` clears the
   path. 1/1 in the host and 1/1 in the clean project. Mutation check: tightening the wall
   assertion to −12 failed with "was −7.64", so the test observes the path.
   The Unity CLI's synchronous `run_tests` refuses Play Mode, so the host run used
   `run_tests --mode playmode --async_tests` plus `test_status`.
4. **Node search popup** captured: host `Temp/stage5/node-search-popup.png`, the Actions level
   grouped by category. The popup was opened and grabbed in the same Editor call
   (`RepaintImmediately` followed by the host view's `GrabPixels`, through the DevTools capture
   helper) before it could lose focus.

### Found during acceptance and fixed

- **Stripped Player.** The first Windows IL2CPP Player with managed stripping **High** failed on
  both layers (host `Temp/stage5/player-il2cpp-high-before-fix.log`):
  - every managed tree failed with `AIBT2021: Node type is not present in the selected registry`,
    because the standard `aibt.unity.*` classes and `NextWaypointNode` were stripped (they are
    created by reflection only);
  - First Agent reported "The node catalog 'AIBT.Samples.FirstAgent.FirstAgentCatalog, …' is
    unavailable (renamed, removed or stripped)".

  Fix, additive and Editor-only: `Editor/PlayerBuild/AibtLinkXmlProcessor.cs`, an
  `IUnityLinkerProcessor` that writes an additional link.xml preserving every non-abstract
  `[BtNode]` class and every `[AibtCatalogSet]` type in the Player's assemblies (test assemblies
  excluded). Test: `AibtLinkXmlProcessorTests` (2). The Player after the fix is described below.
  The folder is named `PlayerBuild` because the package `.gitignore` ignores `[Bb]uild/`.
- **`McpVerificationToolDispatcherTests.CompileReturnsTheSameContentHashAsADirectReferenceCompilerCall`**
  was not host-only. It also failed in the clean project without samples (`fa6b…` against
  `b1f1…`). Its expected registry (built-ins plus `AIBT.Agents` nodes) has lacked the
  `aibt.native.*` standard shard since P9-002. The shard is in every project's catalog, as
  `ZeroCustomNodesReturnsExactlyThePhase1BuiltInCatalog` shows by passing in the clean project
  with exactly built-ins, managed nodes and 4 native nodes. The test now compiles directly with
  the registry the tool uses (`ProjectNodeCatalogCache.Current.Registry`); which nodes that
  catalog contains is the other test's concern.

## Acceptance

### Clean consumer projects

`C:\UnityProjects\_aibt_acceptance\P9Clean`: created by `Unity -createProject` (6000.7.0b1). Its
manifest has the default modules plus `com.azzazello.aibt: file:C:/UnityProjects/Modules/Assets/AIBT`,
`com.unity.test-framework 1.9.0` and `testables: [com.azzazello.aibt]`. Samples were not imported,
because the catalog baseline tests require their absence (as in P8). Runs used
`Tools~/Verification/Run-UnityTests.ps1 -Scope Full -EnableGraphics`, in batch mode.

| Run | Result |
| --- | --- |
| EditMode, first run (before the fixes above) | 1646/1648, failed 1 (`CompileReturnsTheSameContentHash…`), skipped 1 |
| **EditMode, final** | **1653/1654, failed 0, skipped 1** |
| **PlayMode, final** (`AIBT.PlayMode.Tests`) | **1/1** |

Final EditMode by assembly:

| Assembly | Result |
| --- | --- |
| `AIBT.Runtime.Tests` | 707/707 |
| `AIBT.Editor.Tests` | 624/625 |
| `AIBT.Integration.Tests` | 69/69 |
| `AIBT.BehaviorCases.Tests` | 65/65 |
| `AIBT.NativeBurstDispatch.Tests` | 71/71 |
| `AIBT.NativeTreeAgentBlackboard.Tests` | 33/33 |
| `AIBT.NativeSharedBlackboard.Tests` | 36/36 |
| `AIBT.NativeCommandsAndAsync.Tests` | 20/20 |
| `AIBT.NativeSnapshot.Tests` | 7/7 |
| `AIBT.CodeGen.GenerationTests` | 14/14 |
| `AIBT.CodeGen.ContractTests` | 6/6 |
| `AIBT.CodeGen.GenerationConsumerTests` | 1/1 |

The skip in `AIBT.Editor.Tests` is `GraphEditorWorkflowTests.ManagedSampleTreesAreListedAsValid`
("The Simple Agent sample is not imported in this project"). Both former host-only failures pass
here.

`C:\UnityProjects\_aibt_acceptance\P9Samples` is a copy of `P9Clean` without `testables`. The
**Simple Agent**, **First Agent** and **Swarm Arena** samples were copied into
`Assets/Samples/AI-Native Behavior Tree/0.1.0/` as Package Manager would place them. The demo
scenes were created by the samples' own `SimpleAgentSetup.Create` and `FirstAgentSetup.Create`
(batch mode). Play Mode probes (`Assets/Acceptance/Tests/SamplePlayModeProbes.cs`, not part of
the package) passed **3/3**:

- Simple Agent, 30 s: Guard, Scout and Wanderer each travelled more than 2 m (13.0, 13.0 and
  15.9). The guard tracked and chased the wanderer, `spotted` was raised 7 times, nothing faulted.
- First Agent: `CompletedRuns = 2`, `LastResult = Success`, no error (it reached the goal, the goal
  moved, and it reached it again).
- Swarm Arena, 20 s: 64 agents went through the Patrol, Pursue, Flee, Attack and Return phases,
  with 25312 logical updates and 312 attacks.

### Windows Player

The Player was built from `P9Samples` with the scenes SimpleAgent and FirstAgent, Windows x64,
**IL2CPP**, **managed stripping level High**, non-development, windowed, with no `CatalogFactory`
assignment. The probe (`Assets/Acceptance/Runtime/AcceptancePlayerProbe.cs`, started by
`-aibtProbe`) references neither node types nor catalog types.

- Before the linker step: the build succeeded, but at run time all three managed trees failed to
  compile and First Agent's catalog was "unavailable (… stripped)" (see above).
- **After** (`Build/IL2CPP-High-2`): the build succeeded with 0 errors.
  `il2cpp=true` at run time. All 3 managed trees compiled, and the agents moved over 30 s (the
  probe reports the largest distance from the start: Guard 14.4 m, Scout 7.9 m, Wanderer 14.9 m),
  `spotted` was raised 3 times, nothing faulted. First Agent resolved
  `AIBT.Samples.FirstAgent.FirstAgentCatalog` by reflection, was at x = 2.54 after 5 s and
  finished 2 runs with Success. The log has no errors or exceptions. Log: host
  `Temp/stage5/player-il2cpp-high-after-fix.log`. Screenshot of the running Player at 12 s: host
  `Temp/stage5/player-il2cpp-high.png`.

Mono and lower stripping levels were not built separately; High with IL2CPP is the strictest case.

### Host project (`Modules`)

`Invoke-UnityRecompile.ps1`: no compile errors. `Invoke-UnityTests.ps1`, recorded in host
`Temp/stage5/tests-host-final.txt`:

| Assembly | Before P9-006 | Now |
| --- | --- | --- |
| `AIBT.Runtime.Tests` | 707/707 | 707/707 |
| `AIBT.Editor.Tests` | 616/618 | 624/625 |
| `AIBT.Integration.Tests` | 69/69 | 69/69 |
| `AIBT.BehaviorCases.Tests` | 65/65 | 65/65 |
| `AIBT.PlayMode.Tests` (new, Play Mode) | — | 1/1 |

The remaining Editor failure is the known host-only
`McpToolDispatcherTests.ZeroCustomNodesReturnsExactlyThePhase1BuiltInCatalog`. Its extras are
exactly the host's imported sample nodes: `aibt.samples.first-agent.move`, `aibt.samples.swarm.*`
and `samples.simple.next-waypoint`. It passes in the clean project. Console: after the final
recompile, the new errors are the expected logs of tests that check error handling (for example
`ThrowNode`, `host-test-failure`, `handler-failure`); there are no compile errors. The buffer
also holds compile errors from earlier sessions.

### Static checks

- `Tools~/Verification/Verify-Static.ps1`: passed (7 schemas, 164 work items). The first run
  found two broken links (the moved phase history, and this file before it existed); both fixed.
- `Tools~/Verification/P2/CodeGen/Build-And-Verify.ps1` was **not run**. This card does not change
  CodeGen, and the gate needs the 6000.5.8f1 editor, which is not installed, plus .NET 8 (only the
  .NET 10 SDK is installed). This is the same limitation as P9-005.

## Captures (host `Temp/stage5/`, not committed)

- `SimpleAgent-play.mp4`, `FirstAgent-play.mp4`, `SwarmArena-play.mp4`: 30 s of Play Mode in the
  host, with stills every 5 s in `*.mp4.stills/`.
- `player-il2cpp-high.png`: the running IL2CPP Player (Simple Agent).
- `graph-guard.png`: the graph window with `guard.aibt` and the AIBT2043 note in its new wording,
  taken before the `blackboard-key` change. `graph-guard-final.png` is the same tree after it: no
  notes, and the Problems bar is hidden.
- `node-search-popup.png`: the node search popup.
- `public-api-baseline-2612389.txt`, `public-api-head.txt`, `public-api.diff`,
  `public-api-check.log`.
- `tests-host-final.txt`, `tests-playmode-host.txt`, `clean-editmode-run1-results.xml`,
  `clean-editmode-final-results.xml`, `clean-playmode-final-results.xml`,
  `clean-samples-playmode-results.xml`, `player-il2cpp-high-before-fix.log`,
  `player-il2cpp-high-after-fix.log`.

## Not done / not verified

- `Build-And-Verify.ps1` was not run (see above).
- The Player was verified with IL2CPP and stripping High only. Mono, and IL2CPP at Minimal, Low
  and Medium, were not built. Only Windows x64 was built.
- The Swarm Arena sample was not included in the Player. It uses the low-level path with an
  explicit catalog reference, so it was only run in Play Mode.
- The Play Mode clips were recorded in the host project, whose imported sample copies differ from
  `Samples~` only in their README files. The clean-project sample checks were probes, not clips.
- The README was not tried by a separate new user. "From zero to a running agent" was checked
  through the samples, the snippet tests and the clean project, not through a manual walkthrough.
- Known limits stay documented rather than fixed. Native live view shows the active path only.
  Running hosts keep the old program after an automatic re-prepare. Assets prepared before P9-004
  are `Unknown`. MCP `search_nodes` pages by 50.
- Files outside the card's listed owned paths were changed because the work needed them:
  `Authoring/Validation/TreeValidator.cs`, `MCP/Documentation/*`, `Editor/PlayerBuild/`, `Tests/`
  and `Tools~/Verification/P7/Audit/`.
