# AI-Native Behavior Tree (AIBT)

AIBT is a behavior-tree package for Unity. You draw a tree in a graph editor (or write it as
JSON), give it to a component on a GameObject, and press Play. Nodes are small C# classes. The same
trees and node contracts are readable by people, by the validator and compiler, and by AI tools
(CLI and MCP).

It has two runtime layers that share one tree format and one editor:

| | Managed layer | Native layer |
| --- | --- | --- |
| Component | `BehaviorTreeAgent` | `NativeTreeAgent` |
| Tree asset | the `.aibt` file itself | a prepared `ProductionTreeAsset` (made from the `.aibt` file) |
| Custom node | a class deriving from `BtAction` / `BtCondition` | a `partial struct` with `[AibtAction]` / `[AibtCondition]`, compiled by a source generator to Burst |
| Node touches the scene | directly (Transform, components, other objects) | never; it reads facts and emits effects |
| Standard nodes | 52 `aibt.unity.*` nodes (movement, navigation, animation, ...) | 33 `aibt.native.*` nodes (blackboard, Float3 distance, flow, timing, signals, random), plus the core composites and decorators |
| Live debugging in the graph | every node, live blackboard values | active path, just-finished nodes, root result |
| Main-thread cost, IL2CPP Player ([measured](Documentation~/performance.md)) | ≈1.2 µs per agent-tick for a 15-node tree, 0 B allocated | ≈3.8 µs per agent-update waiting in one leaf, ≈8 µs for a looping 16-node tree, 0 B while the tree runs; each restart allocates |
| Best for | gameplay AI, fast iteration; about 800 agents ticking every frame in 1 ms of main thread, thousands with `Interval` ticking | zero GC while running, deterministic Burst execution, a simulation kept off the scene |

Start with the managed layer. Move a tree to the native layer when you need what it guarantees (zero GC while running, deterministic Burst execution).

Requirements: Unity 6 (`6000.0` or newer; this release is verified on `6000.7.0b1`), Burst
`1.8.29`, Collections `6.5.0`, Newtonsoft JSON `3.2.2` (installed as dependencies). The package is
`0.1.0` and its editor and AI integration are experimental. MIT license.

## Install

Add the package to `Packages/manifest.json` from a local checkout (or use **Add package from disk**
in Package Manager):

```json
"com.azzazello.aibt": "file:C:/path/to/AIBT"
```

Then open **Window > Package Manager > AI-Native Behavior Tree > Samples** and import **Simple
Agent** (managed layer) and **First Agent** (native layer). Each has a setup menu that builds a demo
scene.

## Five-minute start: managed layer

1. Import **Simple Agent**, run **AIBT > Samples > Simple Agent > Create Demo**, press Play. A guard
   patrols, chases a wanderer while it sees it and returns; a scout keeps its distance. Select the
   guard and open **AIBT > Graph Editor**: the graph follows the running tree live.
2. Your own tree: **AIBT > Graph Editor > New**, add nodes with right-click or **Space**, **Save**
   (it asks for an `.aibt` path). Add **Behavior Tree Agent** (`AIBT/Behavior Tree Agent`) to a
   GameObject and drag the `.aibt` file into its **Tree** field. Press Play.
3. Your own node: a class with `[BtNode]`. Public fields are the parameters a tree author edits
   (`perSecond` appears as `per-second`); a `BbKey<T>` field is a blackboard key the author picks.
   The node appears in the editor's node search after the scripts compile; no registration, catalog
   or code generation is involved.

```csharp
[BtNode("game.heal", Category = "Combat", Summary = "Restores health until it is full.")]
public sealed class HealNode : BtAction
{
    public float perSecond = 25f;  // a parameter: the tree author edits it in the graph ("per-second")
    public BbKey<float> health;    // a blackboard key: the tree author picks which one

    protected override NodeStatus OnTick()
    {
        var value = Mathf.Min(100f, Get(health) + perSecond * DeltaTime);
        Set(health, value);
        return value >= 100f ? NodeStatus.Success : NodeStatus.Running;
    }
}

[BtNode("game.is-low-health", Category = "Combat", Summary = "Holds while health is below a threshold.")]
public sealed class IsLowHealthNode : BtCondition
{
    public float threshold = 30f;
    public BbKey<float> health;

    protected override bool Check() => Get(health) < threshold;
}
```

An action returns `Running` to be ticked again, or `Success`/`Failure` to finish. Override
`OnEnter`, `OnAbort` (the tree interrupted it while Running) and `OnExit` (always, after success,
failure or abort) when the node starts or cleans something up, and `OnInitialize` to cache
components once. A condition answers in one tick and must not change game state; its `interrupt`
field (`None`, `Self`, `LowerPriority`, `Both`, set per node in the graph) makes a running branch
re-check it every tick. Inside a node you have `Agent`, `gameObject`, `transform`, `GetComponent<T>()`,
`DeltaTime`, `TreeTime`, `Blackboard`, `ResolveGameObject(reference)`,
`ResolveComponent<T>(reference)` and `WarnOnce(message)`. One node instance exists per agent, so
private fields are per-agent state.

`BehaviorTreeAgent` options: **Tick Mode** (`EveryFrame`, `Interval`, or `Manual` with
`agent.Tick(deltaTime)`), **Restart On Complete** (on by default: when the root finishes, the tree
starts again and the blackboard is kept), **Use Unscaled Time**, **References** and **Event
Responses** (below). From code: `Restart(resetBlackboard)`, `Stop()`, `IsRunning`, `LastResult`,
`Completed`, `Faulted`, `LastError`. A node that throws stops the tree and raises `Faulted` once.

## Five-minute start: native layer

1. Import **First Agent**, run **AIBT > Samples > First Agent > Create Demo**, press Play. A cube
   walks to a goal; when it arrives the tree succeeds, the goal moves and the tree starts again.
2. The node is a Burst struct with the short `AIBT.Native` attributes. Value types come from the
   field types; the blackboard access comes from the handle type. Only `Tick` is required
   (`Enter`, `Abort`, `Exit` are optional). From `Samples~/FirstAgent/Nodes/MoveNode.cs`:

```csharp
[AibtCatalogShard("aibt.samples.first-agent", 1u)]
public partial struct FirstAgentShard { }

public partial struct MoveConfig
{
    [AibtParam("speed")] public float Speed;
    [AibtParam("stop-distance")] public float StopDistance;
    [AibtKey("distance")] public BlackboardReadHandle<float> Distance;
    [AibtCommand("move-speed")] public CommandHandle<float> MoveSpeed;
}

public partial struct MoveMemory { }

[AibtAction("aibt.samples.first-agent.move")]
public partial struct MoveNode
{
    public static NodeStatus Tick(in MoveConfig config, ref MoveMemory memory, ref BurstTickContext context)
    {
        if (config.Speed <= 0f || config.StopDistance < 0f) return NodeStatus.Failure;
        if (FirstAgentShard.BurstAccess.TryRead(ref context, config.Distance, out var distance) != BurstContextResult.Success)
            return NodeStatus.Failure;
        if (distance <= config.StopDistance) return NodeStatus.Success;
        return FirstAgentShard.BurstAccess.TryEmit(ref context, config.MoveSpeed, in config.Speed) == BurstContextResult.Success
            ? NodeStatus.Running : NodeStatus.Failure;
    }
}
```

3. Nodes live in a *shard* assembly and are selected by a *catalog* in a second assembly. Both
   reference `AIBT.Runtime` and the packaged analyzer (`"analyzers": ["GUID:31a3f09584684895a0a72916d3ad4de0"]`
   in the `.asmdef`; copy the sample's `Nodes/` and `Catalog/` asmdefs). The catalog, from
   `Samples~/FirstAgent/Catalog/FirstAgentCatalog.cs`:

```csharp
[AibtCatalogSet("aibt.samples.first-agent.catalog", 1u, typeof(FirstAgentShard))]
public static partial class FirstAgentCatalog { }
```

4. Open the tree in **AIBT > Graph Editor** and use **More > Prepare Runtime Asset**: it compiles
   the tree against your catalog into a `ProductionTreeAsset`. Add **Native Tree Agent**
   (`AIBT/Native Tree Agent`) to a GameObject and assign that asset. Gameplay code feeds facts and
   handles effects by name. From `Samples~/FirstAgent/Runtime/FirstAgent.cs`:

```csharp
private void Awake()
{
    _agent = GetComponent<NativeTreeAgent>();
    _agent.OnEffect("move-speed", (float speed) =>
        transform.position = Vector3.MoveTowards(transform.position, _goal.position, speed * Time.deltaTime));
    _agent.Completed += result =>
    {
        if (result != NodeStatus.Success) return;
        _goal.position = Vector3.Scale(_goal.position, new Vector3(-1f, 1f, 1f)); // reached: send the goal to the other side
        ReportDistance();
    };
}

private void Update() => ReportDistance();

private void ReportDistance() => _agent.SetFact("distance", Vector3.Distance(transform.position, _goal.position));
```

`NativeTreeAgent` shares one program per asset and catalog between agents, joins a world (the
assigned `ProductionTreeWorld` or one shared `AIBT Native World`), restarts by **Restart Mode**
(`Always`, `Never`, `OnSuccess`, `OnFailure`) and releases its native memory when disabled. It
also has `Restart()`, `Stop()`, `IsRunning`, `LastResult`, `CompletedRuns`, `Completed`, `Faulted`
and `LastError`. The low-level path (`ProductionTreeAsset.TryCreateProgram`,
`ProductionTreeProgram.TryBootstrap`, `ProductionTreeHost`, `IProductionTreeWorldParticipant`) is
still available: see [Production tree host](Documentation~/production-host.md) and the
**Swarm Arena** sample. Long attribute forms, async actions and conditions:
[Burst node authoring](Documentation~/burst-node-authoring.md) and the **Public Burst Nodes** sample.

## Exchanging data with game code

| Direction | Managed (`BehaviorTreeAgent`) | Native (`NativeTreeAgent`) |
| --- | --- | --- |
| Node acts on the scene | directly, inside the node | not possible; emit an effect |
| Game writes a value for the tree | `agent.Blackboard.Set("key", value)` | `agent.SetFact("key", value)` |
| Game reads a value the tree wrote | `agent.Blackboard.Get<T>("key")`, `TryGet`, `GetOrDefault` | through an effect |
| Tree tells the game something happened | `aibt.unity.raise-event` + `agent.Events.Subscribe(name, handler)` or **Event Responses** in the inspector | `agent.OnEffect("command", handler)` |
| Tree uses another scene object | **References** list (name → object), `agent.SetReference(name, obj)` | pass its position or state as a fact |
| Tree finished | `Completed` event | `Completed` event |

Managed blackboard keys are declared in the graph's Blackboard panel; `Get`/`Set` accept `bool`,
`int`, `long`, `float`, `double`, `Vector2`, `Vector3`, `Quaternion` and `string` for the matching
key type. Values written by game code are seen on
the tree's next tick and survive restarts. The blackboard holds values, not objects: a node that
acts on another object takes a string `reference` parameter (empty means the agent itself) and
resolves it from the agent's **References** list; a missing reference fails the node with one
warning, never an exception. A gameplay component using all of these:

```csharp
[RequireComponent(typeof(BehaviorTreeAgent))]
public sealed class Fighter : MonoBehaviour
{
    public Transform enemy;
    private BehaviorTreeAgent _agent;

    public float Health => _agent.Blackboard.Get<float>("health");          // tree -> game

    public void TakeDamage(float amount) =>
        _agent.Blackboard.Set("health", Health - amount);                   // game -> tree

    private void Start()
    {
        _agent = GetComponent<BehaviorTreeAgent>();
        _agent.SetReference("enemy", enemy);                                // nodes use reference "enemy"
        _agent.Events.Subscribe("healed", agent => Debug.Log(agent.name + " is healed"));
        _agent.Completed += result => Debug.Log("Tree finished: " + result);
    }
}
```

Native facts are Tree-scope keys of the same type that no node writes; supported types are bool,
int, long, float, double, `Float2Value`, `Float3Value`/`Vector3` and `QuaternionValue`. They are
written at the scheduler's safe boundary and kept across restarts; a wrong key or type is logged
once and ignored. Effects are the node's `[AibtCommand]` ids, delivered on the main thread after
the node's update commits, for Int32, Float32 and Float3/`Vector3`; a throwing handler is logged
and does not block later effects.

A key that no node writes or binds as a key parameter is reported in the graph as the note
**AIBT2053 Input key '...'**. That is expected for keys gameplay code feeds (native facts always
are such keys); it is a reminder, not a problem.

## Working in the graph editor

Open **AIBT > Graph Editor**, or double-click an `.aibt` file. The left panel lists the project's
trees with their state (`Valid`, errors, `Needs native compile`).

- **Adding nodes.** Right-click the canvas, press **Space**, or drop an edge on empty canvas to open
  the node search. Composites and decorators are listed directly; conditions and actions are grouped
  by category (Movement, Navigation, Blackboard, ...). Type to filter.
- **Connecting.** Drag from a port to connect or reparent. Child order is the number badge on each
  child, changed with **Alt+Left/Right** or the context menu, never by position.
- **Shortcuts.** **Ctrl+S** save (tree and layout together), **Ctrl+Z/Ctrl+Y** undo/redo (semantic
  and layout edits share one history, also in Edit > Undo), **Ctrl+D** duplicate, **Delete**,
  **F** frame, **Ctrl+Shift+L** auto layout (pinned nodes stay).
- **Inspector.** The selected node's parameters as typed fields; contract defaults are greyed
  (right-click > Reset to default); blackboard-key fields list only keys of the expected type.
  The Blackboard panel declares keys, their types and defaults. The Problems list shows validation
  errors, warnings and notes; click one to select its node.
- **Live debugging.** In Play Mode select a GameObject whose agent runs the open tree; the graph
  attaches automatically (or press **Debug**). Running nodes pulse, finished nodes flash green, red
  or grey, the active path is highlighted and the Blackboard panel shows live values. For a
  `NativeTreeAgent` the view shows the active path, just-finished nodes and the root result.
  Editing or closing the graph detaches it; it never changes the running agent.
- **Preparing native trees.** **More > Prepare Runtime Asset** creates or updates a
  `ProductionTreeAsset` (its GUID is kept). The asset records its source and catalog: saving the
  source or changing the catalog re-prepares it automatically. Its inspector shows the state
  (`Current`, `SourceChanged`, `CatalogChanged`, `SourceMissing`, `CatalogMissing`, `Unknown`)
  with **Prepare Now / Prepare Again**. **More > Check Native Compile** compiles without writing.
- **Preview** (**AIBT > Behavior Tree Preview**) steps a tree in the reference executor without
  entering Play Mode. It does not run trees with `BtAction`/`BtCondition` nodes; those run only in
  a `BehaviorTreeAgent`.

Details: [Visual editor and layout](Documentation~/editor-and-layout.md).

## Standard nodes

The full, generated reference with every parameter, default and status is
[node-catalog.md](Documentation~/generated/node-catalog.md). Summary:

| Category | Nodes |
| --- | --- |
| Composites | `aibt.core.memory-sequence`, `memory-selector`, `reactive-sequence`, `reactive-selector`, `reactive-sequence-retaining`, `reactive-selector-retaining`, `parallel` |
| Decorators | `aibt.core.inverter`, `succeeder`, `failer`, `repeater`, `timeout`, `cooldown` |
| Flow (7) | `aibt.unity.wait-seconds`, `wait-random`, `wait-frames`, `wait-until`, `idle`, `succeed`, `fail` |
| Blackboard (21) | `set-bool`, `set-int`, `set-float`, `set-vector3`, `copy-*`, `increment-int`, `increment-float`, `toggle-bool`, `is-true`, `compare-int`, `compare-float`, `compare-*-keys`, `is-vector3-set`, `random-int`, `random-float`, `random-point`, `distance` |
| Movement (7) | `move-towards`, `move-direction`, `look-at`, `rotate-towards`, `turn-by`, `keep-distance`, `is-within-distance` |
| Navigation (2) | `nav-move-to`, `nav-stop` (needs the AI module) |
| Animation (3) | `animator-trigger`, `animator-bool`, `animator-float` (needs the Animation module) |
| GameObject (4) | `set-active`, `destroy-self`, `get-position`, `has-reference` |
| Audio (1) | `play-audio` (needs the Audio module) |
| Events (1) | `raise-event` |
| Checks (5) | `line-of-sight` (needs Physics), `random-chance`, `start-cooldown`, `is-cooldown-ready`, `time-since-changed` |
| Debug (1) | `log` |
| Native/Blackboard (20) | `aibt.native.bool-is-true`, `float-at-least`, `int-at-least`, `int-equals`, `int-keys-at-least`, `float-keys-at-least`, `float3-within-distance`, `float3-is-set`, `set-bool`, `set-int`, `set-float`, `set-float3`, `copy-bool`, `copy-int`, `copy-float`, `copy-float3`, `increment-int`, `increment-float`, `toggle-bool`, `float3-distance` (native layer only) |
| Native/Flow (4) | `aibt.native.succeed`, `fail`, `idle`, `wait-microseconds` (native layer only) |
| Native/Timing (3) | `aibt.native.wait-until-true`, `set-deadline`, `deadline-passed` (native layer only) |
| Native/Signals (1) | `aibt.native.signal`: an Int32 code on the effect channel `aibt.native.signal` (native layer only) |
| Native/Random (5) | `aibt.native.random-chance`, `random-float`, `random-int`, `wait-random-microseconds`, `random-float3-in-radius`: seeded per tree instance from the host's seed (`NativeTreeAgent.RandomSeed`, default 0) (native layer only) |
| Built-in leaves | `aibt.stdlib.wait`, `aibt.stdlib.random-condition` (both layers) |

`aibt.unity.*` nodes run only in `BehaviorTreeAgent`; `aibt.native.*` nodes only on native hosts
(`NativeTreeAgent`, `ProductionTreeHost`, `TreeSimulation`). The `aibt.native.*` nodes are opt-in: list `typeof(StandardNativeShard)` in your
`[AibtCatalogSet]`; each node's "when not to use" names its `aibt.unity.*` counterpart. The
[Native node library](Documentation~/native-node-library.md) guide maps every managed node to its
native counterpart and has recipes (negation, a cooldown shared by key, signals, guards, randomness); the
**Native Guard** sample builds a tree from package nodes only. Negations
and "below" are an `aibt.core.inverter` over an "at least" node; the per-node contracts are in
[the node catalog](Documentation~/generated/node-catalog.md). Native hosts do not run observer
re-evaluation today, so an `observer` block on an `aibt.native.*` condition changes nothing; the
graph, the tree list and MCP `validate` show the warning `AIBT2054` on it. To
guard a branch natively, put the condition first under a `reactive-sequence`: the branch is
aborted at the next update after the key changes. Above waits and multi-step branches use
`reactive-sequence-retaining` / `reactive-selector-retaining`: a plain reactive composite re-enters
its running child at every update, so a wait under it never runs out. See
[Production tree host](Documentation~/production-host.md#clock-budget-and-completion). Nodes that
need an optional Unity module are compiled only when the module is enabled.

## Which layer to choose

- **Managed** when nodes need the scene (physics queries, animation, NavMesh, audio) and when you
  iterate on gameplay. A tree is ready as soon as it is saved. A warmed-up tick allocates nothing;
  a 15-node tree costs about 1.2 µs per agent-tick in an IL2CPP Player, so about 800 agents ticking
  every frame fit in 1 ms of main thread, and many more with `TickMode = Interval`.
- **Native** for deterministic Burst execution and zero GC while trees run, when the game can
  express the world as facts and effects. It needs a shard and a catalog assembly and a prepared
  runtime asset. For small trees it costs more main-thread time per agent than the managed layer
  (see [Performance](Documentation~/performance.md)); choose it for its guarantees, not for speed.
- Both layers use the same `.aibt` format, core composites, editor and validator, but a tree's leaf
  nodes belong to one layer: the managed `aibt.unity.*` nodes do not run in a `NativeTreeAgent`,
  and generated Burst nodes do not run in a `BehaviorTreeAgent`.

## Known limits

- Native live view shows only the active path, just-finished nodes and the root result: no exit
  statuses of inner nodes, no nodes that start and finish within one frame, no native blackboard
  values.
- After an automatic re-prepare, running native hosts keep the program they started with; restart
  them (or re-enter Play Mode) to pick up the new snapshot.
- Runtime assets prepared before P9-004 have no recorded source and catalog and show `Unknown` until
  prepared once more.
- Player builds with managed code stripping: AIBT creates `[BtNode]` classes and resolves the
  native catalog recorded in a runtime asset by reflection. The package adds a build step
  (`AibtLinkXmlProcessor`) that preserves every `[BtNode]` class and `[AibtCatalogSet]` catalog in
  the Player's assemblies, verified with IL2CPP and stripping level High. If you load node or
  catalog types some other way, assign `NativeTreeAgent.CatalogFactory = YourCatalog.TryCreateRuntimeCatalog`
  before the agent is enabled. See the [P9-006 evidence](Planning~/Evidence/P9-006/README.md).
- MCP `search_nodes` returns 50 nodes per page by default; pass `count` or page with `offset`.
- Condition interrupts (`ConditionInterrupt`) exist only in the managed layer.
- `BehaviorTreeAgent` does not run tree `observer` blocks: a tree with one is refused when it is
  loaded (AIBT2051). Use a condition's `interrupt` field or a reactive composite instead.
- `NativeTreeAgent.Profile = SchedulingProfile.JobsAgentFrame` runs each agent's whole update in a
  Burst job (no capabilities needed); agents it cannot run fall back to Immediate with a reason on
  their frame entry. Every native update stops after `ProductionTreeHost.MaximumStepsPerUpdate`
  steps (default 1 000 000). See [Performance](Documentation~/performance.md).
- A `NativeTreeAgent` profile that forces `BatchedJobsSameFrame` or `PipelinedJobs` needs
  `SchedulerJobsCapabilities` on its world's `ProductionTreeScheduler` (there is no default);
  without them the agent faults with a message that says so. See [Performance](Documentation~/performance.md).
- Every native restart (`NativeTreeRestartMode`) destroys and re-creates the agent's
  `ProductionTreeHost`, which allocates. For a tree that finishes every few ticks, loop inside the
  tree (a repeater at the root) instead of restarting it.
- Fixed-string blackboard values allocate when written and read in the managed layer.
- The reference Preview window does not run managed `BtAction`/`BtCondition` nodes.
- The live graph samples node states about 30 times a second; a node that starts and finishes
  between two samples is not flashed.
- The package has not declared `1.0`; public APIs are experimental.

## AI tools and command line

Agents and scripts can inspect node contracts, edit and validate trees, compile, prepare runtime
assets and read a running host's trace through the same Editor operations. See
[CLI and MCP setup](Documentation~/cli.md) and [AI and MCP integration](Documentation~/ai-and-mcp.md).

## Documentation

- [First production agent](Documentation~/consumer-quickstart.md): native layer step by step.
- [Visual editor and layout](Documentation~/editor-and-layout.md)
- [Subtrees](Documentation~/subtrees.md): one tree used as a node of another; interface keys,
  graph editor, MCP and CLI.
- [Native node library](Documentation~/native-node-library.md): the 33 ready-made `aibt.native.*`
  nodes, how to opt in, the managed → native mapping and recipes.
- [Burst node authoring](Documentation~/burst-node-authoring.md)
- [Production tree host](Documentation~/production-host.md)
- [Execution and scheduling](Documentation~/execution-and-scheduling.md)
- [Simulation container](Documentation~/simulation-container.md) (experimental): trees stepped by
  the game with `Step(tick)`, without GameObjects.
- [Hot reload](Documentation~/hot-reload.md)
- [Data formats](Documentation~/data-formats.md): `.aibt` / `.aibt.json` trees, `.aibt.layout.json`
  layouts, `.aibt/policy.json`, `.aibtcase.json` behavior cases.
- Generated reference: [node catalog](Documentation~/generated/node-catalog.md), API for
  [AIBT.Agents](Documentation~/generated/api-reference-agents.md),
  [AIBT.Runtime](Documentation~/generated/api-reference-runtime.md),
  [AIBT.Authoring](Documentation~/generated/api-reference-authoring.md),
  [AIBT.Editor](Documentation~/generated/api-reference-editor.md),
  [AIBT.Mcp](Documentation~/generated/api-reference-mcp.md).
- [Architecture](Documentation~/architecture.md), [Scope](Documentation~/scope.md),
  [Architectural decisions](Documentation~/decisions.md),
  [Normative specifications](Documentation~/specifications/conventions.md)
- [Performance](Documentation~/performance.md): measured cost of both layers and how to keep it low.
- [Benchmarks](Documentation~/benchmarks.md), [Testing strategy](Documentation~/testing.md)
- [Roadmap and phase history](Documentation~/roadmap.md), [Changelog](CHANGELOG.md)
- [Samples](Samples~/README.md), [Contributing](CONTRIBUTING.md)
- Planning: [master plan](Planning~/MASTER_PLAN.md), [work items](Planning~/work-items.json),
  [user and infrastructure actions](Planning~/USER_ACTIONS.md)
