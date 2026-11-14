# P9-003 — Graph editor usability and live debugging

Status: Review, verified in the `Modules` host project on Unity 6000.7.0b1, 2026-11-14. Local
commit only, no push.

## Result

The graph editor's presentation was rewritten in `Editor/Graph/` over the existing editing model
(`GraphEditSession`, `SemanticEditOperations`, `LayoutPersistenceController`,
`DeterministicAutoLayoutService`, `LayoutHistory`); none of those were changed. Layout still never
controls child order.

1. Visuals. Stylesheets `Editor/Graph/Styles/AibtGraph.uss` (canvas, cards, ports, edges, live
   states) and `AibtGraphWindow.uss` (toolbar, panels, inspector, problems). Grid and minimap.
   `BehaviorTreeNode` is a card: kind accent and tag, readable title, one-line parameter summary
   (`NodeParameterSummary`), child-order badge, problem badge, glow for live states; ports in the
   card's own top/bottom slots. Default layout is `DeterministicAutoLayoutService` scaled to the
   card size (`GraphLayout`; the service's 160x120 units are pinned by golden fixtures and were
   not changed). The tree is framed once the view has a size.
2. Interaction. Context menus for node/edge/canvas; `SearchWindow` node creation
   (`NodeSearchProvider`, grouped by kind) from the context menu, Space, and an edge dropped on
   empty canvas; drag an edge to connect or reparent; Delete, Ctrl+S, Ctrl+Z/Ctrl+Y, Ctrl+D, F,
   Alt+Left/Right, Ctrl+Shift+L. Auto Layout keeps pinned nodes.
3. Simplification. One toolbar (New, Open, Save, Auto Layout, Preview, Debug, More: Check Native
   Compile, Prepare Runtime Asset, Reload From Disk, Frame All). One Save for tree and layout
   (`SaveAll`). One undo history (`GraphEditHistory`) bridged to Unity Undo through a hidden
   `GraphUndoMarker`, so Edit > Undo and Ctrl+Z step semantic and layout edits in one order.
   Incremental `BehaviorTreeGraphView.Sync` keeps unchanged cards. `BehaviorTreeGraphWindow.OpenTree`
   (used by double-click, importer and runtime asset inspector) never raises a modal dialog: the
   window showing the tree is focused, else a clean window is reused, else a second window opens;
   inside one window an unsaved-changes banner replaces the dialog. Runtime-asset errors are banners.
4. Inspector. Typed fields per parameter type with contract defaults shown greyed (`default` tag,
   right-click Reset to default), blackboard-key popups filtered by expected value type, no
   Clear/Set-to-zero buttons. Blackboard panel (keys, types, Tree-scope defaults, add/remove, live
   values). Problems list; clicking an item selects and frames its node. `TreeCatalogEntry`
   validates managed trees with the managed policy and no longer counts info notes as warnings:
   Guard and Wander are `Valid` (previously `[Errors]`).
5. Live debugging. `GraphLiveDebugger` samples `ManagedTreeInstance` node states at about 30 Hz:
   running nodes pulse, Succeeded/Failed/Aborted flash green/red/grey and fade over 0.9 s, edges
   between running nodes are painted as the active path, and the Blackboard panel shows live
   values. In Play Mode, selecting a GameObject whose `BehaviorTreeAgent` runs the open tree
   attaches automatically. Debug with a `ProductionTreeHost` selected keeps the native trace path.
6. Tests. `BehaviorTreeGraphAdapterTests` moved to the new contract (typed `aibt-param-ticks`
   field, `SaveAll`, `CheckNative`, `UndoEdit`/`RedoEdit`, top-down default layout, moves through
   `graphViewChanged`); save, external-change and undo behavior checks kept. New
   `GraphEditorWorkflowTests` (14). `Documentation~/generated/api-reference-editor.md`
   regenerated for the new public members.

## Verification

Unity 6000.7.0b1, `Tools/Unity/Invoke-UnityTests.ps1`, 2026-11-14:

| Assembly | Result |
| --- | --- |
| `AIBT.Runtime.Tests` | 707/707 |
| `AIBT.Editor.Tests` | 563/565 |
| `AIBT.Integration.Tests` | 58/58 |
| `AIBT.BehaviorCases.Tests` | 65/65 |

The two Editor failures are the pre-existing host-only ones
(`McpToolDispatcherTests.ZeroCustomNodesReturnsExactlyThePhase1BuiltInCatalog`,
`McpVerificationToolDispatcherTests.CompileReturnsTheSameContentHashAsADirectReferenceCompilerCall`).
Baseline before this card: Editor 549/551. `Tools~/Verification/Verify-Static.ps1` passed.

## Captures

Saved under `C:\UnityProjects\Modules\Temp\stage2\` (Git-ignored), each inspected:

- `01-overview.png` — `guard.aibt`: top-down tree, kind accents, summaries, order badges, minimap,
  tree list with Guard/Wander `Valid`, Blackboard panel, one info note.
- `02-selected-node-inspector.png` — "Run to target" selected: typed fields, greyed contract
  default (`face-direction`), blackboard-key popup filtered to Float3.
- `04-validation-error.png` — a copy of guard with an undeclared key: red badge and border on the
  node, `1 error` status, AIBT2028 in the Problems list naming the node.
- `05-live-guard-play.mp4` (40 s, window recording) with stills every 4 s and
  `05-live-guard-play-contact.png` — `SimpleAgent.unity` in Play Mode with Guard selected: the
  graph attaches on selection; the highlight and active path move Patrol (0 s) → Chase with a green
  flash on "Sees target?" and grey abort fade on Patrol (8 s) → Patrol (16 s) → Chase (24–32 s);
  live blackboard values update.

## Not done / limits

- Node-search capture: `SearchWindow` opens (`OpenNodeSearch` returns true) but closes at once
  because it loses focus while this Editor is not the foreground application, and the desktop was
  in active use, so no screenshot. Its content is covered by `NodeSearchGroupsNodesByKind`.
- A node that starts and finishes between two 30 Hz samples is not flashed.
- Native `ProductionTreeHost` debugging still uses the Trace window, not in-graph animation.
- Found in this host, not changed: the Editor did not drain `EditorApplication.delayCall` (44
  queued callbacks, Unity's own included), so `ProjectNodeCatalogCache` never refreshed after a
  domain reload. The graph window now discovers the catalog itself once per reload and restores
  and frames through `EditorApplication.update`.
- UnityTest coroutines that fail mid-way leak their test window (nested enumerators skip the
  `finally`); the leaked windows from failing runs during development were closed.
