# P5-010 gate runbook (as executed)

Executed 2026-08-27 against candidate commit
`42a32eab7953944823401eccb40b8b60a5c94bfd`, Unity `6000.5.8f1`. Mirrors
`P4-009`'s own runbook shape.

## 1. Clean snapshot

```bash
git clone "<AIBT repo root>" "<scratchpad>/aibt-clean-clone-p5gate"
git -C "<scratchpad>/aibt-clean-clone-p5gate" status --porcelain   # empty
git -C "<scratchpad>/aibt-clean-clone-p5gate" rev-parse HEAD         # 42a32eab7953944823401eccb40b8b60a5c94bfd
```

## 2. Static and schema verification

```powershell
./Tools~/Verification/Verify-Static.ps1
```

Result: 6 schemas, 83 work items, passed.

## 3. Detached harness

A fresh, otherwise-empty Unity project was created at `<scratchpad>/aibt-harness-p5gate`
with:

- `ProjectSettings/ProjectVersion.txt` set to `6000.5.8f1` (copied from the
  host project, unmodified).
- An empty `Assets/` directory (per `P4-009`'s own recorded gotcha: Unity's
  `-projectPath` validation silently falls back to a relative interpretation
  when `Assets/` does not already exist, even for a fully-qualified path).
- `Packages/manifest.json` referencing `com.azzazello.aibt` via a local
  `file:` path to the clean clone above, plus `com.unity.burst 1.8.29`,
  `com.unity.collections 6.5.0`, `com.unity.nuget.newtonsoft-json 3.2.2`
  (`AIBT`'s own declared `package.json` dependencies), `com.unity.test-framework 1.7.0`,
  `com.unity.ugui 2.5.0`, and the `jsonserialize`/`uielements`/`imgui` engine
  modules; `"testables": ["com.azzazello.aibt"]` so the package's own `Tests/`
  assemblies run.

This project contains nothing from the host `Modules` project -- no
Disposable, Config, LightDI, BehaviourTree, LocalSaveSystem, RootPattern,
AddressableAssets samples, or any other third-party package/asset.

## 4. Unity compile

```powershell
./Tools~/Verification/Run-UnityCompile.ps1 -UnityPath 'C:\Program Files\Unity\Hub\Editor\6000.5.8f1\Editor\Unity.exe' -ProjectPath '<scratchpad>/aibt-harness-p5gate'
```

Result: exit code 0, "Unity compile validation passed." 0 `error CS` matches
in the compile log.

## 5. Full detached EditMode regression

```powershell
./Tools~/Verification/Run-UnityTests.ps1 -UnityPath 'C:\Program Files\Unity\Hub\Editor\6000.5.8f1\Editor\Unity.exe' -ProjectPath '<scratchpad>/aibt-harness-p5gate' -Mode EditMode -Scope Full
```

Result: **1089/1089 passed, 0 failed, 0 skipped.** Results XML SHA-256
`537c92ec7c5408c917add8d375447f0144eca4adea3b552be4384c2c1a8b1507`. Covers the
full Phase 1 + Phase 2 + Phase 3 + Phase 4 + Phase 5 regression, since the
harness contains the whole package (1089 = `P4-GATE`'s own 1060 baseline plus
the 29 tests Phase 5 added: `HotReloadProgramIdentityMapTests` 6,
`HotReloadCompatibilityClassifierTests` 8, `HotReloadFullRestartTests` 5,
`HotReloadStateMigrationTests` 4, `HotReloadSchedulerEstimatorResetTests` 1,
`HotReloadPreviewDriverTests` 5). No pre-existing host-project failures
reproduced here, consistent with every prior gate's own finding that they are
host-project noise, not AIBT defects.

Every Phase 5 test fixture confirmed individually `Passed` within this run,
extracted directly from the results XML (not merely cited from an earlier
session), satisfying this card's specific re-run-against-the-committed-snapshot
requirement for `P5-004` through `P5-006`'s state-preservation proofs and
`P5-007`'s scheduler-interaction proof:

- `HotReloadFullRestartTests` (5/5): `Restart_AbortsAnActiveOldInstance_AndReturnsAFreshWorkingMachine`,
  `Restart_FreshMachineIsBoundToTheNewProgramNotTheOld`, `Restart_RejectsNullArguments`,
  `Restart_RepeatedCyclesLeaveNoGrowingManagedState`, `Restart_SkipsAbortForAnAlreadyIdleOldInstance`.
- `HotReloadStateMigrationTests` (4/4): `Migrate_DoesNotMigrateStateForIncompatibleTypeChange`,
  `Migrate_FallsBackToFullRestart_WhenOldInstanceIsActive`,
  `Migrate_PreservesPerNodeInstanceStateAcrossAParameterEdit`, `Migrate_RejectsNullArguments`.
- `HotReloadSchedulerEstimatorResetTests` (1/1):
  `EstimatorKeyedByCompiledProgramIdentity_IsFreshAfterReload_WithNoSpecialCasing`.
- `HotReloadCompatibilityClassifierTests` (8/8), `HotReloadProgramIdentityMapTests` (6/6),
  `HotReloadPreviewDriverTests` (5/5): all individually `Passed`.

`AIBT.Tests.Editor.Layout.LayoutSemanticIsolationTests` (`P3-007`, this gate's
inherited Phase 3 obligation) is also present and passing in the same run.

## 6. Public API surface

A throwaway `-executeMethod` reflection dump (`PublicApiDumpP5`, same
technique as `Tools~/Verification/P2/Audit/Get-PublicApi.ps1`, extended to
also cover `AIBT.Editor` the same way `P3-013`/`P4-009` did) ran against the
same harness:

```bash
Unity.exe -batchmode -nographics -projectPath '<scratchpad>/aibt-harness-p5gate' \
  -executeMethod PublicApiDumpP5.Run -aibtPublicApiOutput '<out>/public-api-p5.txt' -quit
```

Result: `AIBT_P5_PUBLIC_API_OK|assemblies=3|types=391|members=2024|sha256=6e16c87fe69eaac248c1501528dc42960949e98eaea14eedd7ebd1645d261651`.

**Diff against `P4-GATE/public-api.txt`: additive only, no removals.** Phase 5
legitimately adds new public surface (unlike Phase 4, which added zero):
`AIBT.Authoring.HotReloadPreviewDriver`, `AIBT.Authoring.HotReloadPreviewOutcome`,
`AIBT.Editor.HotReload.HotReloadWorkflowWindow`, `AIBT.HotReloadClassificationResult`,
`AIBT.HotReloadCompatibilityClassifier`, `AIBT.HotReloadNodeIdentitySignature`,
`AIBT.HotReloadNodeVerdict`, `AIBT.HotReloadNodeVerdictCategory`,
`AIBT.HotReloadProgramIdentityMap`, plus their members. Every `diff` line is a
`>` addition against the `P4-GATE` baseline; zero `<` removal lines. Copied
into this directory as `public-api.txt`/`.sha256`. The throwaway dump script
itself lived only in the disposable harness project (deleted after running),
never in the committed repository.

## 7. Assembly dependency and forbidden-token audit

Enumerated `Runtime/AIBT.Runtime.asmdef`, `Authoring/AIBT.Authoring.asmdef`,
`Editor/AIBT.Editor.asmdef` directly (unchanged from `P4-GATE`); grepped
`Runtime/` and `Authoring/` for `using UnityEditor`, `Unity.Entities`,
`UnityMCP`, `OpenAI`, `Anthropic` (no matches), and `Editor/` for
`Unity.Entities`, `UnityMCP`, `OpenAI`, `Anthropic` (no matches; `using
UnityEditor` is expected there). Recorded in `assembly-dependencies.json`.
Phase 5's own benchmark harness (`Benchmarks~/Phase5/HotReload/Unity/`) is
excluded from this audit per the tilde-hidden-folder convention every prior
phase's benchmark folders already used.

## 8. Diff and cleanliness check

```bash
git status --porcelain   # empty at the candidate commit; only the new, then-uncommitted Planning~/Evidence/P5-GATE/ and README.md/CHANGELOG.md updates afterward
git rev-parse HEAD         # unchanged at 42a32eab7953944823401eccb40b8b60a5c94bfd throughout verification
```

## 9. OQ-007 resolution audit

Read `Planning~/OPEN_QUESTIONS.md`: `OQ-007` row reads "Resolved, see [ADR
P5-001]", blocking `None`. Read
`Documentation~/decisions/ADR-P5-001-hot-reload-compatibility-model.md`:
`Status: Accepted 2026-08-27`. Confirmed linked from `Documentation~/decisions.md`'s
`AIBT-023` row. All three (open-questions row, ADR status, decisions-index
link) agree.

## 10. Live interactive Editor workflow re-run (`P5-008`)

`P5-008`'s own evidence (`Planning~/Evidence/P5-008/README.md`) already
recorded a live interactive Unity MCP session driving the real
`HotReloadWorkflowWindow` through all three reload strategies (subtree
restart, compatible migration, full-restart fallback) against the working
tree at the time. This gate re-confirms that session's evidence file is
present, complete, and matches this candidate commit's unchanged
`Editor/HotReload/`/`Authoring/HotReload/` source (no `P5-009`/`P5-010` change
touched those files) rather than re-running the live session a second time --
the same "cite unchanged prior live-interactive evidence, re-run only what
changed" pattern `P3-013` used for `P3-009`'s preview evidence.

## 11. Benchmark-claim audit

Read every `Benchmarks~/Phase5/**/README.md`, `Planning~/Evidence/P5-*/README.md`,
`README.md`, and `CHANGELOG.md` against this gate's own claim discipline (see
`claims-inventory.md`). Found `README.md`'s status line and `CHANGELOG.md`'s
`[Unreleased]` section stale (still describing Phases 1-4 as complete and
omitting Phase 5 entirely) -- not an overclaim, but a material staleness gap
in exactly the documents this card's own "Allowed changes" names. Updated
both to reflect Phase 5 completion without introducing any new default,
threshold, or supported-reload-scale claim; diffed against
`claims-inventory.md` afterward to confirm no claim in the updated text
exceeds recorded evidence.

## Artifacts not committed

The clean clone, the harness project, its `Library/`/`Temp/`, the raw NUnit
XML, and the throwaway public-API-dump script all lived under the session
scratchpad, outside the repository, and were not copied into this directory.
Only `public-api.txt`/`.sha256` (small, meaningful baseline artifacts, per
`P2-GATE`/`P3-GATE`/`P4-GATE`'s own precedent) and this directory's own
markdown/JSON summaries are committed.
