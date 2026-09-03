# P7-027 — Production Play-mode host and live visual debugger

Status: `Draft`

## Objective

Owner request: real production debugging — being able to actually *see*, live, which node is
running/succeeding/failing and how execution is flowing, ideally with clear animated transitions, not
just static state.

This is `ADR-P7-010`'s own explicitly-flagged next step: "A future, not-yet-numbered implementation
card builds the real host type inside `Runtime/Integration/` per this ADR's shape, driving
`SchedulingPolicyDriver`'s (or a promoted equivalent's) `Immediate`/`Budgeted` methods directly." This
is the single most-repeated disclosed gap across the whole project (`P3-009`, `P3-010`, `P3-011`,
`P6-008`, `P6-012`'s gate all independently found it) — no production component anywhere drives a
compiled tree during real Play mode; every debugger/preview tool built so far
(`Editor/Trace/TraceTimelineWindow.cs`, `Editor/Preview/BehaviorTreePreviewWindow.cs`) attaches to a
self-driven or benchmark-driven substitute instead, disclosed each time as out of that card's own
scope.

The visualization half already exists and works, just has nothing real to attach to yet:
`TraceTimelineWindow` already does a scrubbable step timeline with live/scrubbed active-node
highlighting on a real `BehaviorTreeGraphView` and a diagnostic-event list correlated per step,
reading through `NativeExecutionDebuggerSession.TryReadTrace` — `ADR-P7-010`'s own spike (finding 3)
already proved this exact session type attaches to a host-owned `NativeTraceChannelOwnerV1` with zero
API changes. The current highlight (`TraceTimelineWindow.ActiveNodeColor`) is a flat, static color —
matching the owner's own "желательно с красивыми анимациями" ask as a real, not-yet-built gap, not an
existing feature needing polish.

## Depends on

- `ADR-P7-010` (Accepted — the host's shape, location, and initial policy scope are already decided;
  this card builds it, not re-decides it).
- `P3-010`/`P3-011` (native debugger attachment — the read side this card's host must satisfy
  unmodified, per the ADR's own proof).
- `P7-007` (native trace production-wiring — the recorder hook `ADR-P6-015` already expects a real
  production driver to call additively).

## Required reading

- `Documentation~/decisions/ADR-P7-010-production-play-mode-host.md` in full — the host's shape
  (one `MonoBehaviour` per tree instance, `Runtime/Integration/`, `Immediate`/`Budgeted` only,
  `Update()` not `FixedUpdate()`, owns its own `NativeTraceChannelOwnerV1`) is already decided; do
  not re-derive it, implement it, and re-verify each point the ADR's own "Acceptance criteria mapped"
  section lists as already spike-proven.
- `Spikes~/ProductionPlayModeHost/` (the disposable proof this card promotes to real, internal-access
  production code — the spike deliberately used the reference-executor backend to sidestep
  `AIBT.Runtime`-internal access; this card's real host must drive the native
  `SchedulingPolicyDriver`/`NativeLifecycleMachineV1` path directly, per the ADR's own "Explicitly
  unverified" section).
- `Editor/Trace/TraceTimelineWindow.cs`/`TraceTimelineModel` (the existing visualization to attach to
  a real host instead of a substitute — confirm exactly what changes, if anything, versus what
  already just works per the ADR's own spike proof).
- `Runtime/Integration/Snapshots/` (`P2-009` — the existing precedent for this exact assembly
  sub-area and the job-safety boundary a per-frame host must respect).

## Allowed changes

- New production type(s) in `Runtime/Integration/` (`AIBT.Runtime`) implementing the ADR's host
  shape.
- `Editor/Trace/TraceTimelineWindow.cs` — wiring to attach to a real running host in a real Play-mode
  scene (not a self-driven substitute), plus the animated-transition visual work the owner asked for
  (smooth status/color transitions on the active-node highlight, not an instant flat swap) — disclose
  if animation is descoped from this card's own first pass rather than silently dropped.
- `Planning~/Evidence/P7-027/`.

## Forbidden changes

- Do not widen initial scheduling-policy scope beyond `Immediate`/`Budgeted` — the ADR's own
  structural reasoning (a single per-`GameObject` host cannot itself perform
  `BatchedJobsSameFrame`/`PipelinedJobs`'s population-wide batch dispatch) is a real constraint, not
  a convenience; a population-level coordinator for batch policies is explicitly a separate, later
  card per the ADR's own Consequences.
- Do not re-implement or fork `NativeExecutionDebuggerSession`/`P6-015`'s recorder — the ADR proved
  the existing "caller-owned session" shape already works unmodified; this card wires production code
  to it, it does not touch either.

## Deliverables

- A real `MonoBehaviour` host, per `ADR-P7-010`'s shape, ticking a compiled tree every frame in a
  real Play-mode scene (Editor Play mode acceptable per the ADR's own scope; a full Standalone-Player
  proof is explicit follow-up, matching the ADR's own disclosed limitation).
- `TraceTimelineWindow` attached to that real, live host — showing real-time active-node state as the
  tree actually runs, not a scrub-only replay of a finished/substitute session.
- Some visible animated transition on node-status change (the owner's own explicit ask) — scope and
  disclose the actual first-pass extent (e.g. a color lerp/fade is a reasonable minimum; anything
  beyond that is a disclosed stretch goal, not assumed).

## Acceptance criteria

- Live proof: a real Play-mode scene with the new host attached, `TraceTimelineWindow` open and
  showing live (not scrubbed-to-a-fixed-step) active-node highlighting as the tree actually executes.
- No native memory leak on scene teardown (mirrors the spike's own already-proven finding 4 — must
  still hold for the real production type).

## Required verification

```text
Verify-Static.ps1
Run-UnityTests.ps1 -Mode EditMode -Scope Full
live Play-mode proof via Unity MCP: host ticking, debugger attached, real screenshot/recording
```

## Handoff notes

- Owner request this session (2026-09-03) for real production debugging with clear visual state.
  Directly matches `ADR-P7-010`'s own already-accepted "future, not-yet-numbered implementation
  card." The single most-repeated disclosed gap in the whole project's history — recommend
  prioritizing this over `P7-025`/`P7-026`/`P7-028` if only one can be picked up next, given how many
  independent prior cards have deferred exactly this. Confirmed in scope for `1.0`.
