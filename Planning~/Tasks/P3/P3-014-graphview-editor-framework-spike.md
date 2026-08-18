# P3-014 — GraphView editor framework spike

Status: `Done`

## Objective

Resolve the remainder of `OQ-005` (`Documentation~/decisions.md`'s
`AIBT-012`, "Pending spike") after `P3-001` rejected Unity Graph Toolkit: a
measured spike evaluating `UnityEditor.Experimental.GraphView` as AIBT's
visual editor graph technology, or record why it too is unacceptable.

## Depends on

- `P3-001`.

## Required reading

- `Documentation~/architecture.md`
- `Documentation~/decisions.md` (`AIBT-010`, `AIBT-012`)
- `Documentation~/editor-and-layout.md`
- `Planning~/Evidence/P3-001/README.md` and
  `Planning~/Evidence/P3-001/ADR-P3-001-editor-graph-framework.md` (the
  criteria and evidence shape this card must match for a fair comparison)

## Allowed changes

- `Spikes~/EditorGraphFramework/HarnessGraphView/`
- `Planning~/Evidence/P3-014/`
- One proposed ADR; integration owner applies accepted decision updates to
  `Documentation~/decisions.md` and `package.json`.

## Forbidden changes

- Production `Editor/` implementation, `.aibt.layout.json` format decisions,
  or any `package.json` dependency addition. `UnityEditor.Experimental.GraphView`
  is a built-in Editor module (ships in `UnityEditor.Graphs.dll`), not a UPM
  package, so no `package.json` entry is expected either way.
- A recommendation based on toy graphs; the spike must exercise a graph
  shape representative of `Documentation~/editor-and-layout.md`'s
  large-graph and readability requirements, matching `P3-001`'s fixture
  shape closely enough for a fair comparison.
- Modifying `Spikes~/EditorGraphFramework/Harness/` (the `P3-001` evidence
  trail stays untouched).

## Deliverables

- A disposable harness (`Spikes~/EditorGraphFramework/HarnessGraphView/`)
  driving `UnityEditor.Experimental.GraphView` against a synthetic tree of
  the same shape and size `P3-001` used (240 nodes,
  sequence/selector/condition/action/root archetypes).
- Evaluation against the same four criteria `P3-001` used — serialization
  control, large-graph interaction performance, extensibility (custom node
  visuals, comments/groups/reroutes), testability (can graph state be
  asserted from EditMode tests) — **plus a fifth**: concrete package/API
  support-risk evidence. `GraphView` lives under the `Experimental`
  namespace with no formal deprecation notice found, and Unity's own shipped
  Shader Graph source currently depends on it; this card must check the
  *installed* `6000.5.8f1` Editor directly (reflect the loaded types for
  `[Obsolete]` attributes; record the exact assembly/module `GraphView`
  ships from) rather than rely on a web search alone.
- A recommendation: adopt `GraphView`, drop to a from-scratch custom UI
  Toolkit view, or reject both and report why — with evidence for each
  candidate actually evaluated in this card.
- A proposed ADR explicitly superseding `ADR-P3-001`'s "pending second
  spike" recommendation for `AIBT-012` (neither ADR has been promoted into
  `Documentation~/decisions.md` yet; that remains the integration owner's
  separate step).

## Acceptance criteria

- The evaluated graph renders and lets a user reposition/connect at least
  200 nodes without becoming unusable, measured and recorded rather than
  asserted. If true pointer-driven GUI latency cannot be measured in this
  pass (no interactive Editor/MCP session, same limitation `P3-001`
  disclosed), the acceptance criterion is answered for whatever can
  actually be measured headlessly (construction/layout/reflow cost), and the
  gap is stated explicitly, not silently generalized into a pass.
- Serialization behavior (what the framework itself persists vs. what AIBT
  must own) is documented precisely enough that `P3-002` can specify
  `.aibt.layout.json` against it.
- The `[Obsolete]`/support-risk check (fifth criterion above) is answered
  from direct reflection against the installed Editor, not assumption.
- The recommendation states exactly what remains unverified rather than
  generalizing.
- No production code depends on the recommendation until the ADR is
  separately accepted.

## Required verification

```text
Verify-Static.ps1
disposable Unity harness with UnityEditor.Experimental.GraphView imported
recorded interaction/performance observations against the synthetic large tree
```

## Handoff notes

- `P3-002` through `P3-013` are all blocked on this card's ADR being
  accepted (added as a `P3-002` dependency), not merely on this card being
  `Done` — same shape as `P3-001`'s handoff.
- If `GraphView` also fails the evaluation, stop and report rather than
  silently escalating to a from-scratch custom UI Toolkit view; a third
  spike iteration needs its own explicit go-ahead, mirroring `P3-001`'s own
  "stop and report" note applied one level deeper.

## Outcome

- **Adopt `UnityEditor.Experimental.GraphView`.** No criterion failed
  outright, unlike `P3-001`'s two independent disqualifying failures for
  Graph Toolkit. (1) Serialization control passes — a `GraphView` subclass
  constructs standalone, no asset/database backing forced; AIBT owns 100%
  of the persisted format. (2) Extensibility mostly passes — `Group` and
  `StickyNote` exist as native public types (unlike Graph Toolkit's zero
  matches); there is no native reroute type, but Unity's own shipped Shader
  Graph/VFX Graph implement reroutes as a custom element on
  `Edge`/`EdgeControl`, a precedented extension path. (3) Large-graph
  construction of the same 240-node fixture showed no red flag: `181.83 ms`
  build, `0` connect failures, comparable to Graph Toolkit's `169.08 ms`.
  (4) Testability passes fully headlessly, including successfully hosting
  the `GraphView` inside a real `EditorWindow` in `-batchmode -nographics`
  (`EDITORWINDOW_HOST_SUCCEEDED=True`) — stronger than what `P3-001` could
  even attempt. (5) The support-risk check added for this spike (prompted
  by the user's explicit "is this API still supported" question) found
  `OBSOLETE_TYPE_COUNT=0` across all 71 reflected public types in the
  installed `6000.5.8f1` Editor, and `GraphView` itself is not marked
  obsolete.
- **True pointer-driven interaction latency remains unmeasured**, the same
  gap `P3-001` disclosed — no interactive Editor/MCP session was available.
  This does not change the adopt verdict since it rests on the other,
  independent findings, but should be revisited once an interactive session
  exists, ideally by `P3-012`.
- Proposed ADR: `Planning~/Evidence/P3-014/ADR-P3-014-editor-graph-framework.md`
  (`Status: Proposed`). Supersedes `ADR-P3-001`'s "pending second spike"
  recommendation; `P3-002` onward may proceed once this ADR (or
  `ADR-P3-001`, whichever the integration owner formally accepts) is
  accepted.
- Full evidence: `Planning~/Evidence/P3-014/README.md`,
  `verification-results.json`. Harness:
  `Spikes~/EditorGraphFramework/HarnessGraphView/`.
