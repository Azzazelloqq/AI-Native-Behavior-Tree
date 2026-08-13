# P0-002 verification-entrypoint evidence

Observed on 2026-08-13 with Unity `6000.5.8f1`.

## Result

- JSON syntax, JSON Schema metaschemas, bound policy/work-item schema validation, work-item graph, Markdown links, package identity, and Git whitespace: **pass**.
- Isolated AIBT compile validation: **pass**.
- Focused EditMode run: **pass**, 11/11 tests.
- Full EditMode run: **pass**, 75/75 tests.
- Controlled argument failure: **pass**, nonzero exit code returned before Unity launch.

The compile and test runs used a temporary ignored Unity harness under `Tools~/Verification/TestResults/`. It contained a snapshot of the AIBT package source and the exact package versions from the P0-001 harness, plus the now-required Newtonsoft JSON package. No package source was changed by an entrypoint.

## Commands exercised

All four repository-owned entrypoints were invoked by absolute path from the parent repository directory, demonstrating current-directory independence:

- `Verify-Schemas.ps1`
- `Verify-Static.ps1`
- `Run-UnityCompile.ps1`
- `Run-UnityTests.ps1` in both `Focused` and `Full` scope

The controlled failure invoked `Run-UnityTests.ps1 -Scope Focused` without `-TestFilter`. It returned exit code `1` with `Focused test scope requires -TestFilter.` and did not launch Unity.

## Environment caveat

The live parent project remained open in Unity, so it was not started in a second batch process. The isolated harness avoids both the concurrent-project lock and the unrelated parent UniTask compatibility failure already recorded by P0-001.

Machine-specific paths, license data, and full Unity logs are intentionally excluded. Machine-readable results are in `verification-results.json`.
