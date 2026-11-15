# Development roadmap

The order minimizes architectural rework and delivers verifiable vertical slices. Later phases may be refined, but their prerequisites should not be skipped.

## Phase 0 — foundation

- Repository governance, package layout, scope, architecture, draft schemas, and development rules.
- Unity import and assembly validation.
- Initial CI design and compatibility matrix.
- Unity Web/Burst WASM feasibility spike and Android build-smoke preparation.

Exit: the package imports cleanly and all accepted architectural decisions are documented.

## Phase 1 — semantic vertical slice

- Node status and lifecycle contract.
- Typed authoring model and blackboard schema.
- Sequence, selector, condition, and action contracts.
- Validation, deterministic compilation, immediate executor, and behavior tests.
- Canonical tree import/export.
- Single-thread immediate and deterministic step-budget behavior shared by native and Web execution.

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
- Validate Windows x64, Android ARM64, and supported desktop browsers against the published compatibility matrix.

Exit: all criteria in `scope.md` are satisfied for supported platforms.

## Phase history

This section was moved from `README.md` in P9-006 so that the README can be a user guide. Phase 1
delivered the semantic vertical slice described above.

Phase 2 adds the generated Burst dispatch and fixed-capacity native executor while preserving the Phase 1 semantic oracle. The initialized native paths have focused zero-GC evidence, Windows x64 and Android ARM64 each have an actual non-Development IL2CPP/Burst build, and the single-thread Web backend passes Chrome and Firefox conformance. Device performance, Safari/mobile Web, automatic policy selection, and any performance default or threshold are not claimed.

Phase 3 adds editor tooling on `UnityEditor.Experimental.GraphView`, including graph rendering, layout, semantic editing operations, validation, reference preview and native tracing. The Graph Editor now exposes initial tree editing and draft saving; preview, live debugging, project registry integration and parts of layout organization remain separate or incomplete. The editor is experimental.

Phase 4 measures scheduling and batching on real, non-Editor Players (Windows x64, Android ARM64, single-thread Web) and implements a calibrated work-estimation/batching model plus an `Auto` policy-selection heuristic. `Auto` currently underperforms the best fixed policy (`Immediate`/`Budgeted`/`BatchedJobsSameFrame`/`PipelinedJobs`) in most measured cases — reported honestly, not tuned away — and runtime autotuning was evaluated and rejected in favor of the static calibrated model. No performance default, regression threshold, or supported-hardware-class claim is adopted anywhere in the package.

Phase 5 adds hot reload for the reference-executor backend: a resolved compatibility model (construct-fresh-and-selectively-copy by stable authoring node ID, never in-place mutation), full restart, localized subtree restart, idle-instance compatible state migration, and an explicit, explained Editor workflow. Measured evidence shows compatible migration costs roughly half of a full restart at the same tree size, on both Editor and a real Windows Player. Native-backend hot reload does not exist yet, and migration is scoped to an idle old instance (a genuinely active one falls back to full restart) — both disclosed, not silently assumed.

Phase 6 adds a real, working MCP integration: a standalone `dotnet` MCP server (`MCP~/Server/`) bridged to the Unity Editor (`AIBT.Mcp`), with discovery, authoring, validation/compilation/simulation, test/benchmark, and custom-node-generation tools, a fail-closed permission model, custom MCP tool providers a consuming project can register via inversion of control, and generated agent documentation (node catalog, workflow guide, recipes, anti-patterns). Every tool is live-verified against a real MCP client (the official `@modelcontextprotocol/inspector` CLI), including generating, compiling, testing, and applying a genuinely new custom node end-to-end. Its original trace and project-node-discovery gaps were addressed in later Phase 7 work; see the production-host and editor limitations below for the current integration boundaries.

Phase 7 adds native tracing, project leaf registration, profiling, stress tests, contract migration, native hot reload, samples and release tooling. A production Play-mode host and world integration were implemented after the original integration gate; see [Production tree host](production-host.md) and the Swarm Arena sample. The editor and MCP remain experimental, the Graph Editor creates `v2` trees while preserving existing `v1` trees, and the package has not declared 1.0.

> Status: Phases 1 through 7 are complete (`P2-025`, `P3-013`, `P4-009`, `P5-010`, `P6-012`, and `P7-016` integration gates all accepted — see `Planning~/Evidence/`). `1.0.0` has not been declared; that remains a separate owner decision per `Planning~/USER_ACTIONS.md`.

Phase 8 (P8) made the native path consumable: Editor-prepared `ProductionTreeAsset` runtime assets,
the First Agent sample, consumer CLI and agent tools (see `Planning~/Evidence/P8-*`).

Phase 9 (P9, usability) added the managed agent layer (`BehaviorTreeAgent`, C# `BtAction` /
`BtCondition` nodes, P9-001), 52 standard `aibt.unity.*` nodes and the `aibt.native.standard`
shard (P9-002), the rewritten graph editor with live debugging (P9-003), the `NativeTreeAgent`
facade with automatic preparation (P9-004), short `AIBT.Native` node attributes (P9-005), and the
user guide and clean-consumer acceptance (P9-006). See `CHANGELOG.md` and `Planning~/Evidence/P9-*`.

### Design goals

- Scale from a few agents to large populations without forcing one execution strategy.
- Keep semantic authoring, visual layout, and compiled runtime data separate.
- Make custom nodes safe to generate, inspect, validate, test, and compile.
- Give human and AI authors the same versioned node registry and validation rules.
- Preserve readable graphs through deterministic auto-layout, manual pinning, groups, comments, and reroutes.
- Measure scheduling and batching decisions on real platforms instead of relying on a magic agent threshold.
