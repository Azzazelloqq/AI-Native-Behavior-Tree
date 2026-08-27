# P5-009 — Hot-reload benchmark evidence

Status: `Done`

## Objective

Measure hot-reload and debug-instrumentation overhead per
`Documentation~/benchmarks.md`'s required synthetic-scenario category and
compilation/import/hot-reload cost metric. Measurement only: no default,
threshold, or "acceptable reload cost" claim is drawn here, mirroring every
Phase 4 benchmark card's own discipline.

## Depends on

- `P5-004`, `P5-005`, `P5-006` (every strategy this card measures).
- `P5-007` (scheduler-interaction cost this card must isolate from raw
  reload cost).

## Required reading

- `Documentation~/benchmarks.md` (scenario catalog conventions, parameter
  matrix, platform process -- this card extends the existing pattern, it
  does not invent a new benchmark methodology).
- `Documentation~/hot-reload.md`'s "Benchmarks" section.
- `Benchmarks~/Phase4/` (the harness/reporting pattern this card reuses).

## Allowed changes

- `Benchmarks~/Phase5/` (new).
- `Tools~/Verification/P5/` (new, if a dedicated verification driver is
  needed, mirroring `Tools~/Verification/P4/`'s pattern).

## Forbidden changes

- Any performance default, regression threshold, or "supported reload cost"
  claim -- same forbidden-changes clause every Phase 4 card carried,
  restated here per `Planning~/USER_ACTIONS.md`.
- Any change to `P5-001` through `P5-008`'s mechanisms -- this card measures
  what already exists.

## Deliverables

- Cost curves for full restart, subtree restart, and compatible migration,
  each isolated from the others, at multiple tree sizes and multiple
  agent-population sizes (mirroring `P4-002`'s parameter-sweep discipline).
- A breakdown separating pure reload mechanism cost from
  compilation/import cost and from scheduler-interaction cost (`P5-007`'s
  estimator reset-vs-carry-over decision), so a reader can see which part of
  a reload dominates.
- Debug-instrumentation overhead specifically (trace capture, debugger
  attachment) during a reload, reusing `P3-010`/`P3-011`'s existing
  allocation-neutral proof technique where applicable.
- Full environment recording (Unity/package/OS/CPU/build config/scenario
  revision, warmup and measured sample counts) per every prior Phase 4
  benchmark's own convention.

## Acceptance criteria

- Every measured number records its environment and raw samples, not just
  an aggregate, per `benchmarks.md`'s own discipline.
- Migration cost is shown separately from full-restart cost at the same
  tree/population size, so the reader can see whether migration is actually
  cheaper in practice, not merely assumed cheaper by design.
- No threshold, default, or "supported reload size" claim appears anywhere
  in this card's output.
- At least one measurement runs on a real, non-Editor Player build, per
  `P4-008`'s own precedent that Editor batchmode numbers can differ from
  real Player numbers by an order of magnitude and must not be silently
  treated as representative.

## Required verification

```text
Verify-Static.ps1
one full parameter-matrix run per reload strategy, raw samples recorded
at least one real, non-Editor Player run
```

## Handoff notes

- `P5-010` cites this card's numbers as measured evidence, same as every
  prior gate cited its own phase's benchmark cards, without converting them
  into a default.

## Outcome

`Benchmarks~/Phase5/HotReload/Unity/HotReloadBenchmarkRunner.cs` measures full restart, compatible
migration, and subtree restart (each isolated from compile-only cost) across three tree shapes, via
the public `HotReloadPreviewDriver` facade only. Run in Editor batchmode
(`Results/hot-reload-benchmark-windows-editor-20260827.json`) and in a real, non-development Windows
x64 Standalone Player built and run through an isolated project
(`Results/hot-reload-benchmark-windows-player-20260827-074542.json`,
`Run-HotReloadPlayerBenchmark.ps1`/`HotReloadBenchmarkBuild.cs`), satisfying the card's explicit
real-Player acceptance criterion. **Key finding**: full restart costs ~1.9-2x a compatible migration
at the same tree size, on both Editor and Player -- migration is measurably cheaper, not just
theoretically so. A supplementary population-scaling measurement
(`Results/hot-reload-benchmark-population-scaling-windows-editor-20260827.json`) found reload cost
does not amortize across a population of live instances (no batched-reload API exists today) --
disclosed as a real architecture characteristic. **Debug-instrumentation overhead was not measured**
and is disclosed as a genuine, structurally-grounded gap (`HotReloadPreviewDriver` hardcodes
`traceSink: null` with no injection point, and adding one or using an internals-visible assembly
were both outside this card's allowed/forbidden-changes fence) -- full detail and reasoning in
`Benchmarks~/Phase5/HotReload/README.md`'s "Scope and limitations" section. Full detail in
`Planning~/Evidence/P5-009/`.
