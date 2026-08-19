# Known limitations after Phase 3

Prepared 2026-08-19 for the `P3-013` review.

## Carried forward from Phase 2, still true

- Only three fixed native execution policies exist (Immediate, Budgeted,
  BatchedJobsSameFrame); `PipelinedJobs` and `Auto` remain unimplemented.
- No calibrated scheduling defaults or regression thresholds exist.
- The zero-GC claim covers only the twelve Phase 2 measured initialized
  windows; editor/authoring code (all of Phase 3) is not subject to that
  contract and is not claimed to be allocation-free.
- Public API and persisted formats remain experimental below `1.0.0`.

## New in Phase 3, carried into Phase 4 and Phase 5

- **`Editor/Graph/`'s live window is not wired to anything Phase 3 built.**
  `BehaviorTreeGraphView`/`BehaviorTreeNode` (`P3-003`) remain a read-only
  adapter; `P3-004` (auto-layout), `P3-005` (organization), `P3-006`
  (semantic editing), `P3-009` (preview), `P3-010` (debugger), and `P3-011`
  (trace views) each built and tested a complete API/UI layer but host their
  own private view/window instances rather than attaching to the live
  `BehaviorTreeGraphWindow`. Unifying these into one authoring surface is
  real, well-scoped future work, not a defect -- each card's own evidence
  disclosed this rather than silently doing or skipping it.
- **No production Play-mode host exists.** Nothing in AIBT instantiates or
  drives a `NativeLifecycleMachineV1`/`NativeBatchedLifecycleOwnerV1` during
  Play mode, and no production code wires a `NativeTraceChannelOwnerV1` to a
  live pass. `P3-010`'s debugger and `P3-011`'s trace view are proven against
  self-driven channels a caller constructs and hands over; a real "attach to
  my running game" experience needs a future card with its own accepted
  decision.
- **No production per-project leaf-behavior registration mechanism exists.**
  Every executable leaf anywhere in AIBT today is a Phase 1 fixture
  (`aibt.test.success`/`.failure`/`.running`) or a built-in composite/
  decorator. `P3-009`'s preview, and by extension anything built on the same
  registry, can only execute trees built from that fixed set. A real project
  wanting to preview its own custom leaf nodes needs this solved first.
- **Large-graph editor performance is recorded, not calibrated.** `P3-012`'s
  numbers are a single run on one workstation; full-view render/re-render/
  load is explicitly degraded by 1000-2000 nodes (headless `renderMs` 755.6ms,
  `reRenderAfterEditMs` 1276.9ms; a live windowed load 2020.4ms at 2000
  nodes). `BehaviorTreeGraphView.Populate`'s full teardown-and-rebuild on
  every re-render is a known, disclosed candidate for future incremental-
  repopulation work.
- Standalone-Player debugger attachment is explicitly deferred (`P3-010`).
- Manual-organization/layout merge-conflict behavior beyond what canonical,
  deterministic, ordinally-sorted JSON already implies was not separately
  tested (`P3-005`).

## Blocking nothing, recorded for completeness

- The remote `P0-005` Unity CI job remains queued, as it has since Phase 1;
  this was waived to start both Phase 2 and Phase 3 and must not be reported
  as resolved.
