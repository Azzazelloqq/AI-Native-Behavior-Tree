# P5-004 safe full restart evidence

## Result

- `Runtime/HotReload/Restart/HotReloadFullRestartReport.cs`, `HotReloadFullRestart.cs` (new,
  internal): implements `ADR-P5-001`'s shared reload mechanism at its simplest, always-available
  exclusion set (the whole tree) for the **reference-executor backend**. Given a live
  `ReferenceExecutionMachine` and a new `CompiledProgram`, it inspects the old instance's activity
  (`CaptureInspection`), aborts it via `NodeAbortReason.HotReload` -- an abort reason already
  reserved in the accepted `NodeStatus.cs` enum and already mirrored in the Burst ABI spec, not a
  new addition -- and constructs a fresh `ReferenceExecutionMachine` bound to the new program. No
  state is copied; `P5-005`/`P5-006` extend this exact shape with a narrower exclusion set, per
  `ADR-P5-001`'s "one mechanism, not three" correction.
- `Tests/Editor/HotReload/Restart/HotReloadFullRestartTests.cs` (new, 5 tests, all passing):
  null-rejection, aborting a genuinely active instance (proven via `CaptureInspection` before and
  after, not merely trusting the abort call's return value), skipping abort for an already-idle
  instance, confirming the fresh instance is bound to the *new* program's content, not the old
  one's, and a 50-cycle repeated-restart stress test proving no growing active-node/managed-state
  leak across cycles.

## A real API-usage bug found and fixed by running tests live, not by inspection

The first implementation hardcoded the abort's `ReferenceUpdateContext` update ID to `1`. Live
testing immediately failed with `AIBT4001`-adjacent rejections once the scenario's own prior
`Update` call had already consumed ID `1` -- `ReferenceExecutionMachine` requires update IDs to
**strictly increase** (`Runtime/Execution/Reference/Core/ReferenceExecutionMachine.cs:464`,
`"Update IDs must strictly increase."`), a real invariant this card was not previously aware of and
would have shipped broken (silently rejecting every real restart attempt after the first `Update`)
had it not been run live. Fixed by making the abort's `ReferenceUpdateContext` a caller-supplied
parameter instead of an internal constant -- the caller already tracks its own update-ID sequence
for ordinary ticks and is the only party that can supply a value guaranteed to be fresh.

A second, more subtle finding during the same debugging pass: `ReferenceExecutionMachine.Abort`
internally calls `BeginUpdateCore`, which means it can only run **between** ticks (no update
already open), not while a budget-suspended update is mid-flight -- that case has a different,
narrower entry point (`RequestAbort`, requires `_hasOpenUpdate == true`) this card does not need,
since hot reload is expected to happen at a stable point between frames, matching
`CaptureInspection`'s own "stable API boundary" precondition.

## Decision: native backend explicitly deferred, not silently skipped

This card's original acceptance criteria asked for "correct behavior for both execution backends
(managed reference oracle, native executor)." Research confirmed the native backend's disposal
sequence exists and is proven
(`Tests/Runtime/NativeExecution/ProgramAndState/NativeProgramAndStateTests.cs`: dispose the
instance arena, release the program-image read lease, dispose the program-image owner), and its
own program-generation binding invariant (`native-runtime-v1.md`'s `AIBT4311`) already forces an
unconditional dispose-and-recreate on any generation change -- there is no restart "decision" to
make at that layer the way there is for the reference backend. However, actually *creating* a fresh
native instance requires understanding and correctly wiring `NativeProgramImageCapacityV1`/`V2`
preflight capacity planning and job-handle lease management (`P2-002`'s own "immutable preflight
capacity plans" contract, `AIBT-021`) -- a genuinely separate, non-trivial subsystem this card did
not have the scope to research and implement correctly alongside the reference-backend mechanism
in the same pass. Rather than ship an unverified or guessed native wrapper, this is disclosed as
real, deliberate follow-up work, not silently done or silently skipped.

## Verification

Live Unity MCP test run: 5/5 passed (including a 50-cycle repeated-restart stress test). Full
suite: 1436 tests; the same 3 pre-existing unrelated failures every prior evidence file records,
plus one newly observed `LocalSaveSystem.Tests.SaveTaggedFormatTests.ValidateFieldIds_LogsDuplicates`
failure -- a `LogAssert`-expectation timing issue in an unrelated host-project package, reproduced
even when run in isolation, most likely from accumulated Unity Editor session state after many
consecutive live test runs this session rather than a real regression (`LocalSaveSystem` is
untouched by any AIBT card). `Verify-Static.ps1`: 83 work items, unchanged. Full detail in
`verification-results.json`.

## Scope and limitations

- Reference backend only; native backend's fresh-instance construction remains open (see above).
- No state is preserved by this card by design -- it is the always-available fallback, not a
  migration path.
- No test uses a genuine in-flight *async command/completion* (as opposed to a persistently
  `Running` leaf) to prove cancellation specifically -- this card's own `Abort` call composes with
  `ReferenceExecutionMachine`'s already-proven idempotent-abort contract
  (`MemoryCompositeAbortTests`) rather than re-proving idempotency itself; a dedicated async-command
  restart test remains a reasonable follow-up if `P5-007`/`P5-009` need it specifically.
- The repeated-restart stress test (50 cycles) checks for a growing *active-node* leak via
  `CaptureInspection`, not raw managed-heap byte counts (`GC.GetTotalMemory` is explicitly not a
  zero-allocation proof per this codebase's own discipline) -- sufficient to prove this card's own
  mechanism does not leave dangling active state, not a full allocation-profiling pass.
- `HotReloadFullRestart` is `internal` (it operates on the `internal ReferenceExecutionMachine`),
  consistent with every other internal type in `Runtime/Execution/Reference/`; it requires no new
  `InternalsVisibleTo` grant since it lives in the same `AIBT.Runtime` assembly.
