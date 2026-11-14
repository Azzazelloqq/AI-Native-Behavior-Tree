# P9-002 — Ready-made node library

Status: Review, verified in the `Modules` host project on Unity 6000.7.0b1, 2026-11-14. Local
commit only, no push.

## Result

### Managed `aibt.unity.*` nodes

38 new main-thread nodes in `Agents/Nodes/Standard/`. Together with the P9-001 nodes the
package now ships 52 standard managed nodes (`ManagedNodeCatalog.Standard`).

| Category | New nodes |
| --- | --- |
| Flow (3) | `wait-random`, `wait-until`, `wait-frames` |
| Blackboard (15) | `set-vector3`, `copy-bool`, `copy-int`, `copy-float`, `copy-vector3`, `increment-int`, `increment-float`, `toggle-bool`, `compare-int-keys`, `compare-float-keys`, `is-vector3-set`, `random-float`, `random-int`, `random-point`, `distance` |
| Movement (4) | `rotate-towards`, `turn-by`, `move-direction`, `keep-distance` |
| Navigation (2) | `nav-move-to`, `nav-stop` (only with `com.unity.modules.ai`) |
| Animation (3) | `animator-trigger`, `animator-bool`, `animator-float` (only with `com.unity.modules.animation`) |
| GameObject (4) | `set-active`, `destroy-self`, `get-position`, `has-reference` |
| Audio (1) | `play-audio` (only with `com.unity.modules.audio`) |
| Events (1) | `raise-event` |
| Checks (5) | `line-of-sight` (only with `com.unity.modules.physics`), `random-chance`, `start-cooldown`, `is-cooldown-ready`, `time-since-changed` |

Standard nodes by category after this card (`Temp/stage3/standard-nodes.txt` in the host):
Animation 3, Audio 1, Blackboard 21, Checks 5, Debug 1, Events 1, Flow 7, GameObject 4,
Movement 7, Navigation 2.

Module-dependent nodes are guarded by `versionDefines` in `AIBT.Agents.asmdef`
(`AIBT_UNITY_AI`, `AIBT_UNITY_PHYSICS`, `AIBT_UNITY_ANIMATION`, `AIBT_UNITY_AUDIO`), so the
package still compiles in a project that disabled a module.

Running and abort semantics are in each node's manifest summary and usage notes. Notable ones:
`nav-move-to` re-reads its target every tick and clears the path and restores the agent's speed
on exit or abort; `play-audio` with `wait-until-finished` stops the clip on abort; `move-direction`
with duration 0 runs until aborted; `keep-distance` stays Running while it holds the band.

### How nodes address scene objects

The blackboard holds values, not objects. `BehaviorTreeAgent` has a serialized **References**
list (name → `UnityEngine.Object`). A node that acts on another object takes a string
`reference` parameter: empty means the agent itself, otherwise the named entry; a component entry
resolves to its GameObject, and `ResolveComponent<T>` falls back to `GetComponentInChildren<T>`.
Code can call `agent.SetReference(name, obj)`. A missing reference fails the node with one
warning (`WarnOnce`), never an exception.

### Events

`agent.Events.Subscribe(name, handler)` / `Unsubscribe` / `Raise(name)` plus `AnyRaised`.
Inspector **Event Responses** (name → UnityEvent) fire after code handlers. `raise-event`
succeeds immediately.

### Support added to the managed layer

`BbAnyKey` (key parameter of any value type, contract with no value type),
`ManagedTreeInstance` per-slot change time (`time-since-changed`), `BtNode.SecondsSinceChanged`,
`ResolveGameObject`, `ResolveComponent<T>`, `WarnOnce`.

### Discoverability

- Node search (`Editor/Graph/NodeSearchProvider.cs`): composites and decorators stay flat;
  conditions and actions are grouped by manifest category. Level 1/2 of the live tree is in
  `Temp/stage3/node-search-tree.txt` (Conditions: Blackboard, Checks, Core/Conditions,
  GameObject, Movement, Native/Blackboard, …; Actions: Animation, Audio, Blackboard, Checks,
  Core/Actions, Debug, Events, Flow, GameObject, Movement, Native/Blackboard, Native/Flow,
  Navigation, …). Test: `GraphEditorWorkflowTests.NodeSearchListsLeavesUnderTheirCategory`.
- MCP: `McpNodeCatalogDocumentGenerator.PackageRegistry()` = built-ins + standard managed
  nodes; `Documentation~/generated/node-catalog.md` regenerated from it (65 node types). The
  drift test uses the same registry; `ThePackageCatalogListsEveryStandardManagedNodeWithItsContract`
  checks every standard node's embedded contract against `NodeCatalogQuery`. Empty "when to use"
  / "when not to use" fields are now omitted instead of printed with trailing whitespace.

## Native shard: verdict

Works, as a native-only shard. `Authoring/Registry/Generated/StandardNative/` (assembly
`AIBT.StandardNodes`, shard `aibt.stdlib.standard-native`):

- `aibt.stdlib.bool-is-true` — Bool read binding `key`.
- `aibt.stdlib.float-at-least` — Float32 read binding `key`, `threshold`.
- `aibt.stdlib.set-float` — Float32 write binding `key`, `value`.
- `aibt.stdlib.wait-microseconds` — `duration-microseconds`, deadline from the host clock.

Empirical check (`Tests/Integration/NativeRuntime/StandardNativeShardTests.cs`, 3/3): a
test-assembly `[AibtCatalogSet]` lists the package shard together with a shard from another
assembly (the P7-039 fixture shard standing in for a project's nodes); the generator accepts it
(`IsUsable`), `GeneratedCompiledProgramV2Compiler` compiles a tree mixing both shards, and a real
`ProductionTreeHost` runs it: waiting at 0 s and 0.3 s, Success at 0.6 s after `set-float` wrote
2.5 and `float-at-least` read it back through its binding; Failure while the `armed` flag is false.

P7-028 constraints, as found:

- `aibt.core.*` is frozen (AIBT5012); this shard does not use it.
- Byte-identical manifest parity with a reference implementation is unreachable for blackboard
  nodes: the reference compiler still has no `GeneratedHandle` support (P7-028 wall #1). So these
  nodes have no reference/managed counterpart: `ProjectNodeCatalog` lists them as `Burst` with no
  reference handler binding (`StandardNodeCatalogTests.TheProjectCatalogOffersTheStandardNativeShardAsNativeOnlyNodes`),
  `BehaviorTreeAgent` cannot run them, and authoring routes such trees to the generated compiler.
  This is the card's disclosed fallback.

Open for the owner:

1. The shard uses the `aibt.stdlib.` namespace. P7-028 introduced that namespace for always-on
   built-in leaves with both a native and a reference path; these four have only the native path.
   A distinct prefix (for example `aibt.native.`) would keep that meaning. Not changed here.
2. `AIBT.StandardNodes` is a player assembly, so its four nodes appear in every project's node
   search and MCP catalog (under `Native/…`), whether or not a catalog set includes the shard.
   `McpToolDispatcherTests.ZeroCustomNodesReturnsExactlyThePhase1BuiltInCatalog` now expects
   them as part of the package catalog. If that is unwanted, the shard should move to a sample.

## Sample (Samples~/SimpleAgent)

- Wanderer: `random-point` around `arena-center`, `move-towards`, `wait-random`.
- Guard: chase = `is-true` (interrupt both), `set-active` on reference `alert`, an announce
  branch (`is-cooldown-ready` → `raise-event "spotted"` → `start-cooldown 3 s`, else `succeed`),
  `look-at`, `move-towards`; patrol = `set-active` off, custom `samples.simple.next-waypoint`,
  `move-towards`, `turn-by`.
- Scout (new): `has-reference intruder`, `get-position`, `line-of-sight`, `keep-distance 3–5`,
  else roam with `random-point`, `move-towards`, `wait-random`, `turn-by`.
- Beacon: `SpottedFlash` subscribes to `spotted` through `agent.Events`.
- `AlertView.cs` removed (replaced by `set-active`); `NextWaypointNode` is the one custom node.

The first Play Mode run found a sample bug: once the guard reached the wanderer, the chase
sequence restarted every tick and raised `spotted` every frame (454 flashes in 35 s). The guard
tree now rate-limits it with the cooldown nodes; the second run raised it 6 times in 35 s.

## Verification

Unity 6000.7.0b1, `Tools/Unity/Invoke-UnityTests.ps1`, 2026-11-14:

| Assembly | Result | Baseline before this card |
| --- | --- | --- |
| `AIBT.Runtime.Tests` | 707/707 | 707/707 |
| `AIBT.Editor.Tests` | 609/611 | 563/565 |
| `AIBT.Integration.Tests` | 61/61 | 58/58 |
| `AIBT.BehaviorCases.Tests` | 65/65 | 65/65 |

The two Editor failures are the pre-existing host-only ones
(`McpToolDispatcherTests.ZeroCustomNodesReturnsExactlyThePhase1BuiltInCatalog`,
`McpVerificationToolDispatcherTests.CompileReturnsTheSameContentHashAsADirectReferenceCompilerCall`);
both compare against the host project's catalog, which contains imported sample nodes. The
first one also exposed a real problem this card caused: `search_nodes` pages by 50 by default and
the package catalog now has 69 entries, so the test asked for one page and missed 19 standard
nodes. The test now requests `count: 1000` and expects the four native-shard nodes; after that
fix its only extras are the host's imported sample nodes (`aibt.samples.swarm.*`,
`samples.simple.next-waypoint`). The Editor result above was taken before that test fix; the
test class was rerun after it (7/8, the remaining failure is the host-only one).
MCP clients that call `search_nodes` without `count` now see only the first 50 nodes and have
to page with `offset`.

New tests: `Tests/Editor/Agents/` — `StandardFlowAndBlackboardNodeTests`,
`StandardSceneNodeTests` (real GameObjects, Animator controller, AudioSource, physics raycasts),
`StandardNavigationNodeTests`, `StandardNodeCatalogTests`; 44 in total.
`Tests/Integration/NativeRuntime/StandardNativeShardTests` (3).
`McpDocumentationGeneratorsTests` 13/13 after regeneration. `Verify-Static.ps1`: passed
(7 schemas, 164 work items).

NavMesh: in Edit Mode the in-memory NavMesh answers queries but Unity never places a
`NavMeshAgent` on it, so the committed tests cover the missing-agent, not-on-NavMesh and stop
paths (and pin that Editor behavior). Path following was checked in Play Mode with a probe
(`Temp/stage3/navmesh-playmode-probe.txt` in the host): a 20×20 NavMesh with a non-walkable wall
between start (-5, -5) and goal (5, -5); `nav-move-to` routed around the wall's end (z ≈ -8.3),
returned Success at about 4 s, restored the NavMeshAgent speed (6 → 3.5) and `nav-stop` left
velocity 0. This probe is not a committed test.

Play Mode demo (`AIBT/Samples/Simple Agent/Create Demo` after re-copying the sample into
`Assets/Samples/AIBT/Simple Agent`): sampled every ~2 s for 35 s — the guard patrols and chases
(alert marker on while chasing), the beacon flashes on `spotted` at most every 3 s, the scout
holds 3–5 m while it sees the wanderer and roams otherwise. No new console errors or warnings
during the 30 s recording.

Captures (host project, not committed): `Temp/stage3/graph-guard.png`,
`Temp/stage3/graph-scout.png`, `Temp/stage3/simple-agent-play.mp4` (30 s, stills every 6 s in
`simple-agent-play.mp4.stills/`), `Temp/stage3/node-search-tree.txt`.

## Not done / not verified

- No screenshot of the node search popup: `SearchWindow` closes as soon as the Editor loses
  focus to the CLI, so it could not be captured; the category tree is recorded as text instead.
- NavMesh path following has no committed automated test (Edit Mode limitation above).
- Files outside the card's owned paths were changed because the work required them:
  `Agents/` (P9-001 layer), `Editor/Graph/NodeSearchProvider.cs`, `MCP/`, `Samples~/SimpleAgent/`,
  `package.json`.
