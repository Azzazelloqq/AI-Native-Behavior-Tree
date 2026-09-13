# ADR P7-041: Consumer-facing production world integration

- Status: Accepted 2026-09-07
- Date: 2026-09-07
- Decision ID: AIBT-041

## Context

The native production path is deliberately layered: a generated catalog executes node kernels,
`ProductionTreeHost` owns one tree instance, and `ProductionTreeScheduler` owns the population
budget and grouped Jobs dispatch. That core is correct, but it is not yet a complete Unity consumer
boundary. In particular, P7-039 supplies live Tree-scope inputs but there is no public, typed
delivery of committed effect commands to game code, and no component owns the required frame order
of input sampling, scheduler work, and effect application.

The intended product model is a behavior tree as a Unity-facing black box. A game author configures
trees, profiles, world facts and effect receivers; they do not access native buffers, generated
offsets, Jobs or DOTS APIs. MCP remains an authoring and diagnostics surface, never a Player runtime
dependency.

This does not invalidate prior decisions. ADR-P7-010 retains one host as the owner of one tree
instance; ADR-P7-033 retains scheduler ownership of the global budget and deterministic admission;
ADR-P7-037 retains generated-catalog identity, atomic commit and the rejection of public raw
per-byte inspection; ADR-P7-039 retains external Tree-scope write eligibility and safety rules.

## Decision

### One frame-phased world bridge

Add one optional `ProductionTreeWorld` MonoBehaviour per independently scheduled AI world. It owns
the *ordering* of an already configured `ProductionTreeScheduler`, not tree state or native
resources:

```text
sample the latest Unity facts -> complete prior pipelined work -> deliver committed effects
flush staged facts to idle hosts -> admit and execute this scheduler frame -> deliver committed effects
```

`ProductionTreeScheduler` invokes internal world-bridge callbacks at these split boundaries; its
existing autonomous `Update` remains the backwards-compatible path when no world bridge is bound.
The split keeps a pipelined host's `_driving` lock intact until its outstanding logical update
reaches a barrier. The bridge stages the latest facts every frame, but flushes them only to an idle
host. Consequently a PipelinedJobs wave consumes one consistent input snapshot; a fact sampled
while that wave is in flight becomes visible at its next idle barrier. This is deliberate bounded
latency, not a weakened P7-039 write rule. The split does not change due order, budget accounting,
profile semantics, policy selection or Job grouping.

There is no second per-agent `Update` loop. `ProductionTreeHost` remains the sole per-instance
owner. A sample/game may attach a lightweight binding component beside a host, but it registers with
the world and executes only in the world-controlled phases.

### Typed effects, never native-buffer inspection

After a generated callback transaction commits successfully, Runtime may copy its declared
fire-and-forget `EffectCommand` values into a fixed-capacity, host-owned typed effect queue. The
public consumer sees `ProductionTreeEffect` values and `IProductionTreeEffectReceiver`, not command
records, payload bytes, workspace leases or generated offsets.

Effect identity is a stable generated binding ID; generated catalog metadata supplies its value
kind. This first bridge intentionally supports only `Int32`, `Float32`, and `Float3` payloads --
the values required by the sample and common presentation effects. Registered or other structured
payload types and async Start/Cancel operations are not part of this first bridge. A queue overflow
is a structured, observable delivery failure -- effects are never silently dropped. Delivery occurs
on the main thread only after the native transaction has committed, in deterministic production
command order.

The generated catalog gains an additive descriptor lookup for declared `EffectCommand` bindings.
It is explicit source-generated wiring, never reflection or a global registry. This is deliberately
different from promoting P7-037's internal raw command inspection helpers to public API.

Grouped dispatch retains the group workspace's existing disjoint target-ordinal ranges and uses
them only internally to attribute each committed effect command to its participating host before
that host completes the pending callback. An unknown or malformed attribution fails the whole wave
closed before any host commit. The public contract remains the same typed receiver; grouping does
not create a per-agent Job or a managed callback from Burst.

### Inputs and performance

The bridge consumes P7-039's resolve-once/write-many Tree-scope handles. It does not change their
Tree-only, generated-catalog-only, built-in-type-only or no-node-writer eligibility rules.

The bridge adds no managed allocation after warm-up and no Job-time managed callback. It introduces
no extra native tree state, copy of blackboard state or per-tree Job. P7-039's present one-value
`Allocator.Temp` write implementation is measured under the Swarm workload before claiming it is
cheap at population; a reusable batch-input optimization is explicitly deferred unless that
measurement exposes a material bottleneck.

### Scope boundary

This decision does **not** add a generic persisted compiled-tree asset, runtime JSON compiler or
catalog discovery service. The Swarm Arena sample may own an explicit sample bootstrapper that
creates its known generated catalog and definition. A general authored-tree packaging/onboarding
surface needs its own evidence-backed card rather than becoming hidden sample infrastructure.

## Consequences

- P7-034 may demonstrate real movement/attack effects through public APIs, rather than duplicate
  tree decisions in presentation code or access Runtime internals.
- P7-035 measures the same public bridge, including the current P7-039 input cost and real
  pipelined result delivery.
- Games may continue to use `ProductionTreeHost`/`ProductionTreeScheduler` directly where they
  already own an advanced integration; no existing call path changes behavior.

## Alternatives rejected

- **Expose raw command buffers:** conflicts with ADR-P7-037's explicit public-API boundary and
  leaks native lifetime/encoding details to ordinary Unity code.
- **Let node kernels call Unity APIs:** violates the job-safe integration boundary and loses grouped
  execution portability.
- **Add a second agent runner owning a duplicate tree:** contradicts host ownership and duplicates
  native state.
- **Use Unity script execution order as the contract:** cannot make input safety or pipelined
  delivery deterministic for an importing consumer.
- **Add a generic asset pipeline opportunistically:** is a separate persistence/import contract,
  not required to make the first public gameplay bridge honest.

## Approval

The owner approved this direction on 2026-09-07 after a fresh audit of the production host,
scheduler, generated dispatch and P7-039 input path. Production implementation remains subject to
the task plan and verification gates below.
