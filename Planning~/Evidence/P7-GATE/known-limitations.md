# P7-016 known limitations

Carried forward from `P6-GATE`, closed where Phase 7 closed them, and new findings from this gate's
own review — every item disclosed plainly, none silently dropped from the record.

## Closed since `P6-GATE`

- **Trace inspection** (`P6-GATE` gap 1). Closed by `P7-007`: a real external recorder now wires a
  production driver (`SchedulingPolicyDriver`) into `NativeTraceChannelOwnerV1`, proven to read back
  correctly through `P3-010`/`P3-011`'s own unmodified consumers. Scoped to `TryRunImmediate` only
  (`TryRunBudgeted`/`TryRunBatchedJobsSameFrame` remain unwired — a real, disclosed narrower scope,
  not the full original gap, but the "nothing in production wires a trace channel at all" gap is
  closed).
- **Applied-node discoverability** (`P6-GATE` gap 2). Closed by `P7-008`: `NodeRegistryBuilder
  .AddProjectExtension` plus MCP `TypeCache`-based discovery wiring make a project's own applied
  leaves visible to `aibt_search_nodes`/`aibt_get_node_contract`/`get_project_manifest`.
- **Native-backend hot reload** (open since `P5-004`, restated at `P5-GATE`/`P6-GATE`). Closed by
  `P7-011` (decision) + `P7-012` (implementation): migration is not idle-only for native (unlike the
  reference executor), proven live against a genuinely active instance.
- **`P5-007`'s remaining native-backend acceptance criteria** (golden-equivalence re-run, batch
  isolation, `Auto` determinism, all for a hot-reloaded native instance). Closed by `P7-012` in the
  same pass.

## Still open — decision exists, implementation does not

- **Production Play-mode host.** `P7-010`'s `ADR-P7-010` decides the shape, location, and lifecycle
  in full, proven by a real spike (32,295 real `Update()` calls in real Play mode, live
  debugger-attachment proof) — but **no implementation card exists anywhere in Phase 7's own
  decomposition**. Against `scope.md`'s "Production-ready editor and debugger" 1.0 criterion, this
  is a genuine, unclosed gap. The single most-repeated finding across this entire project
  (`P3-009`, `P3-010`, `P3-011`, `P6-008`, `P6-012`, and now `P7-016` itself) remains a decided-but-
  unbuilt design, not working production code.

## New findings from this gate's own review

- **Task-card bookkeeping drift, 4 cards.** `P6-012`, `P7-007`, `P7-010`, `P7-011` all had real,
  accepted `Planning~/Evidence/<ID>/README.md` evidence and `status: "done"` in `work-items.json`,
  but their own task-card files were never flipped to `Status: Done` with an `## Outcome` section —
  a "mark the card done" step silently skipped at least 4 times across two gates. Fixed as part of
  this gate's own review (a disclosed widening of its Allowed-changes fence, since the fix is pure
  bookkeeping with zero implementation content).
- **`AIBT.Mcp`'s external contract has at least one real, undocumented breaking change.**
  `test-node`'s response shape lost its always-present `scopeNote` field when `P7-009` widened the
  tool (replaced by `dispatchProven`/`dispatchReason`/etc.) — a genuine "output field's meaning
  changed" event per `Documentation~/generated/migrations.md`'s own definition, but it was never
  logged there. Found by diffing `MCP/` against the `P6-GATE` candidate commit, not by inspection
  alone. Retroactively logged in `migrations.md` as part of this gate's own documentation-
  consistency pass. See `Planning~/Evidence/P7-GATE/p7-001-stability-decision.md`.
- **`McpApiReferenceGenerator`'s summary-correlation silently no-ops for any real UPM consumer.**
  `CollectTypeSummaries()` hardcodes `Application.dataPath + "/AIBT"` as its source-scan root — only
  correct when AIBT is embedded directly under a host project's `Assets/`. For a `file:`/registry
  UPM package (this repository's own detached-harness gate technique, and how a real end user would
  actually consume this package), the directory does not exist, so every generated
  `api-reference-*.md` silently loses 100% of its inlined type summaries with no error. Caught live
  by this gate's own full detached-harness regression:
  `McpDocumentationGeneratorsTests.GeneratedDocumentationRegeneratesToExactlyTheCommittedFiles`
  fails (`api-reference-runtime.md` differs — the committed file, generated inside the host project,
  has the real summary; a fresh regeneration inside the detached harness does not). This is the
  first time this generator has ever run outside the host project. **Result: this gate's own full
  detached EditMode regression is 1269/1270, not 1270/1270 — one real, disclosed, pre-existing
  failure, not fixed inside this gate per its own Forbidden-changes clause.** Spun off as
  `P7-021` (mirror `FindGeneratedDocumentationDirectory()`'s own already-correct
  `PackageManager`-based resolution).
- **The `migrations.md` entry template itself conflicts with a real, enforced test rule.** The
  generator's own static template text instructs `## <version> (<date, format YYYY-MM-DD>)`, but
  `McpDocumentationGeneratorsTests.GeneratedDocumentsContainNoMachinePathOrRealisticDate` forbids
  any `\d{4}-\d{2}-\d{2}`-shaped text anywhere in a generated document. This tension was invisible
  until this gate became the first session to ever add a real entry (`migrations.md`'s own "None
  yet" text had never been replaced before). Worked around here by citing the shipping card instead
  of a calendar date (`## Unreleased (shipped in \`P7-009\`)`); not fixed at the template/test level
  — a small, disclosed inconsistency for a future documentation-tooling pass, not spun into its own
  card given its low severity.

## Pre-existing, unrelated to Phase 7 (carried forward unchanged)

- `P0-005` (Windows validation CI, self-hosted Unity runner) remains `Review` — the runner has never
  picked up a queued job, reconfirmed live multiple times this week (most recently during `P7-015`).
- `P0-006` (Phase 0 integration gate) and `P1-019` (Phase 1 independent integration gate) remain
  `Blocked`, downstream of the same runner gap.
- `AIBT.Mcp`'s external `dotnet` server process (`MCP~/Server/`) and its own client-config/setup UX
  remain the disclosed scope from `ADR-P6-001`.
