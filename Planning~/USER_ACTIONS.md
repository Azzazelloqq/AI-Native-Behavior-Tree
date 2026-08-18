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

- Install Visual Studio Build Tools 2022 with
  `Microsoft.VisualStudio.Component.VC.Tools.x86.x64` and a Windows 10/11 SDK
  version >= `10.0.19041`, then rerun the P2-022 Windows x64 IL2CPP/Burst Player
  baseline. The current host has neither `cl.exe` nor a Windows SDK.
- Explicitly authorize a local integration commit after reviewing the dirty P2
  scope; P2-025 must run from a clean committed snapshot.
- Explicitly authorize an independent review task for the final P2-025 gate.

Agents must report missing access as a blocker. They must not weaken a platform matrix or fabricate results.
