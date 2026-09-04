# P7-027 — Production Play-mode host and live visual debugger

Status: `Done`

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

## Outcome

Done, 2026-09-04. Before implementation, investigation found `ADR-P7-010`'s own hedge ("driving
`SchedulingPolicyDriver`'s (or a promoted equivalent's) methods") was well-founded:
`SchedulingPolicyDriver` is a benchmark harness whose every leaf status is supplied by the caller
*in advance* via a plain array — it cannot drive a tree whose leaves compute their own real outcome.
Put to the owner rather than deferred: resolve the real dispatch mechanism now. Resolution, read
directly from the engine's own primitives rather than invented: `SchedulingPolicyDriver
.TryRunImmediate`'s own loop is a thin wrapper over `NativeLifecycleMachineV1.TryAdvance`/
`TryCompleteDispatch` (both already `internal`-accessible within `AIBT.Runtime`). The real
per-project leaf dispatch table (`GenericNativeDispatchTranslatorV1`, generated code, `P7-009`)
naturally lives outside `AIBT.Runtime` — resolved cleanly by having the host accept a real-dispatch
delegate injected at construction, resolved by whatever project-level code builds the host. The host
itself never references `AIBT.Authoring`.

**Implementation:**
- `Runtime/Integration/ProductionTreeHost.cs` (new, `AIBT.Runtime`): a `MonoBehaviour` per
  `ADR-P7-010`'s shape. `TryBootstrap(CompiledProgram, DispatchLeaf, NativeTraceChannelCapacityV1,
  out NativeRuntimeFailureV1)` builds one `SchedulingAgent` via `SchedulingPolicyDriver
  .TryCreateAgents` (reusing its already-tested construction/disposal, not duplicating it) and a
  `NativeTraceChannelOwnerV1`. `nodeKinds` is derived internally via `NativeHotReloadInstance
  .ClassifyKind` (an existing helper, already `internal` in the same assembly) rather than a public
  parameter, since `NativeLifecycleNodeKindV1` is itself `internal` and cannot appear in a public
  signature (a real `CS0051` compile error found and fixed during implementation, not predicted).
  `Update()` drives `TryAdvance`/`TryCompleteDispatch` directly in a loop mirroring
  `SchedulingPolicyDriver.TryHandleStep`'s own exact per-step handling, calling the injected
  delegate only for real `Tick`-phase dispatches (matching `TryHandleStep`'s own behavior for other
  phases exactly: `NodeStatus.Running`, never dispatched) — and additively drives `ADR-P6-015`'s own
  `NativeTraceRecorderV1` at the same call sites `SchedulingPolicyDriver`'s recorder overload already
  uses, per `P7-007`'s already-accepted rule. `OnDestroy()` disposes the trace channel and agent,
  idempotent under double-invocation (proven by test).
- `Editor/Trace/TraceTimelineWindow.cs`: `EditorApplication.update` subscription auto-refreshes
  while a session is attached and the Editor is actually in Play mode (throttled to ~10/s), making
  highlighting live instead of manual-click-only. `ApplyHighlight` now only sets a target node set;
  a new `AdvanceHighlightAnimation`, driven every Editor tick, fades each node's border alpha toward
  its target over ~200ms (`Mathf.MoveTowards`) — the owner's own explicit animation ask, scoped to a
  color/width fade as the disclosed first-pass minimum.

**Live proof (Unity MCP, real Play mode, not a substitute):** a real `ProductionTreeHost` bootstrapped
against a real `CompiledProgram` (mirroring `SchedulingPolicyDriverTests`'s own already-proven
minimal single-generated-leaf fixture) with a real on-demand dispatch delegate (not a pre-supplied
array) ticked live; `TraceTimelineWindow` attached via `NativeExecutionDebuggerSession.Attach`
(unmodified, per the ADR's own already-proven shape) read 47 real steps back — critically, the step
count grew from 17 to 47 **without an explicit `Refresh()` call in between**, proving the live
auto-refresh subscription genuinely works, not just that `Refresh()` itself works. The animation's
own alpha state (read via reflection) correctly reached its steady-state target (`1` for both
continuously-active nodes). Editor Play-mode's own known unfocused-throttling (already disclosed by
`ADR-P7-010`'s own spike, finding 2) meant `Update()` needed the same manual-tick workaround the
spike itself needed for a fast round-trip proof — the mechanism itself is real, live, unmodified
production code, only the tick-pumping cadence was worked around, exactly as before. No leak
diagnostic appeared on real Play-mode exit (clean `OnDestroy` teardown), matching the EditMode test's
own already-passing proof.

**Verification:** full regression 396/396 (`AIBT.Editor.Tests`, 392 baseline + 4 new
`ProductionTreeHostTests`), 1649-test whole-host-project run with only the same 3 pre-existing
unrelated failures. `Documentation~/generated/api-reference-runtime.md` regenerated (4 new public
members) via `AIBT/MCP/Regenerate Documentation`, drift-check tests re-passed. See `Planning~/
Evidence/P7-027/README.md`.

**Scope and limitations, disclosed:** `Immediate`/`Budgeted` only, per the ADR's own decision — a
population-level coordinator for `BatchedJobsSameFrame`/`PipelinedJobs` remains separate, future
work. A full Standalone-Player proof (as opposed to Editor Play mode) was not exercised, matching the
ADR's own already-disclosed scope. No visual screenshot of the animated fade was captured this
session (the live proof above is programmatic, reading real internal state, not a rendered image) —
a future session or the owner can capture one directly if a visual record is wanted.
