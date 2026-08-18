# P2-003 independent review round 1 remediation

Round-1 outcome: `Changes required`.

No criterion was weakened. The candidate was remediated as follows.

| Review blocker | Remediation | Executable evidence |
| --- | --- | --- |
| v2 schema dropped v1 node fields and over-required new blackboard fields | Restored the complete v1 top-level/node/observer/tag/metadata shape; Agent/Shared alone conditionally require scope/typeVersion/default | strict Ajv valid canonical/reordered/tree-only fixtures; two negative fixtures |
| Compiled hash modeled only a scope extension | Replaced it with the full v1-derived header/node/child/access/blackboard/blob/debug stream plus optional scope descriptors and raw layouts | full, Agent-only, and Shared-only pins; header/node/child/access/record/blob/layout/debug sensitivity assertions |
| Agent lifecycle was only prose | Added AgentId registry, exact schema/layout and per-binding access validation, immutable binding, quiescence, ordered exclusive leases, callback-before-rejection canaries, atomic reset/equality-failure model | Agent context lifecycle assertions |
| Typed defaults were serialized without complete validation | Added exact integer ranges, finite scalar/vector rules, fixed-string capacity, Enum32 contract/value, and registered schema/version/member validation | positive and negative typed-default assertions |
| Shared invalid streams and metadata could bypass whole-context validation | Streams now carry validity/capacity/owner/sequence; records carry copied capacity/type/version; Reduce validates global keys and every First/Last input; Int64 uses BigInt | invalid-stream atomicity, metadata mismatch, cross-slot duplicate, First/Last invalid-unselected, Int64 boundary/overflow assertions |

Custom reducers remain explicitly deferred and rejected in contract v1.

