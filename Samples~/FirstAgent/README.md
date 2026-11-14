# First Agent

1. Import **First Agent** from AIBT's Package Manager Samples tab.
2. Run **AIBT > Samples > First Agent > Create Demo**, then press Play.
3. The cube walks to the goal sphere. When it arrives (within 0.25 units) the tree finishes with
   Success, the goal jumps to the other side and the tree starts again. The generated Burst node
   owns the decision; the Unity component only reports the distance and applies movement.
4. Open `first-agent.aibt.json` in **AIBT > Graph Editor** and select the cube during Play: the
   graph highlights the running node live (or press **Debug Selected**, or **Debug in Graph** in the
   `NativeTreeAgent` inspector).
5. Change `speed` in the graph and Save. The runtime asset `FirstAgent.runtime.asset` is prepared
   again automatically (its inspector shows whether it is up to date). Agents already running keep
   their program; enter Play Mode again to use the new one.

## How it is wired

- `NativeTreeAgent` (package component) runs the prepared asset. It shares one program between all
  agents with the same asset, joins a shared scheduler world, restarts when the root finishes and
  releases all native memory when disabled.
- `FirstAgent` (this sample, ~30 lines) is the game side:

```csharp
_agent.OnEffect("move-speed", (float speed) => transform.position =
    Vector3.MoveTowards(transform.position, _goal.position, speed * Time.deltaTime));
_agent.SetFact("distance", Vector3.Distance(transform.position, _goal.position));
```

Facts are Tree-scope blackboard keys that no node writes; they are written at the scheduler's safe
boundary. Effects are the node's command ids (`[AibtCommand]`), delivered on the main thread after the
node's update commits.

The node and catalog are separate generated assemblies, as required by the supported Burst ABI.
The Player never reads JSON or compiles trees: the asset is compiled in the Editor. The agent finds
the catalog recorded in the asset by name; if your build strips it, assign
`NativeTreeAgent.CatalogFactory = YourCatalog.TryCreateRuntimeCatalog` before the agent is enabled.

The low-level path (`ProductionTreeAsset.TryCreateProgram`, `ProductionTreeProgram.TryBootstrap`,
`ProductionTreeHost`, `IProductionTreeWorldParticipant`) is unchanged and remains available for full
control; see `Documentation~/production-host.md`.

The node in `Nodes/MoveNode.cs` uses the short `AIBT.Native` attributes (`[AibtAction]`, `[AibtParam]`,
`[AibtKey]`, `[AibtCommand]`) and declares only `Tick`. They expand to exactly the long `AIBT.Burst`
attributes; see `Documentation~/burst-node-authoring.md`.
