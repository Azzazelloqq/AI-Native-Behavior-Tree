# P8-009 — Complete the first consumer workflow

Status: `Review`

## Authorization and dependencies

Owner approved implementation in chat on 2026-11-02 after the five-step proposal:
asset opening, Editor-prepared runtime integration, Float32 parameters, graph/preview/live
trace integration and port connections, then independent consumption and performance checks.
This explicitly supersedes P8-001's preservation instruction for the existing local importer edit.
Dependencies P3-003/P3-005/P3-006/P3-009/P3-011, P7-030/P7-033/P7-037/P7-039/P7-041 are Done.
Existing P8 implementation is reused without claiming its missing baseline acceptance is complete.

## Plan and owned paths

1. Restore selective asset opening and test unrelated-asset handling (`Editor/Graph`, graph tests).
2. Add a versioned Editor-baked runtime asset and shared explicit catalog/program owner. Runtime
   receives compiled data, never authored JSON or an Authoring dependency (`Runtime/Integration`,
   `Editor`, integration tests). Retain the existing low-level APIs and catalog ownership rules.
3. Add finite Float32 node parameters across manifests, schema, generator, packing, validation and
   inspector. Preserve existing enum values, canonical bytes for existing contracts and ABI v2.
   Regenerate the packaged analyzer and test generated execution, invalid values and round trips.
4. Add graph commands for preview and selected-host trace, share the graph view under AIBT-032,
   verify exact program/source identity before highlighting, and connect ports using existing
   semantic operations with append order. Layout never controls child order.
5. Exercise a separate consumer project, including custom nodes, external inputs, effects,
   cancellation and Player execution. Measure execution costs with workload and latency disclosed.

Allowed paths include Runtime, Authoring, Editor, CodeGen~, Analyzers, Schemas~, Tests, Samples~,
Tools~/Verification, Benchmarks~, Documentation~, CHANGELOG.md and this task's planning/evidence.
No package version, scheduler defaults or supported-platform claims are changed.

## Verification

- Behavior-focused regression tests for each step and the complete consumer path.
- Roslyn generator matrix, isolated Unity EditMode suite, schema/static and API checks.
- Player smoke and bounded comparative measurements; preserve raw observations and environment.
- Verify external files, drafts and mismatched live-program identity fail safely.
- Record unavailable Unity 6000.5.8f1 acceptance separately from installed 6000.7.0b1 results.
- Update public API reference, samples, instructions and changelog; run git diff --check.

## Handoff

Task ID: P8-009.
Outcome: planned implementation complete; verification and detailed handoff in
[evidence](../../Evidence/P8-009/README.md). Unity 6000.7.0b1 full suite 1497/1497; final targeted
consumer/graph/trace checks 45/45; Windows Development IL2CPP/Burst Player and six diagnostic
measurement cells passed. Static/schema, reproducible analyzer, 1415 Roslyn assertions and API
compatibility checks passed. Baseline 1481/1481 reproduces the full suite's existing native warning.
Branch: feature/aibt-consumer-workflow; no commit/tag/publish requested.
Normative changes: explicitly authorized additive AIBT-042, recorded in its ADR/specifications.
Known acceptance limits: Unity 6000.5.8f1 is not installed; the independent fresh-user/manual
walkthrough remains unverified. Do not promote this Review state to a 1.0 readiness claim.
