# P10-006 — The package compiles and passes its tests on Unity 6000.6.9f1

Status: `Review`

## Objective

The Modules project runs on Unity 6000.6.9f1 (6000.7.0b1 was removed from the machine on
2026-11-16), AIBT compiles there, and the AIBT test assemblies have a baseline on 6000.6.9f1 that
is compared with the last 6000.7.0b1 baseline.

## Owner decisions for this card

- The owner chose to migrate the Modules project to Unity 6000.6.9f1.
- The compatibility fix uses conditional compilation, so 6000.7 keeps the code it compiled before.

## Depends on

- P10-005 (finding 0.1 in `Planning~/Evidence/P10-005/README.md`: CS0619 in
  `Editor/Graph/BehaviorTreeImporter.cs` on 6000.6.9f1).

## Required reading

- `AGENTS.md`, `Planning~/AGENT_WORKFLOW.md`, `Planning~/Evidence/P10-005/README.md` section 0.1.

## Scope

- `Editor/Graph/BehaviorTreeImporter.cs`, `Tests/Editor/Consumer/GraphConsumerWorkflowTests.cs`
  (the `EntityId` overload condition), `CHANGELOG.md`, `Planning~/Evidence/P10-006/`, this card,
  `work-items.json`.
- The parent Modules project (`Packages/manifest.json`, `ProjectVersion.txt`) is changed in its
  working tree only and is not committed.

## Forbidden changes

- Execution semantics, defaults, ABI v2 (`ExpectedPublicAbiV2.txt`), the package version, public API.
- Weakening a test to make it pass.
- Pushing; committing the parent repository.

## Deliverables

- The conditional-compilation fix, the 6000.6.9f1 test baseline, `Planning~/Evidence/P10-006/README.md`.

## Acceptance criteria

- The Modules Editor on 6000.6.9f1 compiles the project with no errors.
- Every AIBT test assembly is run and compared with the 6000.7.0b1 baseline; every difference is explained.
- `Tools~/Verification/Verify-Static.ps1` passes.

## Required verification

```text
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Unity/Invoke-UnityRecompile.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Unity/Invoke-UnityTests.ps1 -Assemblies <each AIBT test assembly>
powershell -NoProfile -ExecutionPolicy Bypass -File Assets/AIBT/Tools~/Verification/Verify-Static.ps1
```

## Handoff notes

- 6000.7 can no longer be tested on this machine; the 6000.7 branch of each condition is unchanged
  but not re-verified.
