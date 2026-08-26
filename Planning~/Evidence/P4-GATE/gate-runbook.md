# P4-009 gate runbook (as executed)

Executed 2026-08-27 against candidate commit
`9b9744443d9bbcaa3d4b3341343aeda818a26770`, Unity `6000.5.8f1`. Mirrors
`P3-013`'s own runbook shape.

## 1. Clean snapshot

```powershell
git clone "<AIBT repo root>" "<scratchpad>/aibt-clean-clone"
git -C "<scratchpad>/aibt-clean-clone" status --porcelain   # empty
git -C "<scratchpad>/aibt-clean-clone" rev-parse HEAD         # 9b9744443d9bbcaa3d4b3341343aeda818a26770
```

## 2. Static and schema verification

```powershell
./Tools~/Verification/Verify-Static.ps1
```

Result: 6 schemas, 73 work items, passed. (Run both against the clean clone
and, earlier in this session, against the working tree before/after the
`P4-004` addendum commit -- identical result.)

## 3. Detached harness

A fresh, otherwise-empty Unity project was created at `<scratchpad>/aibt-harness`
with:

- `ProjectSettings/ProjectVersion.txt` set to `6000.5.8f1` (copied from the
  host project, unmodified).
- An empty `Assets/` directory. **Note for future gates**: Unity's
  `-projectPath` validation silently falls back to treating the given path as
  relative to the calling process's working directory (producing a
  `Couldn't set project path to: <cwd>/<given path>` failure) when `Assets/`
  does not already exist, even though the path itself is fully qualified.
  `P3-013`'s own runbook did not record needing this; either that harness
  happened to get an `Assets/` folder some other way, or Unity's validation
  order changed. Recorded here so the next gate does not lose time
  rediscovering it.
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
./Tools~/Verification/Run-UnityCompile.ps1 -UnityPath 'C:\Program Files\Unity\Hub\Editor\6000.5.8f1\Editor\Unity.exe' -ProjectPath '<scratchpad>/aibt-harness'
```

Result: exit code 0, "Unity compile validation passed." 0 `error CS` matches
in the compile log.

## 5. Full detached EditMode regression

```powershell
./Tools~/Verification/Run-UnityTests.ps1 -UnityPath 'C:\Program Files\Unity\Hub\Editor\6000.5.8f1\Editor\Unity.exe' -ProjectPath '<scratchpad>/aibt-harness' -Mode EditMode -Scope Full
```

Result: **1060/1060 passed, 0 failed, 0 skipped.** Results XML SHA-256
`3a4e7e6c58c34b24665c07b5a6379d57feaf906864345bc5626866d6dfb416e5`. Covers the
full Phase 1 + Phase 2 + Phase 3 + Phase 4 regression, since the harness
contains the whole package. The 3 failures seen inside the host `Modules`
project (2x `AIBT.Tests.CodeGen.Generation.GeneratedArtifactContractTests`,
1x `LocalSaveSystem.Tests.SaveStoreTests.SaveStore_AutoSave_WritesToDisk`,
recorded as pre-existing/unrelated in every prior P3/P4 evidence file) did
not reproduce here, confirming they were host-project noise, not AIBT
defects -- the same pattern `P3-013` found for its own 3 host failures.

`AIBT.Tests.Runtime.NativeExecution.Scheduling.NativePipelinedPhaseControllerTests`
(`P4-003`'s equivalence proof, including
`BatchPartitionsProduceTheSamePerInstanceAtomicOrderAcrossAPipelineStageBoundary`)
and `AIBT.Tests.Runtime.NativeExecution.Scheduling.Auto.NativeAutoSelectionTests`/
`NativeAutoAdaptiveSelectionTests` (`P4-005`'s determinism proofs, including
`RepeatedSelectionWithIdenticalInputsIsDeterministic`,
`RepeatedAdaptiveSelectionWithIdenticalInputsIsDeterministic`, and
`SelectionIsDeterministicAndReproducibleForEveryP4001Scenario`'s 6 case
variants) are confirmed individually `Passed` within this run, satisfying
this card's specific re-run-against-the-committed-snapshot requirement --
extracted directly from the results XML, not merely cited from an earlier
session.

`AIBT.Tests.Editor.Layout.LayoutSemanticIsolationTests` (`P3-007`, this
gate's inherited Phase 3 obligation) is also present and passing in the same
run.

## 6. Public API surface

A throwaway `-executeMethod` reflection dump (same technique as
`Tools~/Verification/P2/Audit/Get-PublicApi.ps1`, extended to also cover
`AIBT.Editor` the same way `P3-013` did) ran against the same harness:

```powershell
Unity.exe -batchmode -nographics -projectPath '<scratchpad>/aibt-harness' `
  -executeMethod PublicApiDumpP4.Run -aibtPublicApiOutput '<out>/public-api-p4.txt' -quit
```

Result: `AIBT_P4_PUBLIC_API_OK|assemblies=3|types=382|members=1994|sha256=372442bac76bfa7fff50f525d282c97a703fd2ffbc56bbf670a9002e5e4bea04`.
**Byte-identical to `P3-GATE/public-api.txt`** -- confirmed via `diff`. Phase 4
added zero new public API surface (expected: Phase 4's work is entirely
internal scheduling/native-execution machinery, none of it exposed as new
public types). Copied into this directory as `public-api.txt`/`.sha256`. The
throwaway dump script itself lived only in the disposable harness project
(deleted after running), never in the committed repository.

## 7. Assembly dependency and forbidden-token audit

Enumerated `Runtime/AIBT.Runtime.asmdef`, `Authoring/AIBT.Authoring.asmdef`,
`Editor/AIBT.Editor.asmdef` directly; grepped `Runtime/` and `Authoring/` for
`using UnityEditor`, `Unity.Entities`, `UnityMCP`, `OpenAI`, `Anthropic` (no
matches), and `Editor/` for `Unity.Entities`, `UnityMCP`, `OpenAI`,
`Anthropic` (no matches; `using UnityEditor` is expected there). Recorded in
`assembly-dependencies.json`. Unchanged from `P3-GATE`'s own audit -- Phase 4
touched no asmdef reference list.

## 8. Diff and cleanliness check

```powershell
git status --porcelain   # empty at the candidate commit; only the new, then-uncommitted Planning~/Evidence/P4-GATE/ and README.md/CHANGELOG.md updates afterward
git rev-parse HEAD         # unchanged at 9b9744443d9bbcaa3d4b3341343aeda818a26770 throughout verification
```

## 9. OQ-006 resolution audit

Read `Planning~/OPEN_QUESTIONS.md`: `OQ-006` row reads "Resolved: rejected,
see [ADR P4-007]", blocking `None`. Read
`Documentation~/decisions/ADR-P4-007-runtime-autotuning-resolution.md`:
`Status: Accepted 2026-08-21`. Confirmed linked from `Documentation~/decisions.md`'s
`AIBT-013` row. All three (open-questions row, ADR status, decisions-index
link) agree.

## 10. Benchmark-claim audit

Read every `Benchmarks~/Phase4/**/README.md`, `Planning~/Evidence/P4-*/README.md`,
`README.md`, and `CHANGELOG.md` against this gate's own claim discipline
(see `claims-inventory.md`). Found `README.md`'s status line and
`CHANGELOG.md`'s `[Unreleased]` section stale (still describing `P2-025` as
"in progress" and omitting Phase 3/4 entirely) -- not an overclaim, but a
material staleness gap in exactly the documents this card's own "Allowed
changes" names. Updated both to reflect Phases 1-4 completion without
introducing any new default, threshold, or supported-hardware-class claim;
diffed against `claims-inventory.md` afterward to confirm no claim in the
updated text exceeds recorded evidence.

## Artifacts not committed

The clean clone, the harness project, its `Library/`/`Temp/`, the raw NUnit
XML, and the throwaway public-API-dump script all lived under the session
scratchpad, outside the repository, and were not copied into this directory.
Only `public-api.txt`/`.sha256` (small, meaningful baseline artifacts, per
`P2-GATE`/`P3-GATE`'s own precedent) and this directory's own markdown/JSON
summaries are committed.
