# P2-015 Parallel and decorators evidence

Observed 2026-08-17 with Unity 6000.5.8f1.

- `NativeParallelAndDecoratorPolicyTests`: 19/19 inside Runtime 477/477.
- Covers full semantic-order parallel visits, terminal retention, reverse abort,
  thresholds/ties, repeater generation, timeout equality/overflow, cooldown
  persistence, invalid layouts, and observer ownership in retained branches.
- Runtime XML SHA-256:
  `bf33bfdf8000d5a5f285b283f83b3411711ad1f54ed0849e8d0e96d34b741f73`.

Parallelism remains across instances only; sibling execution semantics are not
changed by worker scheduling.
