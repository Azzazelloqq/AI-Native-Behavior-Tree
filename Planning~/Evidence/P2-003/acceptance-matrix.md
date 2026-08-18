# P2-003 acceptance matrix

| Criterion | Evidence | Candidate result |
| --- | --- | --- |
| Versioned canonical authoring | additive-complete `tree-v2.schema.json`, canonical/reordered/tree-only and negative fixtures | Pass |
| Versioned compiled representation | full v1-derived compiled-v2 section includes observers, watched slots, debug runtime indices, and dependency-valid optional scope variants | Pass |
| Hash-covered semantics | ten independent pins and header/node/child/access/record/observer/watched-slot/blob/layout/debug/default-codec sensitivity assertions | Pass |
| Stable missing/mismatched context errors | AIBT2042/AIBT2043 model cases | Pass |
| Stable unconfigured Shared-write error | AIBT2044 model case | Pass |
| Reducer/type/custom negatives | AIBT2045/AIBT2046 model and schema cases | Pass |
| Worker/batch/completion independence | all insertion permutations and partition completion reversals | Pass |
| Explicit First/Last key | mixed TreeInstanceId/sequence vector | Pass |
| Float/NaN/overflow exactness | independent 11-vector shortest Float32 oracle, plain/exponent/sign/zero/ties, folds, NaN/infinity, Int32 and exact BigInt Int64 boundaries/overflow | Pass |
| Whole-context atomicity | zero/negative/overflow TreeInstanceId streams, Enum32 First/Last contract mismatch, overflow, and registered equality failure after an earlier staged slot | Pass |
| Agent lifecycle and sharing | exact UInt64 Agent/Tree IDs, registry, descriptor/layout/access binding, quiescence, atomic reset/equality failure, ordered callback canaries | Pass |
| Typed defaults | ranges plus exact compiled Float2/Float3/Quaternion/Enum32/AgentId/EntityId/OperationId/AssetId bytes and pinned full stream; lossless Int64 JSON path | Pass |
| Bounded contribution stream | exact UInt64 owner/record IDs, validity/capacity/owner/sequence/type metadata, exhaustion, global key uniqueness, malformed metadata and whole-context failure | Pass |
| Enum32 First/Last | both positive selections preserve contract/value; both mismatch paths reject the whole context atomically | Pass |
| Registered Shared equality | registered callback preflight and whole-context atomic rejection on callback failure | Pass |
| Custom reducer ABI | explicitly deferred; schema/model reject `custom` | Pass |
| Production implementation excluded | scoped diff inspection | Pass |
| Independent review | rounds 1 through 3 required remediation; round 4 pending | Pending |
