# Phase 4 benchmarks

Phase 4 benchmark evidence, one subdirectory per work item, following the same
isolated-project pattern established in `Benchmarks~/Phase2/Dispatch/`: a
`Unity/` folder holding source that is only ever copied into a fresh, empty
Unity project by a `Run-*.ps1` driver script, never compiled as part of this
package itself.

- [`Scheduling/`](Scheduling/README.md) -- P4-001: fixed-policy scheduling
  overhead across the scenario catalog from `Documentation~/benchmarks.md`.
- [`CostCurves/`](CostCurves/README.md) -- P4-002: the same harness run at a
  wider agent-count range to produce actual per-policy cost curves.
- [`AutoComparison/`](AutoComparison/README.md) -- P4-006: `Auto` (P4-005)
  measured against the best fixed policy per scenario.
- [`Platform/`](Platform/README.md) -- P4-008: the same matrix run on real
  (non-Editor) per-platform execution; Windows x64 and single-thread Web in
  this session, Android ARM64 pending device access.
