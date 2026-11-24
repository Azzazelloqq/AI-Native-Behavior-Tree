# Known limitations

Limits of the current release that are known and not yet fixed. Each entry says where to look.
Limits that a guide already explains are linked, not repeated.

## Subtrees

- **No size budget for expansion.** A subtree used by many references, or nested references,
  is copied once per reference, and nothing caps the size of the expanded tree. A subtree's errors
  are reported again for every reference to it. See `Authoring/Compilation/Subtrees/SubtreeExpander.cs`.
- **Errors after expansion name expanded IDs.** A diagnostic found in the expanded tree points
  to the expanded node ID (`r:n`), not to the node in the subtree file. See
  [Subtrees, Diagnostics](subtrees.md#diagnostics).

## Native nodes and hosts

- **Very large distances overflow.** `aibt.native.float3-distance` and
  `aibt.native.float3-within-distance` square the coordinate differences in Float32, so points
  more than about 1.8e19 apart overflow: `float3-distance` writes infinity and
  `float3-within-distance` fails for every limit below about 1.8e19 (`StandardNativeMath.SquaredDistance` in
  `Authoring/Registry/Generated/StandardNative/StandardNativeConditions.cs`).
- **Shared bindings are refused at run time.** A native node with a Shared-scope binding compiles,
  but the host refuses it when it starts (`TryResolveTreeSlot` in
  `Runtime/Integration/GeneratedDispatch/GeneratedTreeDispatchAdapterV2.cs`). Native nodes bind Tree
  and Agent keys.
- **Library nodes bind Tree keys only.** See
  [Native node library, A cooldown shared by several branches](native-node-library.md#a-cooldown-shared-by-several-branches).
- **Observers run only in the reference executor.** Native hosts ignore `observer` blocks
  (warning `AIBT2054`). See [Native node library, Guarding a branch](native-node-library.md#guarding-a-branch).
- **A node may touch each binding once per callback.** See
  [Burst node authoring, Read a binding once per callback](burst-node-authoring.md#read-a-binding-once-per-callback).

## Scheduling

- **`PipelinedJobs` refuses reads while a job is in flight.** `CanReadTreeValues` is false and
  `TryReadExternalTreeValue` / `NativeTreeAgent.TryGetValue` fail while the host executes between
  frames. See [Production tree host](production-host.md).
- **`PipelinedJobs` updates a group less often than once per frame.** A group makes one logical
  update per (lifecycle steps of its slowest tree + 1) frames: 3 frames for a tree waiting in one
  leaf, about 32 for the Swarm Arena tree (`NativeBatchedLifecycleOwnerV1.cs`, `AdvanceJob`;
  `ProductionTreeScheduler.cs`, `AdvancePipelinedRuns`). What that means for cost and when to choose
  it: [Performance, `PipelinedJobs` cadence](performance.md#pipelinedjobs-cadence).

## MCP and tools

- **`get_node_contract` shows no blackboard access for generated nodes.** The answer carries the
  manifest and the behaviour-case contract, not the node's bindings (`GetNodeContract` in
  `MCP/McpToolDispatcher.cs`).
- **Lookup by tree ID sees folders Unity does not import.** MCP's tree lookup and the project
  manifest scan every folder, including `~` folders (`TreeSourcePaths.FindFiles` in
  `Authoring/Model/Tree/TreeSourcePaths.cs`), so a copy of a tree in such a folder can make its
  tree ID ambiguous.
