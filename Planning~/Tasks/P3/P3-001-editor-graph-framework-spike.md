# P3-001 — Editor graph framework spike

Status: `Done`

## Objective

Resolve `OQ-005`: select the visual editor's graph technology using a real spike against Unity Graph Toolkit, or record why no available option is acceptable yet.

## Depends on

- `P2-025`.

## Required reading

- `Documentation~/architecture.md`
- `Documentation~/decisions.md` (`AIBT-010`, `AIBT-012`)
- `Documentation~/editor-and-layout.md`
- `Planning~/Evidence/P2-GATE/phase3-inputs.md`

## Allowed changes

- `Spikes~/EditorGraphFramework/`
- `Planning~/Evidence/P3-001/`
- One proposed ADR; integration owner applies accepted decision updates to `Documentation~/decisions.md` and `package.json`.

## Forbidden changes

- Production `Editor/` implementation, `.aibt.layout.json` format decisions, or any `package.json` dependency addition.
- A recommendation based on toy graphs; the spike must exercise a graph shape representative of `Documentation~/editor-and-layout.md`'s large-graph and readability requirements.

## Deliverables

- A disposable harness driving Unity Graph Toolkit `0.5.0-exp.1` (already present in the host project's `Packages/manifest.json`, not yet a package dependency of AIBT) against a synthetic large tree.
- Evaluation against serialization control, large-graph interaction performance, extensibility (custom node visuals, comments/groups/reroutes), testability (can graph state be asserted from EditMode tests), and long-term package risk (experimental-package stability, Unity version coupling).
- A recommendation: adopt Graph Toolkit, adopt an alternative, or build a minimal custom graph view — with evidence for each candidate actually evaluated.
- A proposed ADR superseding `AIBT-012`'s "Pending spike" status.

## Acceptance criteria

- The evaluated graph renders and lets a user reposition/connect at least 200 nodes without becoming unusable, measured and recorded rather than asserted.
- Serialization behavior (what the framework itself persists vs. what AIBT must own) is documented precisely enough that `P3-002` can specify `.aibt.layout.json` against it.
- The recommendation states exactly what remains unverified (e.g., extreme graph sizes, non-Windows editors) rather than generalizing.
- No production code depends on the recommendation until the ADR is separately accepted.

## Required verification

```text
Verify-Static.ps1
disposable Unity harness with the candidate framework(s) imported
recorded interaction/performance observations against the synthetic large tree
```

## Handoff notes

- `P3-002` through `P3-013` are all blocked on this card's ADR being accepted, not merely on this card being `Done` — mirrors how Phase 3's entry gate in `Planning~/WORK_PACKAGES.md` is phrased.
- If Graph Toolkit fails the evaluation, stop and report rather than picking an unevaluated fallback; a second spike iteration is in scope before declaring no option viable.

## Outcome

- **Reject Unity Graph Toolkit.** Two of four criteria fail on evidence: (1)
  serialization control — every graph must be a real Unity YAML asset the
  moment a node is added (`GraphDatabase.CreateGraph`/`LoadGraph` are the
  only valid construction path; a plain `new()` throws), measured at ≈2.85
  KB/node, incompatible with keeping `.aibt.json` as the single AI/MCP-editable
  source of truth without an ephemeral generate/extract/discard workaround;
  (2) extensibility — reflecting over all 37 public types in
  `UnityEditor.GraphToolkitModule` found zero Group/Comment/StickyNote/Reroute
  types, all four "Required authoring tools" per `editor-and-layout.md`.
- Construction/save/headless-reload of a 240-node synthetic tree showed no
  performance red flag (169 ms build, 28 ms save, exact 240/240 reload), but
  that does not offset the two failures above.
- **Acceptance criterion 1 (live interaction latency) was not measured** — no
  Unity MCP bridge or interactive Editor session was available in this pass,
  so "reposition/connect ≥200 nodes" was answered only for the data-model
  side, not actual pointer-driven UI latency. Disclosed, not silently
  skipped; does not change the reject verdict since it rests on the other
  two, independent findings.
- Proposed ADR: `Planning~/Evidence/P3-001/ADR-P3-001-editor-graph-framework.md`
  (`Status: Proposed`). Recommends `AIBT-012` move to a new "Pending second
  spike" state rather than being accepted as-is; no framework is adopted by
  this card. `P3-002` onward remain blocked pending that second spike.
- Full evidence: `Planning~/Evidence/P3-001/README.md`,
  `verification-results.json`. Harness: `Spikes~/EditorGraphFramework/`.
