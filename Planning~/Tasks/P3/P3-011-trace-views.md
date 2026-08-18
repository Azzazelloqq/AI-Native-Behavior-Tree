# P3-011 — Trace views

Status: `Draft`

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
