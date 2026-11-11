# P9-003 — Graph editor usability and live debugging

Status: `Draft`

## Authorization and dependencies

Owner approved in chat on 2026-11-11 (complaints: window visuals, animation, "not really
working"). Depends on P9-001. Replaces the presentation of P8-001/P8-006 while reusing their
editing model.

## Objective

Make the graph editor a comfortable daily tool: styled nodes, context menu and node search,
keyboard shortcuts, one toolbar, one save, one undo history, a typed inspector, a diagnostics
list and live execution highlighting inside the graph for managed agents and native hosts.

## Plan and owned paths

Rewrite `BehaviorTreeGraphWindow`, `BehaviorTreeGraphView` and `BehaviorTreeNode` over the
existing `GraphEditSession`, `SemanticEditOperations`, `LayoutPersistenceController` and
`DeterministicAutoLayoutService`. Layout never controls child order (AIBT rule unchanged).
UI tests bound to element names are updated where the UI contract intentionally changes;
behavior checks (save, external-change conflict, undo) are preserved.

Owned: `Editor/Graph/`, `Editor/Trace/`, `Editor/Preview/`, `Editor/Debugger/`,
`Tests/Editor/Graph/`, `Tests/Editor/Trace/`, `Tests/Editor/Preview/`,
`Documentation~/editor-and-layout.md`, `CHANGELOG.md`, `Planning~/Evidence/P9-003/`.

## Acceptance

Create, connect, reorder, delete, undo and save a tree without the top toolbar; search-create a
node; see Running/Success/Failure animate on the graph in Play Mode. Verified with real Editor
screenshots before and after.
