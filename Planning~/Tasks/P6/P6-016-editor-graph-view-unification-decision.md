# P6-016 — Editor graph-view unification decision

Status: `Draft`

## Objective

Decide whether and how to wire the eight standalone Editor tools built across Phase 3 and Phase 5
— auto-layout (`P3-004`), manual organization (`P3-005`), semantic editing (`P3-006`), validation
UX (`P3-008`), reference-oracle preview (`P3-009`), native execution debugger (`P3-010`), trace
views (`P3-011`), and the hot-reload workflow (`P5-008`) — into `Editor/Graph/
BehaviorTreeGraphWindow.cs`'s actual live `GraphView` window, rather than each continuing to own a
private, disconnected view/window.

This card exists because every one of those eight cards independently disclosed the identical
limitation as "out of scope, flagged as a follow-up" rather than building the wiring themselves
(each was scoped to prove its own mechanism works in isolation, never to integrate it) --
`MASTER_PLAN.md`'s own Phase 3 narrative states this explicitly for `P3-004`-`P3-008` ("None of
... are wired into `Editor/Graph/`'s live UI"), and `P3-009`-`P3-011`/`P5-008` each repeat the same
disclosure individually. The result today is a working graph editor (`BehaviorTreeGraphView`) that
cannot preview, debug, trace, or hot-reload the tree it displays, and seven other windows that can
do those things only against a private, non-interactive `BehaviorTreeGraphView` instance they each
construct for themselves.

## Depends on

- `P3-013` (Phase 3 integration gate, done).
- `P5-010` (Phase 5 integration gate, done).

## Required reading

- `Editor/Graph/BehaviorTreeGraphWindow.cs` and `BehaviorTreeGraphView.cs` -- the real, live window
  this card may recommend extending.
- `Editor/Preview/BehaviorTreePreviewWindow.cs` (`P3-009`), `Editor/Debugger/` (`P3-010`),
  `Editor/Trace/TraceTimelineWindow.cs` (`P3-011`), `Editor/HotReload/HotReloadWorkflowWindow.cs`
  (`P5-008`) -- each constructs its own private `BehaviorTreeGraphView` instance rather than
  reusing a shared one; read each constructor to confirm exactly what would need to change.
- `Editor/Organization/` (`P3-005`), `Editor/Editing/` (`P3-006`), `Editor/Validation/` (`P3-008`)
  -- operate on `TreeDocument`/`LayoutDocument` directly, not on a live view at all; confirm
  whether "wiring" for these means something different (exposing their operations as live window
  commands) than for the view-owning tools above.
- `Planning~/Evidence/P3-004/` through `P3-011/` and `P5-008/`'s own "known limitations" sections
  -- the eight original disclosures this card resolves; do not re-derive from scratch.

## Allowed changes

- `Spikes~/EditorGraphViewUnification/` (new, disposable) -- proves the recommended integration
  shape against the real `BehaviorTreeGraphWindow`, mirroring `P6-002`'s/`P6-013`'s own
  spike-before-ADR methodology.
- `Planning~/Evidence/P6-016/`.
- One proposed ADR.

## Forbidden changes

- Any production change to `Editor/Graph/`, or to any of the eight tools' own files -- this card
  decides on paper (backed by a disposable spike); a separate future card (or cards, one per tool,
  per this card's own recommendation) implements the accepted decision.
- Silently narrowing any of the eight tools' own already-accepted guarantees (parity proofs,
  isolation proofs, allocation proofs) to make integration easier -- if the recommended design
  would require that, say so explicitly rather than doing it.
- Assuming all eight tools should integrate the same way -- confirm on evidence which (if any)
  should stay standalone (e.g. because their own workflow genuinely benefits from a modal/focused
  window) rather than treating "wire everything in" as a foregone conclusion.

## Deliverables

- A decision on the integration architecture: does `BehaviorTreeGraphWindow` host the others as
  panels/tabs of one window, do the others attach to a shared `BehaviorTreeGraphView` instance
  passed in rather than constructing their own, or some other shape -- backed by which of the
  eight tools were actually tried in the spike.
- A disposable spike proving the recommended shape against the real, unmodified
  `BehaviorTreeGraphWindow`/`BehaviorTreeGraphView` for at least two of the eight tools (recommend
  one view-owning tool, e.g. preview or the native debugger, and one document-operating tool, e.g.
  semantic editing) without breaking either tool's own already-accepted tests.
- A proposed ADR recording the decision, its rationale, and an explicit priority/sequencing
  recommendation for the remaining tools' own future implementation cards.

## Acceptance criteria

- The spike demonstrates the recommended architecture against the real, unmodified
  `BehaviorTreeGraphWindow` -- not a hypothetical description.
- A regression check confirms nothing in this investigation weakens any of the eight tools' own
  accepted guarantees (re-run their existing test suites unmodified).
- The ADR states plainly, tool by tool, whether it should integrate (and how) or stay standalone,
  with a real reason for each -- not a blanket "yes."

## Required verification

```text
Verify-Static.ps1
disposable spike: real BehaviorTreeGraphWindow, live Unity MCP execute_code
regression: the two tools spiked against, unmodified, still passing
```

## Handoff notes

- Not required for the Phase 6 integration gate (`P6-012`) -- discovered as cross-phase debt
  during a Phase 6 session, mirroring `P6-013`/`P6-014`/`P6-015`'s own pattern. `P6-012` does not
  depend on it.
- If accepted, expect one future implementation card per integrated tool rather than one giant
  card, matching this project's own "no unrelated changes bundled into one commit" discipline.
