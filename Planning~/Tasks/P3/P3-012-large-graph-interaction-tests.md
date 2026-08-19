# P3-012 — Large-graph interaction and performance tests

Status: `Done`

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

## Outcome

- `Tests/Editor/Performance/LargeGraphFixtures.cs`: a deterministic synthetic large-tree generator
  (built-in `aibt.core.memory-sequence` internal nodes over `aibt.test.success` leaves, matching
  `P3-009`'s registry) sized to a requested node count with a configurable branching factor.
- `Tests/Editor/Performance/LargeGraphPerformanceTests.cs`: proves rendering (P3-003), auto-layout
  (P3-004), reposition (P3-005), a real compiled add-node round trip (P3-006), and pan/zoom all
  still work correctly at 240 (matching `P3-001`'s measured spike scale), 500, 1000, and 2000
  nodes, and logs wall-clock/memory numbers transcribed into `Benchmarks~/Platform/Editor/`.
- **Live interactive measurement closed a gap `P3-001`'s own evidence explicitly flagged** ("no
  live GUI interaction was measured... a real gap"): using this session's live Unity MCP-connected
  Editor, a real `BehaviorTreePreviewWindow` (P3-009) was opened on the 2000-node fixture and timed
  end-to-end -- 2020.4ms, markedly higher than the headless render-only figure (755.6ms) at the same
  scale, since it also covers compilation and real UI Toolkit window construction that headless
  EditMode cannot represent.
- This card's own usability read of the raw numbers (not a shipped threshold -- Phase 4's
  ownership): individual operations (auto-layout/reposition/add-node/pan-zoom) pass at every scale
  up to 2000 nodes (all under 70ms); full-view render/re-render/load explicitly degrades at 1000 and
  2000 nodes, reported as degraded rather than silently passed.
- Notable finding kept in the evidence, not acted on (out of this card's record-only scope):
  `BehaviorTreeGraphView.Populate`'s full teardown-and-rebuild means re-rendering after an edit at
  2000 nodes (1276.9ms) costs *more* than the first render (755.6ms) -- a candidate for future
  incremental-repopulation work.
- Full evidence: `Planning~/Evidence/P3-012/README.md`, `verification-results.json`,
  `Benchmarks~/Platform/Editor/`.
