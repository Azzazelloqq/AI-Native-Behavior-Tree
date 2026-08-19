# Editor large-graph interaction pilot

This directory records the P3-012 one-run large-graph interaction/performance pilot for the
`UnityEditor.Experimental.GraphView`-based editor surfaces (P3-003 rendering, P3-004/P3-005
auto-layout and manual organization, P3-006 semantic editing). It is compatibility/scale evidence
and an initial measurement, not a stable performance baseline or a supported-graph-size claim.

Unity `6000.5.8f1` on one Windows 11 workstation (Intel Core Ultra 9 275HX, 24 logical cores,
32 GB RAM). Four synthetic tree scales were measured -- 240 (matching `P3-001`'s spike scale),
500, 1000, and 2000 nodes -- each a roughly balanced tree of `aibt.core.memory-sequence` internal
nodes over `aibt.test.success` leaves with branching factor 8.

Two measurement modes:

- **Headless EditMode** (`Tests/Editor/Performance/LargeGraphPerformanceTests.cs`): in-process
  `BehaviorTreeGraphView.Populate`, `DeterministicAutoLayoutService.Layout`,
  `LayoutOrganizationOperations.SetNodePosition`, a `SemanticEditOperations.AddNode` +
  `SemanticEditTransaction.Apply` round trip, and a `GraphView.viewTransform` pan/zoom mutation, at
  all four scales, timed with `System.Diagnostics.Stopwatch` and logged via `TestContext.WriteLine`.
- **Live interactive Editor** (Unity MCP `execute_code` against the actually-running Editor): opening
  a real `BehaviorTreePreviewWindow` on the 2000-node fixture, which headless EditMode cannot
  represent (no real window, no UI Toolkit panel/layout/paint pipeline).

The live number is markedly higher than the headless number at the same scale (`loadDocumentMs`
2020.4ms vs. headless `renderMs` 755.6ms at 2000 nodes) -- real window construction and per-node UI
(including the debugger-style breakpoint context-menu manipulator P3-009's preview window attaches
per node) cost more than building the `VisualElement` tree alone. This is exactly the kind of gap
`P3-001`'s spike evidence flagged as unmeasured ("no live GUI interaction was measured... a real
gap") and that P3-012 could close given this session's live Unity MCP Editor access.

Exact results and limitations are in `pilot-results.json`. This card's own usability read of these
numbers is in `Planning~/Evidence/P3-012/README.md`, not here -- this directory mirrors
`Benchmarks~/Platform/Web/`'s role as raw platform evidence, feeding Phase 4's broader benchmark
research rather than substituting for it.
