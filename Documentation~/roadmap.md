# Development roadmap

The order minimizes architectural rework and delivers verifiable vertical slices. Later phases may be refined, but their prerequisites should not be skipped.

## Phase 0 — foundation

- Repository governance, package layout, scope, architecture, draft schemas, and development rules.
- Unity import and assembly validation.
- Initial CI design and compatibility matrix.

Exit: the package imports cleanly and all accepted architectural decisions are documented.

## Phase 1 — semantic vertical slice

- Node status and lifecycle contract.
- Typed authoring model and blackboard schema.
- Sequence, selector, condition, and action contracts.
- Validation, deterministic compilation, immediate executor, and behavior tests.
- Canonical tree import/export.

Exit: a small tree can be authored as JSON, validated, compiled, executed, and tested without editor UI.

## Phase 2 — data-oriented runtime

- Unmanaged compiled program and per-agent state arenas.
- Burst node contract, generated dispatch, serializers, and analyzers.
- World snapshots and command buffers.
- Batched execution and zero-allocation verification.

Exit: representative trees execute in Burst jobs with documented semantics and no GC allocations after initialization.

## Phase 3 — editor and layout

- Graph technology spike and decision.
- Semantic graph editing, inspector, search, palette, validation, and compilation.
- Shared layout document, deterministic auto-layout, pinning, groups, comments, sticky notes, reroutes, and local view state.
- Runtime debugger, trace visualization, breakpoints, and stepping.

Exit: human-authored graphs remain readable after creation, reload, semantic edits, and collaborative diffs.

## Phase 4 — performance research and scheduler

- Benchmark scenario catalog and platform harness.
- Immediate, same-frame jobs, pipelined, and budgeted policies.
- Work estimates, calibrated defaults, batch selection, diagnostics, and manual overrides.
- Evaluate lightweight runtime adaptation against fixed policies.

Exit: scheduling decisions are backed by published results and regressions are detectable.

## Phase 5 — hot reload

- Stable node identity, program versions, state-layout hashes, and compatibility checks.
- Safe full restart, affected-subtree restart, and compatible active-state migration.
- Editor workflows and tests for structural and parameter changes.

Exit: hot reload behavior is deterministic, explainable, and covered by compatibility tests.

## Phase 6 — AI and MCP

- Node catalog, project manifest, structured diagnostics, domain patches, dry-run, diff, and revision checks.
- MCP discovery, authoring, validation, compilation, simulation, trace, test, benchmark, and code-generation tools.
- Custom MCP tool providers and permission model.
- Generated agent guidance, recipes, and anti-patterns.

Exit: an AI agent can safely create, inspect, test, diagnose, and modify a tree without editing Unity serialization or guessing node contracts.

## Phase 7 — production hardening

- Supported-platform matrix, long-running and stress tests, profiler validation, migration tooling, samples, API documentation, and release automation.
- Review public API and formats for `1.0.0` stability.

Exit: all criteria in `scope.md` are satisfied for supported platforms.
