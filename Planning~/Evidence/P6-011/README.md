# P6-011 generated agent documentation evidence

## Result

Done. `MCP/Documentation/` (`AIBT.Mcp` assembly) implements `ai-and-mcp.md`'s "Agent documentation"
deliverables: a generated node catalog, a short workflow guide, recipes, anti-patterns, and a
versioned migrations stub, written to `Documentation~/generated/*.md` by one explicit, opt-in
Editor menu command (`AIBT/MCP/Regenerate Documentation`, mirroring `McpBridgeWindow`'s own
never-automatic pattern).

## Location correction found before writing code

The card's own Allowed-changes text suggests `Authoring/Documentation/`. Research found this
would not work: the workflow guide's own deliverable ("reflecting the actual registered MCP
tools, not an idealized set") needs the real, current bridge tool-name list, which only exists
inside `AIBT.Mcp` (`McpToolDispatcher`'s own switch cases; `P6-010`'s own
`CustomMcpToolProviderDiscovery` already needed the identical list for its own collision check).
`architecture.md`'s dependency direction forbids `Authoring/` from referencing `MCP/`, so a
generator needing MCP-layer facts cannot live in `Authoring/` -- the same kind of correction
`P6-004` already made for an identical reason (its transaction engine moved from
`Authoring/Patching/` to `Editor/Patching/`). The reserved tool-name list itself was promoted out
of `P6-010`'s private copy into a new shared `MCP/McpBuiltInTools.cs`, so there is exactly one real
list of registered tool names, not two -- `CustomMcpToolProviderDiscovery` now references it
instead of keeping its own copy (re-verified: `P6-010`'s own tests still pass unchanged).

## Recipe-sourcing decision

The per-tool JSON schemas (parameter names, `[Description]` text) exist only inside the external
`MCP~/Server/*.cs` process -- genuinely unreachable from any Unity-compiled code (a separate
`dotnet` project, no shared assembly), and adding a schema-dump mode to that project is
`P6-005`-owned, outside this card's allowed paths. Recipes are therefore generator-emitted static
content with tool-call JSON transcribed directly from the real `[McpServerTool]`/`[Description]`
source (read this session, cited above), and correctness is proven the way the card's own
acceptance criterion actually states it: **live execution against a real MCP client**, a stronger
guarantee than static schema-tracing alone. One real discrepancy was caught this way: the recipes
document originally guessed `aibt_run_tests`'s response shape as `{"success": true, "steps": [...]}`;
the real live call returned `{"success": true, "executedStepCount": 1, "inputDiagnostics": [],
"failures": []}` -- fixed and re-verified before commit.

## Scope correction: "inspect a trace" replaced with "run a test"

The card's Deliverables list "inspect a trace" as one of four recipes. `P6-008`'s own evidence
(cited directly in this card's own Forbidden-changes clause) found no production code anywhere
wires a real running native tree into a trace channel -- that capability was spun off into the
still-`Draft` `P6-015`. Generating a trace-inspection recipe would claim a capability that does not
exist, exactly what this card's own Forbidden-changes clause prohibits. Replaced with **run a
behavior-case test** (`aibt_run_tests`, a real `P6-008` tool, against the real, already-committed
`Tests/Editor/Mcp/Testing/Fixtures/success-then-running.aibtcase.json` fixture) -- disclosed here
rather than silently substituted, mirroring `P6-008`/`P6-009`'s own precedent for this exact kind
of premise correction.

## Node catalog: field-for-field by construction

`McpNodeCatalogDocumentGenerator` embeds the exact, unmodified `NodeCatalogQuery.TryGetContract`
`JObject` verbatim (as a fenced JSON block) for every node, rather than re-deriving fields by hand
-- the "matches P6-003's output field for field" acceptance criterion is true by construction, not
by parallel maintenance of a second description. A dedicated test parses each embedded block back
and deep-compares it against a fresh `TryGetContract` call for every one of the 11 real built-in
nodes.

## Verification

```text
Unity EditMode full regression (host project) -- 1581/1581 executed (10 more than P6-010's own
  1571 baseline: the 10 new McpDocumentationGeneratorsTests), same 3 pre-existing failures every
  recent P6 card's evidence already documents as unrelated host-project noise, zero regressions
New tests, all passing (10): determinism (5, one per generator), diff-locality (adding one real
  fixture node via NodeRegistryBuilder.AddUserExtension changes only that node's own section, every
  other section proven byte-identical), field-for-field parity (11/11 real built-in nodes' embedded
  JSON blocks deep-equal a fresh NodeCatalogQuery.TryGetContract call), no-machine-path/no-date scan
  across all 5 generated documents, workflow-guide-references-only-real-tool-names, and a drift
  check proving the committed Documentation~/generated/*.md files match a fresh in-memory
  regeneration byte-for-byte
Live end-to-end (real bridge via Unity MCP execute_code, real permanent MCP~/Server/, official
  @modelcontextprotocol/inspector CLI):
  - Recipe "create and validate a tree": aibt_create_tree -> aibt_add_node (using the real returned
    contentHash) -> aibt_validate, exactly as documented; valid: true; the live-created tree file
    cleaned up afterward
  - Recipe "run a scheduling benchmark": aibt_run_benchmark with the real scheduling-baseline-
    empty-job scenario, matches the documented shape
  - Recipe "run a behavior-case test": aibt_run_tests against the real committed fixture; caught
    and fixed the response-shape discrepancy above
  - Recipe "generate, compile, and apply a custom node": verified narrower per the plan --
    aibt_generate_node -> aibt_preview_node_diff live-confirmed working exactly as documented
    (steps 3-7 are the identical sequence P6-009's own evidence already proved live end-to-end;
    re-running the full compile/apply here would create another real generated node for no new
    information). The one real generated staging file this produced was removed afterward to
    restore the (git-ignored) staging slot to its empty state.
  - Bridge stopped cleanly afterward; discovery file confirmed removed
Tools~/Verification/Verify-Static.ps1 -- passed, 105 work items
git diff --check -- clean
```

## Scope and limitations

- Recipe tool-call JSON is transcribed once from real source, not mechanically re-derived on every
  regeneration -- if `MCP~/Server/*.cs`'s parameter names ever change, the recipes document would
  need a manual update (disclosed, not silently risked: the drift-check test only proves internal
  consistency between the generator and its own committed output, not consistency with the external
  server's current schema).
- Node-catalog/workflow-guide/anti-patterns/migrations content has no such gap: the catalog is
  sourced live from the real registry every regeneration, and the workflow guide's tool names are
  validated against `McpBuiltInTools.BridgeToolNames` at generation time.
