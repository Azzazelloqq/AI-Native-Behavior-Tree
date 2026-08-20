# P4-002 fixed-policy scheduling overhead and cost curves evidence

## Result

- `Benchmarks~/Phase4/CostCurves/README.md` and `Results/cost-curves-windows-editor-20260820.json`
  (new): `P4-001`'s harness (`Run-SchedulingBenchmark.ps1`) run unmodified with a wider
  `-AgentCounts '16,64,256,1024'` sweep (four points; P4-001's own harness-proving run used two)
  and the full `-WorkerThreadCounts '1,23'` / three-batch-size sweep already built. No file under
  `Benchmarks~/Phase4/Scheduling/` was touched -- this card's own `Forbidden changes` bars any
  change to the harness itself, and none was needed: `-AgentCounts` already accepted an arbitrary
  comma list.
- Six implemented scenarios x four agent-count points x eight policy/parameter combinations
  (Immediate, Budgeted, and BatchedJobsSameFrame x 3 batch sizes x 2 worker-thread counts) = 192
  measured cases, each five warmup and fifteen measured samples.
- Real, reproducible findings recorded (not just raw numbers -- see
  `Benchmarks~/Phase4/CostCurves/README.md`'s "Reading these curves" section):
  - Immediate and Budgeted are flat (population-independent) per-agent cost across the full
    16-1024 range, in every scenario -- the expected result for non-batched policies.
  - BatchedJobsSameFrame at a **fixed** batch size (32) is **not** flat: per-agent cost roughly
    doubles to quadruples from 16 to 1024 agents, because the number of Job-scheduling
    chunks/`JobHandle.CombineDependencies` calls grows with population at a fixed batch size. This
    is exactly why `P4-004`'s work-estimation/batching calibration (scaling batch size with
    population, per `Documentation~/execution-and-scheduling.md`'s own formula) is necessary, not
    optional -- this card hands that card the curve showing it, not a calibrated answer.
  - `workerThreadCount=23` costs more than `workerThreadCount=1` in every cell, consistent with
    `P4-001`'s smaller-scale finding, narrowing but never reversing at higher agent counts.
  - `deep-sequence-selector-traversal`'s curve is non-monotonic (16 agents costs more than 64) --
    traced to batch-size/population divisibility (16 agents fits one 32-sized chunk; 64 is exactly
    two), not measurement noise.
- No new managed allocation claim, threshold, or policy recommendation is made -- this card's own
  `Forbidden changes` bars it, and none of the above findings are framed as one.

## Decision

No architectural decision was needed. Unlike `P4-001`/`P4-003`, this card's "Allowed changes"
(`Benchmarks~/Phase4/CostCurves/`, `Planning~/Evidence/P4-002/`) already matched what its
acceptance criteria required -- driving the existing harness with a wider parameter sweep and
publishing curves, nothing more. It was promoted from `Draft` to `Ready` after this review found
no gap, then implemented directly.

## Scope and limitations

- One measured run per case on one workstation (Intel Core Ultra 9 275HX, 24 logical processors,
  `JobsUtility.JobWorkerMaximumCount = 23`, Unity 6000.5.8f1, Windows); not generalized to other
  hardware classes (`Planning~/USER_ACTIONS.md` requires owner approval across multiple hardware
  classes before any threshold is adopted).
- `many-programs-small-populations` inherits `P4-001`'s own documented limitation: it reuses the
  same single-leaf tree shape as `scheduling-baseline-empty-job` (a placeholder, not yet a genuine
  multi-program scenario) -- its curve here is consistent with that, not a new problem.
- Batch size was swept at three fixed points (8/32/128) per agent-count point, not co-varied with
  population to find an "optimal" batch size per population -- that co-varying search is
  `P4-004`'s job, not this card's.
- No threshold, regression bound, or scheduling recommendation is drawn from any of this data, per
  this card's own `Forbidden changes`.

See `verification-results.json` for exact commands and results.
