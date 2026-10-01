# P7-040: V1 native hot-reload Tree blackboard

Owner approved the narrowed scope and implementation on 2026-09-19. Base AIBT commit: `3f04417`.
Implementation is uncommitted in the live Modules checkout; unrelated parent changes are preserved.

## Contract and implementation

- Fresh hot-reload instances initialize Tree slots from existing compiled defaults after range
  validation. The general V1 arena factory and V2 creation path are unchanged.
- Migration preserves compatible built-in live bytes by stable key, using separate old/new offsets.
  Type/version, enum contract, size and alignment must match a known built-in layout. A changed
  default does not replace a compatible live value, including a value never written since creation.
- New/incompatible keys retain new defaults; removed keys disappear. Registered types default
  conservatively because the V1 input contains no registered schema/equality proof.
- Tree storage is instance-wide: subtree exclusions do not reset it. Full restart uses defaults.
- No public API, persisted format, classifier or node/frame lifecycle change. The existing
  Parallel/observer/budget-state migration limitations disclosed by P7-029 remain.

The initial card's proposed P7-039 test is explicitly excluded by the owner-approved narrowing:
production external writes target a separate `GeneratedTreeDispatchAdapterV2` allocation. These
tests exercise the actual `NativeHotReloadInstance` arena and native lifecycle dispatch protocol;
they do not claim generated production-host reload or typed V2 blackboard access.

## Verification

At verification time, the open Modules Editor was Unity `6000.7.0a6`, with pre-existing compiler errors in
`Assets/LocalizedDomain/Runtime/TextMeshPro/LocalizedTMPText.cs` (obsolete `enableWordWrapping` and
`enableKerning`). An isolated project uses the same Editor and this checkout as the local UPM
package, with `com.azzazello.aibt` in `testables` and test-framework `1.9.0`.

The pre-fix focused run discovered 14 new tests: 1 passed, 13 failed with zero bytes where the
expected live/default values were nonzero. The existing source remained unmodified for that run.
Two further byte-complete regressions were added afterward, with declared node access tables.

Results were completed on 2026-09-19; evidence and final self-review were closed on 2026-09-20.
See [machine-readable results](verification-results.json) and [focused NUnit output](focused-results.xml).

| Run | Passed / total | Failed | Skipped |
| --- | --- | --- | --- |
| Pre-fix new tests | 1 / 14 | 13 | 0 |
| Final native hot-reload focused suite | 38 / 38 | 0 | 0 |
| Full isolated package, with fix | 1453 / 1454 | 1 | 0 |
| Full isolated package, original 3f04417 | 1437 / 1438 | 1 | 0 |

All 16 new tests pass. Both full runs fail only
`GeneratedArtifactContractTests.EmittedMetadata_MaterializesConsumerDescriptorAndCompilesCanonicalV2Artifact`,
with the same expected/actual generated-artifact hash pair recorded in JSON. Baseline was a clean
detached worktree of `3f04417`, selected through the isolated project's local package dependency;
the working implementation was never reverted. This proves the failure predates P7-040, not its cause.
Both full runs also print `Persistent allocates 112 individual allocations` at shutdown; the focused
run prints no leak warning. General leak freedom is not claimed.

The initial headless full run passed 1439/1454: twelve tests required the hard-coded
`Assets/AIBT/Tests/Fixtures/P2/CodeGen/generated-tree-v2.aibt.json` path, and two graph tests required
a graphics device. The final and baseline runs copied that existing JSON fixture into the isolated
project and enabled graphics. No test expectation or production source was changed for the harness.

`Verify-Static.ps1`: passed (7 schemas, 146 work items). `git diff --check`: passed.
The initial invalid work-item spelling `in-progress` was corrected to schema-valid `in_progress`
before static verification; the final status is `done`.

## Reproduction

Use an isolated Unity project with this package as a `file:` dependency, this package in
`testables`, test-framework 1.9.0, and the imgui/uielements/jsonserialize built-in modules. Copy the
existing fixture to the hard-coded Assets path above. Do not open a second Editor on Modules.

From the package root, with the paths to the isolated project, results directory and Unity set:

```powershell
& './Tools~/Verification/Run-UnityTests.ps1' -UnityPath $UnityPath -ProjectPath $ProjectPath -OutputPath $OutputPath -Scope Focused -TestFilter 'AIBT.Tests.Runtime.NativeExecution.HotReload'
& './Tools~/Verification/Verify-Static.ps1'
git diff --check
```

The standard Full command was also executed; its `-nographics` setting accounts for the two graph
failures above. For the final full run, launch Unity with `-batchmode -projectPath <isolated-project>
-runTests -testPlatform EditMode -testResults <results.xml> -logFile <log>` and omit `-nographics`.
On Windows launch the background process with `Start-Process -WindowStyle Hidden` and await exit.
Repeat with the local package path pointing to a clean `3f04417` worktree for baseline comparison.

## Handoff and self-review

- Task: P7-040; implementation complete within the approved narrowed scope.
- Branch/base: `main` / `3f04417`; no commit, staging or push performed.
- Changed: two runtime hot-reload files, one test fixture and Unity-generated meta, P7-011 addendum,
  this evidence/card, changelog and work-item status.
- The old instance survives rejected migration; successful migration does not dispose it.
  Defaults are validated before writes and fresh-build failures follow existing owner cleanup.
- No tick-path change, speculative facade, V2 upgrade or production external-write migration.
- Remaining baseline hash failure and full-suite allocation warning are disclosed, not fixed here.
- No Player, platform, benchmark or zero-GC claim. This is Editor correctness evidence on 6000.7.0a6;
  it does not certify historical 6000.5.8f1 or a separate live production-host reload workflow.
- No additional task is automatically started. The production-host storage integration requires its
  own scope decision if the owner wants it next.
