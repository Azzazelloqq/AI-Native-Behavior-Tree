# Known limitations after Phase 6

Prepared 2026-08-31 for the `P6-012` review.

## Carried forward from earlier phases, still true

- `Editor/Graph/`'s live window is not wired to anything Phase 3, 4, 5, or 6 built; every
  preview/debugger/trace/hot-reload/MCP surface hosts its own private view/window instance.
  Unchanged (`P6-016`, `Draft`).
- No production Play-mode host exists to attach a debugger, trace view, or hot-reload trigger to a
  real running game. Unchanged.
- No production per-project leaf-behavior registration mechanism exists; every executable leaf used
  across every phase's evidence, including every Phase 6 MCP verification/simulation tool, is still
  a Phase 1 fixture or built-in composite/decorator. Unchanged, but now sharpened by a concrete new
  finding this gate made (below).
- Native-backend hot reload does not exist, and compatible/subtree migration only runs against an
  idle old instance, falling back to full restart for a genuinely active one. Unchanged since
  `P5-GATE`; no Phase 6 card touched hot reload at all.
- Only 6 of 14 `P4-001` catalog scenarios are measured end-to-end; `Auto` underperforms the best
  fixed policy in most measured cases (`P4-006`, `P4-007`; recalibration, not runtime adaptation, is
  `P6-019`, `Draft`).
- Calibration remains two devices (one Windows workstation, one Android phone), not a hardware-class
  generalization.
- Public API and persisted formats remain experimental below `1.0.0`.

## New in Phase 6, carried into Phase 7 and beyond

- **No production code wires a real running native tree into a trace channel.** `P6-008` found this
  before building against a false premise and spun trace/compare-trace off into `P6-015` (`Draft`).
  This is one of two roadmap Phase 6 exit-criterion items ("inspects a trace") this gate could not
  demonstrate — disclosed directly to the owner via `AskUserQuestion` before this gate's own
  verification began; the owner accepted Phase 6 with this gap explicitly disclosed, mirroring
  `P5-010`'s own acceptance of Phase 5 with two disclosed scope reductions.
- **A newly-generated, applied custom node is invisible to the discovery tools.** Found live by this
  gate's own end-to-end session: after a real `aibt_apply_node` call persisted
  `aibt.p6012gate.threshold-condition` into the project, `aibt_search_nodes`/`aibt_get_node_contract`
  both reported it not found. Root cause confirmed by reading source: both discovery tools build
  their registry via `NodeRegistryBuilder.CreateWithBuiltIns()`, which only ever includes the
  hardcoded `BuiltInNodeManifests.All` list -- nothing wires an applied shard's manifest into that
  same registry. `P6-009`'s own `test_node` tool proves registry-materializability through a
  separate, different path (`GeneratedShardMetadataMaterializer`/`GeneratedNodeRegistry.Build`) and
  never claimed discovery-tool visibility, so this gap was never actually exercised end-to-end
  before this gate. This is the second roadmap exit-criterion item ("modify a tree without...
  guessing node contracts") this gate could not fully demonstrate for a self-generated node, and the
  sharpest concrete manifestation yet of the already-tracked `P6-017` (per-project leaf-registration
  mechanism, `Draft`).
- **`generate_node`'s condition template does not compile for a `Bool`-typed blackboard read.** The
  template unconditionally emits `current >= config.Minimum`, and `bool` has no `>=` operator. Found
  live by this gate on the first attempt (a real `CS0019`); worked around for this gate's own proof
  by using a numeric type instead. A real, disclosed `P6-009` template defect, not fixed by this
  gate (out of its own allowed-changes fence) -- real follow-up work for whichever card next touches
  `MCP/NodeDevelopment/NodeTemplateGenerator.cs`.
- **`analyze_and_compile_node`'s two-call design assumes prompt external-change detection.** In a
  fully headless, unfocused automation session, a staged file's write can sit as
  `external_changes_dirty: true` until something explicitly asks Unity to refresh (e.g. a Unity MCP
  `refresh_unity` call) -- not a defect in the tool itself (an interactively-used Editor notices
  external changes on its own), but worth knowing for anyone driving this tool from a similarly
  headless automation context.
- **Phase 6 legitimately added new public API surface**: `AIBT.Authoring.NodeCatalogQuery`/
  `ProjectManifestQuery`/`ProjectPolicySnapshot` (`P6-003`), `AIBT.Editor.Patching.*` (11 types,
  `P6-004`), and a genuinely new `AIBT.Mcp` assembly with 7 public types
  (`ICustomMcpToolProvider`, `McpBridgeListener`, `McpBridgeWindow`, `McpPermissionCategory`,
  `McpPermissionEnforcer`, `McpToolDispatcher`, `AibtTreeDiscovery`). See `README.md`'s Verdict
  section for the full diff.
- **Cancellation is declared, never enforced, anywhere in the MCP surface.** The entire bridge wire
  protocol (`McpBridgeListener`/`BridgeClient`) is one blocking TCP request/response with no message
  IDs and no cancellation transport, for every tool built in Phase 6, built-in or custom
  (`ICustomMcpToolProvider.SupportsCancellation`, `P6-010`).
- **No regression threshold, supported-platform claim, or "acceptable MCP latency" claim exists
  anywhere in the package.** Every P6 card's own "Forbidden changes" restates
  `Planning~/USER_ACTIONS.md`'s requirement that such a claim needs the owner's explicit approval,
  which has not been sought.

## Blocking nothing, recorded for completeness

- The remote `P0-005` Unity CI job remains queued, as it has since Phase 1; this was waived to start
  Phases 2 through 6 and must not be reported as resolved.
- `P6-013` through `P6-022` (ten `Draft` tech-debt/decision cards) remain open. None of them blocks
  this gate or Phase 7's own start, per each card's own recorded dependency shape -- they are
  decided-on-paper-first cross-phase debt, the same pattern `P3-001`/`P4-007`/`P5-001` already
  established for this project.
