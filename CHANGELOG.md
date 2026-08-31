# Changelog

All notable changes to this project will be documented here. The project follows Semantic Versioning while public APIs remain explicitly marked as experimental before `1.0.0`.

## [Unreleased]

### Added

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
