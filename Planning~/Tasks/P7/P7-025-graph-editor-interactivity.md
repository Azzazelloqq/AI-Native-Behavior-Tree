# P7-025 — Graph editor interactivity and readability

Status: `Done`

## Objective

Live usability review this session (owner, after seeing a real tree rendered) found the `AIBT Graph`
window genuinely hard to use compared to other graph tools: **no pan, no zoom, no drag/selection at
all.** Confirmed by reading the code, not assumed: `Editor/Graph/BehaviorTreeGraphView.cs` extends
Unity's own `GraphView`, but nowhere in the whole codebase (grepped, excluding `Spikes~/`) is
`ContentZoomer`/`ContentDragger`/`SelectionDragger`/`RectangleSelector` ever added via
`AddManipulator` — the standard manipulators every other Unity `GraphView`-based tool (Shader Graph,
Visual Scripting) wires up. This is a small, mechanical, well-understood gap, not an architectural
one.

Two more real, verified gaps found in the same review:

1. **Node titles are unreadable.** `NodeManifest` (`Authoring/Model/Nodes/NodeManifest.cs`) has no
   `Title`/`DisplayName` field at all — only the canonical dotted `TypeId` (e.g.
   `"aibt.core.parallel"`), `Summary`, and `Category`. `BehaviorTreeNode` renders `TypeId` as the
   node's title text, which is exactly what the owner saw and objected to.
2. **Layout is never actually persisted or read.** `BehaviorTreeGraphView.Populate`'s own doc
   comment admits positions are "a transient default until a real `*.aibt.layout.json` reader exists
   (P3-005)" — but `P3-005` is `Done`, and a full layout-persistence subsystem already exists and is
   unused by this window: `Editor/Organization/LayoutPersistenceController.Load(treeJsonPath,
   semanticTree)` already returns a real `LayoutLoadResult` (`Document`/`Diagnostics`/`usedDefault`).
   `BehaviorTreeGraphWindow`/`BehaviorTreeGraphView` never calls it, always falling back to a fresh
   depth/column grid every time a tree is opened.

## Depends on

- `P3-003` (graph adapter foundation — this card's own base).
- `P3-005` (layout persistence — already `Done`; this card wires the window to the API it already
  built, does not build new persistence logic).

## Required reading

- `Editor/Graph/BehaviorTreeGraphView.cs`/`BehaviorTreeGraphWindow.cs`/`BehaviorTreeNode.cs` (the
  three files this card touches).
- `Editor/Organization/LayoutPersistenceController.cs` (`Load`/`Save`/`LayoutPathFor` — the existing,
  already-tested API to wire in for real layout reads).
- `Authoring/Model/Nodes/NodeManifest.cs` and every `BuiltInNodeManifests.cs` construction site (a
  new `Title` field, if added, is a constructor-signature change touching every existing manifest —
  confirm the real blast radius before committing to the shape).
- Any other `GraphView`-based Unity Editor tool's own manipulator setup (public precedent for the
  standard `ContentZoomer`/`ContentDragger`/`SelectionDragger`/`RectangleSelector` wiring) —
  confirm current Unity/`UnityEditor.Experimental.GraphView` API surface for this Editor version
  (`6000.5.8f1`) before assuming an older tutorial's exact API still matches.

## Allowed changes

- `Editor/Graph/BehaviorTreeGraphView.cs` — add standard interaction manipulators; call
  `LayoutPersistenceController.Load` and use its real positions when available, keep the existing
  grid fallback only when `usedDefault` is true.
- `Editor/Graph/BehaviorTreeGraphWindow.cs` — thread the tree's own JSON path through so the layout
  sibling file can be located (`LayoutPathFor`).
- `Editor/Graph/BehaviorTreeNode.cs` (or wherever the node's title label is set) — render a
  human-readable title.
- `Authoring/Model/Nodes/NodeManifest.cs` and every built-in/sample manifest — a new optional
  `Title`/`DisplayName` field, defaulted sensibly (e.g. derived from the last `TypeId` segment) where
  not explicitly supplied, so this is additive, not a forced rewrite of every manifest.
- `Planning~/Evidence/P7-025/`.

## Forbidden changes

- **Still read-only.** This card does not add node creation, deletion, or edge editing via the UI —
  `BehaviorTreeGraphView`'s own doc comment ("Never mutates the document and never writes to disk")
  stays true. Authoring-via-UI is a separate, much larger future card, not silently folded in here.
- No change to the canonical `TypeId` scheme, `.aibt.json` format, or `NodeTypeIdRules` — a display
  title is presentation-only, never a second identity for a node type.

## Deliverables

- Pan, zoom, box-select, and single-click-select all work in `AIBT Graph` against a real tree.
- A real, previously-authored `.aibt.layout.json` (once one exists — see `P7-023`) is actually read
  and used; nodes are not silently re-gridded on every open.
- Node boxes show a human-readable title, not the raw dotted `TypeId`.

## Acceptance criteria

- Live proof: opening a real tree in `AIBT Graph`, the owner can scroll-zoom, drag-pan, and
  box-select nodes.
- Live proof: a tree with a real, hand-adjusted layout file reopens at the adjusted positions, not a
  fresh grid.

## Required verification

```text
Verify-Static.ps1
Run-UnityTests.ps1 -Mode EditMode -Scope Full
live interaction proof via Unity MCP + a real screenshot
```

## Handoff notes

- Completed 2026-09-04: standard pan/zoom/select/box-select are active; stored layouts load through
  P3-005, invalid layouts expose diagnostics, missing layouts retain the default, and titles are
  readable without changing semantic identity. Live mouse-event proof and screenshot passed.
  Focused graph tests 9/9; full host EditMode 1732/1735 with the same three established failures.
  Static verification passed (7 schemas, 137 items). See [evidence](../../Evidence/P7-025/README.md).

- Spun off from a direct owner usability review this session (2026-09-03), after a live demo of
  `tree.golden.parallel-decorator` in `AIBT Graph` surfaced these three gaps in person. Confirmed in
  scope for `1.0`.
