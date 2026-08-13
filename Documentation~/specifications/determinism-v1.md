# Determinism contract v1

Deterministic mode guarantees, for identical compiled program, initial state, ordered inputs, clock values, random seed, runtime version, platform architecture, and execution policy latency:

- identical semantic child traversal;
- identical lifecycle and abort ordering;
- identical blackboard mutations;
- identical command ordering and payloads;
- random values produced from an AIBT-owned seeded stream;
- no dependence on worker count, batch partition, or worker completion order.

The first release does not promise bit-identical floating-point results across CPU architectures, browsers, Burst versions, or compiler versions. Cross-platform behavior cases SHOULD use tolerances for float observations and MUST record the tolerance.

Wall-clock budgeting, frame-varying input, nondeterministic node declarations, external integrations, and browser background throttling may change when an effect becomes visible. These factors must be present in trace metadata and are outside strict replay guarantees.

Nodes marked nondeterministic are rejected when project policy requires strict determinism.
