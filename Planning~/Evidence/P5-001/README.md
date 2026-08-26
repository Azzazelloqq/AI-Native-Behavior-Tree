# P5-001 hot-reload compatibility model decision evidence

## Result

Resolved `OQ-007` via `Documentation~/decisions/ADR-P5-001-hot-reload-compatibility-model.md`
(`AIBT-023`, Accepted 2026-08-27). Full decision, rationale, and consequences are in the ADR; this
file records the evidence behind it.

## Research: the real data structures, not assumed intent

Before deciding anything, the actual `CompiledProgram`/`ReferenceCompiler`/`ReferenceExecutionMachine`/
native-runtime code was read directly (not just the specs' prose):

- `Authoring/Compilation/ReferenceCompiler.cs`'s `OrderNodes`/`IndexNodes`: compiled node index is
  assigned by a fresh pre-order DFS traversal of the authoring document **on every compile**, with
  no indirection table. There is no stability guarantee across recompiles at all.
- `Runtime/Execution/Reference/Core/ReferenceExecutionMachine.cs`: every live-state array
  (`_nodeMemory`, `_activationGenerations`, `_observerStates`) is flatly indexed by that same
  unstable compiled index, sized off the compiled program at construction.
- `Runtime/State/Native/NativeInstanceArenaOwner.cs` and `native-runtime-v1.md`: the native layer
  hard-rejects running an instance against a different `(programOwnerId, programGeneration)` at
  all (`AIBT4311`) -- there is no in-place rebind path today, and this card does not propose adding
  one.
- The only identities surviving a recompile unchanged: stable authoring `NodeId` (via
  `CompiledProgram.DebugMap`), blackboard `StableKeyId`, and async `OperationId`'s embedded node ID.
- `Runtime/Execution/Reference/Composites/Memory/ReferenceMemoryCompositeHandlers.cs`:
  `ReferenceCompositeDecision.CursorAfterAcceptance` is a plain positional `uint`, not a stable
  child identity -- confirms a Memory composite's own "which child am I on" state is invalidated
  by reordering, independent of whether the children themselves migrate fine.

## The spike: proves the model against real `CompiledProgram` data

`Spikes~/HotReloadCompatibilityModel/SpikeHotReloadCompatibilityModel.cs` (archived here from a
temporary `Tests/Editor/Compilation/_SpikeHotReloadCompatibilityModel.cs` location, run live via
Unity MCP against the open host Editor, then removed from `Tests/` per this card's own Forbidden
changes -- never shipped as production test surface):

- Builds real `TreeDocument`s and compiles them via the real `ReferenceCompiler` (same pattern
  `Tests/Editor/Compilation/ReferenceCompilerTests.cs` and
  `Tests/Editor/Preview/ReferencePreviewParityTests.cs` already use), not synthetic-toy data.
- A `Classify(oldProgram, newProgram)` function builds a stable-`NodeId`-keyed identity map from
  each program's `DebugMap` + `Nodes` (type ID, type version, instance-memory size/alignment/
  lifetime) and diffs them -- the exact mechanism `ADR-P5-001` decided.
- One real test per `testing.md` category: parameter edit (`aibt.core.repeater`'s `count`
  changing), insertion, removal, reordering, and type change (a `NodeId` switching from
  `aibt.test.success` to `aibt.test.running`).
- **Live Unity MCP test run result: 5/5 passed.**
- The reordering test is the load-bearing proof: it asserts the two reordered nodes' compiled
  indices genuinely differ before vs. after, while the classifier still correctly says
  `CompatibleMigrate` for both by stable ID -- directly demonstrating why compiled index cannot be
  the migration key.

## Decision

See `ADR-P5-001` in full. Summary: reload is never an in-place array mutation; it is always
construct-fresh-and-selectively-copy, keyed by stable node ID. Full restart, subtree restart, and
compatible migration are the same mechanism with a different exclusion set (whole tree / localized
subtree / empty), not three independent implementations -- `Planning~/Tasks/P5/P5-004-*.md`,
`P5-005-*.md`, and `P5-006-*.md` were corrected to reflect this before any of them start
implementation.

## Scope and limitations

- No production code ships from this card, per its own Forbidden changes. `P5-002`/`P5-003` build
  the real identity/hashing and classifier; `P5-004` builds the real shared copy mechanism.
- The composite-cursor-reset rule's scope (Memory composites yes, Reactive composites presumed no)
  is stated as a decision `P5-002`/`P5-003` must confirm empirically against
  `ReferenceReactiveCompositeRegistry`'s actual real state, not re-derive from this card alone.
- The spike does not exercise `ReferenceExecutionMachine` itself (constructing a live instance and
  observing migrated state end-to-end) -- that would need `AIBT.Editor.Tests`' existing internals
  access to `ReferenceExecutionMachine`, which is sufficient for a test file, but actually
  *building* the copy mechanism is production work reserved for `P5-004`, not this decision spike.
  Classification (this card's actual deliverable) needed only the public `TreeDocument`/
  `ReferenceCompiler`/`CompiledProgram` surface, proven directly.
- One workstation, one Unity version (`6000.5.8f1`) -- no cross-platform claim is made or needed;
  this is a data-structure/compiler-behavior fact, not a performance measurement.

See `verification-results.json` for exact commands and results.
