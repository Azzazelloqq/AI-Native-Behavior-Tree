# P8-002 — Resolve integration fixtures in installed packages

Status: `In Progress`

## Objective

Run the existing generated-dispatch integration behavior tests when AIBT is installed as a UPM
package as well as when its source is embedded under `Assets/AIBT`.

## Dependencies

- P7-037 is complete; the integration tests and their fixture already exist.

## Scope

- Resolve the owning package's root with Unity's Package Manager and keep the existing embedded
  source fallback. Do not change the fixture, expected behavior, runtime, or generated outputs.
- Verify the existing integration cases against an isolated installed-package project; report
  any unrelated failures separately.

## Acceptance

- The same fixture is read from the package or embedded checkout without a hardcoded host path.
- Existing generated-dispatch lifecycle tests run with their behavioral assertions unchanged.
- Relevant Unity tests, static verification and `git diff --check` pass.

## Current verification

- Isolated Unity 6000.7.0b1 EditMode run: 1481/1481 passed on the integrated snapshot.
- Static/schema verification passed; targeted graph, preview and project-catalog checks passed.
- Swarm Arena's project-generated tree compiled in a separate consumer project; changing a
  blackboard binding in the actual EditorWindow and saving it passed an automated check.
- Unity 6000.5.8f1 and a manual interactive editor acceptance remain unverified in this environment.
  This card remains in progress until its applicable acceptance gates are completed.
