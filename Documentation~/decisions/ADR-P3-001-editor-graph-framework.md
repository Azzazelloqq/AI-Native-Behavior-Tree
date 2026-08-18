# ADR P3-001: Editor graph framework — Unity Graph Toolkit rejected

- Status: Accepted by explicit owner direction on 2026-08-18
- Date: 2026-08-18
- Decision ID: AIBT-012

## Context

`OQ-005` (`Documentation~/decisions.md`'s `AIBT-012`, "Pending spike") required
selecting AIBT's visual editor graph technology through a measured spike
before any P3 editor implementation. Unity Graph Toolkit — a built-in Editor
module as of Unity 6000.4, evaluated live against a synthetic 240-node
representative behavior-tree fixture — was the only candidate available in
the host project and the one `Documentation~/editor-and-layout.md` and the
`P3-001` task card named for evaluation.

The spike (`Spikes~/EditorGraphFramework/`, evidence in
`Planning~/Evidence/P3-001/`) found two disqualifying results:

1. Graph Toolkit graphs cannot exist without Unity asset-database backing
   (`GraphDatabase.CreateGraph`/`LoadGraph` are the only valid construction
   path; a plain `new Graph()` throws immediately on first mutation). The
   persisted format is Unity's native YAML object serialization, measured at
   ≈2.85 KB per node for the test fixture — not a format compatible with
   AIBT's requirement that `.aibt.json` remain the single, directly
   AI/MCP-editable canonical source of truth. A transient generate/extract/
   discard workaround is technically possible but adds real, ongoing
   engineering cost to keep `.aibt.json` authoritative.
2. Groups, comments, sticky notes, and reroutes — each explicitly a
   "Required authoring tool" in `Documentation~/editor-and-layout.md` — do
   not exist anywhere in Graph Toolkit's public API (confirmed by reflecting
   over all 37 public types in `UnityEditor.GraphToolkitModule`, not merely a
   documentation scan). There is no partial workaround from outside the
   module for this gap.

Data-model performance (240-node construction/save/headless-reload) showed
no problem on its own, but does not offset either finding above.

## Decision

Reject Unity Graph Toolkit as AIBT's editor graph framework. Do not add
`com.unity.graphtoolkit` (or rely on the built-in module) as a dependency of
production `Editor/` code.

This decision does **not** select a replacement on its own — see
`ADR-P3-014-editor-graph-framework.md` for the follow-up spike that selects
`UnityEditor.Experimental.GraphView`, accepted together with this ADR.

## Consequences

- `.aibt.json` remains uncontested as the canonical semantic source of truth;
  this decision closes off any design that would have let a Unity-native
  graph asset format compete with it.
- `Documentation~/editor-and-layout.md`'s manual-organization requirements
  (groups, comments, sticky notes, reroutes) constrain the accepted
  candidate directly, per `ADR-P3-014-editor-graph-framework.md`.
