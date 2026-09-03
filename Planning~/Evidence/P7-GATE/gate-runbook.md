# P7-016 gate runbook (as executed)

Executed 2026-09-03 against candidate commit `eedeb3c8408714ed5e5b3ee773a7a76c258e9864` (the
`P7-015` commit — the last committed state before this gate's own review began), Unity `6000.5.8f1`.
Mirrors `P6-012`'s own runbook shape.

## 0. Pre-harness review, before any mechanical verification ran

Before touching the harness, this gate re-verified `work-items.json`/task-card consistency for every
`P7-00X` card and found real bookkeeping drift (4 cards with accepted evidence but a stale `Draft`
task-card `Status`) and one genuinely open acceptance criterion (`P7-001`'s stability proposal never
got a recorded owner decision). Both fixed/resolved before the mechanical run — see
`Planning~/Evidence/P7-GATE/README.md`'s own summary and `p7-001-stability-decision.md`. One small,
disclosed documentation fix was also made and live-verified in the host Editor before the detached
run: `Documentation~/generated/migrations.md`'s "MCP surface migrations" log was retroactively
corrected to record a real, previously-undocumented breaking change (`test-node`'s `scopeNote` field
removal, `P7-009`) — `McpDocumentationGeneratorsTests` (11/11) passed live against this fix in the
host project before it was folded into the same commit as everything else below. None of this
touches the mechanical verification below, which runs against the immutable candidate commit
(`eedeb3c`, predating these edits) — the doc/bookkeeping layer is verified independently, not
re-proven through the detached harness, since it changes zero runtime-affecting production code.

## 1. Clean snapshot

```bash
git clone "C:/UnityProjects/Modules/Assets/AIBT" "<scratchpad>/aibt-clean-clone-p7gate"
git -C "<scratchpad>/aibt-clean-clone-p7gate" status --porcelain   # empty
git -C "<scratchpad>/aibt-clean-clone-p7gate" rev-parse HEAD        # eedeb3c8408714ed5e5b3ee773a7a76c258e9864
```

## 2. Static and schema verification

```powershell
./Tools~/Verification/Verify-Static.ps1 -RepositoryPath <clean clone>
./Tools~/Verification/Verify-Schemas.ps1 -RepositoryPath <clean clone>
```

Result: both passed — 6 schemas, 122 work items (the candidate commit's own count, before this
gate's own 5 new follow-up cards were added on top).

## 3. Detached harness

Fresh, otherwise-empty Unity project at `<scratchpad>/aibt-harness-p7gate`: `ProjectVersion.txt`
copied unmodified from `Planning~/Evidence/P0-001/Harness/ProjectSettings/`, empty `Assets/`,
`Packages/manifest.json` referencing `com.azzazello.aibt` via `file:` at the clean clone plus
`com.unity.burst 1.8.29`/`com.unity.collections 6.5.0`/`com.unity.nuget.newtonsoft-json 3.2.2`/
`com.unity.test-framework 1.7.0` (matching `.github/workflows/validation.yml`'s own proven-working
manifest exactly), `"testables": ["com.azzazello.aibt"]`. Contains nothing from the host `Modules`
project.

## 4. Unity compile

```powershell
./Tools~/Verification/Run-UnityCompile.ps1 -UnityPath 'C:\Program Files\Unity\Hub\Editor\6000.5.8f1\Editor\Unity.exe' -ProjectPath '<harness>'
```

Result: exit code 0, "Unity compile validation passed."

## 5. Full detached EditMode regression

```powershell
./Tools~/Verification/Run-UnityTests.ps1 -UnityPath 'C:\Program Files\Unity\Hub\Editor\6000.5.8f1\Editor\Unity.exe' -ProjectPath '<harness>' -Mode EditMode -Scope Full
```

**Result: 1269/1270 passed, 1 failed, 0 skipped** (up from `P6-GATE`'s 1224/1224). The one failure
is real, reproducible, and root-caused, not fixed inside this gate per its own Forbidden-changes
clause:

- `AIBT.Tests.Editor.Documentation.McpDocumentationGeneratorsTests
  .GeneratedDocumentationRegeneratesToExactlyTheCommittedFiles` — `api-reference-runtime.md`
  differs between the committed file (generated inside the host project, where type-`<summary>`
  inlining silently works) and a fresh regeneration inside this detached harness (where it silently
  does not). Root cause: `McpApiReferenceGenerator.CollectTypeSummaries()` hardcodes
  `Application.dataPath + "/AIBT"` as its source-scan root, which only resolves correctly when AIBT
  is embedded directly under a host project's `Assets/` — for any real `file:`/registry UPM
  consumer (exactly this gate's own harness technique, and how a real end user would consume this
  package), the directory does not exist, so the correlator returns nothing and every generated
  `api-reference-*.md` silently loses 100% of its inlined type summaries with no error. This is the
  first time this generator has ever been exercised outside the host project. Spun off as `P7-021`.
  See `known-limitations.md`.

A separate false alarm, corrected during analysis rather than reported as a finding: an initial
per-type diff of the raw public-API dump (see step 6) appeared to show 5 "removed" member lines
and a large block of unrelated members "misattributed" to `AIBT.TreeInstanceId`/
`AIBT.Mcp.McpToolDispatcher`. Investigating the dump file's own actual format (traced directly, not
assumed) found this is expected: `Get-FullPublicApi.ps1`'s dump lists every `TYPE` header in one
block, then every unique member *signature* in a second, separately-sorted, deduplicated block —
member lines are never associated with a specific type in this file format, by original design, not
by a printing bug. The apparent "5 removed" lines were solely an artifact of comparing `P6-GATE`'s
two separately-generated files (each internally deduplicated per run) against this gate's single
combined 4-assembly run (deduplicated once, globally) — not a real change. Confirmed by checking the
type-level diff (step 6) independently, which is unaffected by this format quirk and shows a clean,
purely additive result.

## 6. Public API surface

`Tools~/Verification/P7/Audit/Get-FullPublicApi.ps1` (unmodified, run against the host project
directly rather than the clean clone — equivalent for this purpose, since every uncommitted edit at
run time was either non-public-API documentation/bookkeeping or an `internal` generator method,
confirmed by inspection before relying on this shortcut):

```text
AIBT_PUBLIC_API_OK|assemblies=4|types=425|members=2130|sha256=03344454eeed509027b0d52d1855c9731f60ec4cd3e2d59bc516c2ef3e89a460
```

**Diff against `P6-GATE`'s own combined baseline (405+7=412 types, 2067+29=2096 members):
+13 types, +34 members, confirmed purely additive** — a direct type-set comparison
(`comm -23`/`comm -13` against sorted `TYPE` lines only, immune to the member-attribution format
quirk above) shows **zero type removals**, 13 new types: `AIBT.Authoring.IReferenceLeafBehaviorProvider`,
`AIBT.Authoring.Migration.{DocumentMigrator,NodeFieldAddition,NodeFieldRename,NodeMigrationChange,
NodeMigrationOutcome,NodeMigrationRegistry,NodeMigrationRule}` (`P7-006`),
`AIBT.Editor.Migration.MigrationNotificationWindow` (`P7-006`), `AIBT.IReferenceLeafBehavior`,
`AIBT.ReferenceLeafContext` (`P7-008`), `AIBT.NodeAbortReason`, `AIBT.NodeExitReason` (`P7-008`/`P7-012`).
Every addition traces to a real, already-accepted Phase 7 card; nothing unexplained. Saved as
`public-api.txt`/`.sha256` in this directory — a single combined 4-assembly dump going forward,
superseding `P6-GATE`'s own split `public-api.txt`+`public-api-mcp.txt` convention (both assemblies
have been audited together since `P7-001`).

## 7. Assembly-dependency and forbidden-token audit

Same greps `P6-GATE` ran, against the clean clone directly. All 4 production `.asmdef` files'
`references`/`includePlatforms` confirmed byte-identical to `P6-GATE`'s own recorded baseline — zero
drift across all of Phase 7, no new production assembly introduced. See `assembly-dependencies.json`.

## 8. `scope.md`'s "Release criteria for 1.0", checked item-by-item

See `scope-release-criteria-checklist.md` — 5 of 7 fully met, 2 partially met with real, disclosed
gaps (tree-format v2 promotion; production Play-mode host).

## 9. `P7-001`'s stability proposal — recorded owner decision

Presented live to the owner during this gate's own session; see `p7-001-stability-decision.md` for
the full record and the 3 new follow-up cards it produced (`P7-018`, `P7-019`, `P7-020`).

## 10. README.md / CHANGELOG.md staleness

Both stopped at the Phase 6 paragraph/bullet — no Phase 7 section existed. Updated, checked against
`claims-inventory.md` to confirm nothing stronger than verified evidence was introduced.

## Artifacts not committed

The clean clone, the harness project, its `Library/`/`Temp/`, the raw NUnit XML, and the throwaway
public-API-dump project all lived under the session scratchpad, outside the repository, and were not
copied into this directory.
