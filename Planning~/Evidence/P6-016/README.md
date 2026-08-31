# P6-016 Editor graph-view unification decision evidence

## Result

Done, accepted, tool-by-tool (not a blanket "wire everything in"). `ADR-P6-016` (`AIBT-032`) found
the "eight tools" are not architecturally uniform: only two (`BehaviorTreePreviewWindow`/
`TraceTimelineWindow`) actually own a duplicated `GraphView` worth unifying. Five own no view at all
(they integrate as UI commands, not view-sharing); one (`HotReloadWorkflowWindow`) has a good reason
to stay standalone.

## Real finding: five of the eight tools have no view to "unify"

Reading every one of the eight tools' own directories (not assuming uniformity) found:
`BehaviorTreePreviewWindow`/`TraceTimelineWindow` each construct their own private
`new BehaviorTreeGraphView()`; `HotReloadWorkflowWindow` owns a window but no `GraphView`; auto-layout
(`P3-004`), manual organization (`P3-005`), semantic editing (`P3-006`), validation UX (`P3-008`),
and the native execution debugger (`P3-010`, confirmed during this session's own `P6-015` research)
own no window or view at all -- pure library code operating on documents directly. Treating all
eight as needing the same integration mechanism would have been a real mistake this card's own
Forbidden-changes clause explicitly warned against assuming.

## Verification

```text
Disposable spike (SpikeEditorGraphViewUnification, Tests/Editor/EditorGraphViewUnificationSpike/
  during this session, archived afterward): 2/2 tests passing, live via Unity MCP run_tests --
  SharedViewInstance_LetsTwoIndependentConsumersSeeTheIdenticalLiveNodes,
  DocumentOperatingTool_NeedsNoNewMechanism_JustCallsItsExistingApiAndRepopulatesTheSharedView
Regression (required by this card's own acceptance criteria, unmodified, live via Unity MCP):
  AIBT.Tests.Editor.Graph.BehaviorTreeGraphAdapterTests -- 3/3 passing
  AIBT.Tests.Editor.Editing.SemanticEditOperationsTests -- 4/4 passing
Verify-Static.ps1 -- passed
git diff --check -- clean
```

No production file (`Editor/Graph/`, or any of the eight tools' own files) was touched, per this
card's own Forbidden-changes clause. The spike deliberately tested at the `BehaviorTreeGraphView`
level directly rather than through a real, shown `BehaviorTreeGraphWindow` -- constructing and
displaying a real `EditorWindow` here would have opened a visible window in the live Editor session
this spike ran in, a disclosed limitation stated plainly in the ADR, not the shipped design. The
spike lived temporarily in `Tests/Editor/EditorGraphViewUnificationSpike/`, then archived to
`Spikes~/EditorGraphViewUnification/` and deleted from `Tests/`, mirroring this session's own
established precedent.

## Handoff

Per this card's own Handoff notes discipline: two future implementation cards apply the shared-`View`
accessor to `BehaviorTreePreviewWindow`/`TraceTimelineWindow`; five more (one per tool) add UI
commands to `BehaviorTreeGraphWindow` for the document-operating tools; `HotReloadWorkflowWindow`
needs no future card under this decision.
