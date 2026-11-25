# Burst node authoring

Configuration scalars support `Bool`, `UInt32`, `UInt64` and finite `Float32`, plus generated
handles. Use `[AibtConfigField("radius", "Float32", 1u)] public float Radius;` for an ordinary
fractional parameter. The **First Agent** sample demonstrates Float32 parameters and an
Editor-prepared runtime tree. Existing nodes changing a parameter's type require a new node
version; old millimetre-based sample contracts are preserved.

Public Burst nodes are declared in a shard assembly and consumed by a separate
catalog assembly. Both assemblies reference `AIBT.Runtime` and attach the
packaged `AIBT.CodeGen` analyzer.

The **Public Burst Nodes** package sample demonstrates the complete supported
authoring path:

- a Condition with a typed blackboard read and observer evaluation;
- an Action with typed read/write, command emission, asynchronous start and
  completion, `Running`, and cancellation from `Abort`;
- a separate generated catalog that selects the public shard.

## Short declarations

`using AIBT.Native;` gives short attributes that the generator expands into exactly the long
`AIBT.Burst` attributes, so a node written either way has the same manifest, metadata, catalog
fingerprint and generated dispatch:

| Short | Long equivalent |
|---|---|
| `[AibtAction("id")]` | `[AibtBurstNode("id", 1u, BurstNodeKind.Action, typeof(TConfig), typeof(TMemory), NodeMemoryLifetime.Activation, true, BurstCancellationMode.AbortOnly, BurstNodeCost.Low, Success \| Failure \| Running)]` |
| `[AibtCondition("id")]` | `[AibtBurstNode("id", 1u, BurstNodeKind.Condition, typeof(TConfig), typeof(TMemory), NodeMemoryLifetime.Activation, true, BurstCancellationMode.NotApplicable, BurstNodeCost.Trivial, Success \| Failure)]` |
| `[AibtParam("speed")] public float Speed;` | `[AibtConfigField("speed", "Float32", 1u)]` (bool → `Bool`, uint → `UInt32`, ulong → `UInt64`, float → `Float32`) |
| `[AibtMemory("count")] public uint Count;` | `[AibtMemoryField("count", "UInt32", 1u)]` (type and version from the field type) |
| `[AibtKey("target")] public BlackboardReadHandle<float> Target;` | `[AibtConfigField("target", "GeneratedHandle", 1u), AibtBlackboardBinding("target", BurstBlackboardAccess.Read, BlackboardScope.Tree, "Float32", 1u)]` (access from the handle type; `Scope =` to change) |
| `[AibtCommand("move")] public CommandHandle<float> Move;` | `[AibtConfigField("move", "GeneratedHandle", 1u), AibtCommandBinding("move", "Float32", 1u)]` |

`TConfig` and `TMemory` are the first two parameter types of the node's `Tick`. Every default can
be overridden by a named argument (`Version`, `MemoryLifetime`, `Deterministic`, `Cancellation`,
`Cost`, `Statuses`). Mistakes are reported with the usual AIBT5xxx diagnostics at the short
attribute. `[AibtNodeDocumentation]`, `[AibtCatalogShard]`, snapshot, async and completion
bindings have no short form. The short attributes are not part of the ABI v2 contract
(`AIBT.Burst`); see `ADR-P9-005`.

## Callbacks

`Tick` is required. `Enter`, `Abort` and `Exit` are optional: omit the ones that would be empty
and the generated dispatch skips them. A declared callback must keep its exact signature
(AIBT5003 otherwise). Omitting callbacks does not change the node's manifest or catalog
fingerprint. The First Agent `MoveNode` declares only `Tick`.

Handle fields use `GeneratedHandle` configuration fields paired with one binding
attribute. Call only the generated `TShard.BurstAccess` methods from lifecycle
callbacks. Check every `BurstContextResult`; an ignored failure is latched and
prevents publication. Callback code must stay unmanaged and Burst-compatible.

Import the sample from Unity Package Manager, then use its shard/catalog as the
minimal reference. Runtime ownership, native backing, and dispatch internals are
intentionally not part of the public authoring surface.

## Read a binding once per callback

Read each blackboard binding at most once per callback: read the value into a local and use the
local. The same holds for writes (write once, at the end).

Every `TryRead`/`TryWrite` call takes one value session and staging bytes for its value. The
generated catalog sizes the room of one callback for the largest need of any one of its nodes: one
session per `Read` or `Write` binding (and per input or command), two per `ReadWrite` binding (a read
and a write) and per external action, with their bytes counted the same way.

A second read of one binding in one callback therefore succeeds only while both the sessions and
the staging bytes still fit. Whether they fit depends on the rest of the catalog (a larger node
elsewhere leaves room) and on what the node did earlier in the callback (a binding it does not use
leaves room). A host that dispatches members together sizes the room for the whole group, so on such
a path the outcome may also depend on the other members (not traced or tested). Do not rely on it.

When it does not fit, the call returns `BurstContextResult.CapacityExceeded`, and because the
rejection is latched the host stops with `NativeLifetimeStateInvalid`. The Console names the node:
`Callback rejected: CapacityExceeded at node '<id>'.` The callback's own refusal does not record
which capacity was too small, so the message lists the possible limits: the value sessions and
their staging bytes described here, and the node's command, command payload or operation capacity
(the effects, simulation commands or action changes it publishes in one callback).

`CapacityExceeded` has other causes, and where the host knows the cause the message names it:

- the host's effect queue (or a simulation container's command quota) had too few free entries
  for the effects the node may publish in one callback, so the node was not called;
- the node ran, but the effects it published did not fit the effect queue (or the container did
  not accept its commands or action changes);
- under `BatchedJobsSameFrame` or `PipelinedJobs`, a batched group of nodes of one catalog was
  refused; every host in the group stops, and the message lists the possible limits.

A catalog that also holds a node with more bindings has spare sessions, so the second read
succeeds there and the same node seems to work. Moving the node into a smaller catalog, or
removing the larger node, makes the fault appear. Do not rely on the spare sessions.
`Tests/Integration/NativeRuntime/MixedScopeNativeTreeTests.SessionLimit.cs` shows both cases.
