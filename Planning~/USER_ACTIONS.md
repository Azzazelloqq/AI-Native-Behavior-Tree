# User and infrastructure actions

These actions require owner credentials, hardware, or product decisions and are not silently performed by implementation agents.

## Required before Phase 0 closes

- Unity `6000.5.8f1` is the approved development baseline and is installed.
- Android Build Support, SDK, NDK, and OpenJDK are installed for the selected editor.
- Unity Web Build Support is installed for the selected editor.
- Connect a pre-activated Windows x64 GitHub Actions runner to `Azzazelloqq/AI-Native-Behavior-Tree` with label `unity-6000.5.8f1`, then rerun the queued `Validation` workflow. The runner must define `UNITY_EDITOR_PATH` for Unity `6000.5.8f1`; no license data belongs in the repository or workflow.
- Identify at least one Android ARM64 device class for benchmark evidence.
- Provide access to macOS/Safari hardware or CI before Safari can become a verified Web target.

## Required before public 1.0 claims

- Approve the exact supported browser/version policy.
- Approve performance hardware classes and acceptable regression thresholds after research results exist.
- Provide console platform access before any console support claim.
- Approve final public API and persisted-format stability review.

## Required to close Phase 2 on the current Windows host

### 1. Install the MSVC and Windows SDK toolchain

The host has neither `cl.exe` nor a Windows SDK, so Unity cannot complete the
P2-022 Windows x64 IL2CPP/Burst Player build. Confirm the gap, review the plan,
and apply it from an elevated shell:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File './Tools~/Verification/P2/Windows/Assert-WindowsToolchain.ps1'
powershell -NoProfile -ExecutionPolicy Bypass -File './Tools~/Verification/P2/Windows/Install-WindowsToolchain.ps1'
powershell -NoProfile -ExecutionPolicy Bypass -File './Tools~/Verification/P2/Windows/Install-WindowsToolchain.ps1' -Apply
```

The plan installs `Microsoft.VisualStudio.Component.VC.Tools.x86.x64` and a
Windows SDK at or above `10.0.19041`. The second command is a dry run and changes
nothing. `Tools~/Verification/P2/Windows/README.md` holds the full runbook,
including the harness rerun and evidence verification that actually close P2-022.

### 2. Authorize the Phase 2 integration commit

Explicitly authorize a local integration commit after reviewing the dirty P2
scope; P2-025 must run from a clean committed snapshot. The scope, ordering, and
hygiene checks are prepared in
`Planning~/Evidence/P2-GATE/commit-package.md`.

### 3. Run the P2-025 gate verification

Run the P2-025 gate's full verification pass yourself using the ordered
commands and artifact map prepared in
`Planning~/Evidence/P2-GATE/gate-runbook.md`.

Agents must report missing access as a blocker. They must not weaken a platform matrix or fabricate results.
