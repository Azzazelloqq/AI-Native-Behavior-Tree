# P2-014 native reactive evidence

Observed 2026-08-17 with Unity 6000.5.8f1.

- Reactive sequence replacement, retained running subtree exit, reset from
  ordinal zero, nested ownership, and observer-triggered scoped replacement are
  covered by `NativeLifecycleMachineTests` and the P2-020 golden matrix.
- Old running subtrees abort and exit before the replacement Enter.
- Final Runtime gate: 477/477; native/reference Integration gate: 26/26.
- Runtime XML SHA-256:
  `bf33bfdf8000d5a5f285b283f83b3411711ad1f54ed0849e8d0e96d34b741f73`.
- Integration XML SHA-256:
  `368989dc280282728cb4d21d86c1845d64fadeb10831836db2311defe7a4cddc`.
