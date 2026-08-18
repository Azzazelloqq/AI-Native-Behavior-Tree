# P2-019 BatchedJobsSameFrame evidence

Observed 2026-08-17 with Unity 6000.5.8f1.

- Scheduling suite: 6/6.
- Batch sizes 1/2/3/4 preserve per-instance atomic order; duplicate scheduling
  and live-dependency disposal reject.
- `NativeSameFramePhaseControllerV1` enforces
  Snapshot → Execute rounds → Reduce → Publish, consumes aborted update IDs, and
  reports exact lane/round/semantic-step metrics.
- Shared 36/36, Snapshot 7/7, Tree/Agent 33/33, Commands/Async 20/20, and the
  P2-020 policy matrix prove deterministic subsystem publication independent of
  batch partition.
- Scheduling XML SHA-256:
  `ecd9e821fddadf1c3e4882f3c0674f51398d35eaa810c187831e587a7e4b474e`.
- Native subsystem XML SHA-256:
  `49fdd5e030727f7416f313b90910986b41810d39917ab46cf79780498607755c`.
