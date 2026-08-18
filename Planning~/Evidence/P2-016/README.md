# P2-016 native observers evidence

Observed 2026-08-17 with Unity 6000.5.8f1.

- `NativeObserverQueueTests`: 6/6 inside Runtime 477/477.
- Covers changed-write queueing, deduplication and node-index order, baseline
  seeding, no-op repeated results, invalid/Running faulting, Self/Lower/Both
  transitions, nested retained branches, and deepest-first abort.
- Shared post-Reduce visibility is covered by Shared 36/36.
- Runtime XML SHA-256:
  `bf33bfdf8000d5a5f285b283f83b3411711ad1f54ed0849e8d0e96d34b741f73`.
- Shared/subsystem XML SHA-256:
  `49fdd5e030727f7416f313b90910986b41810d39917ab46cf79780498607755c`.
