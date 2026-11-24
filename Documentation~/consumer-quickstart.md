# First production agent

This page walks through the native layer (`NativeTreeAgent`, generated Burst nodes, prepared
runtime assets). For the managed layer (`BehaviorTreeAgent` and C# `BtAction`/`BtCondition`
nodes), start with the **Simple Agent** sample and the five-minute start in `README.md`.

Import **First Agent** through AIBT's Package Manager Samples tab, then run **AIBT > Samples >
First Agent > Create Demo** and press Play. The cube moves to the goal using one generated Action,
typed external input and a committed Float32 effect. Its runtime assembly has no Authoring reference.

## Without writing a Burst node

A native tree does not need your own Burst nodes for blackboard logic, timers, signals or random
choices: the package ships 33 ready-made native leaves (`aibt.native.*`, shard
`aibt.native.standard`). Opt in once by listing the shard in your project's catalog set, as the
**Native Guard** sample does (`Catalog/NativeGuardCatalog.cs`):

```csharp
[AibtCatalogSet("aibt.samples.native-guard.catalog", 1u, typeof(StandardNativeShard))]
public static partial class NativeGuardCatalog { }
```

- `Native/Blackboard` (20): compare, set, copy and count Bool, Int32, Float32 and Float3 keys, distances.
- `Native/Flow` (4): `succeed`, `fail`, `idle`, and a fixed wait.
- `Native/Timing` (3): wait until a Bool key is true, and a deadline in an Int64 key shared by branches.
- `Native/Signals` (1): `signal` tells the game "do X" with an Int32 code on `aibt.native.signal`.
- `Native/Random` (5): chance, random int, float, point and wait, from a stream the host seeds.

They run on `NativeTreeAgent`, `ProductionTreeHost` and `TreeSimulation`, not in a
`BehaviorTreeAgent`. The **Native Guard** sample (Package Manager Samples tab, then **AIBT >
Samples > Native Guard > Create Demo**) is a tower built only from `aibt.core.*` and
`aibt.native.*` nodes. The [Native node library](native-node-library.md) guide maps every managed
node to its native counterpart and has recipes; agents build such a tree with the MCP/CLI recipes
in [generated/recipes.md](generated/recipes.md).

## Author and prepare

1. Create/open a version 2 tree in **AIBT > Graph Editor**. Save incomplete drafts whenever needed;
   compiler errors still prevent execution and runtime-asset preparation.
2. Add your generated nodes, Tree-scope variables and bindings. Float32 parameters accept ordinary
   fractional values; their units are part of your node contract. Drag ports to connect or reparent
   a child. Connections append in semantic order; **Earlier/Later** change that explicit order.
3. Save, then **More > Prepare Runtime Asset** and choose your generated catalog. Preparation compiles with
   exactly its declared shards and creates a `ProductionTreeAsset` at your selected asset path.
   Updating an existing runtime asset preserves its Unity GUID. A different asset type is refused.
4. Assign this compiled asset to a `NativeTreeAgent` (or your own integration component). The
   authored tree remains the editable authority. The asset records its source and catalog: saving
   the source or changing the catalog re-prepares it automatically, and its inspector shows a
   stale snapshot with a **Prepare Again** button when that fails. Running hosts keep the program
   they started with; restart them to pick up a new snapshot.

## Runtime integration

Add `NativeTreeAgent` to a GameObject and assign the prepared asset. It shares one program per
asset and catalog between agents, creates its host, joins a world (an assigned
`ProductionTreeWorld` or a shared `AIBT Native World`) and restarts the tree when the root finishes
(`Restart Mode`). Gameplay code feeds facts and handles effects by name:

```csharp
var agent = GetComponent<NativeTreeAgent>();
agent.OnEffect("move-speed", (float speed) => Move(speed));
agent.Completed += result => Debug.Log("Tree finished: " + result);
// Every frame or whenever it changes; written at the world's safe input boundary.
agent.SetFact("distance", Vector3.Distance(transform.position, goal.position));
```

A fact must be a Tree-scope key of the same type that no node writes; a wrong key is logged once
and ignored. Facts are kept across restarts; Tree-scope keys that nodes write start each run from
their defaults, because every run is a new host, while Agent-scope keys belong to the agent and keep
their values from run to run (see "What a restart keeps" below). A throwing effect handler is logged and does not block
later effects. The agent resolves the catalog from the type recorded in the asset; assign
`CatalogFactory` from code (for example `ProjectCatalog.TryCreateRuntimeCatalog`) when code
stripping may remove that type in a Player build (see "Player builds" below). Disabling the agent stops its host, leaves the
world and releases the program; the last agent disposes it.

### What a restart keeps

Agent-scope keys of a `NativeTreeAgent` belong to the component, not to a run (ADR-P11-013). They
start at their defaults when the component is enabled and keep their values across automatic
restarts, `Restart()`, `Stop()` followed by `Restart()`, and a change of `Tree` to a tree with the
same Agent contract (contract id, version, schema and layout). They return to their defaults when
the component is disabled, after a run whose host faulted (its values may be mid-update), when
`Tree` changes to another Agent contract, on `Restart(true)`, and when your code stopped the host
itself (`agent.Host.Stop()`, then `Restart()`: a stopped host cannot be read). Tree-scope keys start
from their defaults in every run; facts are written again. The values are copied as bytes, so do not
keep a handle into one run's runtime (an `OperationId`, for example) in an Agent key: the next run's
host does not know it.

| What happens | `BehaviorTreeAgent` (all keys) | `NativeTreeAgent`, Tree keys | `NativeTreeAgent`, Agent keys | `TreeSimulation` agent |
| --- | --- | --- | --- | --- |
| The root finishes and the tree restarts | kept | defaults (facts written again) | kept | defaults (`RootCompletionPolicy.Restart` makes a fresh instance) |
| `Restart()` (`TryRestart` in a group) | kept | defaults | kept | defaults |
| `Restart(true)` | defaults | defaults | defaults | — |
| `Stop()`, then `Restart()` | kept | defaults | kept | defaults |
| Disable, then enable | kept (ticking pauses) | defaults | defaults | — |
| A run whose host faulted, then `Restart()` | — | defaults | defaults | — |
| Your code calls `agent.Host.Stop()`, then `Restart()` | — | defaults | defaults | — |
| Another tree assigned | defaults (the agent is rebuilt) | defaults | kept with the same Agent contract, defaults otherwise | — |

Agent memory lives in the agent's own value buffer: it is not shared with other agents and is not
saved. `SetFact` writes Tree keys only, and the library nodes (`aibt.native.*`) bind Tree keys only
(an Agent key bound to one is refused with AIBT3018), so Agent keys are read and written by your
project's nodes. A counter or cooldown that only library nodes use lasts as long as the run; loop
inside one run to keep it (see [Native node library](native-node-library.md#a-cooldown-shared-by-several-branches)).

### Reading a value back

`TryGetValue` reads a Tree-scope or Agent-scope key of the running tree, for UI, animation or
debugging: a key nodes write, a fact, or a key nobody writes. It takes the same types as `SetFact`;
`TryGetVector3` reads a Float3 key as a `Vector3`.

```csharp
// The current run's value; false while the agent has no run.
if (agent.TryGetValue("sweep-spot", out int spot))
    spotText = spot < 0 ? "no spot" : "spot " + spot;
// The final values of a run: read them in Completed, before the next run starts.
agent.Completed += result => agent.TryGetValue("weapon-ready-at", out lastDeadline);
```

A read shows the current run's tree: its defaults before the first update, its live values while it
runs, its final values until the next run starts, and false when the agent has no run (stopped,
faulted or disabled).

- Every run is a new tree, so a new run starts from the Tree defaults (Agent keys carry their
  values over, see above). With Restart Mode `Always` the next run starts right after the root
  finishes, so read a run's final Tree values in `Completed`; with `Never` they stay readable until
  `Restart`.
- A fact is written at the world's next update, not when you call `SetFact`. Until then the key
  still reads its old value, and right after an automatic restart it reads its default for up to
  one frame.
- With a profile that forces `PipelinedJobs`, a tree's update runs across frames and cannot be read
  while it runs, so most reads return false. The agent logs one warning about it. Use another
  profile for agents whose values you read every frame.
- A key that does not exist, or has another type, is logged once and returns false. An unsupported
  type throws.
- No allocation after the first read of a key. Without the key lookup, read through the host
  (`ProductionTreeHost.TryResolveExternalTreeRead` once, then `TryReadExternalTreeValue`; see
  [Production host](production-host.md)).

### Low-level path

For custom scheduling or populations managed by your own code, keep one `ProductionTreeProgram` per shared asset/catalog, rather than repeating compilation or
catalog allocation for every agent:

```csharp
if (!treeAsset.TryCreateProgram(ProjectCatalog.TryCreateRuntimeCatalog,
        out var program, out var catalogFailure))
    throw new InvalidOperationException(catalogFailure.ToString());

if (!program.TryBootstrap(host, out var hostFailure, effectCapacity: 4))
    throw new InvalidOperationException(hostFailure.Code.ToString());
```

`ProjectCatalog` is your source-generated catalog class. Choose effect capacity to cover your
node's command burst; 4 is the small example's choice, not a scheduler setting. Additional hosts
use the same program. The default host clock is scaled Unity time; the optional clock and trace
capacity arguments retain the existing host contracts. Catalog mismatch is an explicit failure.

Implement `IProductionTreeWorldParticipant`: resolve typed input handles once, stage Unity facts
in `SampleInputs`, flush them in `TryFlushInputs`, and apply committed values in `Receive`. Register
with `ProductionTreeWorld` and a selected `SchedulingProfile`. The world preserves the existing
safe input/execute/effect order, including pipelined idle barriers.

On shutdown unregister participants, call `host.Stop()` on each host, then dispose the program.
`Stop()` is idempotent and permanently cancels that host; destruction calls the same path. A stop
requested from a callback finishes cleanup after that callback. `program.TryDispose` refuses while
native hosts retain its catalog, so deferred cleanup can be retried explicitly. Root Success/Failure
still stops execution and does not automatically restart or pool an instance.

## Player builds

A `NativeTreeAgent` Player never reads JSON or compiles its tree: the runtime asset is compiled
in the Editor. That holds only for `NativeTreeAgent`. A `BehaviorTreeAgent` parses its tree's JSON
and compiles it in the Player when it first loads it. So does a scene that builds its native trees
from JSON at run time, as Swarm Arena's low-level path does.
`NativeTreeAgent` resolves the catalog type recorded in the asset by reflection and
`BehaviorTreeAgent` creates `[BtNode]` classes by reflection, so a Player with managed code
stripping would lose them. The package's build step `AibtLinkXmlProcessor` writes an additional
link.xml for every `[BtNode]` class and `[AibtCatalogSet]` catalog in the Player's assemblies.
P9-006 verified a Windows IL2CPP Player with stripping level High: without that step every managed
tree failed with unknown node types and the native agent reported its catalog as stripped; with
it both samples ran. Assigning `CatalogFactory` from code remains the explicit alternative.

## Inspect execution

**Preview** opens reference controls annotating the same graph. Built-in leaves work directly;
project native-only nodes require an explicit reference provider. Preview does not execute native
gameplay effects.

During Play, select an agent GameObject (the open graph attaches automatically) or click **Debug** in its source graph. The
host supplies its exact immutable program/debug map. For a `NativeTreeAgent` the graph shows the
active path, nodes that just finished and the root result; exit statuses of inner nodes, nodes that
start and finish within one frame and native blackboard values are not shown. A mismatched source is refused. Trace
scrubbing and live highlighting preserve graph node objects and layout. Editing/reloading the
document or closing the graph detaches highlighting; it never changes the running agent.
**AIBT > Attach Selected Host Trace** remains available for unmapped raw trace. Dropped trace
events remain visibly marked as incomplete history.

## Derived asset format

The Unity runtime asset stores an envelope with magic `0x41544250`, envelope version 1, the existing
semantic-program hash preimage, compiled-v2 outer preimage and generated binding tables. The payload
is checksummed and the generated catalog fingerprint is pinned. Unknown versions and truncated or
corrupt data fail before native ownership is allocated. This is derived compiled storage, not a
new authoring format or a save-game format. Regenerate it when its source/catalog changes.

The low-level compiled/bootstrap APIs remain available. No scheduler defaults, platform claims,
automatic migration or Agent/Shared graph-authoring capabilities are changed by this workflow.
