# Phase 6 claims inventory

Prepared 2026-08-31 for the `P6-012` review, against candidate commit
`97e3501e71534f8de2e063cf74cdf52a36a43d04`. Every supported claim below already has committed
evidence.

## Supported claims

- AIBT exposes a real, working MCP server (`MCP~/Server/`, an external `dotnet` process on the
  official C# SDK) bridged to the Unity Editor over a thin, no-SDK-dependency listener
  (`AIBT.Mcp`), per `ADR-P6-001` (`AIBT-024`) -- proven live via the official
  `@modelcontextprotocol/inspector` CLI in every `P6-005` through `P6-011` card and again by this
  gate's own full end-to-end session.
- Every MCP tool call is checked against a fail-closed permission enforcer
  (`McpPermissionEnforcer`) covering all 8 `ADR-P6-001` categories; a call outside the session's
  granted set is rejected with a structured diagnostic, never silently downgraded or allowed
  (`P6-005` through `P6-011`, each with its own permission-negative test; re-confirmed by `P6-010`'s
  own custom-tool permission proof).
- An AI agent can discover the real node catalog and project manifest (`aibt_get_project_manifest`,
  `aibt_search_nodes`, `aibt_get_node_contract`) sourced from the same production registry the
  compiler uses -- no second, hand-maintained catalog exists (`P6-003`).
- An AI agent can create a tree and add/remove/move/replace/configure nodes, declare blackboard
  keys, extract/inline subtrees, and apply several operations as one atomic domain patch, every
  mutation gated by the real compiler/validator -- proven again by this gate's own live session
  (create → atomic add+connect → configure → validate → compile → simulate, with the simulated
  trace confirming the configured value actually took effect).
- An AI agent can validate, compile, and simulate a tree, and look up a returned diagnostic code's
  stable meaning, all through the real production `TreeValidator`/`ReferenceCompiler`/
  `ReferencePreviewDriver` (`P6-007`).
- An AI agent can run a real behavior-case test and a real `P4-001` scheduling benchmark through the
  same production runners the project's own test suite and Phase 4 research used, with no threshold
  or default attached to the raw measurement (`P6-008`; re-confirmed by this gate's own live
  `run_benchmark` call).
- An AI agent can generate, preview, compile-check, and apply a genuinely new custom Burst node into
  the real project, through the real packaged Roslyn analyzer, with every step before `apply_node`
  touching only a quarantined staging slot -- proven twice now: `P6-009`'s own two maintained
  templates, and again by this gate's own independently-generated node
  (`aibt.p6012gate.threshold-condition`).
- A consuming project can register its own high-level MCP tool (`ICustomMcpToolProvider`),
  discovered purely via `UnityEditor.TypeCache` with zero AIBT-side reference to the consumer's
  assembly, exposed as a real, individually-schema'd MCP tool (not a generic passthrough), and
  enforced by the identical permission path every built-in tool uses (`P6-010`).
- Generated agent documentation (node catalog, workflow guide, recipes, anti-patterns, a versioned
  migrations stub) is produced from the same real production data every MCP tool itself uses, never
  a hand-duplicated second catalog, and regeneration is deterministic and idempotent (`P6-011`,
  re-verified by this gate's own drift-check fixes).
- Unity `6000.5.8f1` compiles `AIBT.Runtime` + `AIBT.Authoring` + `AIBT.Editor` + `AIBT.Mcp` as a
  detached UPM installation and passes 1224 EditMode tests with 0 failed and 0 skipped, including
  every Phase 6 test fixture re-run against this exact committed snapshot (this gate).
- `AIBT.Mcp` depends on `AIBT.Editor`/`AIBT.Authoring`/`AIBT.Runtime` only, is never referenced back
  by any of them, and depends on no third-party MCP client library or literal LLM-provider SDK
  (`assembly-dependencies.json`) -- the real MCP protocol implementation is a hand-rolled
  newline-delimited JSON relay over a plain `TcpListener`.

## Claims intentionally not made

- **Trace inspection.** No MCP tool exists to inspect a running native tree's trace, because no
  production code anywhere wires a real running native tree's lifecycle into a trace channel yet
  (`P6-008`'s own finding). Deferred to `P6-015`, still `Draft`. This is one of two roadmap
  exit-criterion items this gate explicitly could not demonstrate -- disclosed, not silently
  substituted with something else and presented as equivalent.
- **Discoverability of a just-generated custom node.** `aibt_search_nodes`/`aibt_get_node_contract`
  do not see a node after `apply_node` persists it -- both discovery tools query
  `NodeRegistryBuilder.CreateWithBuiltIns()`, which only ever includes the hardcoded built-in list.
  Found live by this gate's own end-to-end session, tied to the already-tracked `P6-017`
  (per-project leaf-registration mechanism), still `Draft`. This is the second roadmap
  exit-criterion item this gate could not fully demonstrate.
- **A working `Bool`-typed condition template.** `generate_node`'s condition template emits an
  unconditional `>=` comparison that does not compile for `blackboardReadType: Bool`. Found live by
  this gate; a real, disclosed `P6-009` defect, not fixed by this gate (out of its own
  allowed-changes fence).
- **Event/completion/resume/abort/step-budget injection through `simulate`.** The driver assigns
  `updateId`/`snapshotRevision` itself sequentially; only plain `update` steps are supported.
  Widening this is `P6-013`, still `Draft`.
- **Agent or Shared blackboard scope over MCP.** Only tree-scoped, built-in-scalar-typed keys are
  supported; the real project policy (`ReferenceCompilationPolicy.Phase1`) has both capability flags
  `false` in production. Deciding whether to support this is `P6-014`, still `Draft`.
- **Any MCP tool wired into `Editor/Graph/`'s live window.** Every Phase 3/5/6 Editor-facing tool
  remains its own private view; unifying them is `P6-016`, still `Draft`.
- **A production per-project leaf-registration mechanism.** Every verification/simulation tool
  (`P6-007`, `P6-008`) is fixed to the same Phase 1 fixture/built-in node set `P3-009` already used
  -- not a real consuming project's own custom leaf nodes. Deciding this is `P6-017`, still `Draft`;
  this gate's node-discoverability finding is the sharpest concrete demonstration yet of why this
  matters.
- **Native-backend hot reload, or a production Play-mode host.** Unchanged since `P5-GATE`: neither
  exists. No Phase 6 card built either. See `phase7-inputs.md`.
- **Cancellation actually being enforced anywhere in the MCP bridge.** The entire wire protocol
  (`McpBridgeListener`/`BridgeClient`) is one blocking request/response with no cancellation
  transport for any tool, built-in or custom. `ICustomMcpToolProvider.SupportsCancellation` is
  declaration-only (`P6-010`).
- **A real registry-materialized custom node actually dispatches through native execution.**
  `test_node` proves compile-clean + registry-materialization-valid, never dispatch execution --
  that translator does not exist yet (`P6-022`, still `Draft`).
- **Any performance default, regression threshold, or supported-platform claim.** Every P6 card's
  own "Forbidden changes" repeats `Planning~/USER_ACTIONS.md`'s requirement that such a claim needs
  the owner's explicit approval -- none has been sought or granted.
- **Stable public API compatibility beyond the recorded experimental `0.1.0` baseline.** Phase 6
  legitimately added new public types to `AIBT.Authoring`/`AIBT.Editor` and a genuinely new
  `AIBT.Mcp` assembly (see `README.md`'s Verdict section); none of them are claimed stable
  pre-`1.0.0`.
- **Anything about Phase 1 through 5's own runtime, editor, scheduling, platform, or hot-reload
  claims beyond what `P2-GATE` through `P5-GATE` already recorded.** This gate does not re-litigate
  any earlier accepted gate.
