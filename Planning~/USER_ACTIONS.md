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

Agents must report missing access as a blocker. They must not weaken a platform matrix or fabricate results.
