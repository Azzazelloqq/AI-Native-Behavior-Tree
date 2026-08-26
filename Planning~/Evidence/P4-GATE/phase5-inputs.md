# Phase 5 inputs (Phase 4 addendum)

Prepared 2026-08-27 for the `P4-009` review. `Planning~/Evidence/P3-GATE/phase5-inputs.md`
remains the primary Phase 5 handoff (layout/semantic isolation, the shared
compiled-artifact identity, and the cross-boundary-driving patterns); nothing
in Phase 4 changed the editor or the compiled-program format. This document
only adds what Phase 4 itself contributes: confirmation that the scheduler
contract is stable and semantically inert, which `P4-009`'s own handoff notes
name as a specific precondition for hot reload's design.

## What Phase 5 additionally inherits from Phase 4

- **The scheduler contract is stable and closed for Phase 4's scope.** Four
  accepted execution policies (`Immediate`, `Budgeted`, `BatchedJobsSameFrame`,
  `PipelinedJobs`) plus `Auto` selection exist with a fixed, documented
  decision surface (`NativeAutoSelectionV1`); nothing about that surface is
  expected to change shape before Phase 5 needs to build against it, only to
  be recalibrated (see below) or extended with new scenarios.
- **No scheduling decision changes tree semantics, proven across every
  accepted policy, not assumed.** `PipelinedJobs`'s golden-case equivalence
  matrix (`P4-003`) and the native/reference golden equivalence inherited
  from Phase 2 together cover all four fixed policies; `Auto` only ever
  selects among them, introducing no fifth execution path of its own. A
  future hot-reload mechanism can therefore treat "which policy is
  scheduling this instance" as completely orthogonal to "what does this
  instance compute" -- exactly the separation `Documentation~/execution-and-scheduling.md`'s
  own "Semantic guarantees" require.
- **Calibration state is separate from compiled-program identity and is safe
  to leave untouched across a reload.** `NativeWorkEstimatorV1`'s smoothed
  per-agent estimate is keyed by the caller (one estimator per distinct
  compiled program/population, per `P4-004`'s own design), not embedded in
  `CompiledProgram` itself. A hot-reloaded program is a new compiled-program
  identity; whether to reset or carry over an existing estimator instance
  across a reload is a Phase 5 design decision this gate does not make for
  it, but the two are structurally decoupled either way.
- **Runtime autotuning was evaluated and rejected** (`OQ-006`, `P4-007`):
  Phase 5 should not assume any live-adapting scheduling state exists that a
  reload would need to migrate or reset. The shipped `Auto` path
  (`TrySelect`) is a pure function of its inputs each call, not a stateful
  tracker -- one less kind of state a hot-reload design has to reason about.
- **A concrete example of a real, disclosed defect surfacing outside the
  formal card workflow, caught by re-running tests rather than trusting a
  prior session's assumption** (`P4-004`'s addendum): worth reusing as a
  methodology note if Phase 5's own reload logic ever assumes a test
  fixture "will just keep working" after changing a value it depends on --
  verify by actually running it, not by symbolic-reference reasoning alone.

## Constraints Phase 5 must not violate (unchanged, restated from `P3-GATE`)

- Node coordinates, colors, groups, and comments still never influence
  semantics or reload decisions.
- A hot-reload path must not weaken `P3-006`'s "every semantic edit is gated
  by the real compiler/validator" contract to make reloading more convenient.
- New, restated for Phase 4: a hot-reload path must not weaken any accepted
  policy's proven semantic equivalence to the reference oracle to make
  reloading more convenient, and must not introduce a new execution path that
  bypasses the four accepted policies.
