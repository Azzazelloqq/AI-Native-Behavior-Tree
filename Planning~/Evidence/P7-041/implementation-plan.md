# P7-041 implementation plan

Status: approved 2026-09-07; implementation starts in this session.

## Invariants

- `ProductionTreeHost` remains the sole owner of one native tree, trace channel, lifecycle and
  disposal.
- `ProductionTreeScheduler` remains the sole owner of registration, budget, due order and group
  execution. The bridge only calls its split phases.
- Public consumers receive typed committed effects only; no `NativeArray`, byte offset, lease,
  command record or generated case offset escapes Runtime.
- P7-039 remains the only external input mechanism and is used only while a host is idle.

## 1. Generated effect descriptors

1. Add an additive public descriptor value and lookup member to the generated catalog executor
   contract. It identifies a declared `EffectCommand` by stable binding ID and its supported
   `Int32`, `Float32` or `Float3` value kind; it deliberately has no raw payload member.
2. Extend source generation to emit the lookup from the same declared catalog binding metadata used
   for dispatch. It rejects unknown/non-effect ordinals and never discovers assemblies at runtime.
3. Add CodeGen contract tests for stable descriptor identity, type identity and non-effect rejection.

## 2. Host-owned typed effect queue

1. Define `ProductionTreeEffect`, a closed built-in value reader, bounded effect capacity and
   `IProductionTreeEffectReceiver` in `Runtime/Integration/`.
2. At each successful generated dispatch commit, resolve the command's generated effect descriptor,
   canonical-decode its supported value and append it in command order to the host queue. In a
   group, attribute commands internally from the existing remapped target-ordinal range before any
   participant commits. Do not surface async Start/Cancel commands in this version.
3. Expose a public drain-to-receiver operation only while the host is idle. Queue overflow produces
   a structured delivery failure and is covered explicitly; no event is discarded silently.
4. Prove immediate and generated-group commits produce equal public effects; rejected callbacks,
   malformed group attribution and invalid descriptors produce none.

## 3. Scheduler frame split and world bridge

1. Add internal world-bridge callbacks only at the existing scheduler frame boundaries, preserving
   exactly one frame progression. `Update` remains unchanged when the scheduler has no world bridge.
2. `ProductionTreeWorld` samples the latest Unity facts each frame. `BeginFrame` completes/drains
   prior pipelined work and makes committed effects available; between phases it flushes the staged
   facts only to idle hosts through P7-039, then invokes `DriveFrame`. A continuing pipelined wave
   keeps its earlier consistent snapshot; newest facts wait for its next idle barrier.
3. Add `ProductionTreeWorld` as the optional component that binds one scheduler, invokes both
   delivery points on main thread and maintains registration. It has no per-agent native state and
   no extra per-agent `Update` loop.
4. Test immediate and pipelined order, newest-staged-input delivery at the next pipelined barrier,
   host destroy/unregister during either phase, direct scheduler compatibility and no reentrant
   input write.

## 4. Documentation and proof

1. Regenerate runtime API documentation and document the world/effect contract in the production
   host guide.
2. Run focused behavior tests, full EditMode regression, static verification, API-diff and allocation
   measurements.
3. Prove in Unity Play mode: a live generated host receives an input, emits an effect through the
   world, the receiver applies it, and the existing debugger attaches; repeat with a pipelined
   profile. Exit Play mode with no native-leak diagnostics.

## Explicit non-goals

- No generic compiled-tree asset or runtime compiler/import pipeline.
- No registered effect value, async Start/Cancel delivery, scheduler tuning or batch-input rewrite.
- No Swarm Arena gameplay assets/nodes in this card.
