# Phase 2 inputs

Phase 2 should preserve every Phase 1 behavior case while replacing only the execution specialization:

1. define the native packed instance/blackboard layout and explicit handler ABI;
2. implement Burst-compatible dispatch without reflection or managed payloads;
3. add job-safe snapshot/command boundaries and measured scheduling policies;
4. rerun the Web spike before exposing any Burst-direct Web policy;
5. establish allocation and throughput baselines only after warmup with representative hardware;
6. keep the managed reference executor as the semantic oracle.
