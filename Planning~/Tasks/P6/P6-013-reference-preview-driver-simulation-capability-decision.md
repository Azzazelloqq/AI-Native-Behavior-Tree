# P6-013 — ReferencePreviewDriver simulation-capability decision

Status: `Draft`

## Objective

Decide, on real evidence against the actual `Authoring/Execution/ReferencePreviewDriver.cs` (P3-009)
and the `ReferenceExecutionMachine`/`ReferenceExecutionContracts` it wraps (not spec prose alone),
whether and how to widen `ReferencePreviewDriver`'s public facade so MCP's `simulate` tool (`P6-007`)
— and any future consumer, e.g. a live editor preview enhancement — can drive completions
injection, resume-with-a-step-budget, abort, and a caller-supplied `TreeInstanceId`.

This card exists because a fix session on 2026-08-28 (see `Planning~/Evidence/P6-007/README.md`'s
2026-08-28 addendum for the full writeup) found that `P6-008`'s own task card does **not** cover
this gap at all — it drives entirely different entry points (`NativeExecutionDebuggerSession`,
`TraceTimelineModel`, the `P1-017` behavior-case runner, `SchedulingPolicyDriver`), never
`ReferencePreviewDriver` — and that the gap is narrower than `P6-007`'s own evidence first framed
it: `ReferenceExecutionMachine` (the already-accepted engine `ReferencePreviewDriver` wraps)
**already implements** completions injection (`ReferenceUpdateContext.Completions`, currently
always passed as `CompletionBatch.Empty` by the driver), `Resume(ReferenceStepBudget)`, and
`RequestAbort`/`Abort(...)` as `internal` methods, and its constructor already accepts a caller
`TreeInstanceId` (the driver hardcodes `new TreeInstanceId(1)`). Widening the facade for these four
would be *surfacing* existing accepted-engine capability, not building new engine capability — but
per `DECISION_BOUNDARIES.md` it is still a public API surface change to an already-accepted P3-009
file, so it must not be implemented without a review/decision pass, which is this card's job.

Two things found to be genuinely absent, not just unexposed, and likely out of this card's own
scope (confirm or refute, do not assume):

- **`rootSeed` / deterministic randomness.** No such concept exists anywhere in the reference
  (managed) executor — it exists only in the native path (`Runtime/State/Native/NativeRandomStreamV1.cs`).
  Supporting it would mean adding a new concept to the reference executor itself, not surfacing an
  existing one.
- **`behavior-case-v1.md`'s "events" concept.** `ReferenceUpdateContext` has no field distinct from
  `Completions`/`TimeMicroseconds` that "events" could map to — unclear whether this is a naming
  mismatch (events and completions are the same underlying mechanism under two names) or a genuine
  missing capability. Needs its own investigation before any design decision.

## Depends on

- `P6-007` (done — the card whose evidence discovered and disclosed this gap).
- `P3-009` (done — owns `ReferencePreviewDriver.cs`, the file any accepted decision here would
  change).

## Required reading

- `Authoring/Execution/ReferencePreviewDriver.cs` — the public facade this card may recommend
  widening.
- `Runtime/Execution/Reference/Core/ReferenceExecutionMachine.cs` and
  `ReferenceExecutionContracts.cs` (`ReferenceUpdateContext`, `ReferenceStepBudget`) — the wrapped
  engine's actual already-implemented capability surface, `internal` to `AIBT.Runtime`.
- `Planning~/Evidence/P6-007/README.md`'s 2026-08-28 addendum — this card's own originating finding,
  including the exact facts already established (do not re-derive from scratch; confirm and build
  on them).
- `Planning~/Evidence/P3-009/` — the accepted guarantees (step-sequence parity against the raw
  oracle machine, live-Editor verification) this card's recommendation must not weaken.
- `MCP/Verification/McpVerificationJson.cs`'s `ReadUpdateStep` and
  `MCP/Verification/McpVerificationToolDispatcher.cs`'s `Simulate` — `simulate`'s own current
  restricted step reader, the first real consumer any accepted widening would unblock.
- `Documentation~/specifications/behavior-case-v1.md` (or wherever it currently lives — confirmed
  absent as a standalone file at that exact path during this session; locate it fresh via
  `Documentation~/data-formats.md`'s own reference) for the "events" concept's actual defined shape.

## Allowed changes

- `Spikes~/ReferencePreviewSimulationCapability/` (new, disposable) — proves whichever design is
  recommended against the real `ReferenceExecutionMachine`/`ReferencePreviewDriver`, mirroring
  `P6-002`'s own spike-before-ADR methodology.
- `Planning~/Evidence/P6-013/`.
- One proposed ADR.

## Forbidden changes

- Any production change to `ReferencePreviewDriver.cs`, `ReferenceExecutionMachine.cs`, or
  `MCP/Verification/` itself — this card decides on paper (backed by a disposable spike); a
  separate future card implements an accepted decision, mirroring `P6-002`/`P6-004`'s split.
- Inventing a `rootSeed` concept inside the reference executor without treating that as its own,
  separately escalated design question — likely out of this card's scope entirely; disclose, don't
  build.
- Silently assuming "events" and "completions" are the same concept without confirming against
  `behavior-case-v1.md`'s actual defined shape.
- Weakening any of `P3-009`'s own accepted parity/verification guarantees.

## Deliverables

- A decision on whether to widen `ReferencePreviewDriver` for completions/resume+step-budget/abort/
  custom `TreeInstanceId`, and if so, the exact shape (new overloads? a richer options/context
  parameter? a new type mirroring `ReferenceUpdateContext`'s own shape at the public boundary?).
- An explicit, evidenced scope call on `rootSeed` (very likely: out of scope, flagged as its own
  future card) and "events" (either "same as completions, already covered" or "a genuine gap,
  flagged as its own future card" — decided on evidence, not assumed).
- A disposable spike proving the recommended design actually works against the real engine: at
  minimum, one completions-injection round trip, one abort mid-tick, one resume-after-step-budget-
  yield, and (if `TreeInstanceId` is in scope) two concurrent sessions with distinct instance IDs
  not interfering with each other.
- A proposed ADR recording the decision, its rationale, and exactly what remains out of scope.

## Acceptance criteria

- The spike demonstrates the recommended capability set against the real, unmodified
  `ReferenceExecutionMachine` (through a temporary spike-only facade, not a production change), not
  a hypothetical description.
- A regression check confirms nothing in this investigation weakens `P3-009`'s own accepted
  step-sequence parity guarantee (re-run `ReferencePreviewParityTests` or equivalent unmodified).
- The ADR states plainly which of the four originally-requested capabilities (events, completions,
  resume, abort) plus step-budget/custom-TreeInstanceId/rootSeed are recommended, deferred, or
  rejected, with a real reason for each — not a blanket "yes" or "no."

## Required verification

```text
Verify-Static.ps1
disposable spike: real ReferenceExecutionMachine/ReferencePreviewDriver, live Unity MCP execute_code
regression: P3-009's own parity test suite, unmodified, still passing
```

## Handoff notes

- Not required for the Phase 6 integration gate (`P6-012`) — this card was discovered mid-session
  as optional follow-up work on an already-`Done` card's disclosed limitation, not part of the
  original Phase 6 decomposition's own scope. `P6-012` does not depend on it.
- If accepted, a future implementation card (not yet numbered) applies the ADR to production
  `ReferencePreviewDriver.cs` and widens `P6-007`'s `simulate` tool to actually use the new surface.
