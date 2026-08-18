# Proposed ADR: Editor graph framework (GraphView)

Status: `Proposed`

## Context

`ADR-P3-001-editor-graph-framework.md` rejected Unity Graph Toolkit for
`AIBT-012` and recommended a second spike evaluating
`UnityEditor.Experimental.GraphView` and/or a from-scratch custom UI Toolkit
view before `AIBT-012` (currently "Pending spike" in
`Documentation~/decisions.md`) could move forward. This ADR is the outcome
of that second spike (`P3-014`, `Spikes~/EditorGraphFramework/HarnessGraphView/`,
evidence in `Planning~/Evidence/P3-014/`), evaluated against the same four
criteria `P3-001` used, plus a fifth added specifically because the user
raised a concrete concern about `GraphView`'s "Experimental" namespace
status: whether the API is still safe to build on.

The spike found:

1. **Serialization control passes.** A `GraphView` subclass constructs with
   no asset, database, or window backing (`new BehaviorTreeGraphView()`
   succeeds unconditionally). `GraphView` has no built-in persistence at
   all — AIBT would own the entire `.aibt.layout.json` format from `P3-002`
   onward, with no framework-imposed asset format competing with it.
2. **Extensibility mostly passes.** `Group` and `StickyNote` (Unity's own
   vehicle for free-form comments) exist as real, native public types —
   unlike Graph Toolkit's zero matches. There is no native "reroute" type,
   but Unity's own shipped Shader Graph and VFX Graph implement reroutes as
   a custom element built on `Edge`/`EdgeControl`/`Node` — a precedented,
   available extension point, not an absent one.
3. **Large-graph construction performance shows no red flag.** `181.83 ms`
   to build/connect the same 240-node fixture `P3-001` used, comparable to
   Graph Toolkit's `169.08 ms`.
4. **Testability passes.** All checks ran headlessly in
   `-batchmode -nographics`; hosting the `GraphView` inside a real
   `EditorWindow` also succeeded without throwing.
5. **Support-risk check found no red flag.** Reflecting over all 71 public
   types in `UnityEditor.Experimental.GraphView` (installed `6000.5.8f1`
   Editor) found zero `[Obsolete]`-attributed members, and `GraphView`
   itself is not marked obsolete. The namespace still carries Unity's
   standard "Experimental — may change or be removed" documentation
   disclaimer, which this check cannot remove, but it rules out the
   concrete, checkable failure mode of the API already being flagged for
   removal in this Editor version. Unity's own shipped Shader Graph source
   currently depends on this same namespace.

No criterion failed outright, in contrast to `P3-001`'s two independent,
disqualifying failures for Graph Toolkit.

## Decision

Adopt `UnityEditor.Experimental.GraphView` as AIBT's editor graph framework.
This supersedes `ADR-P3-001`'s "pending second spike" recommendation for
`AIBT-012`. Neither this ADR nor `ADR-P3-001` has been promoted into
`Documentation~/decisions.md` yet — that promotion, and any `package.json`
implication (none expected, since `GraphView` is a built-in Editor module,
not a package dependency), remains the separate integration-owner step.

Two conditions carry forward into `P3-002`+ rather than being resolved here:

- Reroute support must be designed and implemented by AIBT on top of
  `Edge`/`EdgeControl`, following the precedent in Unity's own shipped
  tools; it is not available off the shelf.
- The residual "Experimental" documentation status is an accepted,
  disclosed risk, not an eliminated one — mitigated by the zero-`[Obsolete]`
  finding and the namespace's multi-year load-bearing use in Unity's own
  tools, but not a formal support guarantee.

## Consequences

- `P3-002` (layout model v1 contract) and every downstream `P3` card may now
  proceed once this ADR (or `ADR-P3-001`, whichever the integration owner
  accepts together) is formally accepted; `work-items.json` records `P3-014`
  as a `P3-002` dependency.
- `P3-002`'s layout schema must account for the fact that `GraphView` itself
  persists nothing — every positional/grouping/comment/reroute field in
  `.aibt.layout.json` is AIBT's own design, not inherited from the
  framework.
- `.aibt.json` remains uncontested as the canonical semantic source of
  truth; adopting `GraphView` does not introduce any competing asset format.
- True pointer-driven interaction latency at scale remains unverified (see
  `Planning~/Evidence/P3-014/README.md`'s Scope and limitations) and should
  be revisited once an interactive Editor/MCP session is available, ideally
  no later than `P3-012` (large-graph interaction and performance tests).
