# Phase 6 contract checklist

Prepared 2026-08-31 for the `P6-012` review, against candidate commit
`97e3501e71534f8de2e063cf74cdf52a36a43d04`. This is the checklist the gate verification pass
checks, not a separate acceptance record from what each P6 card's own evidence already established.

## Phase 5 gate's constraints Phase 6 must not violate (`P5-GATE/phase6-inputs.md`)

| Constraint | Check |
| --- | --- |
| Node coordinates, colors, groups, and comments still never influence semantics or reload decisions | Unchanged: no P6 card touches layout or reload code at all |
| A hot-reload path must not weaken `P3-006`'s "every semantic edit is gated by the real compiler/validator" contract | Unchanged; Phase 6's own patching (`P6-004`) runs through the identical `ReferenceCompiler`, never a second, weaker one |
| An MCP tool built on top of `HotReloadPreviewDriver` must not present an incompatible reload as a silently successful migration | No Phase 6 card built an MCP reload/preview tool at all (disclosed: no MCP hot-reload tool exists yet) |
| An MCP tool must not claim native-backend hot reload works, or silently degrade a requested native reload to the reference-executor backend without telling the caller | Satisfied vacuously -- no MCP tool touches hot reload; `aibt_simulate`'s own description explicitly names its backend and node set every response, matching the "never silently degrade" spirit for the tool that does exist (verification/execution) |

## Each P6 card's own contract, verified

| # | Requirement | Resolution |
| --- | --- | --- |
| `P6-001` | Decide MCP transport, hosting-process model, new-assembly shape, and permission taxonomy | `ADR-P6-001` (`AIBT-024`), Accepted 2026-08-27; external `dotnet` process + thin no-SDK-dependency Editor bridge, proven via 5 real spike checks including a genuine external-process TCP round trip and a real restarted-Claude-Code-session tool call |
| `P6-002` | Decide the domain-patch/revision/dry-run/diff transaction model | `ADR-P6-002` (`AIBT-025`), Accepted 2026-08-27; confirms `SemanticEditTransaction.Apply` already provides atomicity/dry-run, decides an expected-revision/content-hash precondition and two separate diff kinds (semantic vs layout, never unified) |
| `P6-003` | Node catalog and project manifest query layer over the accepted registry | `Authoring/Discovery/` (`NodeCatalogQuery`, `ProjectManifestQuery`, `ProjectPolicySnapshot`); 13 tests, re-run clean in this gate's detached harness; this gate's own public-API diff confirms these are the only new `AIBT.Authoring` public types |
| `P6-004` | Domain-patch transaction engine, built on `P3-006`'s existing operations | `Editor/Patching/SemanticPatchTransaction.cs`/`LayoutPatchTransaction.cs`; 8 tests plus a 9/9 isolation-suite regression; this gate's own public-API diff confirms these are the only new `AIBT.Editor` public types; live-verified again this gate (`aibt_apply_domain_patch` adding a decorator and its required child atomically) |
| `P6-005` | MCP server host, discovery tools, permission enforcement | `MCP~/Server/` + `AIBT.Mcp`; 30 tests; the first real running MCP surface, fail-closed `McpPermissionEnforcer` covering all 8 taxonomy categories; live-verified again this gate (every call in the end-to-end session went through this same enforcement path) |
| `P6-006` | MCP authoring tools (create/add/remove/move/replace/configure nodes, blackboard keys, extract/inline subtrees, domain-patch, layout) | `MCP/Authoring/`, 11 tools, 17 tests; live-verified again this gate (`create_tree`/`apply_domain_patch`/`configure_node` all exercised for real) |
| `P6-007` | MCP verification tools (validate/compile/simulate/explain-diagnostic) | `MCP/Verification/`, 4 tools, 14 tests; live-verified again this gate (`validate`/`compile`/`simulate` all exercised for real, `simulate`'s trace confirmed the configured parameter took effect) |
| `P6-008` | MCP test/benchmark tools (narrowed from an original trace/test/benchmark scope) | `MCP/Testing/`, 2 tools (`run_tests`/`run_benchmark`); trace/compare-trace correctly spun off to `P6-015` before being built against a false premise; live-verified again this gate (`run_benchmark` exercised for real against a real `P4-001` scenario) |
| `P6-009` | Node development tools (generate/preview/test-scaffold/compile/test/apply) | `MCP/NodeDevelopment/`, 6 tools, 18 tests; the first genuinely new custom node generated, compiled, and registered end-to-end in the project's history; **re-proven again this gate**, live, with a second, independently-generated node (`aibt.p6012gate.threshold-condition`), which additionally surfaced a real template bug (see `known-limitations.md`) and the node-discoverability gap (below) that `P6-009`'s own narrower `test_node` scope never actually exercised |
| `P6-010` | Custom MCP tool provider registration and permission model | `MCP/CustomTools/`; discovery via `UnityEditor.TypeCache`, zero AIBT-side reference to the sample provider assembly; 11 tests; live-verified (a real dynamically-registered MCP tool with its own schema, a permission-denial proof by observable file-absence, and a provider-assembly-removed regression check) |
| `P6-011` | Generated agent documentation (catalog, workflow guide, recipes, anti-patterns, migrations stub) | `MCP/Documentation/`; node catalog embeds `P6-003`'s own contract verbatim (field-for-field by construction); 10 tests plus this gate's own two addendum fixes; "inspect a trace" correctly substituted with "run a test" before building, since the literal recipe would have claimed a nonexistent capability |
| `P6-012` | This gate: re-verify the whole phase from a clean snapshot, prove the roadmap exit criterion end-to-end, hand off to Phase 7 | This document and the rest of `Planning~/Evidence/P6-GATE/` |

## Verified from existing Phase 6 evidence, re-confirmed by this gate

| Claim | Evidence |
| --- | --- |
| Custom tools are discovered via IoC (`TypeCache`), never a hardcoded reference | `Evidence/P6-010/`; this gate's own `assembly-dependencies.json` confirms no production assembly references any consumer assembly |
| Domain patches are atomic; an invalid multi-operation patch leaves the document byte-unchanged | `Evidence/P6-004/`; this gate's own live session applied a real two-operation patch successfully as one unit |
| Every MCP tool call is gated by the same `McpPermissionEnforcer`, not a parallel path | `Evidence/P6-005/` through `Evidence/P6-011/`, each card's own permission-negative test; this gate's `P6-010` re-verification proved the same gate also blocks a custom tool |
| A configured parameter change actually takes effect through compilation and execution, not just at the API boundary | This gate's own live session: `aibt_configure_node` changed `count` from 1 to 3, and `aibt_simulate`'s resulting trace shows exactly 3 leaf ticks |
| Full detached-package regression | **1224/1224** EditMode, 0 failed, 0 skipped, this gate's harness; XML SHA-256 `e0b8f0f9283d972b6df9bc059850f50a364bdd7010a37f20bfc53d00a7ed49fb` |
| Clean detached-UPM-harness compile | This gate: exit code 0, see `verification-results.json` |
| Static and schema hygiene | 105 work items, 6 schemas, both passed; clean working tree at the candidate commit |
| Public API surface: legitimate new public types, not a smuggled claim | `public-api.txt`/`.sha256`: 3 assemblies, 405 types (+14), 2067 members (+43), additive-only diff against `P5-GATE`'s 391/2024 dump |
| `AIBT.Mcp`'s own public surface is small and intentional | `public-api-mcp.txt`/`.sha256`: 7 types, 29 members -- everything else in the assembly is `internal` |
| Runtime dependency direction unchanged, extended correctly: `MCP` depends on `Editor`/`Authoring`/`Runtime` only, never the reverse | `assembly-dependencies.json` |

## Explicitly disclosed, not silently claimed

| Item | Where disclosed |
| --- | --- |
| Trace inspection: no production code wires a real running native tree into a trace channel | `Evidence/P6-008/README.md`; restated by this gate as one of two exit-criterion gaps |
| A newly-generated, applied custom node is not discoverable via `aibt_search_nodes`/`aibt_get_node_contract` | New finding by this gate's own live session; tied to the already-tracked `P6-017` (per-project leaf-registration mechanism, still `Draft`) |
| `generate_node`'s condition template does not compile for a `Bool` blackboard-read type paired with its own "threshold" comparison (`>=` on `bool`) | New finding by this gate's own live session; a real `P6-009` template defect, not fixed by this gate |
| `simulate` cannot inject events/completions or drive resume/abort/step-budget | `Evidence/P6-007/README.md`; `P6-013`, still `Draft` |
| Agent/Shared blackboard scope is rejected by MCP; only Tree scope is supported | `Evidence/P6-006/README.md`; `P6-014`, still `Draft` |
| No Phase 3/5 Editor tool (including anything Phase 6 built) is wired into one live `Editor/Graph/` window | Every relevant card's own disclosure; `P6-016`, still `Draft` |
| No production per-project leaf-registration mechanism exists; every MCP verification/simulation tool is fixed to the Phase 1 fixture/built-in node set | `Evidence/P6-007/`, `Evidence/P6-008/`; `P6-017`, still `Draft` -- this gate's node-discoverability finding is the sharpest concrete demonstration of this gap yet |
| Native-backend hot reload and a production Play-mode host still do not exist | Carried forward unchanged from `P5-GATE`; see `phase7-inputs.md` |
| No performance default, regression threshold, or supported-platform claim is introduced anywhere in Phase 6 | Every P6 card's own "Forbidden changes"; confirmed again in `claims-inventory.md` |

No normative contract was relaxed to obtain the verified rows above, and no gap found by this gate
was patched by this gate -- each is either an already-tracked `Draft` decision card or is recorded
here as a disclosed limitation for a future card to pick up.
