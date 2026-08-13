# P0-001 toolchain baseline evidence

Observed on 2026-08-13. The current user instruction authorizes Unity `6000.5.8f1` as the baseline, superseding the work-item card's stale `6000.5.2f1` expectation for this run.

## Result

- AIBT isolated package baseline: **pass**.
- Live `Modules` Editor readiness: **pass**.
- Full parent-project clean batch compile: **blocked by an unrelated UniTask compatibility error**.

The passing isolated run copied the exact AIBT `Runtime`, `Authoring`, `Editor`, and `Tests` directories into a temporary Unity project. The committed harness marker files were copied only into that temporary project to force Unity to materialize every otherwise-empty assembly. No marker was added to production package paths.

Unity exited with code `0` and compiled:

- `AIBT.Runtime.dll`
- `AIBT.Authoring.dll`
- `AIBT.Editor.dll`
- `AIBT.Runtime.Tests.dll`
- `AIBT.Editor.Tests.dll`

The passing log contains no compiler error, compiler warning, AIBT import error, or AIBT import warning.

## Exact environment

| Item | Observed value |
| --- | --- |
| Unity | `6000.5.8f1 (5cb7df797b7d)` |
| Unity executable file version | `6000.5.8.6076383` |
| OS/editor platform | Windows x64 / `WindowsEditor` |
| Live MCP state | ready, idle, not compiling, not updating assets |
| Android playback engine | installed and reported supported by `BuildPipeline` |
| Android SDK / NDK / OpenJDK | installed |
| Web playback engine | installed and reported supported by `BuildPipeline` |
| Harness Burst | `1.8.29` |
| Harness Collections | `6.5.0` |
| Harness Test Framework | `1.7.0` |

The live parent project resolves Burst `1.8.30`, while the AIBT package and isolated harness request `1.8.29`. The isolated baseline proves the package's declared version; the parent resolution is recorded but is not used to redefine the package contract.

## Live MCP checks

- Instance: `Modules`, Unity `6000.5.8f1`.
- Project root and active build platform matched the expected `Modules` project and `StandaloneWindows64`.
- `ready_for_tools` was `true`; no blocking reasons were present.
- Console returned zero error entries and zero warning entries at observation time.
- Unity imported all five AIBT assembly-definition assets.
- Because the production package contains no C# source yet, the live compilation pipeline correctly had no materialized AIBT assemblies. The isolated markers were required to verify the asmdef dependency graph and compilation.

## Batch commands

Passing isolated AIBT harness command, with machine-specific paths removed:

```text
<UNITY_EDITOR> -batchmode -nographics -projectPath <ISOLATED_AIBT_HARNESS> -quit -logFile <REPOSITORY>/Assets/AIBT/Planning~/Evidence/P0-001/batch-aibt-harness.log
```

Full parent source-copy diagnostic command:

```text
<UNITY_EDITOR> -batchmode -nographics -projectPath <ISOLATED_PARENT_PROJECT_COPY> -quit -logFile <REPOSITORY>/Assets/AIBT/Planning~/Evidence/P0-001/batch-compile.log
```

The second command exited with code `1`. Its errors come from `com.cysharp.unitask` editor sources using obsolete `TreeView`, `TreeViewItem`, and `TreeViewState` APIs. AIBT does not appear in any error or warning. The parent-project dependency must be handled by a separately authorized task; P0-001 did not modify it.

## Artifacts

- `baseline.json`: machine-readable baseline summary.
- `batch-aibt-harness.log`: passing, sanitized isolated compile log.
- `batch-compile.log`: sanitized parent-project diagnostic log showing the unrelated blocker.
- `Harness/`: reproducible minimal manifest, editor version, and compile markers.

Machine paths, machine/license identifiers, local IP, and host name are redacted from logs.

## Coordinator follow-up

1. Update the stale P0-001 card and shared toolchain documentation from `6000.5.2f1` to the user-approved `6000.5.8f1` in an integration-owned change.
2. Decide whether the parent repository's UniTask incompatibility is a separate repository-health task or excluded from AIBT phase gates.

