# Burst node authoring

Public Burst nodes are declared in a shard assembly and consumed by a separate
catalog assembly. Both assemblies reference `AIBT.Runtime` and attach the
packaged `AIBT.CodeGen` analyzer.

The **Public Burst Nodes** package sample demonstrates the complete supported
authoring path:

- a Condition with a typed blackboard read and observer evaluation;
- an Action with typed read/write, command emission, asynchronous start and
  completion, `Running`, and cancellation from `Abort`;
- a separate generated catalog that selects the public shard.

Handle fields use `GeneratedHandle` configuration fields paired with one binding
attribute. Call only the generated `TShard.BurstAccess` methods from lifecycle
callbacks. Check every `BurstContextResult`; an ignored failure is latched and
prevents publication. Callback code must stay unmanaged and Burst-compatible.

Import the sample from Unity Package Manager, then use its shard/catalog as the
minimal reference. Runtime ownership, native backing, and dispatch internals are
intentionally not part of the public authoring surface.
