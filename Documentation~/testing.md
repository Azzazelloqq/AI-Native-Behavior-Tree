# Testing strategy

## Principle

Tests specify observable behavior and contracts. They must not be rewritten merely to preserve an implementation decision.

## Test layers

### Semantic unit tests

Cover node lifecycle, composite ordering, decorators, cancellation, aborts, events, blackboard scopes, async completion, deterministic random behavior, and error contracts.

### Compiler and format tests

Cover schema validation, structured diagnostics, deterministic output, invalid graphs, type checking, migrations, stable identities, runtime-layout generation, and canonical formatting.

### Runtime equivalence tests

The same behavior cases run against Immediate and supported job policies. Scheduling may change timing only where the selected latency contract permits it; statuses, commands, and blackboard effects remain equivalent.

### Allocation and safety tests

Verify zero GC allocations after warmup on the Burst path, native-container lifetime, job safety, cancellation, domain boundaries, and deterministic command ordering.

### Editor tests

Cover undo/redo, copy/paste IDs, semantic/layout separation, auto-layout determinism, pinned nodes, affected-region layout, groups, comments, reroutes, serialization, reload, large-tree interaction, and debugger overlays.

### AI/MCP contract tests

Cover discovery, schemas, pagination, revision conflicts, dry-run, atomic transactions, permissions, structured errors, code-generation gates, and custom tool registration. Tests invoke the protocol contract and do not require a live model.

### Hot-reload tests

Cover parameter edits, insertions, removals, reordering, type-version changes, compatible state migration, incompatible subtree restart, full restart, and trace explanation.

### Performance tests

Use the methodology in `benchmarks.md`. Functional tests never loosen correctness to satisfy a performance target.

## Behavior cases

`.aibtcase.json` scenarios provide initial state, input events, tick sequence, and observable expectations. They are shared by runtime, editor simulation, MCP verification, samples, and regression tests.

## Completion gate

A change is complete only when relevant tests pass, performance-sensitive paths have measured evidence, documentation and schemas match behavior, and no unrelated functionality was added.
