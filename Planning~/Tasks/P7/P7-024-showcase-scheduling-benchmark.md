# P7-024 — Showcase Job-system vs. non-Job scheduling benchmark report

Status: `Done`

## Objective

`Benchmarks~/Phase4/Scheduling`, `CostCurves`, `AutoComparison`, and `Platform` already contain real,
committed measurements comparing `Immediate`/`Budgeted` (plain managed loops, no Jobs involved) against
`BatchedJobsSameFrame` and same-frame `Auto` selections. The data audit performed by this card found
that `PipelinedJobs` was not integrated into the benchmark harness and has no committed measurement.
The available data is the "Job system vs plain" comparison requested, collected across multiple P4
cards plus
`P7-002`'s own Windows Player run (`Documentation~/compatibility-matrix.md`'s cited
`windows-player-scheduling-20260821.json`). None of it exists as an actual readable, showcase-oriented
report or chart — it's raw per-case JSON (`Benchmarks~/Phase4/Scheduling/Results/scheduling-windows-
editor-20260819-165205.json` etc.) meant for `P4`'s own research process, not for someone to glance at
and understand the tradeoff.

This card packages already-existing, already-validated data into a genuinely demonstrative artifact —
it is not a request for new benchmark runs unless a real gap is found while assembling it (disclose,
don't silently expand scope, per this session's established discipline).

## Depends on

- `P4-001`/`P4-004`/`P4-006`/`P7-002` (the real benchmark data and its already-accepted interpretation
  — `compatibility-matrix.md` is the reference every platform claim already points at; do not
  re-derive conclusions that document already states, cite it).

## Required reading

- `Documentation~/compatibility-matrix.md` (the already-accepted, real-hardware-grounded numbers and
  their regression-threshold interpretation — the authoritative source for any claim this card makes).
- `Benchmarks~/Phase4/Scheduling/README.md`, `CostCurves/README.md`, `AutoComparison/README.md` (what
  each dataset actually measures and its own disclosed scope/limitations — do not present a number
  outside the scope its own README already discloses).
- `Samples~/README.md` ("scheduling policies" is already listed as planned-but-unbuilt sample
  coverage — confirm whether this card's own deliverable should also be a runnable `Samples~/` piece,
  not only a static report, and disclose the choice made).

## Allowed changes

- A new showcase artifact: a report (Markdown with tables, following this project's own existing
  `compatibility-matrix.md` citation discipline) and/or a chart, built from real, already-committed
  benchmark JSON — no invented numbers.
- Optionally, a new `Samples~/` piece that runs the same tree under different `SchedulingPolicy`
  values live and shows the difference directly (if judged worth the added scope — disclose the
  decision either way rather than silently picking one).
- `Planning~/Evidence/P7-024/`.

## Forbidden changes

- No new benchmark methodology and no re-running of existing benchmarks to get "nicer" numbers —
  reuse the real, already-accepted data as-is. If assembling the showcase finds the existing data
  insufficient for an honest comparison, disclose that finding and ask, rather than quietly running
  new benchmarks outside this card's own reviewed scope.
- Do not restate or contradict `compatibility-matrix.md`'s own accepted conclusions — cite it.

## Deliverables

- A real, readable artifact (report and/or chart) that lets someone who has never read the raw JSON
  understand, at a glance, the real performance difference between Jobs-based and non-Jobs scheduling
  policies at real measured populations.

## Acceptance criteria

- Every number in the showcase artifact traces to a real, already-committed benchmark result file —
  no fabricated or estimated figures.

## Required verification

```text
Verify-Static.ps1
every cited number spot-checked against its real source JSON/README
```

## Handoff notes

- Spun off the same session as `P7-023` (2026-09-03), same owner request, confirmed in scope for
  `1.0`. Recommended to scope as a repackaging task first (cheap, uses already-validated data) —
  re-evaluate whether a new live-comparison sample is also worth the added cost once the report
  itself is assembled and reviewed.
- Completed 2026-09-04 as a report-first scope. `Documentation~/scheduling-benchmark-report.md`
  presents all 42 comparable Windows/Android Player points, a deterministic SVG and a traceable
  derived-data file. No benchmark was rerun, no live sample was added, and the missing
  `PipelinedJobs` measurement is disclosed. See `Planning~/Evidence/P7-024/README.md`.
