# First production agent

This page walks through the native layer (`NativeTreeAgent`, generated Burst nodes, prepared
runtime assets). For the managed layer (`BehaviorTreeAgent` and C# `BtAction`/`BtCondition`
nodes), start with the **Simple Agent** sample and the five-minute start in `README.md`.

Import **First Agent** through AIBT's Package Manager Samples tab, then run **AIBT > Samples >
First Agent > Create Demo** and press Play. The cube moves to the goal using one generated Action,
typed external input and a committed Float32 effect. Its runtime assembly has no Authoring reference.

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
and ignored. Facts are kept across restarts. A throwing effect handler is logged and does not block
later effects. The agent resolves the catalog from the type recorded in the asset; assign
`CatalogFactory` from code (for example `ProjectCatalog.TryCreateRuntimeCatalog`) when code
stripping may remove that type in a Player build (see "Player builds" below). Disabling the agent stops its host, leaves the
world and releases the program; the last agent disposes it.

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

The Player never reads JSON or compiles native trees; the runtime asset is compiled in the Editor.
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
