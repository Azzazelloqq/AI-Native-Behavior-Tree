# P2-013 native lifecycle and memory evidence

Observed 2026-08-17 with Unity 6000.5.8f1.

- `NativeLifecycleMachineTests`: 14/14 inside the final Runtime 477/477 run.
- Covers exact Enter/Tick/Abort/Exit, generation and Activation/Instance memory,
  empty/success/failure/running composites, deepest-first abort, terminal Exit
  precedence, a 2048-node fixed stack, deterministic RNG vectors, and replay.
- Runtime XML SHA-256:
  `bf33bfdf8000d5a5f285b283f83b3411711ad1f54ed0849e8d0e96d34b741f73`.
- Runtime log SHA-256:
  `8c396f96c45ccdd54eb9abd748c3ff51ff48991bb3b60e8bd287ad134a0d48a6`.

The reference executor remains unchanged and is used by the P2-020 golden
adapter as the oracle.
