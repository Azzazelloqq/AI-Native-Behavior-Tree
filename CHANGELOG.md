# Changelog

All notable changes to this project will be documented here. The project follows Semantic Versioning while public APIs remain explicitly marked as experimental before `1.0.0`.

## [Unreleased]

### Added

- `AIBT Graph` now supports pan, zoom, node dragging, click and box selection; it reads existing
  layout positions and shows readable titles while keeping semantic editing disabled.

- Reproducible Windows IL2CPP tree-count size comparison: 1/100 authored trees, actual Player
  payload verification and file inventories. The measured +36,028-byte delta is serialized data
  and resource metadata; code is byte-identical. See `Planning~/Evidence/P7-026/` for scope limits.

- MCP node compilation now uses an explicit, domain-reload-persistent `attemptId` and an Editor
  compilation request. Legacy log-offset checks require a new start/check sequence. Apply validates
  Assets containment and rejects link/reparse ancestry. Background TCP test dispatch uses owned
  native storage; successful apply clears its staging-only catalog so subsequent compilation works.
- SameFrame and Pipelined controllers retain scheduled ownership after rejected completion buffers,
  allowing a valid retry without replay. Completed lane failures preserve their cleanup path.

- Document migration preserves blackboard, description, revision, scope contracts and bindings.
  Native hot reload preserves compatible cooldown state and cancels the previous active child
  path before traversing reordered children. See `Planning~/Evidence/P7-029/`.
- Full-lifecycle `ProductionTreeHost` dispatch with callback reasons and injected clock, retained
  failure diagnostics, and a per-frame step budget. The original Tick-only overload remains supported.
- Production-host regression coverage for terminal stopping, clock/decorator boundaries, budget
  resume identity, cancellation/disposal and callback failure. See `Documentation~/production-host.md`.

- Production host no longer starts updates after a terminal root or supplies a permanently zero
  clock. Budgeted frames preserve logical-update inputs; destruction cancels active work before
  releasing native storage. Trace records retain actual leaf exit/abort reasons and budget events.

- Initial repository and Unity package structure.
- Architecture, scope, editor, AI/MCP, benchmark, testing, and roadmap documentation.
- Strict canonical JSON contracts and schemas for semantic trees, node manifests, policies, and behavior cases.
- Normative v1 contracts for execution, update phases, blackboards, async commands, determinism, compiled programs, and platform backends.
- Agent execution workflow, dependency-aware work packages, Definition of Done, and atomic Phase 0–1 work items.
- Immutable authoring and compiled-program models, structured diagnostics, node registry, semantic validation, and deterministic reference compiler.
- Explicit-stack reference executor with lifecycle safety, memory/reactive/parallel composites, decorators, blackboard observers, commands, async completions, and deterministic step budgeting.
- Backend-neutral behavior-case runner and an end-to-end golden semantic slice covering canonical parse, validate, compile, and execute.
- Repeatable static, Unity compile, EditMode, Android ARM64 IL2CPP, and Unity Web verification entrypoints with sanitized evidence.
- Generated ABI v2 Burst node dispatch, public analyzers, deterministic catalog
  prebinding, and a public Condition/Action package sample.
- Fixed-capacity native program/state owners, Tree/Agent/Shared blackboards,
  snapshots, commands/completions, diagnostics/trace, lifecycle/composites,
  observers, async actions, budgeting, and same-frame batched Jobs scheduling.
- Native/reference golden equivalence, initialized zero-GC/lifetime gates,
  non-Development Windows x64 IL2CPP/Burst Player evidence, Android ARM64
  IL2CPP/Burst AOT evidence, and Chrome/Firefox Web conformance.
- `UnityEditor.Experimental.GraphView`-based editor components: read-only graph
  adapter, deterministic auto-layout, manual organization and layout
  persistence (groups/notes/reroutes), gated semantic editing, validation UX,
  reference-oracle-backed preview, read-only native execution debugger
  attachment, and a trace timeline view; proven layout/semantic isolation and
  large-graph (up to 2000 node) interaction performance measurements. Not yet
  wired into one live editor window.
- Fixed-policy scheduling cost curves, a calibrated work-estimation/batching
  model (recalibrated against real Windows x64 and Android ARM64 Player data,
  not Editor batchmode), a `PipelinedJobs` executor, and an `Auto`
  policy-selection heuristic with full explainability; `Auto` measured against
  fixed policies with underperformance disclosed rather than tuned away.
  Runtime autotuning evaluated and rejected (`OQ-006`) in favor of the static
  calibrated model. Real, non-Development Player benchmark evidence on Windows
  x64, Android ARM64 (physical device), and single-thread Web. No performance
  default, regression threshold, or supported-hardware-class claim adopted.
- Hot reload for the reference-executor backend (`OQ-007` resolved): a
  compatibility classifier localizing structural changes to the smallest
  necessary restart region, safe full restart, and idle-instance compatible
  state migration (memory/generation/cooldown flags/blackboard) built as one
  construct-fresh-and-selectively-copy mechanism keyed by stable authoring
  node ID, never by compiled index or in-place mutation. An explicit,
  explained Editor hot-reload workflow shows the actual classification and
  strategy used for every reload, verified live in the Editor. Measured
  evidence (Editor batchmode and a real, non-development Windows x64
  Standalone Player) shows compatible migration costs roughly half of a full
  restart at the same tree size. Native-backend hot reload does not exist;
  migration runs only against an idle old instance, falling back to full
  restart for a genuinely active one — both disclosed. No regression
  threshold or "acceptable reload cost" claim adopted.
- A real, working MCP integration: a standalone `dotnet` MCP server bridged to
  the Unity Editor, discovery/authoring/verification/test/benchmark/
  node-development tools, a fail-closed permission model covering 8
  categories, custom MCP tool providers discovered via inversion of control
  with no AIBT-side reference to the consuming project, and generated agent
  documentation (node catalog, workflow guide, recipes, anti-patterns,
  versioned migrations stub) sourced from the same production data every
  tool uses. Every tool live-verified against a real MCP client, including a
  genuinely new custom node generated, compiled, tested, and applied
  end-to-end. Trace inspection does not exist (no production code wires a
  real running native tree into a trace channel yet), and a just-generated
  custom node is not yet discoverable through the same discovery tools —
  both disclosed, not silently assumed.
- Production hardening: real Burst-confirmed Profiler instrumentation on
  every native/reference hot path; a long-running/stress test suite (20,000
  -tick soak, 10,240-agent stress, repeated reload under load); a real
  node-contract migration mechanism (declarative rename/add-with-default
  rules, an MCP tool, and a non-blocking Editor notification window); real
  production trace wiring (closing the trace-inspection gap above) and
  per-project leaf-registration/discovery (closing the node-discoverability
  gap above); a generic native-dispatch translator; native-backend hot
  reload, including active-instance migration (not idle-only, unlike the
  reference executor); two new samples; a generated API reference covering
  100% of public member signatures across all 4 assemblies; and local-first
  release automation. The Phase 7 integration gate independently
  re-verified all of this against a clean detached snapshot and accepted it,
  with real gaps disclosed rather than smoothed over: a production
  Play-mode host remains a fully-decided but unbuilt design; the tree
  format's `v2` (Agent/Shared blackboard) is not yet the production
  default; `AIBT.Editor`/`AIBT.Mcp` stay explicitly experimental (a real,
  previously-undocumented breaking change was found in `AIBT.Mcp`'s own
  tool-surface history); and the generated API reference's type-summary
  inlining silently no-ops for a real UPM (`file:`/registry) consumer,
  found for the first time by the gate's own detached-harness technique.
  `1.0.0` has not been declared.
