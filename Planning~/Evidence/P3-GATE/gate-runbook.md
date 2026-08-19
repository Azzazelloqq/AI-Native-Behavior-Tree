# P3-013 gate runbook (as executed)

Executed 2026-08-19 against candidate commit
`4700b22e4a17de5d8c118c5d22dfb271a04177fc`, Unity `6000.5.8f1`.

Unlike `P2-025`'s runbook (written before that gate ran), this document
records the commands actually executed and their actual results, since this
gate ran in one continuous session immediately after P3-011 landed.

## 1. Clean snapshot

```powershell
git clone "<AIBT repo root>" "<scratchpad>/aibt-clean-clone"
git -C "<scratchpad>/aibt-clean-clone" status --porcelain   # empty
git -C "<scratchpad>/aibt-clean-clone" diff --check          # empty
git -C "<scratchpad>/aibt-clean-clone" rev-parse HEAD         # 4700b22e4a17de5d8c118c5d22dfb271a04177fc
```

## 2. Static and schema verification

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File './Tools~/Verification/Verify-Static.ps1'
```

Result: 6 schemas, 64 work items, passed.

## 3. Detached harness

A fresh, otherwise-empty Unity project was created at `<scratchpad>/aibt-harness`
with only:

- `ProjectSettings/ProjectVersion.txt` set to `6000.5.8f1` (copied from the host
  project, unmodified).
- `Packages/manifest.json` referencing `com.azzazello.aibt` via a local `file:`
  path to the clean clone above, plus `com.unity.burst 1.8.29`,
  `com.unity.collections 6.5.0`, `com.unity.nuget.newtonsoft-json 3.2.2`
  (`AIBT`'s own declared `package.json` dependencies, listed explicitly rather
  than relying on transitive resolution), `com.unity.test-framework 1.7.0`, and
  the `jsonserialize`/`uielements`/`imgui` engine modules; `"testables":
  ["com.azzazello.aibt"]` so the package's own `Tests/` assemblies run.

This project contains nothing from the host `Modules` project -- no
Disposable, Config, LightDI, BehaviourTree, LocalSaveSystem, RootPattern,
AddressableAssets samples, or any other third-party package/asset.

## 4. Unity compile

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File './Tools~/Verification/Run-UnityCompile.ps1' `
  -UnityPath 'C:\Program Files\Unity\Hub\Editor\6000.5.8f1\Editor\Unity.exe' `
  -ProjectPath '<scratchpad>/aibt-harness'
```

Result: exit code 0, "Unity compile validation passed."

## 5. Full detached EditMode regression

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File './Tools~/Verification/Run-UnityTests.ps1' `
  -UnityPath 'C:\Program Files\Unity\Hub\Editor\6000.5.8f1\Editor\Unity.exe' `
  -ProjectPath '<scratchpad>/aibt-harness' -Mode EditMode -Scope Full
```

Result: 953/953 passed, 0 failed, 0 skipped. Results XML SHA-256
`9855e2c158a78650b4b2d5b65f75ce4d6fb6888650047ae1ce2b4b3f0f44b415`. Covers the
full Phase 1 + Phase 2 + Phase 3 regression, since the harness contains the
whole package.

`AIBT.Tests.Editor.Layout.LayoutSemanticIsolationTests`'s two tests
(`P3-007`) are confirmed `Passed` individually within this run, satisfying
this card's specific re-run-against-the-committed-snapshot requirement.

## 6. Public API surface

A throwaway `-executeMethod` reflection dump (same technique as
`Tools~/Verification/P2/Audit/Get-PublicApi.ps1`, extended to also cover
`AIBT.Editor` since Phase 3 gave it a real public surface) ran against the
same harness:

```powershell
Unity.exe -batchmode -nographics -projectPath '<scratchpad>/aibt-harness' `
  -executeMethod PublicApiDumpP3.Run -aibtPublicApiOutput '<out>/public-api-p3.txt' -quit
```

Result: `AIBT_P3_PUBLIC_API_OK|assemblies=3|types=382|members=1994|sha256=372442bac76bfa7fff50f525d282c97a703fd2ffbc56bbf670a9002e5e4bea04`.
Copied into this directory as `public-api.txt`/`public-api.sha256`. The
throwaway dump script itself lived only in the disposable harness project,
never in the committed repository.

## 7. Assembly dependency and forbidden-token audit

Enumerated `Runtime/AIBT.Runtime.asmdef`, `Authoring/AIBT.Authoring.asmdef`,
`Editor/AIBT.Editor.asmdef` directly; grepped `Runtime/` and `Authoring/` for
`using UnityEditor`, `Unity.Entities`, `UnityMCP`, `OpenAI`, `Anthropic` (no
matches). Recorded in `assembly-dependencies.json`.

## 8. Diff and cleanliness check

```powershell
git status --porcelain   # only the new, then-uncommitted Planning~/Evidence/P3-GATE/
git diff --check          # empty
git rev-parse HEAD         # unchanged at 4700b22e4a17de5d8c118c5d22dfb271a04177fc throughout
```

## 9. P3-012 measurement-claim audit

Read `Benchmarks~/Platform/Editor/pilot-results.json` and
`Planning~/Evidence/P3-012/README.md` against this gate's own claim
discipline: confirmed both record measurements and an explicit "degraded at
1000/2000 nodes" read without introducing a shipped default, threshold, or
supported-size claim.

## Artifacts not committed

The clean clone, the harness project, its `Library/`/`Temp/`, the raw NUnit
XML, and the throwaway public-API-dump script all lived under the session
scratchpad, outside the repository, and were not copied into this directory.
Only `public-api.txt`/`.sha256` (small, meaningful baseline artifacts, per
`P2-GATE`'s own precedent) and this directory's own markdown/JSON summaries
are committed.
