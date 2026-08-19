# Phase 4 benchmarks

Phase 4 benchmark evidence, one subdirectory per work item, following the same
isolated-project pattern established in `Benchmarks~/Phase2/Dispatch/`: a
`Unity/` folder holding source that is only ever copied into a fresh, empty
Unity project by a `Run-*.ps1` driver script, never compiled as part of this
package itself.

- [`Scheduling/`](Scheduling/README.md) -- P4-001: fixed-policy scheduling
  overhead across the scenario catalog from `Documentation~/benchmarks.md`.
