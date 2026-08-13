# Architecture

## Principles

1. Semantic authoring, visual presentation, compilation, and execution are separate concerns.
2. The runtime is data-oriented and does not depend on editor or AI tooling.
3. Performance decisions are measured and explainable.
4. Extensibility is explicit: Burst-compatible and managed execution paths are never confused.
5. Every persisted and public contract is versioned and migratable.

## Layers

```text
Editor UI          MCP server          Direct JSON authoring
   |                   |                       |
   +------------- Authoring API ---------------+
                       |
            Validation and policy engine
                       |
                 Tree compiler
                       |
        Immutable compiled program / registry
                       |
             Execution scheduler and VM
                       |
       snapshots -> node kernels -> commands
```

### Runtime

Owns node statuses and lifecycle, compiled instructions, state arenas, blackboard storage, execution contexts, scheduling policies, trace events, world snapshots, and command buffers. It may depend on Burst and Unity Collections, but not `UnityEditor`, Graph Toolkit, MCP, or DOTS Entities.

### Authoring

Owns the semantic tree document, node manifests, validation, policies, migrations, domain patches, and compilation interfaces. It translates canonical documents into immutable runtime programs.

### Editor

Owns human interaction, visual layout, comments, groups, search, debugging, profiling, and hot-reload orchestration. Presentation data cannot influence behavior semantics.

The graph technology is intentionally not fixed before a spike. Unity Graph Toolkit is available in the host project but experimental; it must be evaluated against serialization control, performance, extensibility, testing, and long-term package risk before becoming a dependency.

### Code generation

Generates stable node registration, parameter serialization, runtime dispatch, node manifests, editor metadata, and analyzers from declared C# node contracts. Generated artifacts must be deterministic and reviewable.

### MCP server

Exposes the authoring and diagnostic APIs through model-neutral MCP resources and tools. It never becomes the source of truth and is not required in player builds.

## Core data ownership

| Data | Mutability | Owner | Persisted form |
| --- | --- | --- | --- |
| Tree semantics | Mutable during authoring | Authoring | `.aibt.json` |
| Shared graph layout | Mutable during authoring | Editor | `.aibt.layout.json` |
| Local view state | Mutable per user | Editor | ignored local state |
| Node registry | Generated/versioned | CodeGen + Authoring | manifest/catalog |
| Compiled program | Immutable | Compiler + Runtime | generated cache/asset |
| Agent state | Mutable per execution | Runtime | memory only |
| World snapshot | Immutable for a scheduled pass | Integration | native buffers |
| Commands | Append-only during a pass | Runtime | native buffers |

## Runtime shape

Compiled programs use contiguous unmanaged storage. Node definitions reference child ranges, parameter ranges, and state-layout descriptors by index. Per-agent state is stored separately so one compiled program can serve many agents.

Execution avoids recursive object graphs, managed dictionaries, reflection, virtual dispatch, and per-tick allocations. Custom Burst nodes are dispatched through generated code. Managed nodes cross an explicit execution boundary.

## Integration boundary

Jobs never call scene objects or arbitrary Unity APIs. Integrations build job-safe snapshots before execution and apply emitted commands afterward. Same-frame and pipelined application are explicit scheduling modes.

## Dependency direction

```text
Runtime <- Authoring <- Editor
   ^          ^          ^
CodeGen ------+          |
MCP ---------------------+
Benchmarks and tests may reference public layers as required.
```

No lower layer may reference a higher layer. Optional integrations live behind adapters.

