# P3-012 — Large-graph interaction and performance tests

Status: `Draft`

## Objective

Prove the editor's rendering, manual-organization, and editing surfaces remain usable at the graph sizes `Documentation~/editor-and-layout.md` and `P3-001`'s spike targeted, with recorded measurements rather than an assumed ceiling.

## Depends on

- `P3-005`.
- `P3-006`.

## Required reading

- `Documentation~/editor-and-layout.md`.
- `P3-001`'s spike evidence (`Planning~/Evidence/P3-001/`) for the scale class already measured.

## Allowed changes

- `Tests/Editor/Performance/` (new).
- `Benchmarks~/Platform/Editor/` (new, mirrors the existing `Benchmarks~/Platform/Web/` pattern).
- `Planning~/Evidence/P3-012/`.

## Forbidden changes

- Any performance default, threshold, or "supported graph size" claim — this card records measurements, matching the repository-wide rule that regression thresholds and platform defaults are Phase 4's ownership.
- Weakening `P3-004`/`P3-005`/`P3-006` to pass a synthetic benchmark rather than measuring their actual behavior.

## Deliverables

- A synthetic large-tree fixture generator at multiple scales (matching and exceeding `P3-001`'s spike scale).
- Recorded interaction latency (add/connect/reposition node, pan/zoom) and memory footprint at each scale, editor-only (not a Runtime/Player claim).

## Acceptance criteria

- Every measured scale either passes a stated usability bar the card itself defines from the raw numbers, or is explicitly reported as degraded with the measurement kept.
- Results record Unity version, editor machine specs, and graph shape (depth/width/edge density) — no result is generalized beyond what was measured, matching the discipline already used in `Documentation~/benchmarks.md`.

## Required verification

```text
Verify-Static.ps1
Run-UnityTests.ps1 -Mode EditMode -Scope Focused -TestFilter <large-graph fixture>
recorded interaction/memory measurements at each fixture scale
```

## Handoff notes

- Raw results here are an input to Phase 4's broader benchmark research, not a substitute for it.
