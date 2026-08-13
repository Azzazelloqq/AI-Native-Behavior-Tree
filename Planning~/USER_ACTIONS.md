# User and infrastructure actions

These actions require owner credentials, hardware, or product decisions and are not silently performed by implementation agents.

## Required before Phase 0 closes

- Install the repository's exact Unity `6000.5.2f1` editor or explicitly approve upgrading `ProjectVersion.txt` and the package baseline.
- Install Android Build Support, SDK, NDK, and OpenJDK through Unity Hub for the selected editor.
- Install Unity Web Build Support for the selected editor.
- Decide where CI runs and provide repository Actions permission if GitHub Actions is selected.
- Provide or approve Unity license activation for CI without committing license data.
- Identify at least one Android ARM64 device class for benchmark evidence.
- Provide access to macOS/Safari hardware or CI before Safari can become a verified Web target.

## Required before public 1.0 claims

- Approve the exact supported browser/version policy.
- Approve performance hardware classes and acceptable regression thresholds after research results exist.
- Provide console platform access before any console support claim.
- Approve final public API and persisted-format stability review.

Agents must report missing access as a blocker. They must not weaken a platform matrix or fabricate results.
