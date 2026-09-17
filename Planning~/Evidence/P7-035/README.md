# P7-035 Swarm Arena Windows Player benchmark evidence

## Result

The complete deterministic matrix finished in a real release Windows x64 Player: 5 populations
(16/64/256/512/1024) x 6 policies x rendering off/on, or 60 cells total. Every cell discarded a
20-second warmup and retained raw samples from a further 60 seconds. The Player log contains
`AIBT_P7_035_SWARM_ARENA_OK|cells=60` and no `_FAIL|` marker.

Build proof is recorded in `swarm-arena-player-20260917-090844.build.raw.json`:
`StandaloneWindows64`, `x86_64`, IL2CPP, Burst enabled, non-development, succeeded, 0 errors.
The raw report is `swarm-arena-player-20260917-090844.raw.json` (370.3 MB); it records workload
tree SHA-256 `bc3c0e0ea7a807376267c427018b959a743e19b2d66153a75f0bb50605944c33` and generated-catalog
descriptor hash `6bc66d6ead4bce3e12507819912e7c3e2bf443673953677b106a73951817237b`.

## AIBT scheduler time

Each table cell is `median/p95/p99` AIBT scheduler milliseconds per frame. These are
`ProductionTreeScheduler.LastFrameConsumedMicroseconds`, not FPS or total-frame time.

### CPU isolation (rendering disabled)

| Population | Auto | Immediate | Budgeted | Jobs calibrated | Jobs fixed 32 | Pipelined |
|---:|---:|---:|---:|---:|---:|---:|
| 16 | 0.668/0.835/0.949 | 0.665/0.831/0.907 | 0.672/0.851/0.958 | 1.363/1.911/2.136 | 1.405/1.937/2.147 | 0.012/0.094/0.130 |
| 64 | 2.516/3.184/3.504 | 2.471/3.159/3.392 | 2.478/3.179/3.430 | 5.095/6.858/7.194 | 5.053/6.895/7.188 | 0.033/0.339/0.489 |
| 256 | 9.891/12.747/13.281 | 12.703/16.267/16.830 | 9.902/12.578/13.106 | 28.229/35.221/39.326 | 29.405/39.046/39.960 | 0.214/1.894/2.583 |
| 512 | 20.871/26.723/28.032 | 21.122/26.860/27.896 | 20.982/26.967/28.623 | 67.045/88.384/89.794 | 66.984/88.206/89.969 | 0.370/5.607/7.052 |
| 1024 | 41.742/53.741/54.871 | 41.851/53.660/54.961 | 42.715/55.048/57.994 | 208.555/282.510/305.956 | 209.225/278.230/279.885 | 0.791/19.312/22.565 |

### Normal rendered Player

| Population | Auto | Immediate | Budgeted | Jobs calibrated | Jobs fixed 32 | Pipelined |
|---:|---:|---:|---:|---:|---:|---:|
| 16 | 0.678/0.849/0.933 | 0.676/0.844/0.936 | 0.678/0.849/0.932 | 1.347/1.953/2.240 | 1.384/2.025/2.318 | 0.011/0.090/0.124 |
| 64 | 2.482/3.204/3.501 | 2.438/3.104/3.302 | 2.429/3.103/3.315 | 5.319/7.181/7.862 | 5.156/7.163/7.587 | 0.034/0.344/0.500 |
| 256 | 9.853/12.473/12.973 | 9.759/12.410/12.772 | 9.752/12.384/12.802 | 25.853/35.294/37.406 | 26.280/35.482/37.053 | 0.177/1.899/2.545 |
| 512 | 21.916/27.924/29.859 | 20.816/26.548/27.425 | 20.707/26.228/26.798 | 67.069/88.526/89.625 | 67.031/88.961/90.409 | 0.328/5.565/7.009 |
| 1024 | 41.664/52.807/54.205 | 41.917/53.642/54.807 | 41.822/53.521/54.769 | 207.160/276.812/278.541 | 207.796/277.746/279.836 | 0.781/19.291/22.389 |

The last measured frame's `frameEntrySnapshot` confirms the calibrated/fixed distinction. For
populations 16/64/256/512/1024, calibrated same-frame Jobs used respectively
13x2, 3x22, 12x22, 13x40 and 13x79 average batch-size x batch-count; the fixed comparison used
32x1, 32x2, 32x8, 32x16 and 32x32. Pipelined Jobs recorded 13x2, 3x22, 12x22, 13x40 and 13x79.
Each snapshot accounts for exactly the cell population; raw data also records selected policy,
selection reason, confidence, expected/executed node steps, requested/observed latency, worker
count and allocated/consumed budget.

## Validation

- Recomputed 420 reported aggregates (median/p95/p99 frame and AIBT timing plus median render
  timing) from raw arrays: 420/420 match at the report's published 0.1 microsecond precision.
- Checked 660 raw-array lengths against their cells' `frameCount`: 660/660 match.
- Checked 60 frame-entry snapshots against their populations and policy-count totals: 60/60 match.
- `PipelinedJobs` retains its actual delayed-result behavior; no same-frame reinterpretation was
  performed.

## Editor versus Player

P4-008's approximately 13-14x Editor-to-Player difference was measured on its Phase 4 scenario
sweep, not this Swarm Arena workload. This card provides Player-only data and has no identically
configured Editor control series, so it neither confirms nor refutes that multiplier for real
gameplay. No default policy or scheduler constant is changed by this measurement.

## Limitations

- Movement/input sampling remains inside `ProductionTreeScheduler.Update`; its isolated cost is
  not directly bracketed. Scheduler and render timings are measured directly; only a derived
  frame-time remainder is available for movement/engine overhead.
- Jobs capabilities use disclosed non-optimal round-number settings (20,000 ns target, 5,000 ns
  minimum, maximum batch 2048); actual batch size/count is reported rather than assumed optimal.
- Pipelined effects land at the next idle barrier and are reported with that real latency.
- This is one uncontrolled Windows x64 workstation run, not a claim across hardware.
- `frameEntrySnapshot` is the last measured frame, not a full per-agent timeline.
- No scheduler, profile, tree, node, gameplay, or population-selection tuning occurred after the
  canonical run began.
