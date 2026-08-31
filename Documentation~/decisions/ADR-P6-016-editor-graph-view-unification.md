# ADR P6-016: Editor graph-view unification

- Status: Accepted 2026-08-31
- Date: 2026-08-31
- Decision ID: AIBT-032

## Context

Eight Editor tools built across Phase 3 and Phase 5 (auto-layout, manual organization, semantic
editing, validation UX, reference-oracle preview, native execution debugger, trace views, hot-reload
workflow) each independently disclosed the same limitation: none is wired into `Editor/Graph/
BehaviorTreeGraphWindow.cs`'s actual live `GraphView` window. This card decides the integration
architecture on paper, backed by a disposable spike against two representative tools.

## Real finding: only three of the eight tools own any `GraphView` at all

Reading every one of the eight tools' own directories found the "eight tools" are not architecturally
uniform, and treating them as one integration problem would have been wrong:

- **View-owning (own a private `BehaviorTreeGraphView`, confirmed by grep):** `BehaviorTreePreviewWindow`
  (`P3-009`) and `TraceTimelineWindow` (`P3-011`) each construct `new BehaviorTreeGraphView()`
  independently -- a real, duplicated live-view problem, exactly what "unification" should mean.
- **Own a window, but no `GraphView`:** `HotReloadWorkflowWindow` (`P5-008`) -- a standalone
  controls/status window with no tree visualization at all.
- **No window, no view -- pure library code operating on `TreeDocument`/`LayoutDocument` directly:**
  auto-layout (`P3-004`), manual organization (`P3-005`), semantic editing (`P3-006`,
  `Editor/Editing/SemanticEditOperations`), validation UX (`P3-008`), and the native execution
  debugger (`P3-010`, confirmed this session during `P6-015`'s own research -- `NativeExecutionDebuggerSession`
  is a plain class with no `EditorWindow` anywhere in `Editor/Debugger/`).

Five of the eight tools have no view to "unify" at all -- for them, "wiring" can only mean adding UI
affordances to `BehaviorTreeGraphWindow` that call their already-public, already-working library
APIs, never a view-sharing mechanism.

## Spike evidence (`Spikes~/EditorGraphViewUnification/`, 2026-08-31, this workstation)

A disposable NUnit spike (`SpikeEditorGraphViewUnification`, run live via Unity MCP `run_tests`)
tested at the `BehaviorTreeGraphView` level directly (not through a real, shown `BehaviorTreeGraphWindow`
instance, to avoid opening a visible window in the live Editor session this spike ran in -- a real,
disclosed simplification, not the shipped design).

1. **View-owning tool pattern (`P3-009`/`P3-011`).** One `BehaviorTreeGraphView` instance, held by
   two independent consumers (standing in for `BehaviorTreeGraphWindow` itself and a second tool
   attaching to it). Both consumers observed the *identical* node objects by reference, not merely
   equal data -- proving real sharing, the actual property "unification" requires (a trace tool
   highlighting a node highlights the exact object the user is editing, not a stale copy). **Passed.**
2. **Document-operating tool pattern (`P3-006`).** Using the same shared view: called
   `SemanticEditOperations.AddNode` (a real, already-public, pure function) on the view's current
   document, then re-`Populate`-d the same view with the result. The new node appeared immediately,
   visible to both consumers -- confirming this category of tool needs **no new mechanism at all**,
   just calling its existing API and re-populating whichever view is already on screen. **Passed.**

Full raw output is in `Planning~/Evidence/P6-016/README.md`.

## Decision

Tool-by-tool, not a blanket "wire everything in":

1. **`BehaviorTreePreviewWindow` (`P3-009`) and `TraceTimelineWindow` (`P3-011`): integrate, by
   attaching to a shared view.** `BehaviorTreeGraphWindow` gains one small, additive public accessor
   (e.g. `public BehaviorTreeGraphView View => _view;`) so these two tools can attach to and
   annotate the SAME live view instead of each constructing its own private, disconnected copy.
   This is the one real "unification" case among the eight.
2. **`HotReloadWorkflowWindow` (`P5-008`): stays standalone.** It owns no `GraphView`, and a reload
   workflow's own focused, sequential flow (classify → migrate/restart → report) genuinely benefits
   from a dedicated window rather than being squeezed into panel space alongside live graph editing
   -- a real, reasoned exception, not an assumption that everything should integrate.
3. **Auto-layout (`P3-004`), manual organization (`P3-005`), semantic editing (`P3-006`), validation
   UX (`P3-008`), and the native execution debugger (`P3-010`): integrate as UI commands calling
   existing APIs, not as view-sharing.** None of these five needs a shared-view mechanism at all --
   each future implementation card just adds a menu item/toolbar command/panel to
   `BehaviorTreeGraphWindow` that calls the tool's own already-public function and re-`Populate`s
   the (now-shared, per decision 1) view with the result. This is a materially smaller, more
   mechanical integration than decision 1's, and each can ship independently.
4. **No `BehaviorTreeGraphWindow`-hosts-everything-as-tabs redesign.** The spike and this analysis
   found no evidence that a bigger structural rewrite of `BehaviorTreeGraphWindow` itself is
   necessary -- a small accessor plus per-tool UI commands is sufficient for all eight cases.

## Consequences

- Two future implementation cards apply decision 1 to `BehaviorTreePreviewWindow` and
  `TraceTimelineWindow` respectively (adding the `View` accessor to `BehaviorTreeGraphWindow` once,
  the first time either lands).
- Five future implementation cards (one per tool, per this project's own "no unrelated changes
  bundled into one commit" discipline, matching this card's own Handoff notes) apply decision 3 for
  `P3-004`/`P3-005`/`P3-006`/`P3-008`/`P3-010`.
- `HotReloadWorkflowWindow` requires no future integration card at all under this decision, unless a
  later, separately-justified reason to reconsider its standalone status emerges.
- `BehaviorTreeGraphAdapterTests` (the view's own accepted test suite) and
  `SemanticEditOperationsTests` (`P3-006`'s own accepted suite) both re-run unmodified, still
  passing -- this decision weakens neither tool's existing guarantees.

## Explicitly unverified (stated, not generalized)

- The real `BehaviorTreeGraphWindow`/`EditorWindow` hosting behavior (window lifecycle, docking,
  `rootVisualElement` timing) was not exercised end-to-end -- this spike deliberately tested at the
  `BehaviorTreeGraphView` level to avoid opening a visible window in the live Editor session it ran
  in. A future implementation card must verify the real `EditorWindow`-level accessor and attachment
  flow, not assume it generalizes automatically from this spike.
- The native execution debugger's (`P3-010`) own future integration was reasoned about (it has no
  view, so it fits decision 3) but not itself spiked -- `P6-015`'s own research this session already
  established it has no `EditorWindow`/view, which this ADR relies on rather than re-deriving.
- Which specific UI affordance (menu item, toolbar button, context menu, side panel) each of the
  five decision-3 tools should get was not designed here -- left to each tool's own future
  implementation card.
