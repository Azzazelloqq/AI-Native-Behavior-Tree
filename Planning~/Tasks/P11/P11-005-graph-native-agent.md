# P11-005 — A truthful AIBT Graph for a native agent

Status: `In Progress` — implemented on `feature/aibt-consumer-workflow` after `2fff7e5`; awaits the
orchestrator's acceptance.

## Objective

Two defects of the AIBT Graph window seen with the P11-002 recipe tree and the Native Guard sample,
approved for fixing by the owner ("find the cause, then fix"):

1. With the graph attached to a `NativeTreeAgent`, the blackboard panel shows `live: —` for every key.
   Cause: `GraphLiveDebugger.TryFormatValue` reads only the managed instance. Fix: read Tree-scope keys
   through `NativeTreeAgent.Host.TryReadTreeValue` (ADR-P11-004 D7).
2. The tree list shows "Needs native compile" for a native tree whose generated code exists and whose
   prepared runtime asset is running. Cause: `TreeCatalogEntry.Read` sets that status for every valid
   tree with a native user node, without checking anything.

## Depends on

- `P11-004` (the internal type-erased read), `P11-001` (subtree key maps), `P11-002` (Native Guard tree).

## Status rule (written before the code)

A tree whose nodes are all known, valid and not managed, and that has at least one project node
without a reference handler (a native node, the same test as before), gets "Needs native compile"
only when the project catalog the list was built with has no generated descriptor
(`ProjectNodeCatalog.Generated`) for one of those node types: its generated code is missing, so no
runtime asset can be prepared from it. Otherwise it is "Valid", and the detail says what is known:
the native nodes have generated code, and either the prepared runtime assets (`ProductionTreeAsset`)
compiled from this tree, as its source or as one of its subtrees, by path, or that no runtime asset
is prepared from it yet. The list does not check whether a prepared asset is stale (the asset's
inspector does, and assets are re-prepared on save and after a script reload). A caller that passes
only a registry, without the project catalog, gets "Valid" with a detail that says generated code was
not checked. The facts are gathered once per list refresh: one dictionary lookup per node and one
`AssetDatabase.FindAssets("t:ProductionTreeAsset")` with a load of each found asset; no file-system scan.

Implementation note (after measuring): that `FindAssets` costs about 12 ms, so its result is cached
until an `.asset` file is imported, deleted or moved, or the domain reloads. See the evidence.

## Scope

- `Editor/Graph/GraphLiveDebugger.cs`, `Editor/Graph/BehaviorTreeGraphWindow.Inspector.cs`,
  `Editor/Graph/BehaviorTreeGraphWindow.Chrome.cs`, `Editor/Graph/TreeCatalogEntry.cs`.
- Editor tests under `Tests/Editor/Graph/`; the one changed expectation in
  `Tests/Editor/Validation/TreeScopeSlotNeverWrittenNativeBindingTests.cs`.
- The graph window page under `Documentation~/`, `CHANGELOG.md`, planning and evidence.

## Forbidden changes

- Anything under `Runtime/`, `Authoring/`, `Agents/`, `MCP/`, `CodeGen~`; ABI v2, the compiled program
  format, the package version, `Documentation~/specifications/`, execution semantics, the behaviour of
  `ProductionTreeHost`, `NativeTreeAgent`, `ProductionTreeScheduler`.

## Acceptance criteria

- Attached to a native agent, the panel shows `live: <value>` for every Tree-scope key; `live: —`
  without a run, for an unknown key and for a key of another scope; the last text is kept while the
  host executes; values return to defaults when a new run starts.
- `Format(BlackboardValue)` prints every built-in type a Tree key can have.
- A subtree window shows the right value or `live: —`, never a wrong one.
- The tree list follows the status rule above; refresh time measured before and after.
- Live evidence: before/after pictures of the panel and the list, a 20–30 s clip of the window.

## Required verification

```text
Tools/Unity/Invoke-UnityRecompile.ps1
Tools/Unity/Invoke-UnityTests.ps1 -Filter GraphNativeLiveBlackboardTests,TreeCatalogNativeStatusTests,TreeScopeSlotNeverWrittenNativeBindingTests,GraphEditorWorkflowTests,SubtreeGraphEditorTests
Tools/Unity/Save-UnityClip.ps1 -Scene Assets/AibtNativeGuardDemo/NativeGuard.unity -Play -Source window -Window 'AIBT Graph'
Tools~/Verification/Verify-Static.ps1
```

## Handoff notes

- Evidence: `Planning~/Evidence/P11-005/README.md`; raw files in `C:\UnityProjects\_aibt_acceptance\P11-005\`.
- The full run of all test assemblies was not made here; the orchestrator makes it.
- `README.md` is outside the allowed paths and was not edited; its status list stays true.
