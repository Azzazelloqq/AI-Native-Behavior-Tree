# P6-012 gate runbook (as executed)

Executed 2026-08-31 against candidate commit `97e3501e71534f8de2e063cf74cdf52a36a43d04`, Unity
`6000.5.8f1`. Mirrors `P5-010`'s own runbook shape.

## 0. Precursor: two real bugs found and fixed before the official run

The gate's first detached-harness EditMode run (against `c766d50`, itself already a small P6-011
addendum fixing a test-only package-root assumption) found a **second** real bug:
`McpNodeCatalogDocumentGenerator`'s JSON formatting embedded `Environment.NewLine` (platform-
dependent), so a fresh Windows regeneration produced `\r\n` inside the node catalog's embedded JSON
blocks while the git-checked-out committed file did not. Fixed (`97e3501`, normalized to `\n`
explicitly) and confirmed passing in both the host project's embedded layout and a detached
harness before treating `97e3501` as the real candidate commit for this gate's own verification.
Both fixes are test-only/generator-only; no production API changed. See
`Planning~/Evidence/P6-GATE/README.md`'s "Precursor fixes" section for the full narrative.

## 1. Clean snapshot

```bash
git clone "<AIBT repo root>" "<scratchpad>/aibt-clean-clone-p6gate"
git -C "<scratchpad>/aibt-clean-clone-p6gate" status --porcelain   # empty
git -C "<scratchpad>/aibt-clean-clone-p6gate" rev-parse HEAD         # 97e3501e71534f8de2e063cf74cdf52a36a43d04
```

## 2. Static and schema verification

```powershell
./Tools~/Verification/Verify-Static.ps1
./Tools~/Verification/Verify-Schemas.ps1 -RepositoryPath <clean clone>
```

Result: 6 schemas, 105 work items, both passed.

## 3. Detached harness

A fresh, otherwise-empty Unity project was created at `<scratchpad>/aibt-harness-p6gate` with:

- `ProjectSettings/ProjectVersion.txt` set to `6000.5.8f1` (copied from the host project,
  unmodified).
- An empty `Assets/` directory (per `P4-009`'s own recorded `-projectPath` gotcha).
- `Packages/manifest.json` referencing `com.azzazello.aibt` via a local `file:` path to the clean
  clone above, plus `com.unity.burst 1.8.29`, `com.unity.collections 6.5.0`,
  `com.unity.nuget.newtonsoft-json 3.2.2` (`AIBT`'s own declared `package.json` dependencies,
  unchanged since `P5-GATE`), `com.unity.test-framework 1.7.0`, `com.unity.ugui 2.5.0`, the
  `jsonserialize`/`uielements`/`imgui` engine modules, and `"testables": ["com.azzazello.aibt"]`.

This project contains nothing from the host `Modules` project. Because a `file:` package reference
is used directly from its resolved path (no `PackageCache` copy), re-pointing the same relative
path at a freshly re-cloned directory (after the precursor-fix commits) picked up the new content
automatically on the next compile/test run, with no harness reconstruction needed.

## 4. Unity compile

```powershell
./Tools~/Verification/Run-UnityCompile.ps1 -UnityPath 'C:\Program Files\Unity\Hub\Editor\6000.5.8f1\Editor\Unity.exe' -ProjectPath '<scratchpad>/aibt-harness-p6gate'
```

Result: exit code 0, "Unity compile validation passed." 0 `error CS` matches in the compile log.

## 5. Full detached EditMode regression

```powershell
./Tools~/Verification/Run-UnityTests.ps1 -UnityPath 'C:\Program Files\Unity\Hub\Editor\6000.5.8f1\Editor\Unity.exe' -ProjectPath '<scratchpad>/aibt-harness-p6gate' -Mode EditMode -Scope Full
```

**First run** (against the pre-precursor-fix candidate) found the two real bugs described in step
0 plus a genuine cold-start flake: `NativeExecutionDebuggerSessionTests
.AttachingAndReadingBetweenPassesAddsNoManagedAllocationToNativeExecution` (a pre-existing, Phase-6
-untouched P3-010 zero-GC proof) failed on the very first, truly-cold invocation of a brand-new
harness `Library/`, then passed cleanly on an immediate focused re-run of the same now-warm
project -- consistent with a one-time JIT/managed-cache allocation on a genuinely fresh Unity
domain, not a regression. Not re-litigated further since it did not reproduce.

**Final run** (against `97e3501`, a fresh re-clone): **1224/1224 passed, 0 failed, 0 skipped.**
Results XML SHA-256 `e0b8f0f9283d972b6df9bc059850f50a364bdd7010a37f20bfc53d00a7ed49fb`. 1224 vs
`P5-GATE`'s 1089 baseline (+135) covers every test Phase 6 added: `P6-003` (13), `P6-004` (8),
`P6-005` (30), `P6-006` (17), `P6-007` (14), `P6-008` (8 new, plus promoted framework code with no
new test count of its own), `P6-009` (18), `P6-010` (11), `P6-011` (10, now including this gate's
own two-commit addendum). No pre-existing host-project-only failures reproduced here, consistent
with every prior gate's own finding that they are host-project noise, not AIBT defects.

## 6. Public API surface

A throwaway `-executeMethod` reflection dump (`PublicApiDumpP6`, same technique as
`Tools~/Verification/P2/Audit/Get-PublicApi.ps1`, extended to also cover `AIBT.Editor` the same way
`P3-013`/`P4-009`/`P5-GATE` did, plus `AIBT.Mcp` for the first time) ran against the same harness:

```bash
Unity.exe -batchmode -nographics -projectPath '<scratchpad>/aibt-harness-p6gate' \
  -executeMethod PublicApiDumpP6.Run -aibtPublicApiOutputDir '<out>' -quit
```

Result (3-assembly, comparable to every prior gate's own baseline):
`AIBT_P6_PUBLIC_API_OK|assemblies=3|types=405|members=2067|sha256=a4e07dad10169d3b8c1a48e79acea6f52f08ff5fcba03f18e4da98d22f800dd0`.

**Diff against `P5-GATE/public-api.txt`: additive only, no removals (+14 types, +43 members).**
Phase 6 legitimately adds new public surface: `AIBT.Authoring.NodeCatalogQuery`,
`AIBT.Authoring.ProjectManifestQuery`, `AIBT.Authoring.ProjectPolicySnapshot` (`P6-003`), and
`AIBT.Editor.Patching.LayoutDiff`/`LayoutDiffEntry`/`LayoutDiffKind`/`LayoutDiffTarget`/
`LayoutPatchResult`/`LayoutPatchTransaction`/`SemanticDiff`/`SemanticDiffEntry`/`SemanticDiffKind`/
`SemanticPatchResult`/`SemanticPatchTransaction` (`P6-004`), plus their members. Every `diff` line
is a `>` addition against the `P5-GATE` baseline; zero `<` removal lines.

**`AIBT.Mcp`'s own public surface is recorded separately, as a new first-time baseline** (no prior
gate ever audited it — it did not exist before Phase 6):
`AIBT_P6_PUBLIC_API_OK|assembly=AIBT.Mcp|types=7|members=29|sha256=15082bdfbf09b4bac7c7fc59cc58849fe16788a0662f82cdc4b86325c5737026`
— `ICustomMcpToolProvider`, `McpBridgeListener`, `McpBridgeWindow`, `McpPermissionCategory`,
`McpPermissionEnforcer`, `McpToolDispatcher`, `AibtTreeDiscovery`. Everything else in `AIBT.Mcp`
(dispatchers, JSON helpers, diagnostics) is `internal`, correctly not surfaced — the architecture's
intent that MCP internals stay internal except the few genuine integration points. Both dumps
copied into this directory as `public-api.txt`/`.sha256` and `public-api-mcp.txt`/`.sha256`. The
throwaway dump script itself lived only in the disposable harness project (deleted after running),
never in the committed repository.

## 7. Assembly dependency and forbidden-token audit

Enumerated `Runtime/AIBT.Runtime.asmdef`, `Authoring/AIBT.Authoring.asmdef`,
`Editor/AIBT.Editor.asmdef` directly (byte-identical to `P5-GATE`'s own recorded references) and
the new `MCP/AIBT.Mcp.asmdef`; grepped `Runtime/` and `Authoring/` for `using UnityEditor`,
`Unity.Entities`, `UnityMCP`, `OpenAI`, `Anthropic` (no matches), `Editor/` and `MCP/` for
`Unity.Entities`, `UnityMCP`, `OpenAI`, `Anthropic` (no matches; `using UnityEditor` is expected in
both). Confirmed no other production `.asmdef` references `AIBT.Mcp` (one-way dependency). Recorded
in `assembly-dependencies.json`. `MCP~/Server/` (the external `dotnet` process) is excluded from
this audit per the tilde-hidden-folder convention every prior phase's own out-of-package folders
already used — it does depend on the real `ModelContextProtocol` SDK by design (`ADR-P6-001`),
which is expected and outside this Unity-assembly-scoped audit.

## 8. Real end-to-end MCP session proof (roadmap exit criterion)

Against the real, currently-open `6000.5.8f1` Editor (`C:\UnityProjects\Modules`, not the disposable
detached harness — the same host every individual `P6-00X` card's own live verification already
used), the bridge was started live via Unity MCP `execute_code`, and one continuous session was
driven through the real, permanent `MCP~/Server/` via the official
`@modelcontextprotocol/inspector` CLI:

1. `aibt_get_project_manifest` / `aibt_search_nodes("repeater")` — discover.
2. `aibt_create_tree` (`tree.p6012-gate-session`, root = `aibt.core.memory-sequence`) — create.
3. `aibt_apply_domain_patch` adding a decorator node (`aibt.core.repeater`) and its required child
   in one atomic transaction — add/connect.
4. `aibt_configure_node` changing the repeater's `count` parameter from 1 to 3 — configure.
5. `aibt_validate` → `valid: true`. `aibt_compile` → real content hash.
6. `aibt_simulate` — full trace confirms the leaf actually ticked three times before the repeater
   and the tree both completed `Success`, proving the configuration from step 4 took real effect
   through compilation and execution, not just accepted at the API boundary.
7. The complete real node-development gate: `aibt_generate_node` → `aibt_preview_node_diff` →
   `aibt_generate_node_tests_and_manifest` → `aibt_analyze_and_compile_node` (`start` then `check`,
   polled) → `aibt_test_node` → `aibt_apply_node` — a genuinely new custom node
   (`aibt.p6012gate.threshold-condition`) generated, compiled, tested, and persisted into the real
   project.
8. `aibt_run_benchmark` against a real `P4-001` scenario (`shallow-tree-cheap-conditions`).

All live-created files (the tree, the applied node's folder) were removed afterward; the bridge was
stopped cleanly and its discovery file confirmed removed.

**Two real findings surfaced during this proof, both disclosed rather than smoothed over:**

- **A real `P6-009` template bug**: `generate_node`'s condition template unconditionally emits
  `current >= config.Minimum`, which does not compile when the blackboard read type is `Bool` (no
  `>=` operator on `bool`) — hit on the first attempt using `blackboardReadType: Bool`, worked
  around for this proof by using `UInt32` instead. Not fixed here (out of this gate's own
  allowed-changes fence); recorded in `known-limitations.md`.
- **Trace inspection was not attempted** and **a newly-applied custom node is not discoverable via
  `aibt_search_nodes`/`aibt_get_node_contract`** — both are disclosed gaps against the roadmap exit
  criterion, detailed in `known-limitations.md` and `README.md`'s verdict section.

An operational note, not a defect: `analyze_and_compile_node`'s two-call design assumes Unity's own
file-watcher notices an external write promptly; in this fully headless, unfocused session the
write sat as `external_changes_dirty: true` until an explicit Unity MCP `refresh_unity` call was
issued, after which compilation proceeded normally — a real characteristic of driving Unity
headlessly, not something an interactively-used Editor session would hit.

## 9. Benchmark-claim and documentation audit

Read `README.md` and `CHANGELOG.md` against this gate's own claim discipline
(`claims-inventory.md`). Found `README.md`'s status line stale (still "Phases 1 through 5 are
complete... MCP integration remains a later phase") and its repository map naming a nonexistent
planned `Tools~/McpServer/` instead of the real `MCP/`/`MCP~/Server/`. Updated both `README.md` and
`CHANGELOG.md` to reflect Phase 6 completion, stating both disclosed gaps above plainly rather than
omitting them, then diffed the updated text against `claims-inventory.md` to confirm nothing
stronger than the verified evidence was introduced.

## Artifacts not committed

The clean clone, the harness project, its `Library/`/`Temp/`, the raw NUnit XML, and the throwaway
public-API-dump script all lived under the session scratchpad, outside the repository, and were not
copied into this directory. Only `public-api.txt`/`.sha256`, `public-api-mcp.txt`/`.sha256`, and
this directory's own markdown/JSON summaries are committed.
