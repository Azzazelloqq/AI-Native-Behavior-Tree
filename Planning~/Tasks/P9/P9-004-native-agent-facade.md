# P9-004 — Native agent facade and automatic preparation

Status: `Review`

## Authorization and dependencies

Owner approved in chat on 2026-11-11. Depends on P9-001 (shared agent vocabulary). Builds on the
P8-009 runtime asset/program path without changing its low-level API.

## Objective

`NativeTreeAgent` wraps `ProductionTreeAsset`/`Program`/`Host`/`World`: shared program cache,
automatic world and scheduler, named facts with cached handles flushed at the safe boundary,
named effect handlers instead of hash comparison, restart policy and correct teardown. Runtime
assets remember their source and catalog and are re-prepared when the source changes.

Owned: `Runtime/Integration/`, `Editor/Graph/ProductionTreeAssetBaker.cs`,
`Editor/Graph/ProductionTreeAssetInspector.cs`, `Samples~/FirstAgent/`, `Tests/`,
`Documentation~/production-host.md`, `Documentation~/consumer-quickstart.md`, `CHANGELOG.md`,
`Planning~/Evidence/P9-004/`.

## Acceptance

The First Agent sample needs no manual program/host/world code; editing and saving its tree
updates the runtime asset. Existing production host, world and scheduler tests stay green.

## Verification

New `NativeTreeAgentTests` (Integration, 8) and `ProductionTreeAssetStalenessTests` (Editor, 6);
full AIBT assemblies; `Verify-Static.ps1`; public API additivity against a `HEAD` dump; Play Mode
probe, 30 s clip and inspector/graph captures of the First Agent demo through the Unity CLI.

## Handoff

Task ID: P9-004. Outcome: implemented and verified in the `Modules` host on Unity 6000.7.0b1;
status Review, not Done. Branch: feature/aibt-consumer-workflow; local commit only, no push.
Verification: `AIBT.Runtime.Tests` 707/707, `AIBT.Editor.Tests` 616/618 (the two pre-existing
host-only MCP catalog/hash failures), `AIBT.Integration.Tests` 69/69, `AIBT.BehaviorCases.Tests`
65/65; `Verify-Static.ps1` passed; public API 0 removed. First Agent script 65 → 32 lines.
See ../../Evidence/P9-004/README.md.
Known limits: native live view shows the active path and root result only (no inner exit statuses,
no sub-frame activations, no native blackboard values); the catalog is resolved by type name
(Player stripping needs `CatalogFactory`; no Player build tested); running hosts keep their program
after automatic re-preparation.
