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
  Android ARM64 IL2CPP/Burst AOT evidence, and Chrome/Firefox Web conformance.
