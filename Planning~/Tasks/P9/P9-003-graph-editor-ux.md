# P9-003 — Graph editor usability and live debugging

Status: `Review`

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

## Verification

Updated and new EditMode tests in `Tests/Editor/Graph/` (adapter tests moved to the new UI
contract; `GraphEditorWorkflowTests` for summaries, reorder/duplicate/detach, edit history, node
search, incremental sync and order badges, window create/connect/reorder/delete/undo, Unity Undo
bridge, one Save, problem badges, dirty-open banner, managed sample status); full AIBT EditMode
assemblies; `Verify-Static.ps1`; real Editor screenshots and a Play Mode clip of the graph through
the Unity CLI.

## Handoff

Task ID: P9-003. Outcome: implemented and verified in the `Modules` host on Unity 6000.7.0b1;
status Review, not Done. Branch: feature/aibt-consumer-workflow; local commits only, no push.
Verification: `AIBT.Runtime.Tests` 707/707, `AIBT.Editor.Tests` 563/565 (the two pre-existing
host-only MCP catalog/hash failures), `AIBT.Integration.Tests` 58/58,
`AIBT.BehaviorCases.Tests` 65/65; `Verify-Static.ps1` passed.
See ../../Evidence/P9-003/README.md.
Known limits: the node-search popup was not captured (it closes when the Editor is not the
foreground application); live state is sampled at about 30 Hz; native hosts keep the trace
window path rather than the in-graph animation.
