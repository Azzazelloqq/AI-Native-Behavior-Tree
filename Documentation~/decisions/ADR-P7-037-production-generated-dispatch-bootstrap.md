# ADR P7-037: Production generated-catalog bootstrap and lifecycle dispatch

- Status: Proposed 2026-09-05
- Date: 2026-09-05
- Decision ID: AIBT-038

## Context

The generated catalog facade exposes static `ExecuteImmediate` and `Schedule` methods. This proves
the Burst ABI, but a production host/coordinator cannot retain an arbitrary project catalog behind
one runtime type without reflection or a generated adapter. `ProductionTreeHost.DispatchRequest`
also exposes only callback identity; it does not expose the host-owned configuration, node memory,
blackboard, transaction or command storage required by generated dispatch.

The existing `GenericNativeDispatchTranslatorV1` solves a different boundary. It materializes one
generated shard for the authoring/MCP test harness. It uses reflection to locate the generated
facade and is intentionally not a Player production path.

The P7-037 disposable proof compiles a normal v2 tree and derives every case, binding and byte offset
from generated metadata and compiler output. Immediate and scheduled calls can then drive the same
lifecycle machine. The proof also shows that production needs a shared catalog object, a batch
commit boundary and an explicit blackboard source; wrapping the current host delegate is
insufficient.

## Decision

### Generated runtime catalog

Add one small public runtime interface implemented by source-generated code:

```csharp
public interface IGeneratedBurstCatalogExecutorV2
{
    BurstExecutionResult ExecuteImmediate(ref BurstExecutionBatch batch);
    JobHandle Schedule(ref BurstExecutionBatch batch, JobHandle dependency);
}
```

Each generated `[AibtCatalogSet]` facade exposes one cached executor and one factory:

```csharp
public static IGeneratedBurstCatalogExecutorV2 Executor { get; }

public static bool TryCreateRuntimeCatalog(
    Allocator allocator,
    out GeneratedBurstCatalogV2 catalog,
    out BurstContextResult failure);
```

`TryCreateRuntimeCatalog` passes the executor, exact generated handshake and a generated canonical
binary layout blob to Runtime. Runtime validates and decodes the blob into immutable native catalog
metadata. The blob uses one versioned little-endian format owned by the generator and Runtime; users
do not construct case offsets. A catalog may be shared by many tree instances and is disposed only
by its explicit owner.

This is explicit generated wiring, not catalog discovery. It preserves IL2CPP/AOT reachability and
requires no reflection, service locator or hidden global registry.

### Compiled tree runtime definition

`GeneratedCompiledProgramV2` gains one factory that freezes its existing semantic program,
blackboard binding and generated config/default blobs into a Runtime-owned
`GeneratedTreeRuntimeDefinitionV2`. The definition is immutable and shareable across instances. It
records the expected node-registry/catalog identity; bootstrap rejects a missing node case, version
mismatch, registry mismatch or layout-handshake mismatch before allocating an instance.

The existing `TryBootstrap(CompiledProgram, DispatchLifecycle, ...)` overload remains unchanged for
custom adapters. A new generated path takes the runtime definition and generated catalog explicitly;
the caller does not select per-node callbacks.

### Ownership and dispatch waves

The lifecycle machine remains authoritative for activation, callback phase, terminal state and
dispatch tokens. Per-instance config, node memory and blackboard state remain instance-owned.

For each execution wave, the driver:

1. advances eligible lifecycle machines until each reaches a dispatch barrier, Waiting or Completed;
2. groups pending requests by the exact `GeneratedBurstCatalogV2` identity;
3. snapshots the required config/memory/blackboard/completion inputs into one contiguous group batch;
4. calls the catalog executor once for Immediate or schedules one Job for the whole group;
5. validates the completed batch and atomically commits node memory, blackboard reductions and
   commands for accepted requests;
6. acknowledges each matching lifecycle dispatch token, then advances to the next wave.

A rejected/faulted batch commits no partial node memory and does not acknowledge a dispatch token as
successful. Cancellation and disposal complete or reject outstanding work through the P7-032
ownership rules before releasing instance/catalog storage.

Standalone host execution uses the same request/commit path with a one-instance group. It executes
Immediate on the main thread; it does not schedule one Job per callback. P7-033 owns population
same-frame/pipelined scheduling and chooses when a group Job is worthwhile.

### Blackboard and commands

The generated path reuses the existing v2 compiled blackboard binding, scope owners, transaction
ledger and deterministic command/reduction rules. Bootstrap initializes defaults from the compiled
definition. Runtime input updates target existing typed slot handles; the dispatcher never receives
caller-authored live-value byte offsets.

The first production promotion must prove a generated node that reads and writes a blackboard value
and emits a command. Immediate and scheduled execution must publish identical accepted results.

## Public API scope

Add only:

- `IGeneratedBurstCatalogExecutorV2`;
- `GeneratedBurstCatalogV2` with explicit ownership/disposal and identity diagnostics;
- `GeneratedTreeRuntimeDefinitionV2`;
- generated `Executor` and `TryCreateRuntimeCatalog` members;
- one generated bootstrap overload/factory on the production integration surface.

Exact result/failure carrier names may follow existing runtime conventions during implementation,
but they may not change the ownership, reflection, grouping or atomic-commit semantics above.

## Alternatives rejected

- **Reflection over generated static facades:** unavailable as a reliable IL2CPP/Player contract and
  already restricted to the MCP test harness.
- **A global generated-catalog registry:** hides ownership, complicates domain/scene teardown and
  conflicts with explicit AI-world coordination.
- **Caller-authored case/field offsets:** duplicates generated ABI authority and can silently corrupt
  memory.
- **One Job per tree/node callback:** loses the batching model P7-033 exists to provide.
- **Moving compilation/materialization into Runtime:** reverses the current Authoring dependency and
  would add JSON/reflection concerns to the Player hot path.
- **Keeping blackboard values in a separate adapter-owned copy:** creates two mutable authorities and
  breaks deterministic reduction/publication.

## Consequences

- A project explicitly creates one runtime catalog per generated catalog set and may share it across
  hosts/coordinators.
- Generated and compiled identities fail fast before execution.
- The production bridge requires a real batch result/commit view; per-byte test inspection methods
  are not promoted as public API.
- P7-033 can group different project catalogs without reflection and without knowing concrete
  generated types.

## Approval required

This ADR is **Proposed**. Owner approval is required because it adds the public generated-catalog
executor/bootstrap contract and fixes which object owns catalog metadata and batch commit.
