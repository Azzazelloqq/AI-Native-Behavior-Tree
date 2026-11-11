# First Agent

1. Import **First Agent** from AIBT's Package Manager Samples tab.
2. Run **AIBT > Samples > First Agent > Create Demo**, then press Play.
3. The cube moves toward x=5 and finishes within 0.25 units. The generated node owns the decision;
   the Unity component supplies distance and applies committed movement effects.
4. Open `first-agent.aibt.json` in **AIBT > Graph Editor**, select the cube during Play, then click
   **Debug Selected**. The trace annotates that exact authoring graph. Editing detaches it safely.
5. Stop Play, change `speed` to another positive float, Save, then **Prepare Runtime**, choosing
   `FirstAgentCatalog` and the existing demo runtime asset. Restart Play to use the new program.

The preparation step compiles in the Editor. The sample's runtime assembly references only Runtime
and its generated catalog; it never reads JSON, materializes metadata, or compiles trees in Player.
The node and catalog are separate generated assemblies, as required by the supported Burst ABI.

In your game, assign a `ProductionTreeAsset`, create a shared program once with
`asset.TryCreateProgram(YourCatalog.TryCreateRuntimeCatalog, out program, out failure)`, and call
`program.TryBootstrap(host, out failure, effectCapacity: capacity)` for each agent. Supply facts and
consume effects through `IProductionTreeWorldParticipant`. Resolve typed input handles once.
On shutdown unregister participants, call `host.Stop()` for each host, then dispose the program.
Stop is permanent for that host; create a fresh host for another instance.

Generated native-only nodes need an explicit reference provider for **Preview**. Use **Debug
Selected** to inspect the actual running native node. Runtime assets are derived snapshots; changing
the source or catalog requires preparing them again. No automatic live migration is implied.
