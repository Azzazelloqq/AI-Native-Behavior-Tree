# ADR P6-013: ReferencePreviewDriver simulation-capability widening

- Status: Accepted 2026-08-31
- Date: 2026-08-31
- Decision ID: AIBT-026

## Context

`P6-007`'s own 2026-08-28 addendum found that MCP's `simulate` tool cannot inject completions,
resume with a step budget, abort, or drive a caller-chosen `TreeInstanceId`, because
`ReferencePreviewDriver`'s public facade (`Authoring/Execution/ReferencePreviewDriver.cs`, `P3-009`)
never exposes them -- even though the engine it wraps (`ReferenceExecutionMachine`, internal to
`AIBT.Runtime`) already implements all four internally. Widening a `P3-009`-owned public facade is
a "must escalate" change per `DECISION_BOUNDARIES.md`, so this card decided the design on paper,
backed by a disposable spike, before any production file is touched.

Two further questions were investigated on real evidence rather than assumed:

- **`rootSeed`**: confirmed absent from the reference (managed) executor entirely -- it exists only
  in the native path (`Runtime/State/Native/NativeRandomStreamV1.cs`). Supporting it would mean
  adding a new concept to the reference executor itself, not surfacing an existing one.
- **"events"** (`behavior-case-v1.md`'s own input concept, distinct from completions): confirmed
  **not** a naming overlap with completions. `behavior-case-v1.md` itself defines them as two
  separate `update`-step inputs ("ordered external events, and completions"), `BehaviorCaseJson.cs`
  already parses a real `BehaviorCaseEvent` type (`sourceId`/`sourceSequence`/`eventTypeId`/
  `eventTypeVersion`/`payload`) distinct from `CompletionRecord`, but
  `AuthoringBehaviorCaseExecutorFactory.cs` throws a literal, already-shipped
  `NotSupportedException("Phase 1 reference execution does not consume external events.")` the
  moment a case supplies one. This is a genuine, already-disclosed missing **engine** capability
  (`ReferenceUpdateContext`/`ReferenceExecutionMachine` have no events field or pipeline at all),
  not a driver-facade gap -- out of this card's own scope regardless of outcome.

## Spike evidence (`Spikes~/ReferencePreviewSimulationCapability/`, 2026-08-31, this workstation)

A disposable NUnit spike (`SpikeReferencePreviewSimulationCapability`, run live via Unity MCP
`run_tests` against the real, unmodified `6000.5.8f1` Editor) built a temporary facade mirroring
`ReferencePreviewDriver`'s own incremental `BeginTick`/`StepAtomic` shape, with the four
recommended widenings layered on top, and drove it against the real `ReferenceExecutionMachine`:

1. **Completions injection round trip.** A hand-built `CompiledProgram` running the real
   `ReferenceAsyncActionHandler` fixture (the driver's own fixture registry has no `NodeManifest`
   for this leaf type, so — exactly like the already-accepted `ReferenceAsyncLifecycleTests.cs` —
   only a hand-built `CompiledProgram` can exercise it; a real `TreeDocument` compiled through
   `ReferenceCompiler` cannot reach this leaf at all). `BeginTick(completions: batch)` with a
   caller-supplied `CompletionBatch` (already `public`, `Runtime/Commands/CompletionContracts.cs`)
   was consumed correctly and the tree reached `Completed`/`Success`. **Passed.**
2. **Abort mid-tick.** Found live, not assumed: `ReferenceExecutionMachine.RequestAbort(reason,
   index)` requires an *already-open* update (`_hasOpenUpdate`) and is rejected outright once a
   tick has reached a `Waiting` boundary -- exactly the state a preview caller most wants to cancel
   from. The *other* overload, `Abort(update, reason, index[, budget])`, opens its own fresh update
   context and drives the entire abort traversal to a real boundary in one call; this is the one the
   facade should wrap. Verified: a real cancel command was emitted for a genuinely waiting
   operation. **Passed** (after this correction).
3. **Resume after a step-budget yield.** A real `TreeDocument` (root sequence → child sequence →
   `aibt.test.success` leaf) compiled through the exact same `ReferenceCompiler.Compile` +
   `ReferencePreviewFixtureEnvironment.CreateNodeRegistry()` path the driver itself uses.
   `Update(context, ReferenceStepBudget.Limited(1))` yielded (`Progress: Suspended`) before
   completing a 3-node tick; `Resume(ReferenceStepBudget.Unlimited)` continued the same suspended
   tick to `Completed`/`Success`. **Passed.**
4. **Two concurrent sessions, distinct `TreeInstanceId`s.** Two independently constructed machines
   (`TreeInstanceId(101)`/`TreeInstanceId(202)`, both already-public with a public constructor)
   ran side by side; completing session A's operation left session B's own trace showing zero
   completions consumed, and the emitted `OperationId.TreeInstanceId` correctly carried the
   caller-supplied value end to end. **Passed.**

All four ran against the real, unmodified `ReferenceExecutionMachine` -- no production file was
touched. `Tests/Editor/Preview/ReferencePreviewParityTests.cs` (`P3-009`'s own accepted
step-sequence parity guarantee) was re-run unmodified in the same full regression pass and remains
passing, confirming this investigation weakens nothing already accepted.

Full raw output is in `Planning~/Evidence/P6-013/README.md`.

## Decision

1. **Completions injection: widen.** Add an optional `CompletionBatch completions = null` parameter
   to `ReferencePreviewDriver.BeginTick` (and thread it through `RunTick`). Pure facade surfacing --
   `CompletionBatch`/`CompletionRecord` are already public; the engine already accepts this exact
   shape via `ReferenceUpdateContext`'s existing optional 4th constructor parameter.
2. **Resume with a step budget: widen.** Add a `ReferencePreviewEnvelope Resume(ulong? stepLimit =
   null)` method. The public signature takes a plain `ulong?` (null = unlimited), never the internal
   `ReferenceStepBudget` type itself -- the driver translates internally, exactly like it already
   translates `ReferenceExecutionProgress` → `ReferencePreviewProgress`.
3. **Abort: widen, using the `Abort(update, reason, index)` overload, not `RequestAbort`.** Add a
   `ReferencePreviewEnvelope Abort(NodeId sourceNode)` method. `NodeAbortReason` stays `internal`;
   the driver hardcodes `NodeAbortReason.Explicit` (the only reason an external caller's abort can
   ever mean -- the other five reasons are all internally-generated: observer preemption, hot
   reload, timeout, tree-stop). The driver needs a reverse `NodeId → RuntimeNodeIndex` lookup at
   `TryCreate` time (it already builds the forward direction); this is a small, private,
   non-escalating addition.
4. **Caller-supplied `TreeInstanceId`: widen.** Add an optional `TreeInstanceId? instanceId = null`
   parameter to `TryCreate`, defaulting to the current hardcoded `new TreeInstanceId(1)` when
   omitted -- fully backward compatible.
5. **`rootSeed`: rejected, out of scope.** No such concept exists in the reference executor; adding
   one is new engine capability requiring its own separately escalated design, not a facade
   widening. Not pursued by this ADR.
6. **"events": rejected, out of scope.** Confirmed a genuine, already-disclosed missing *engine*
   capability (`ReferenceUpdateContext`/`ReferenceExecutionMachine` have no events pipeline at all),
   not a driver-facade gap this card can resolve by widening a public method signature. A future
   card wanting to support behavior-case-style external events through preview/MCP would need to
   design and build that engine capability first -- a materially bigger undertaking than this ADR's
   own scope, and explicitly not attempted here.

## Consequences

- A future implementation card (not yet numbered) applies decisions 1-4 to the real
  `ReferencePreviewDriver.cs` and widens `P6-007`'s `simulate` tool's own step reader
  (`MCP/Verification/McpVerificationJson.cs`'s `ReadUpdateStep`) to accept the newly-available
  fields, mirroring `P6-002`→`P6-004`'s own decide-then-implement split.
- That future card must reuse the exact abort mechanism this ADR specifies
  (`Abort(update, reason, index)`, never `RequestAbort`) -- the spike found the two overloads are
  not interchangeable for a "cancel a waiting operation" use case, a real behavioral fact, not a
  style preference.
- `rootSeed` and "events" support for `simulate` remain explicitly unresolved; either would need its
  own future decision card scoped to the reference executor's own engine capability, not to this
  driver's facade.

## Explicitly unverified (stated, not generalized)

- Concurrent-session behavior was proven for two in-process machine instances driven from one
  thread, sequentially -- not for genuine multi-threaded concurrent access to one driver instance
  (out of scope; nothing in the existing driver or this ADR claims thread safety).
- The reverse `NodeId → RuntimeNodeIndex` lookup's own performance/memory characteristics at large
  tree sizes were not measured -- a private-implementation detail for the future implementation
  card, not a concern this decision-only card needed to resolve.
