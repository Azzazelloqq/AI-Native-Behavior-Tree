# Phase 4 inputs (Phase 3 addendum)

Prepared 2026-08-19 for the `P3-013` review. `Planning~/Evidence/P2-GATE/phase4-inputs.md`
remains the primary Phase 4 handoff (native runtime, scheduler, and platform
research); nothing in Phase 3 changed the native runtime, so every item there
still holds unchanged. This document only adds what Phase 3 itself
contributes.

## What Phase 4 additionally inherits from Phase 3

- **Raw editor-side large-graph measurements** (`P3-012`,
  `Benchmarks~/Platform/Editor/pilot-results.json`): render/auto-layout/
  reposition/semantic-add/pan-zoom timings and managed-heap deltas at 240,
  500, 1000, and 2000 nodes, plus one live-interactive-Editor sample at 2000
  nodes. These are raw measurements only -- no editor performance default,
  threshold, or "supported graph size" claim exists for Phase 4 to inherit as
  a decided value; it inherits the methodology and the numbers to calibrate
  against, matching how Phase 2's own microbenchmarks were handed off.
- **The `Benchmarks~/Platform/Editor/` directory shape**, mirroring
  `Benchmarks~/Platform/Web/`'s existing precedent, as a reference shape for
  any future editor-focused benchmark work Phase 4 undertakes.
- A concrete example of live-interactive-Editor measurement closing a gap a
  headless-only spike could not (`P3-001`'s spike vs. `P3-012`'s live number)
  -- worth reusing as a methodology note if Phase 4's own research needs
  Editor-side (not just Player-side) measurement.

## Constraints Phase 4 must not violate (unchanged, restated from `P2-GATE`)

- No default, threshold, or crossover point derives from a single
  workstation -- now including editor-side measurements, not just native
  runtime ones.
- Every published number records environment, build/Editor version, and raw
  samples.
- `GC.GetTotalMemory` is not a zero-allocation proof for editor code either;
  Phase 3 never claimed editor code is allocation-free.
