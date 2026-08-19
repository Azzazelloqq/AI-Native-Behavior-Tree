# P3-011 — Trace views

Status: `Done`

## Objective

Visualize the execution trace (active node, step history, diagnostic events) read by `P3-010`'s debugger attachment.

## Depends on

- `P3-010`.

## Required reading

- `Documentation~/editor-and-layout.md` (debugging/profiling requirements).
- `P3-010`'s accepted read API.

## Allowed changes

- `Assets/AIBT/Editor/Trace/` (new).
- `Tests/Editor/Trace/` fixtures.
- `Planning~/Evidence/P3-011/`.

## Forbidden changes

- Any change to `P3-010`'s attach/read protocol; this card is a pure consumer.

## Deliverables

- A trace timeline view (step-by-step history, scrubbing) and a live active-node highlight on the `P3-003` graph adapter, both sourced from `P3-010`'s channel.
- A diagnostic event list correlated to the node/step that produced it.

## Acceptance criteria

- Scrubbing the trace timeline to a past step highlights the graph state that step actually produced, verified against the raw channel data.
- The view degrades explicitly (states "channel full/events dropped") when the bounded trace channel overflows, rather than silently showing a truncated trace as complete.

## Required verification

```text
Verify-Static.ps1
Run-UnityTests.ps1 -Mode EditMode -Scope Focused -TestFilter <trace view fixture>
```

## Handoff notes

- None beyond the dependency on `P3-010`.

## Outcome

- `Editor/Trace/TraceTimelineModel.cs`: replays a `NativeDebuggerTraceView` snapshot (P3-010's own
  read-only view; never reads the channel itself) into a per-step active-node history and
  step/node-correlated diagnostic events, plus `HasDroppedEvents`/`DroppedCount`/`IsFaulted` carried
  straight through from the channel.
- `Editor/Trace/TraceTimelineWindow.cs`: a scrub slider over the timeline, live highlighting on a
  private `BehaviorTreeGraphView` instance (P3-003, not modified) reflecting the *scrubbed* step's
  actual active-node state, a diagnostic list, and an explicit red degraded banner on drops/fault.
- 5/5 automated tests passing, including a scrub-parity proof verified against an independently
  hand-replayed oracle (not the model's own logic) and a real-overflow test that forces the
  channel's own unmodified bounded-capacity eviction logic to actually drop records. Live-verified
  interactively in the running `6000.5.8f1` Editor via Unity MCP.
- **Self-driven channels only**, inherited from `P3-010`'s scope narrowing -- no production
  Play-mode host exists yet; `AttachSession` accepts any caller-owned session so this works
  unchanged once one exists.
- **No `Editor/Graph/` live wiring** -- same disclosed pattern as `P3-004` through `P3-012`.
- Full evidence: `Planning~/Evidence/P3-011/README.md`, `verification-results.json`.
