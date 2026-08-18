# P3-014 GraphView editor framework spike evidence

Observed on 2026-08-18 with Unity `6000.5.8f1` on Windows 11 x64. The
evaluated API is `UnityEditor.Experimental.GraphView`, which ships in the
built-in Editor module assembly `UnityEditor.GraphViewModule` (confirmed by
live reflection from inside the spike harness, not a doc scan) — not a UPM
package, same as Graph Toolkit. This is the second and final planned
iteration of `OQ-005`'s spike per `P3-001`'s handoff note; results are
directly comparable to `Planning~/Evidence/P3-001/` since the harness
targets the same 240-node sequence/selector/condition/action/root fixture
shape.

## Result

- **Serialization control — passes.** `new BehaviorTreeGraphView()`
  constructs standalone with no asset, database, or window backing required
  (`GRAPHVIEW_CONSTRUCTED_STANDALONE=True`). `GraphView` has no built-in
  save/load of its own at all — AIBT would own 100% of the persisted format.
  A minimal custom snapshot (id/kind/x/y per node) serialized via
  `JsonUtility` round-tripped all 240 nodes correctly: `47.32 bytes/node`,
  `0.31 ms` to serialize. This number is **not a fair size comparison**
  against Graph Toolkit's measured `≈2.85 KB/node` — that figure was Unity's
  forced YAML asset format; this one is an intentionally minimal
  representative schema AIBT itself would design in `P3-002`, not something
  the framework imposes. The comparable fact is qualitative, not
  quantitative: nothing is written to disk unless AIBT's own layout format
  chooses to.
- **Extensibility — passes with one caveat.** Reflecting over all 71 public
  types in the `UnityEditor.Experimental.GraphView` namespace found real,
  native types for two of the four required authoring tools: `Group` and
  `StickyNote` (plus its `StickyNoteChange`/`StickyNoteTheme`/
  `StickyNoteFontSize` support types). `SearchTreeGroupEntry` also matched
  the substring scan but is unrelated to grouping nodes (it's a
  search-window UI type) — noted so the count isn't overstated. There is
  **no dedicated "Comment" type** (in practice `StickyNote` is Unity's own
  vehicle for free-form canvas comments — the same pattern Shader Graph
  uses) and **no dedicated "Reroute" type**: Unity's own shipped tools
  (Shader Graph, VFX Graph) implement edge reroutes as a custom node/element
  built on top of `Edge`/`EdgeControl`/`Node`, not a framework primitive.
  This is a real extension-point exercise AIBT would need to do itself, but
  it is *possible* here — unlike Graph Toolkit, where reflection found zero
  matches and no extension point at all.
- **Large-graph construction performance — no red flag.** Building the same
  240-node tree (random parent selection, seed `12345`, identical shape to
  `P3-001`'s fixture): `181.83 ms`, `239` edges connected with `0` connect
  failures. Comparable to Graph Toolkit's `169.08 ms` for the same shape —
  neither framework showed a construction-time problem at this size.
- **Testability — passes.** All measurements above ran headlessly in
  `-batchmode -nographics`, including a live `EditorWindow` host attempt:
  `EDITORWINDOW_HOST_SUCCEEDED=True` — a `GraphView` could be created and
  added to an `EditorWindow`'s visual tree without throwing, even without a
  display. (This does not by itself prove real pointer-driven interaction
  works — see Scope and limitations.)
- **Support-risk (fifth criterion added for this spike, per the user's
  explicit concern about `GraphView`'s "Experimental" status) — no red
  flag found.** Reflecting over the 71 public types in the namespace found
  **zero** `[Obsolete]`-attributed members (`OBSOLETE_TYPE_COUNT=0`), and
  `GraphView` itself is not marked obsolete
  (`GRAPHVIEW_TYPE_ITSELF_OBSOLETE=False`) in the installed `6000.5.8f1`
  Editor. This does not amount to a formal support guarantee — the
  namespace itself still carries Unity's standard "Experimental" disclaimer
  in its public documentation — but it rules out the concrete, checkable
  risk that the API is already flagged for removal in this Editor version.

## Decision

See `ADR-P3-014-editor-graph-framework.md` (`Status: Proposed`).
Recommendation: **adopt `UnityEditor.Experimental.GraphView`**, superseding
`ADR-P3-001`'s "pending second spike" state for `AIBT-012`. Unlike Graph
Toolkit, none of the four original evaluation criteria failed outright; the
one real gap (no native reroute primitive) has established precedent for a
custom extension within this same framework, not a hard blocker.

## Scope and limitations

- **No live GUI pointer interaction was measured**, same limitation
  disclosed in `P3-001`: no Unity MCP bridge or interactive Editor session
  was available in this pass, so "lets a user reposition/connect ≥200
  nodes" is answered only for programmatic construction/positioning cost
  (`181.83 ms` for 240 nodes), not actual drag-latency in a real window.
  `EDITORWINDOW_HOST_SUCCEEDED=True` shows hosting itself doesn't throw
  headlessly, which is a strictly stronger signal than `P3-001` could
  produce (Graph Toolkit's public API had no `Window`/`GraphView`-shaped
  type to even attempt this with), but it is still not proof of usable
  interaction latency.
- Reroute support is not native and was not itself implemented or timed in
  this pass — only confirmed to have a plausible, precedented extension
  path (`Edge`/`EdgeControl`). Building and measuring it is `P3-002`+ scope,
  not this spike's.
- The "Experimental" namespace disclaimer is a documentation-level status
  this spike cannot make disappear; only the concrete `[Obsolete]`-attribute
  check was verified, which is a necessary but not sufficient signal for
  long-term support.
- All measurements are from this single workstation, this Unity version,
  and this synthetic fixture shape; no generalization beyond that is made.

Machine-readable findings (the full 71-type inventory and every raw
measurement) are in `verification-results.json`. Harness source is
`Spikes~/EditorGraphFramework/HarnessGraphView/`.
