# Phase 6 inputs (Phase 5 addendum)

Prepared 2026-08-27 for the `P5-010` review. `Planning~/Evidence/P3-GATE/phase5-inputs.md`
and `Planning~/Evidence/P4-GATE/phase5-inputs.md` remain in force for whatever
Phase 6 inherits from the editor/compiled-program contract and the scheduler.
This document adds what Phase 5 itself contributes: the hot-reload contract
an MCP tool provider would trigger and observe, per `Documentation~/roadmap.md`'s
Phase 6 scope ("MCP discovery, authoring, validation, compilation, simulation,
trace, test, benchmark, and code-generation tools") and `Documentation~/ai-and-mcp.md`.

## What Phase 6 additionally inherits from Phase 5

- **A stable, resolved reload model.** `OQ-007` is closed: reload is always
  construct-fresh-and-selectively-copy by stable authoring `NodeId`, never an
  in-place mutation. An MCP tool that edits a tree and wants to see the
  effect live does not need to invent its own reload semantics or reason
  about compiled-index stability -- the model is decided (`ADR-P5-001`).
- **A working, driveable reload entry point exists today, but only for the
  reference-executor backend.** `HotReloadPreviewDriver` (public
  `AIBT.Authoring`) is the concrete shape an MCP "reload"/"apply and preview"
  tool would wrap: `TryCreate(document, sourceId)` then
  `TryReload(newDocument, newSourceId)`, returning a `HotReloadPreviewOutcome`
  that names the actual per-node classification and strategy used. This is
  the same reference-oracle-only environment `P3-009`'s preview tooling
  already lives in -- an MCP simulation/preview tool built against it inherits
  that same fixed Phase 1 fixture/built-in node-behavior set, not a
  production per-project leaf registry (still unbuilt, unchanged since
  Phase 1).
- **No native-backend reload exists to wrap.** An MCP tool cannot offer
  "reload my Burst-compiled tree in Play mode" today -- only the managed
  reference executor supports hot reload. If Phase 6 needs that, it is new
  work, not a wrapper around something Phase 5 already built (see
  `known-limitations.md`).
- **A reload's classification and outcome are structured and inspectable**,
  not just a pass/fail flag: `HotReloadClassificationResult` exposes
  per-node verdicts (`Migrate`/`New`/`Dropped`/`IncompatibleRestart`), the
  restart-subtree root set, and whether a full restart was required.
  `HotReloadMigrationReport`/`HotReloadPreviewOutcome` expose migrated/reset/
  dropped node counts and whether a fallback to full restart occurred. This
  is exactly the shape `Documentation~/ai-and-mcp.md`'s "structured
  diagnostics" discipline expects an agent-facing tool to surface, rather
  than a boolean success flag with no explanation.
- **Reload cost is measured, not assumed, and has a real, disclosed
  characteristic an MCP tool provider should design around**: reload cost
  does not amortize across a population of live instances sharing one tree
  (`P5-009`). An MCP tool that reloads N running agents against an edited
  tree will pay compile+classify cost N times, not once, until a batched
  reload API is built -- worth knowing before Phase 6 assumes reload is free
  at scale.
- **Reload never bypasses the real compiler/validator.** Both
  `HotReloadPreviewDriver.TryCreate` and `TryReload` compile through the
  same production `ReferenceCompiler`; an uncompilable edited document is
  rejected with diagnostics, never silently accepted. An MCP "apply edit and
  reload" tool inherits this for free -- it cannot produce an unvalidated
  live instance by construction.

## Constraints Phase 6 must not violate (unchanged, restated from `P4-GATE`)

- Node coordinates, colors, groups, and comments still never influence
  semantics or reload decisions.
- A hot-reload path must not weaken `P3-006`'s "every semantic edit is gated
  by the real compiler/validator" contract to make reloading more
  convenient -- confirmed still true by this gate.
- An MCP tool built on top of `HotReloadPreviewDriver` must not present an
  incompatible reload as a silently successful migration; `P5-008`'s own
  workflow already establishes the expected disclosure discipline
  (`HotReloadPreviewOutcome` names the actual category), and any MCP-facing
  tool should preserve it rather than collapse it to a boolean.
- New, restated for Phase 5: an MCP tool must not claim native-backend hot
  reload works, or silently degrade a requested native reload to the
  reference-executor backend without telling the caller -- that backend gap
  is real and disclosed, not a detail an agent-facing tool should paper over.
