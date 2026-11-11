# First production agent

Import **First Agent** through AIBT's Package Manager Samples tab, then run **AIBT > Samples >
First Agent > Create Demo** and press Play. The cube moves to the goal using one generated Action,
typed external input and a committed Float32 effect. Its runtime assembly has no Authoring reference.

## Author and prepare

1. Create/open a version 2 tree in **AIBT > Graph Editor**. Save incomplete drafts whenever needed;
   compiler errors still prevent execution and runtime-asset preparation.
2. Add your generated nodes, Tree-scope variables and bindings. Float32 parameters accept ordinary
   fractional values; their units are part of your node contract. Drag ports to connect or reparent
   a child. Connections append in semantic order; **Earlier/Later** change that explicit order.
3. Save, then **Prepare Runtime** and choose your generated catalog. Preparation compiles with
   exactly its declared shards and creates a `ProductionTreeAsset` at your selected asset path.
   Updating an existing runtime asset preserves its Unity GUID. A different asset type is refused.
4. Assign this compiled asset to your Unity integration component. The authored tree remains the
   editable authority. Re-prepare after source or catalog changes and restart its hosts.

## Runtime integration

Keep one `ProductionTreeProgram` per shared asset/catalog, rather than repeating compilation or
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

## Inspect execution

**Preview** opens reference controls annotating the same graph. Built-in leaves work directly;
project native-only nodes require an explicit reference provider. Preview does not execute native
gameplay effects.

During Play, select an agent GameObject and click **Debug Selected** in its source graph. The
host supplies its exact immutable program/debug map. A mismatched source is refused. Trace
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
