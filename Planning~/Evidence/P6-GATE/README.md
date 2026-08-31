# Phase 6 integration gate status

Checkpoint: 2026-08-31, Unity `6000.5.8f1`. Gate executed and accepted 2026-08-31 against commit
`97e3501e71534f8de2e063cf74cdf52a36a43d04`.

## Verdict: Accepted, with two exit-criterion gaps explicitly disclosed

Every step in `gate-runbook.md` ran against a clean detached snapshot and passed. Full
machine-readable results are in `verification-results.json`. Summary:

- `P6-001` through `P6-011` complete: MCP transport/permission taxonomy and domain-patch model
  decided with real spike evidence (`P6-001`/`P6-002`), the node catalog and manifest query layer
  built over the accepted registry (`P6-003`), a real domain-patch transaction engine
  (`P6-004`), a real running MCP server with fail-closed permission enforcement (`P6-005`),
  authoring/verification/test/benchmark tools all wrapping already-accepted production entry points
  (`P6-006` through `P6-008`), the first genuinely new custom node ever generated, compiled, and
  registered through AIBT end-to-end (`P6-009`), a real custom-MCP-tool-provider extension point
  discovered via IoC (`P6-010`), and a real, tested, deterministic generated-documentation pipeline
  (`P6-011`);
- Phase 5 gate's constraints Phase 6 must not violate are all confirmed still true
  (`contract-checklist.md`);
- **precursor to this gate's own official run**: two real bugs in `P6-011`'s own test/generator code
  were found by this gate's first detached-harness attempt and fixed as addendum commits
  (`c766d50`, `97e3501`) before treating `97e3501` as the real candidate -- both test-only/generator-
  only, no production API touched;
- clean detached-UPM-harness compile: a fresh project containing nothing but `com.azzazello.aibt`
  (as a local `file:` package) and its declared dependencies, exit code 0;
- full detached-package EditMode: **1224/1224**, 0 failed, 0 skipped (XML SHA-256
  `e0b8f0f9283d972b6df9bc059850f50a364bdd7010a37f20bfc53d00a7ed49fb`); the pre-existing
  host-project-only failures seen in every prior gate's evidence did not reproduce here; a genuine
  cold-start GC-allocation flake in a pre-existing, Phase-6-untouched `P3-010` test was found,
  confirmed non-reproducing on an immediate warm re-run, and not re-litigated further;
- public API surface recorded for all three previously-audited assemblies -- **405 types (+14), 2067
  members (+43) versus `P5-GATE`'s 391/2024** -- confirmed by `diff` to be **purely additive**:
  `AIBT.Authoring.NodeCatalogQuery`/`ProjectManifestQuery`/`ProjectPolicySnapshot` (`P6-003`) and
  `AIBT.Editor.Patching.LayoutDiff`/`LayoutDiffEntry`/`LayoutDiffKind`/`LayoutDiffTarget`/
  `LayoutPatchResult`/`LayoutPatchTransaction`/`SemanticDiff`/`SemanticDiffEntry`/`SemanticDiffKind`/
  `SemanticPatchResult`/`SemanticPatchTransaction` (`P6-004`), plus their members;
- **`AIBT.Mcp`'s own public surface is recorded for the first time**, as a separate baseline (no
  prior gate audited it -- it did not exist): **7 types, 29 members**
  (`ICustomMcpToolProvider`, `McpBridgeListener`, `McpBridgeWindow`, `McpPermissionCategory`,
  `McpPermissionEnforcer`, `McpToolDispatcher`, `AibtTreeDiscovery`) — everything else in the
  assembly is `internal`;
- assembly dependency audit: `AIBT.Runtime`/`AIBT.Authoring`/`AIBT.Editor` are byte-identical to
  `P5-GATE`'s own recorded references; the new `AIBT.Mcp` depends on `Editor`/`Authoring`/`Runtime`
  only, is never referenced back, and depends on no third-party MCP client library or literal
  LLM-provider SDK;
- `git status`/`git rev-parse HEAD` clean at every checkpoint (HEAD unchanged at the candidate
  commit throughout the official run);
- **a real, live, end-to-end MCP client session** against the real permanent `MCP~/Server/` and the
  real open Editor demonstrated discover → create → atomic add/connect → configure → validate →
  compile → simulate (with the trace proving the configured value actually took effect) → the
  complete generate/preview/test-scaffold/compile/test/apply gate for a genuinely new custom node →
  run a real benchmark — every operation the roadmap's own exit criterion names, **except one**:
  "inspects a trace";
- `README.md` and `CHANGELOG.md` were found stale (still describing Phases 1-5 as complete, and the
  repository map still naming a nonexistent planned `Tools~/McpServer/`) and updated to reflect
  actual Phase 6 completion, stating both disclosed gaps below plainly, checked against
  `claims-inventory.md` afterward to confirm nothing stronger than evidence was introduced.

**Two roadmap exit-criterion gaps are explicitly disclosed, not smoothed over:**

1. **Trace inspection.** No production code anywhere wires a real running native tree into a trace
   channel (`P6-008`'s own finding, deferred to `P6-015`, still `Draft`). Put to the owner directly
   before this gate's own verification began; the owner's decision was to accept Phase 6 with this
   gap explicitly disclosed, mirroring `P5-010`'s own acceptance of Phase 5 with two disclosed scope
   reductions rather than blocking the gate on them.
2. **Node discoverability after generation.** Found live, for the first time, by this gate's own
   end-to-end session: a custom node this same session had just generated, compiled, tested, and
   applied via `aibt_apply_node` returned `found: false`/zero results from
   `aibt_get_node_contract`/`aibt_search_nodes`. Root cause: both discovery tools build their
   registry via `NodeRegistryBuilder.CreateWithBuiltIns()`, which only ever includes the hardcoded
   built-in list -- nothing wires an applied custom shard's manifest into it. This is a concrete,
   now-proven manifestation of the already-tracked `P6-017` (per-project leaf-registration
   mechanism), still `Draft`. Not fixed by this gate, per its own Forbidden-changes clause (a gap
   found here becomes a disclosed limitation or a follow-up card, never a same-gate production fix).

A third, smaller real finding — `generate_node`'s condition template does not compile for a
`Bool`-typed blackboard read (`current >= config.Minimum` on `bool`) — surfaced during the live
session and is recorded in `known-limitations.md`; it did not block the proof, since a numeric type
was used instead once found.

**Phase 6 is complete**: `P6-001` through `P6-012` are all `Done`, with the trace-inspection and
node-discoverability gaps above explicitly disclosed as scoped-out rather than silently missing --
the same discipline `P5-010` already established for Phase 5's own two scope reductions.

## Gate package

| Document | Purpose |
| --- | --- |
| `contract-checklist.md` | Each Phase 6 card's own contract, and Phase 5's constraints Phase 6 must not violate, mapped to its evidence |
| `claims-inventory.md` | Exactly what Phase 6 claims and what it deliberately does not |
| `known-limitations.md` | Scope limits carried into Phase 7 and beyond |
| `gate-runbook.md` | The verification commands actually executed, and their actual results |
| `assembly-dependencies.json` | Per-asmdef reference audit against the forbidden-dependency list, now covering `AIBT.Mcp` too |
| `public-api.txt` / `.sha256` | Reflected public surface of `AIBT.Runtime` + `AIBT.Authoring` + `AIBT.Editor` at the accepted commit (additive-only versus `P5-GATE`) |
| `public-api-mcp.txt` / `.sha256` | Reflected public surface of the new `AIBT.Mcp` assembly -- a first-time baseline, not diffed against any prior gate |
| `verification-results.json` | Machine-readable result of every gate-runbook step |
| `phase7-inputs.md` | What Phase 7 (production hardening) additionally inherits from Phase 6, on top of `P3-GATE`/`P4-GATE`/`P5-GATE`'s own handoffs |
