# Phase 7 inputs (Phase 6 addendum)

Prepared 2026-08-31 for the `P6-012` review. `Planning~/Evidence/P3-GATE/phase5-inputs.md` and
`Planning~/Evidence/P4-GATE/phase5-inputs.md` (via `P5-GATE/phase6-inputs.md`) remain in force for
whatever Phase 7 inherits from the editor/compiled-program contract, the scheduler, and hot reload.
This document adds what Phase 6 itself contributes, per `Documentation~/roadmap.md`'s Phase 7 scope
("supported-platform matrix, long-running and stress tests, profiler validation, migration tooling,
samples, API documentation, and release automation... review public API and formats for `1.0.0`
stability").

## Stated plainly, per this card's own handoff requirement

**Native-backend hot reload and a production Play-mode host both remain open before Phase 7
begins.** Neither exists. No Phase 6 card built either -- Phase 6 built the MCP surface entirely on
top of the reference-executor backend (`aibt_simulate` explicitly names its backend and node set in
every response, never silently substituting). This affects what "production" can honestly mean for
Phase 7: any Phase 7 claim about hot reload or in-game debugging in a real Play-mode session needs
new work, not a wrapper around something Phase 5 or 6 already built.

## What Phase 7 additionally inherits from Phase 6

- **A real, working MCP server exists and is proven end-to-end**, not merely designed. `MCP~/Server/`
  (external `dotnet` process, official C# SDK) bridged to Unity via `AIBT.Mcp`
  (`McpBridgeListener`/`McpToolDispatcher`/`McpPermissionEnforcer`), reachable through a real AI
  client via the official `@modelcontextprotocol/inspector` CLI and confirmed again by this gate's
  own live session. Phase 7's own "API documentation" and "release automation" scope should account
  for the MCP surface as a real, shipped feature requiring the same `1.0.0`-stability review as the
  C# public API -- `.NET SDK` remains a plain, explicit prerequisite for AIBT's MCP features
  specifically (`ADR-P6-001`), not bundled into the core package.
- **Every MCP tool is permission-gated by one real, fail-closed enforcer**, not a per-tool ad hoc
  check. A `1.0.0` API-stability review of the MCP surface should treat
  `McpPermissionCategory`/`McpPermissionEnforcer` as the stable contract surface, not each tool's own
  argument shape (which may still evolve; `MCP/Documentation/`'s generated migrations stub exists
  precisely so a future breaking MCP change has somewhere real to be recorded).
- **A real, disclosed gap an agent-facing "safe modification" story cannot yet honestly claim
  closed**: a custom node an agent itself just generated and applied is not discoverable through the
  same discovery tools (`aibt_search_nodes`/`aibt_get_node_contract`) the agent would naturally use
  next. Before any Phase 7 "an agent can safely build a whole project's node vocabulary through MCP"
  claim, `P6-017` (per-project leaf-registration mechanism) needs a real decision and this specific
  registry-wiring gap needs closing -- this gate's own live session is the first concrete
  reproduction of exactly why `P6-017` matters, not merely a theoretical concern anymore.
- **A real, disclosed generator-template defect**: `generate_node`'s condition template does not
  compile for a `Bool`-typed blackboard read (`current >= config.Minimum` on `bool`). Any Phase 7
  "samples" or "migration tooling" work that touches node generation should account for this real,
  reproducible bug in `MCP/NodeDevelopment/NodeTemplateGenerator.cs` rather than assume the two
  maintained templates are unconditionally correct for every declared parameter type.
- **Custom MCP tool providers are a real, working extension point** (`P6-010`), discovered via
  `UnityEditor.TypeCache` with zero AIBT-side coupling to any specific consumer. Phase 7's "samples"
  scope could reasonably include a real custom-tool-provider sample, since the mechanism is proven,
  not merely designed.
- **Generated agent documentation is a real, working, tested pipeline** (`P6-011`), not hand-written
  prose to keep in sync manually. Phase 7's "API documentation" work should treat
  `MCP/Documentation/`'s generators, not the committed `Documentation~/generated/*.md` files
  themselves, as the thing to extend -- the files are regenerated output, and a `1.0.0` documentation
  review should verify the generators' own correctness (field-for-field parity, determinism) rather
  than hand-editing the generated files directly.
- **Trace inspection remains entirely unbuilt** (`P6-008`, `P6-015`). Any Phase 7 "profiler
  validation" or "long-running and stress tests" scope that wants an agent-drivable trace/inspection
  story needs `P6-015` resolved first; it is not a small extension of existing MCP verification
  tools, since the underlying native-trace-channel production wiring does not exist at all yet.

## Constraints Phase 7 must not violate (unchanged, restated from `P5-GATE`)

- Node coordinates, colors, groups, and comments still never influence semantics, reload, or MCP
  patch decisions.
- Every semantic edit (whether driven by a human, the Editor, or an MCP tool) remains gated by the
  real compiler/validator -- confirmed still true by this gate for the entire MCP authoring surface.
- No MCP tool may claim native-backend hot reload, a production Play-mode host, or trace-inspection
  capability that does not exist -- restated once more, now with a concrete gate-level proof that
  these gaps are real (this gate deliberately did not attempt to demonstrate trace inspection,
  exactly to avoid this).
- No performance default, regression threshold, or supported-platform claim may be introduced by any
  agent without the owner's explicit approval, per `Planning~/USER_ACTIONS.md` -- unchanged, and now
  extends to any claim about MCP tool latency, throughput, or "safe for N concurrent agents."
