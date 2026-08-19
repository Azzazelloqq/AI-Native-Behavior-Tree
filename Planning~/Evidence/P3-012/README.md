# P3-012 large-graph interaction and performance tests evidence

## Result

- `Tests/Editor/Performance/LargeGraphFixtures.cs` (new, internal): a deterministic synthetic
  large-tree generator producing a roughly balanced tree of `aibt.core.memory-sequence` internal
  nodes over `aibt.test.success` leaves (the same built-in/fixture node kinds
  `ReferencePreviewDriver.CreatePreviewNodeRegistry()` already compiles against, reused rather than
  inventing a second registry-building path), sized to a requested node count with a configurable
  branching factor. Generates canonical `.aibt.tree` JSON text and parses it through the real
  `CanonicalTreeJson.Parse` pipeline -- no bespoke document construction bypassing validation.
- `Tests/Editor/Performance/LargeGraphPerformanceTests.cs` (new): at 240 (matching `P3-001`'s
  measured spike scale), 500, 1000, and 2000 nodes, proves `BehaviorTreeGraphView.Populate` (P3-003),
  `DeterministicAutoLayoutService.Layout` (P3-004), `LayoutOrganizationOperations.SetNodePosition`
  (P3-005), a real `SemanticEditOperations.AddNode` + `SemanticEditTransaction.Apply` round trip
  through the actual compiler/validator (P3-006), and a `GraphView.viewTransform` pan/zoom mutation
  all still succeed and produce the correct node counts at every scale -- and logs each operation's
  wall-clock time (`System.Diagnostics.Stopwatch`) and a coarse managed-heap delta
  (`GC.GetTotalMemory`) via `TestContext.WriteLine`, transcribed into
  `Benchmarks~/Platform/Editor/pilot-results.json`.
- **Live interactive measurement** (this environment's live Unity MCP-connected `6000.5.8f1`
  Editor, matching the "live interactive-Editor verification" this card plausibly needs, per
  `P3-001`'s own disclosed gap: "no live GUI interaction was measured... a real gap"): opened a real
  `BehaviorTreePreviewWindow` (P3-009) on the 2000-node fixture via `unityMCP.execute_code` and timed
  `LoadDocument` end-to-end in a real window with a real UI Toolkit panel -- 2020.4ms, markedly
  higher than the headless `renderMs` (755.6ms) at the same scale, because it also covers
  `ReferenceCompiler.Compile` and per-node breakpoint-menu manipulator attachment inside a real
  window, not just `Populate`'s `VisualElement` construction.
- Full EditMode suite: 1305 tests (1304 + this one new test), only the same 3 pre-existing failures
  already recorded in `Planning~/Evidence/P3-009/` and `P3-010/` (unrelated to this card).

## Recorded numbers

See `Benchmarks~/Platform/Editor/pilot-results.json` for the exact figures; summarized here:

| Scale | renderMs | autoLayoutMs | repositionMs | addNodeMs | reRenderAfterEditMs | panZoomMs | managedHeapDeltaBytes |
|---|---|---|---|---|---|---|---|
| 240  | 136.2 | 7.1 | 1.4 | 53.7 | 138.7 | 1.3 | 4.27 MB |
| 500  | 203.2 | 0.7 | 0.8 | 17.4 | 246.6 | 0.01 | 14.09 MB |
| 1000 | 427.3 | 1.2 | 1.2 | 31.2 | 441.4 | 0.01 | 32.96 MB |
| 2000 | 755.6 | 2.3 | 2.6 | 67.0 | 1276.9 | 0.01 | 82.49 MB |

Live interactive (real window, 2000 nodes): `LoadDocument` end-to-end 2020.4ms.

## This card's usability read of the numbers

Per-operation costs (auto-layout, reposition, add-node, pan/zoom) -- the individual actions a user
actually triggers one at a time -- stay under 70ms at **every** measured scale up to 2000 nodes,
comfortably inside typical "feels instant to responsive" interactive budgets (roughly sub-100ms).
**Pass** at every scale for these four operation kinds.

Full-view construction and re-render (`renderMs`/`reRenderAfterEditMs`) grow from a responsive
136-139ms at 240 nodes past the sub-100ms "instant" mark almost immediately, crossing the
roughly-one-second "starts to feel like a wait" mark by 2000 nodes (755.6ms headless render,
1276.9ms headless re-render after an edit, 2020.4ms for a full live window load). **Explicitly
reported as degraded, not silently passed**, at 1000 and 2000 nodes for full-view
render/re-render/load; **pass** at 240 and 500 nodes. This is a read of the raw numbers for this
card's own record, not a shipped threshold, default, or supported-graph-size claim (`Documentation~/benchmarks.md`
and this card's own Forbidden-changes clause both reserve that to Phase 4).

`reRenderAfterEditMs` exceeding `renderMs` at 2000 nodes (1276.9ms vs. 755.6ms) is a genuine,
non-obvious finding: `BehaviorTreeGraphView.Populate` tears down the existing view
(`DeleteElements(graphElements.ToList())`) before rebuilding, so refreshing an already-populated
large view after a semantic edit costs *more* than the first render, not the same. This is a
real candidate area for future optimization (incremental diff-based repopulation instead of
full teardown-and-rebuild) but implementing that is out of this card's scope (which records
measurements, not optimizations).

## Decision

No new decision. Consistent with `P3-004`--`P3-010`'s pattern, this card reuses
`ReferencePreviewDriver.CreatePreviewNodeRegistry()` (P3-009's public Authoring facade) rather than
building a second, parallel node-registry-construction path for its own fixtures.

## Scope and limitations

- **One measured run per scale/mode on one workstation.** These are not stable baselines; no result
  is generalized beyond this machine, this Unity version, and this synthetic fixture shape, matching
  `Documentation~/benchmarks.md`'s discipline.
- **`GC.GetTotalMemory` is coarse** -- it reports a managed-heap delta, not allocation sites, and
  does not by itself indicate a problem (Editor UI/authoring code is not subject to the Runtime's
  zero-GC contract).
- **Edge density is structurally fixed**: every fixture is a tree (`nodeCount - 1` edges by
  construction); AIBT trees have single-parent semantics (reroutes are presentation-only per
  `Documentation~/editor-and-layout.md`), so no independent "edge density" parameter exists to vary.
- **The live interactive measurement is a single sample** at one scale (2000 nodes only, not all
  four) from one session; a first "cold" attempt (23.8s) was discarded as dominated by the MCP
  execute_code harness's own ad hoc script compilation, not the measured operation -- disclosed in
  `Benchmarks~/Platform/Editor/pilot-results.json`'s limitations, not hidden.
- Raw results here are an input to Phase 4's broader benchmark research, not a substitute for it,
  per this card's own handoff note.

See `verification-results.json` for exact commands and results.
