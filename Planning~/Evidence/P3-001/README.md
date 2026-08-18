# P3-001 editor graph framework spike evidence

Observed on 2026-08-18 with Unity `6000.5.8f1` on Windows 11 x64. Unity Graph
Toolkit is a built-in Editor module as of Unity 6000.4 (the
`com.unity.graphtoolkit` package entry in the host project's
`Packages/manifest.json` is a dead shim; its own `package.json` states
`"type": "shim"` and that the real implementation moved into the Editor).
The evaluated API is `UnityEditor.GraphToolkitModule.dll`, namespace
`Unity.GraphToolkit.Editor`, inspected both from its shipped XML API docs and
by live reflection from inside the spike harness (37 public types found by
reflection; not merely the 19 originally located by a doc-text scan).

## Result

- **Serialization control — fails.** A `Graph` subclass constructed with a
  plain `new()` throws `InvalidOperationException: Only Graph instances
  returned by either GraphDatabase.LoadGraph or GraphDatabase.CreateGraph are
  valid.` the moment a node is added. `Graph` is not a `ScriptableObject`
  (confirmed: `GRAPH_IS_SCRIPTABLE_OBJECT=False`), but the framework still
  enforces database/asset backing at runtime — there is no supported
  in-memory-only mode. The only valid construction path is
  `GraphDatabase.CreateGraph<T>(assetPath)`, which writes a real Unity asset
  immediately.
- **Persisted format is Unity's native YAML object serialization**, not a
  lightweight or JSON-like format: `%YAML 1.1`, `tag:unity3d.com,2011`, a
  `MonoBehaviour:` block with `m_ObjectHideFlags`/`m_Script: {fileID:
  12501, guid: ...}` and so on — the same shape any ordinary
  `ScriptableObject` asset uses.
- A synthetic 240-node representative tree (sequence/selector/condition/
  action-shaped nodes, connected root-to-leaf) built and saved successfully:
  construction `169.08 ms`, save `28.25 ms`. The resulting `.aibtspike` asset
  is **684,296 bytes — ≈2.85 KB per node**.
- Reloading the saved asset headlessly via `GraphDatabase.LoadGraph` (no
  window, no `-nographics` restriction issue) reproduced all 240 nodes
  exactly. Headless EditMode testability of graph/node/port state is viable.
- **Extensibility — fails a required item.** Reflecting over all 37 public
  types for anything matching Group/Comment/StickyNote/Reroute/Window/View
  found **zero** matches. There is no `Window`/`GraphView`-shaped type in the
  public API either — Unity most likely supplies the editing surface
  automatically for any `[Graph]`-attributed asset type, which was not
  independently confirmed since no live GUI session was available in this
  pass (see Scope and limitations).
- The API itself is attribute-driven (`[Graph(extension, options)]`,
  `[Node(categoryPath, ...)]`) and reasonably rich for the data-model half of
  the job: typed ports (`IPortDefinitionContext.AddInputPort`/
  `AddOutputPort`), wires (`Graph.Connect`/`Disconnect`), variables,
  constant nodes, subgraphs, and a `BlockNode`/`ContextNode` pattern (Shader
  Graph-style stacks) that AIBT's flat parent/child tree shape doesn't need.

## Decision

See `ADR-P3-001-editor-graph-framework.md` (`Status: Proposed`). Recommendation:
**reject Unity Graph Toolkit** for AIBT's visual editor. Two of the four
evaluation criteria fail on evidence, not assumption:

1. It cannot be used as a transient, AI/MCP-friendly view over `.aibt.json`.
   Every graph must exist as a real Unity YAML asset the moment a node is
   added; the only way to avoid a *committed* secondary asset is an
   ephemeral create → edit → extract-to-`.aibt.json` → delete cycle, adding
   real engineering cost (a ~2.85 KB/node Unity-specific YAML format an AI
   agent would never author directly) to preserve `.aibt.json` as the single
   source of truth.
2. Groups, comments, sticky notes, and reroutes — all four explicitly
   "Required authoring tools" in `Documentation~/editor-and-layout.md` — do
   not exist anywhere in the public API. There is no partial-credit
   workaround available from outside the module.

Construction/save/headless-reload performance at 240 nodes showed no red
flag on its own, but that finding does not offset the two failures above.

## Scope and limitations

- **No live GUI interaction was measured.** No Unity MCP bridge was
  connected in this session and no interactive Editor session was available,
  so the card's literal acceptance criterion ("lets a user reposition/
  connect at least 200 nodes... measured and recorded") was answered only
  for the *data-model* side (construction/save/load timings above), not
  actual pointer-driven interaction latency in whatever window Unity
  provides for a `[Graph]` asset. This is a real gap, not a rounding error —
  but it does not change the recommendation, since the two disqualifying
  findings above are independent of interaction latency.
- Whether Unity auto-generates a usable default editor window for a
  `[Graph]`-attributed asset (and what that window can/cannot do) was not
  directly observed, only inferred from the absence of any `Window`/
  `GraphView` type in the public API.
- This spike did not evaluate any alternative (a custom UI Toolkit graph
  view, or the older `UnityEditor.Experimental.GraphView` API some other
  Unity tools still use). Per the task card's own handoff note, rejecting
  Graph Toolkit does not by itself authorize adopting a specific alternative
  — that needs its own evaluation before any P3-002+ card can proceed.
- All measurements are from this single workstation, this Unity version,
  and this synthetic fixture shape; no generalization beyond that is made.

Machine-readable findings (including the full 37-type inventory and every
raw measurement) are in `verification-results.json`. Harness source is
`Spikes~/EditorGraphFramework/`.
