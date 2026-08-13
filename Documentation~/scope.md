# Product scope

## Product statement

AIBT is a Unity behavior-tree library with a data-oriented Burst execution path, extensible C# nodes, a readable visual authoring and debugging environment, an adaptive execution scheduler, reproducible platform benchmarks, and model-agnostic AI tooling through MCP and machine-readable contracts.

## In scope

### Runtime semantics

- Sequence, selector, parallel and explicitly defined custom composites.
- Conditions, actions, decorators, services, subtrees, and typed parameters.
- `Success`, `Failure`, and `Running` statuses with explicit enter, tick, cancel, and exit behavior.
- Conditional aborts, event-driven reevaluation, configurable update frequency, timeouts, cooldowns, repeaters, and deterministic random selection.
- Typed blackboards with local, tree, agent, and explicitly shared scopes.
- Synchronous, asynchronous, Burst-compatible, and explicit managed/main-thread nodes.
- Immutable compiled tree programs and isolated per-agent execution state.
- World snapshots for job-safe reads and command buffers for side effects.

### Execution

- Immediate, batched job, same-frame completion, pipelined, budgeted, and automatic policies.
- Scheduling based on estimated work rather than a single agent-count threshold.
- Manual overrides and diagnostics explaining automatic decisions.
- Zero GC allocations per tick after initialization on the Burst execution path.
- Deterministic semantics independent of scheduling policy, except for explicitly documented timing and latency.

### Extensibility

- Custom Burst node structs with generated registration, serialization, dispatch, editor metadata, and AI schema.
- Explicit managed-node fallback for APIs that cannot run in jobs.
- Project-defined validators, policies, adapters, commands, and high-level MCP tools.
- Versioned node contracts and data migrations.

### Authoring and debugging

- Visual graph authoring, semantic text format, validation, compilation, simulation, live debugging, trace history, breakpoints, and step execution.
- Deterministic auto-layout, manual layout, pinned nodes, groups, comments, sticky notes, reroutes, minimap, search, breadcrumbs, and subtree navigation.
- Separate semantic and presentation files so layout-only changes never alter runtime behavior.
- Hot reload with safe restart first and compatible state migration later.

### AI-native tooling

- Strict schemas, introspectable node manifests, structured diagnostics, domain patches, dry-run, diffs, transactions, and revision checks.
- MCP tools for discovery, authoring, validation, compilation, simulation, testing, trace explanation, benchmark execution, and node generation.
- Model- and vendor-neutral core contracts.
- Generated integration guidance for agent ecosystems without making any provider a runtime dependency.

### Quality and research

- Behavior-focused unit, integration, property, migration, editor, and end-to-end tests.
- Performance baselines and regression thresholds.
- Platform-specific benchmark profiles and documented compatibility claims.
- Samples, recipes, anti-patterns, node catalog, and migration guides.
- Mandatory pre-1.0 validation for Windows x64, Android ARM64, and supported desktop browsers through the single-thread Unity Web backend.

## Not in scope

- A required DOTS Entities dependency.
- A custom operating-system thread pool or replacement for Unity Job System.
- Game-specific movement, combat, animation, navigation, perception, or networking implementations.
- Network replication of tree state.
- Cloud model hosting or a built-in LLM provider.
- Silent application of generated code without validation and explicit approval.
- Burst compatibility for arbitrary managed C#.
- Persistent save/load of live execution state. Hot reload is supported independently.
- Claims of optimal scheduling on unmeasured hardware or platforms.
- C# worker-thread parallelism on Unity Web. Web remains a supported functional single-thread backend.

## Release criteria for 1.0

- Stable runtime, node, tree, layout, policy, test-case, and trace contracts.
- Production-ready editor and debugger.
- Verified zero-allocation Burst path and published benchmark methodology.
- Automatic scheduler validated against fixed policies on supported platforms.
- Hot reload with documented compatibility behavior.
- MCP server and custom tool extension contract.
- Complete tests, samples, API documentation, migration tooling, and supported-platform matrix.
