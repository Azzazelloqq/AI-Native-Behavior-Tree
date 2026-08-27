# P5-007 scheduler and backend interaction -- progress notes (not fully closed)

## Status: partially done, blocked on the native-backend gap `P5-004` already disclosed

This card's own objective is to verify how the three reload strategies interact with **Phase 4's
scheduler** (`NativeWorkEstimatorV1`, `NativeAutoSelectionV1`, the four accepted **native**
policies including `PipelinedJobs`/`BatchedJobsSameFrame`) and with **both execution backends**.
The scheduler and its four policies are exclusively a **native-backend** concept --
`Documentation~/execution-and-scheduling.md`'s policy table has no equivalent for the managed
reference executor at all. `P5-004`/`P5-005`/`P5-006` (this session) implemented restart and
migration for the **reference-executor backend only**, explicitly disclosing native-backend
fresh-instance construction (capacity-plan/lease preflight wiring) as deferred follow-up work.

This means most of this card's own acceptance criteria -- re-running a policy's golden-equivalence
proof against "a hot-reloaded instance," proving batch isolation between a reloaded and untouched
native instance, proving `Auto`'s post-reload determinism -- describe a native-backend hot-reload
mechanism that does not exist yet. Attempting to fake or approximate these proofs against the
reference-executor mechanism instead would misrepresent what was actually verified. This gap is
disclosed here explicitly, not silently worked around.

## What was actually done

**The estimator reset-vs-carry-over decision** (`hot-reload.md`'s own explicitly-left-open
question) does not depend on native reload machinery existing yet, and is answered here:

**Decision: reset, never carry over.** `NativeWorkEstimatorV1` (per its own `P4-004` design) has no
persistence or reload-awareness of its own -- the caller owns keying one estimator instance per
distinct compiled-program identity/population. Since every reload strategy always constructs a
genuinely new `CompiledProgram` (`ADR-P5-001`: reload is never an in-place mutation), a caller
keyed by compiled-program identity automatically gets a fresh, unseeded estimator after any reload,
with zero special-casing required anywhere in the reload mechanism. This is the deliberate,
reasoned choice, not merely the default: carrying a smoothed steps-per-agent estimate across a
structural change (an insertion/removal genuinely changes total step count) risks seeding `Auto`'s
very first post-reload decision from a stale estimate for the new program's actual shape --
compounded by `P4-007`'s own finding that a wrong policy choice, once made, is never
re-evaluated (no exploration mechanism exists, by `OQ-006`'s own resolution). Resetting costs at
most one estimator seed period (`TryEstimateWorkPerAgentNanoseconds` fails until the first real
observation -- already `NativeWorkEstimatorV1`'s existing, accepted contract), not a correctness
risk.

`Tests/Runtime/HotReload/HotReloadSchedulerEstimatorResetTests.cs` (new, 1 test, passing) proves
the keying discipline this decision depends on: a distinct post-reload program identity does not
resolve to the old estimator, and a freshly constructed estimator starts unseeded.

## What remains blocked

- Golden-equivalence re-run for a hot-reloaded **native** instance, for all four accepted policies.
- Batch-isolation proof (a reloaded native instance does not disturb untouched siblings in the
  same batch/worker pool).
- `Auto` determinism/explainability proof for a post-reload native instance.

All three require a native-backend restart/migration mechanism this session did not build (see
`Planning~/Evidence/P5-004/README.md`'s own disclosed native-backend gap). Building that mechanism
is a prerequisite for closing this card, not something this card can substitute for.

## Verification

Live Unity MCP test run: 1/1 passed (`HotReloadSchedulerEstimatorResetTests`). `Verify-Static.ps1`:
83 work items, unchanged. Full detail in `verification-results.json`.
