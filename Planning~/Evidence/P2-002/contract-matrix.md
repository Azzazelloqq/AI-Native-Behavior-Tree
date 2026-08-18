# P2-002 contract matrix

| Card requirement | Normative contract | Spike evidence |
| --- | --- | --- |
| One owner and deterministic disposal for every allocation | `native-runtime-v1.md` ownership graph and allocator/disposal sections | success and scheduled-fault cleanup tests; per-allocation probe returns zero |
| Initialized/executing/aborted/faulted/disposed states | owner-specific state table, token precedence, and terminal cleanup rules | create/schedule/complete, distinct abort/fault, stale/foreign, and live-job conflict tests |
| Immutable capacity inputs before scheduling | exhaustive `NativeCapacityPlanV1` record/payload/alignment/work/scratch list and checked arithmetic | parameterized limit and overflow tests for every normative field |
| No wrap, partial publication, resize, allocation, or managed fallback | bounded execution/publication section | capacity rejection leaves instance unchanged and publishes zero commands; release log scan |
| Job-visible memory cannot mutate/move/dispose while owned | lifecycle and mutation rules | explicit owner rejection plus Unity safety-system early-disposal test |
| Success and fault/abort cleanup | completion/disposal section | successful commit/dispose plus separate scheduled abort and fault discard/dispose tests |
| Snapshots and completions | input-frame ownership and pass normalization staging | read-only input owner is leased until final dependency completes |
| Commands, completions, diagnostics, and trace | separate pass-owned record/payload staging, out-of-band rejection, reserved trace summary | every record/payload limit and partial allocation point is exercised; command publication is end-to-end |
| Shared contribution storage boundary | per-instance record/payload reservations in strict `TreeInstanceId` order | ordered streams pass; reversed streams reject before callbacks; reducer semantics remain excluded for P2-003 |
| Stable diagnostics for all capacity/lifetime failures | exact `AIBT4301`–`AIBT4312` catalog, fields, and 4311/4312 precedence | every code is reached through allocator/plan/arithmetic/capacity/token/live-lease behavior |
| Safety and release compilation | safety/release requirements | `ENABLE_UNITY_COLLECTIONS_CHECKS`, Unity native safety, warning gate, non-development Windows Burst AOT build and launched exact ownership path |
