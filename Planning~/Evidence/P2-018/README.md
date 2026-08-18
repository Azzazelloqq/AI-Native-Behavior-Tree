# P2-018 native budgeting evidence

Observed 2026-08-17 with Unity 6000.5.8f1.

- Focused budget driver: 2/2 inside Runtime 477/477, covering zero/one-step
  segments, resume cursor, and counter overflow without advancing state.
- P2-020 mechanically runs every golden case under one-step Budgeted policy and
  compares it with Immediate and BatchedJobsSameFrame.
- Web conformance executes identical lifecycle traces under Immediate and
  Budgeted partitions in Chrome and Firefox.
- Runtime XML SHA-256:
  `bf33bfdf8000d5a5f285b283f83b3411711ad1f54ed0849e8d0e96d34b741f73`.
