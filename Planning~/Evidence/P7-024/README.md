# P7-024 evidence

## Outcome

The Phase 4 scheduling results are now available as a readable
[Jobs-versus-plain-loops report](../../../Documentation~/scheduling-benchmark-report.md) with a
reproducible chart. No benchmark was rerun and no runtime or scheduling policy changed.

The data audit found one important correction to the draft card's premise: the committed Player
harness compares `Immediate`, `Budgeted` and `BatchedJobsSameFrame`; it does not measure
`PipelinedJobs`. `AutoComparison` is an Editor-only same-frame selection comparison and reuses the
cost of the selected fixed policy. The report presents these as evidence gaps rather than supplying
estimated values.

All 42 comparable Player points are included in the chart. `BatchedJobsSameFrame` is 11.95x-29.46x
the fastest plain-loop median on Windows and 13.57x-23.34x on Android for the measured scenarios and
populations. This is a descriptive result, not a new default or threshold.

## Artifact provenance

- `generate_report_data.py` reads the canonical Windows Player, Android Player and Auto Editor JSON.
- `derived-data.json` records every source path and SHA-256, all 42 normalized Player points, the
  ratio formula and the Auto summary.
- `jobs-vs-non-jobs.svg` is deterministic output generated without third-party packages.
- `Documentation~/scheduling-benchmark-report.md` links the raw JSON beside every group of claims.

The existing planned scheduling sample was deliberately not implemented. A live sample would add a
second application and runtime presentation scope while failing to fill the real evidence gap:
`PipelinedJobs` is absent from the benchmark harness. P7-024's approved next-step plan explicitly
starts with a report and forbids a new benchmark run for presentation.

## Verification

Run from the package root:

```powershell
python Planning~/Evidence/P7-024/generate_report_data.py --check
powershell.exe -NoProfile -ExecutionPolicy Bypass -File Tools~/Verification/Verify-Static.ps1
git diff --check
```

The 2026-09-04 verification passed:

- generator `--check`: all 42 Player points reparsed and generated artifacts byte-identical;
- SVG parsed as valid XML;
- static verification: passed, 7 schemas and 137 work items;
- `git diff --check`: passed.

## Scope limits

- Windows: one release IL2CPP/Burst run on one Intel Core Ultra 9 275HX workstation.
- Android: one release IL2CPP/Burst run on one physical Google Pixel 10 Pro.
- `PipelinedJobs` has no committed Phase 4 performance result.
- `Auto` evidence is Editor-only, same-frame and cold-estimator (`Low` confidence in all 24 cases).
- Web has no Unity worker-Job policy and is outside the comparison chart.
- The catalog contains six small built-in-node scenarios; it does not establish a universal policy.
