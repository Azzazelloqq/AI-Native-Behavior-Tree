# P7-007 — Native trace production-wiring implementation

Status: `Draft`

## Objective

Apply `ADR-P6-015` (`AIBT-027`, Accepted) to production: give a real, currently-driving native
lifecycle loop (`Runtime/Scheduling/SchedulingPolicyDriver.cs` today, whichever production driver
exists by the time this card is assigned) an additive recorder that translates its own
`NativeLifecycleStepKindV1`/dispatch-completion-phase events into `NativeTraceEventKindV1` records
on a real `NativeTraceChannelOwnerV1`, per the ADR's own fixed mapping table. This closes the
"nothing in production wires a real running native tree into a trace channel" gap every
`P6-008`/`P6-012` evidence file disclosed, and is a prerequisite for any Phase 7 "profiler
validation" or trace-inspection MCP claim.

## Depends on

- `P6-015` (the accepted decision this card implements).

## Required reading

- `Documentation~/decisions/ADR-P6-015-native-trace-production-wiring.md` (the accepted mapping
  table and "external recorder, not a machine change" constraint).
- `Spikes~/NativeTraceProductionWiring/` (the disposable spike that proved this against
  `SchedulingPolicyDriver` — the production implementation should match its own proven shape).
- `Editor/Debugger/NativeExecutionDebuggerSession.cs` and `Editor/Trace/TraceTimelineModel.cs`
  (`P3-010`/`P3-011`'s own existing, unmodified trace consumers this card's real trace records must
  read back through correctly, per the ADR's own re-run proof).

## Allowed changes

- `Runtime/Scheduling/SchedulingPolicyDriver.cs` and/or a new file alongside it implementing the
  recorder (additive hook only, per the ADR — no change to the driver's own control flow beyond the
  hook call sites).
- `Tests/Runtime/NativeExecution/` (new production-wiring tests).
- `Planning~/Evidence/P7-007/`.

## Forbidden changes

- Any change inside `NativeLifecycleMachineV1` itself — the ADR is explicit that the translation
  lives outside it.
- Any change to `NativeExecutionDebuggerSession`/`TraceTimelineModel`'s own already-accepted,
  unmodified read path — this card produces real trace input for them, it does not touch them.
- Claiming a production Play-mode host now exists — this card wires trace production into whatever
  driver already runs in production (a scheduling/benchmark driver), not a new Play-mode component;
  that remains `P7-010`'s own separate scope.

## Deliverables

- A real recorder wired into `SchedulingPolicyDriver`'s own existing call sites, producing genuine
  `NativeTraceEventKindV1` records for a real compiled tree's real execution, exactly matching the
  ADR's mapping table.
- Proof the resulting trace reads back correctly and unmodified through `NativeExecutionDebuggerSession.TryReadTrace`
  and `TraceTimelineModel.Build`, the same proof shape the disposable spike already established.
- An MCP-facing consequence noted for follow-up (not built here unless trivially in scope): once
  real trace production exists, `P6-008`'s narrowed `trace`/`compare-trace` tools become buildable —
  flag this rather than silently expanding this card's own scope to include them.

## Acceptance criteria

- The recorder hook is proven additive: `SchedulingPolicyDriver`'s own existing test suite passes
  unchanged with the recorder attached and produces bit-identical scheduling results.
- A real, multi-update, multi-node compiled tree run through the wired driver produces a trace that
  `TraceTimelineModel.Build` replays into the same active-node history an independent hand-check of
  the same run would produce.
- No new trace event kind or field is added beyond the ADR's own accepted mapping table.

## Required verification

```text
Verify-Static.ps1
Run-UnityTests.ps1 -Mode EditMode -Scope Full
SchedulingPolicyDriver regression re-run, with and without the recorder attached, bit-identical results
trace read-back proof through NativeExecutionDebuggerSession/TraceTimelineModel
```

## Handoff notes

- Once this exists, `P6-008`'s originally-narrowed `trace`/`compare-trace` MCP tools (spun off
  because this exact gap made them unbuildable) become assignable — a future card, not folded in
  here.
