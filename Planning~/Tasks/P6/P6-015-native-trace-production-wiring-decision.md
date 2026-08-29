# P6-015 — Native trace production-wiring decision

Status: `Draft`

## Objective

Decide, on real evidence against the actual `Runtime/Trace/Native/NativeTraceChannelV1.cs`
(`NativeTraceChannelOwnerV1`/`NativeTraceWriterV1`) and `Editor/Debugger/
NativeExecutionDebuggerSession.cs` (`P3-010`), whether and how to build a genuine production
mechanism that wires a *real* running native tree's lifecycle steps (`NativeLifecycleMachineV1`'s
own step results — `NodeEntered`/`NodeTicked`/`NodeExited`/etc.) into a `NativeTraceChannelOwnerV1`,
so a caller can attach `NativeExecutionDebuggerSession`/`Editor/Trace/TraceTimelineModel.cs` to a
trace of an actual compiled-tree execution, not a synthetic one.

This card exists because `P6-008` ("MCP verification tools: trace, test, benchmark") was found
mid-session (2026-08-29) to assume this wiring already exists as an "already-accepted production
entry point" it could simply wrap. It does not. Investigation before implementation found:

- Nothing in production code anywhere connects a real `NativeLifecycleMachineV1` step to
  `NativeTraceWriterV1.TryAppend` — searched every production file referencing
  `NativeTraceChannelOwnerV1` (`Editor/Debugger/NativeExecutionDebuggerSession.cs` and the trace
  channel's own file); neither creates or drives a lifecycle machine.
- The only two things anywhere in the repository that ever write trace records into a channel are:
  (1) `NativeExecutionDebuggerSessionTests.cs`'s private `Scenario` helper, which hand-writes a
  fixed 6-record sequence via a real Burst job but never derives those records from an actual
  compiled tree's execution (it is a synthetic fixture proving the *reading* side only); and
  (2) `Runtime/Diagnostics/Native/NativeChannelsBurstProbeV1.cs`, an unrelated diagnostic
  compile-proof probe, also synthetic.
- `Planning~/Evidence/P3-010/README.md` already discloses the root cause: "no production Play-mode
  host component anywhere in AIBT... drives a native lifecycle machine during Play mode, and no
  production code wires a native trace channel to a live pass at all" — `P3-010` deliberately
  narrowed its own scope to attach/detach/read against a *self-driven* pass instead, exactly
  mirroring `P3-009`'s pattern, and left this wiring gap as an explicitly disclosed known
  limitation, not something it (or `P3-011`/`P5-008`, which inherit the same limitation) ever
  built.

Building this wiring is real, new engineering (composing already-public `NativeLifecycleMachineV1`
step results with the already-public `NativeTraceWriterV1` API) — legitimate, but a genuine new
capability closing a gap three prior `Done` cards (`P3-010`, `P3-011`, `P5-008`) each independently
found and deliberately left open, not "wrapping an accepted entry point." Per `DECISION_BOUNDARIES.md`
("lifecycle, status, abort, ordering, timing, or deterministic behavior... must escalate before
implementation"), it must not be built silently inside a tool-wrapping card. This card decides the
design on paper with a disposable spike; a separate future card implements the accepted decision
and P6-008's `trace`/`compare-trace` tools.

## Depends on

- `P6-005` (MCP server host and permission enforcement) — this card's own future implementation
  target, once accepted.
- `P3-010` (done — owns `NativeExecutionDebuggerSession.cs`, the read side this wiring feeds).

## Required reading

- `Runtime/Trace/Native/NativeTraceChannelV1.cs` (`NativeTraceChannelOwnerV1.TryAcquireWriter`/
  `TryReleaseWriter`, `NativeTraceWriterV1.TryAppend`) — the existing, unmodified write API any
  design here must drive through, not duplicate.
- `Runtime/Scheduling/` (native lifecycle machine step results — `NativeLifecycleStepResultV1`,
  `NativeLifecycleStepKindV1`) — the source of the real events this wiring would translate into
  trace records.
- `Editor/Debugger/NativeExecutionDebuggerSession.cs` (`P3-010`) and `Editor/Trace/
  TraceTimelineModel.cs` (`P3-011`) — the read side this wiring must remain compatible with; no
  change to either is in this card's own scope.
- `Tests/Editor/Debugger/NativeExecutionDebuggerSessionTests.cs`'s `Scenario` helper — the closest
  existing example, explicitly disclosed above as synthetic and non-representative, not a template
  to extend in place.
- `Planning~/Evidence/P3-010/README.md`, `Planning~/Evidence/P3-011/README.md`,
  `Planning~/Evidence/P5-GATE/known-limitations.md` — the three prior disclosures of this exact
  gap; this card's own decision must not silently narrow their scope further or contradict them.
- `Planning~/Tasks/P6/P6-008-mcp-verification-tools-trace-test-benchmark.md` — the card this
  decision unblocks, narrowed on 2026-08-29 to exclude `trace`/`compare-trace` pending this card.

## Allowed changes

- `Spikes~/NativeTraceProductionWiring/` (new, disposable) — proves the recommended design against
  a real, unmodified `NativeLifecycleMachineV1` and `NativeTraceChannelOwnerV1`, mirroring
  `P6-002`'s/`P6-013`'s own spike-before-ADR methodology.
- `Planning~/Evidence/P6-015/`.
- One proposed ADR.

## Forbidden changes

- Any production change to `Runtime/Trace/Native/`, `Runtime/Scheduling/`, `Editor/Debugger/
  NativeExecutionDebuggerSession.cs`, or `Editor/Trace/TraceTimelineModel.cs` — this card decides
  on paper (backed by a disposable spike); a separate future card implements it, mirroring
  `P6-002`/`P6-004`'s and `P6-013`'s split.
- Building or claiming a production Play-mode host — still explicitly out of scope everywhere in
  AIBT (`Planning~/Evidence/P5-GATE/known-limitations.md`); this card's wiring must work for a
  self-driven pass only, the same scope `P3-010`/`P3-011`/`P5-008` already accepted.
- Silently reproducing this card's own "self-driven, not attached to an arbitrary running game"
  boundary any narrower or wider than `P3-010`'s already-accepted framing.

## Deliverables

- A decision on where the real-lifecycle-step-to-trace-record translation should live (a new
  `Runtime/Trace/Native/` adapter type driven by the caller after each `NativeLifecycleMachineV1`
  step? a wrapper around `NativeLifecycleMachineV1` itself? something else) and exactly which step
  kinds map to which `NativeTraceEventKindV1` values.
- A disposable spike proving the recommended design against a real, unmodified
  `NativeLifecycleMachineV1` driving an actual compiled tree (reusing `SchedulingPolicyDriver`'s or
  equivalent single-agent construction, not a synthetic record set) with a real
  `NativeTraceChannelOwnerV1`, read back correctly through the existing, unmodified
  `NativeExecutionDebuggerSession`/`TraceTimelineModel`.
- A proposed ADR recording the decision, its rationale, and exactly what remains out of scope
  (Play-mode host attachment stays deferred regardless of this card's outcome).

## Acceptance criteria

- The spike demonstrates the recommended wiring against a real compiled tree's actual execution
  (not a hand-written record sequence), with `NativeExecutionDebuggerSession.TryReadTrace` and
  `TraceTimelineModel.Build` both unmodified and both correctly reproducing the tree's real
  active-node history.
- A regression check confirms nothing in this investigation weakens `P3-010`'s or `P3-011`'s own
  accepted guarantees (re-run their existing test suites unmodified).
- The ADR states plainly what P6-008's future `trace`/`compare-trace` tools may claim (self-driven
  real-tree trace, still never "attached to an arbitrary running game") and what remains
  explicitly out of scope.

## Required verification

```text
Verify-Static.ps1
disposable spike: real NativeLifecycleMachineV1 + NativeTraceChannelOwnerV1, live Unity MCP
  execute_code
regression: NativeExecutionDebuggerSessionTests and TraceTimelineModel's own test suite,
  unmodified, still passing
```

## Handoff notes

- Not required for the Phase 6 integration gate (`P6-012`) — this card was discovered mid-session
  as a narrowing of `P6-008`'s own original scope, the same way `P6-013`/`P6-014` were discovered
  as narrowings of `P6-007`/`P6-006`. `P6-012` does not depend on it, and `P6-008` (as narrowed to
  `run-tests`/`run-benchmark`) does not depend on it either.
- If accepted, a future implementation card (not yet numbered) applies the ADR to production and
  implements `P6-008`'s originally-scoped `trace`/`compare-trace` tools on top of it.
