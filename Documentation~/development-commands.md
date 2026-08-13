# Development commands

All commands are run from the AIBT repository unless stated otherwise.

## Static verification

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File './Tools~/Verification/Verify-Static.ps1'
```

The command validates JSON syntax, all schema metaschemas, schema-bound policy/work-item documents, work-item references and dependency cycles, local Markdown links, package identity, and Git whitespace. Schema checks use pinned `check-jsonschema` through `uvx`; install [uv](https://docs.astral.sh/uv/), pass `-UvxPath`, or set the task-specific `AIBT_UVX_PATH` environment variable.

Run only schema verification with `Verify-Schemas.ps1` and the same optional `-UvxPath` argument.

## Unity compile validation

Close any Editor process using the project, then run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File './Tools~/Verification/Run-UnityCompile.ps1' `
  -UnityPath 'C:/Program Files/Unity/Hub/Editor/6000.5.8f1/Editor/Unity.exe' `
  -ProjectPath 'C:/UnityProjects/Modules'
```

## Unity tests

Do not start a second Unity process while the same project is open in the Editor. Either close the Editor and run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File './Tools~/Verification/Run-UnityTests.ps1' `
  -UnityPath 'C:/Program Files/Unity/Hub/Editor/6000.5.8f1/Editor/Unity.exe' `
  -ProjectPath 'C:/UnityProjects/Modules' `
  -Mode EditMode `
  -Scope Full
```

or use Unity MCP `run_tests` against the connected `Modules` instance and retain its job summary in the task handoff.

For a focused suite, use `-Scope Focused -TestFilter 'Fully.Qualified.Test.Name'`. Focused and full modes use distinct result/log names. Generated logs and result files belong under the verification tool's ignored `TestResults` directory and are not committed.

## Android IL2CPP ARM64 build smoke

Run against an isolated harness containing the current package snapshot:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File './Tools~/Verification/Android/Run-AndroidBuildSmoke.ps1' `
  -UnityPath 'C:/Program Files/Unity/Hub/Editor/6000.5.8f1/Editor/Unity.exe' `
  -ProjectPath '<ISOLATED_HARNESS>' `
  -OutputPath './Tools~/Verification/TestResults/Android'
```

The command proves unsigned development-build compatibility for Android, IL2CPP, ARM64 only, and Burst enabled. It does not install or run the APK and does not establish device or performance support.
