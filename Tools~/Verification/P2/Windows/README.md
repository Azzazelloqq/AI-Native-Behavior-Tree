# P2-022 Windows Player verification tools

This directory owns the host-side tooling for `P2-022 — Windows Player conformance
and Phase 2 baseline`. The Player harness itself lives under
`Benchmarks~/Phase2/Dispatch/Player/` and the result schemas under
`Benchmarks~/Phase2/Windows/Schemas/`.

| Script | Effect | Elevation |
| --- | --- | --- |
| `Assert-WindowsToolchain.ps1` | Read-only preflight for MSVC x64 and the Windows SDK | No |
| `Install-WindowsToolchain.ps1` | Installs the missing components; dry run unless `-Apply` | Only with `-Apply` |
| `Verify-WindowsBaselineEvidence.ps1` | Schema and digest verification of produced baseline evidence | No |

## Why the preflight exists

Unity reaches the IL2CPP C++ stage before it discovers that the host has no MSVC
compiler or Windows SDK, and then fails with
`Unity.IL2CPP.Bee.BuildLogic.ToolchainNotFoundException`. That costs a full build
cycle for a condition detectable in under a second. `Assert-WindowsToolchain.ps1`
uses the same detection authority as the harness's post-build environment
snapshot, so a passing preflight and a recordable environment block are the same
condition.

The preflight is a convenience gate, not an acceptance criterion. It proves the
toolchain is present. It proves nothing about the Player, the baseline, or
performance.

## Runbook

### 1. Preflight

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File './Tools~/Verification/P2/Windows/Assert-WindowsToolchain.ps1' `
  -ReportPath './Tools~/Verification/TestResults/Windows/toolchain-preflight.json'
```

Exit code `0` with `AIBT_P2_022_TOOLCHAIN_OK` means step 2 can be skipped. Exit
code `1` prints `AIBT_P2_022_TOOLCHAIN_MISSING` with one reason per missing
component. The report path is optional and lands in the ignored `TestResults`
directory; it is a machine-local diagnostic and is never committed.

### 2. Install the toolchain

Review the resolved plan first. The dry run changes nothing:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File './Tools~/Verification/P2/Windows/Install-WindowsToolchain.ps1'
```

Then execute it from an elevated shell:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File './Tools~/Verification/P2/Windows/Install-WindowsToolchain.ps1' -Apply
```

The plan installs exactly two components:

- `Microsoft.VisualStudio.Component.VC.Tools.x86.x64`
- `Microsoft.VisualStudio.Component.Windows11SDK.22621`

Override the SDK with `-WindowsSdkComponentId` when a different version is
required; any Windows 10/11 SDK at or above `10.0.19041` satisfies the preflight.
On a host without winget, download `vs_BuildTools.exe` from Microsoft and pass
`-Method Bootstrapper -BootstrapperPath <file>`.

An exit code of `3010` from the installer means the components are staged and the
host must reboot before Unity can use them.

### 3. Confirm and rerun the harness

Repeat step 1. Once it reports `AIBT_P2_022_TOOLCHAIN_OK`, close every Editor
process holding the project and run the Player harness:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File './Benchmarks~/Phase2/Dispatch/Player/Run-GeneratedDispatchPlayerAot.ps1' `
  -UnityPath 'C:/Program Files/Unity/Hub/Editor/6000.5.8f1/Editor/Unity.exe'
```

The harness builds a non-Development Windows x64 IL2CPP Player with Burst enabled
from an isolated snapshot, runs the behavior matrix and the Windows baseline
probe, and writes raw plus acceptance JSON under
`Benchmarks~/Phase2/Dispatch/Results/`.

### 4. Verify the produced evidence

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File './Tools~/Verification/P2/Windows/Verify-WindowsBaselineEvidence.ps1' `
  -EvidencePath '<raw evidence written by step 3>'
```

Prove the negative case in the same session:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File './Benchmarks~/Phase2/Dispatch/Player/Run-GeneratedDispatchPlayerAot.ps1' `
  -ControlledInvalid
```

That run must stop with `AIBT_P2_022_CONTROLLED_INVALID_REJECTED` before Unity
launches.

### 5. Record the result

Replace the blocked section of `Planning~/Evidence/P2-WINDOWS/README.md` with the
observed values: Unity version, MSVC and SDK versions, scenario p50/p95/p99 frame
contribution, throughput, scheduling and completion cost, native bytes per program
and per instance, the coarse Player GC signal, and the SHA-256 of each retained
artifact. Report the resulting state in the session summary and update
`Planning~/work-items.json` directly once the result is known.

Do not commit logs, Players, isolated projects, or machine paths. Do not derive a
scheduling threshold, a performance default, or any Android, Web, or console
conclusion from this single workstation result; those belong to Phase 4.
