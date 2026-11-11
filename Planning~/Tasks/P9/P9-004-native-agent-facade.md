# P9-004 — Native agent facade and automatic preparation

Status: `Draft`

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
