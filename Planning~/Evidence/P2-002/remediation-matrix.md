# P2-002 independent-review remediation

| Rejected finding | Contract remediation | Behavioral evidence |
| --- | --- | --- |
| `AIBT4311` vs `AIBT4312` ambiguous | Exact precedence: invalid/stale/foreign/wrong-generation credential is always 4311; only a conflict targeting a valid currently live lease is 4312 | stale, foreign-with-another-live-lease, wrong-generation, missing-owner, and six live-conflict assertions |
| Instance not bound to exact program generation; no lease token | Nonzero `(ownerId, generation, leaseId)` token and exact instance `(programOwnerId, programGeneration)` binding validated before callbacks/schedule | wrong-generation and foreign-program tests assert 4311, zero callbacks, unchanged owner state |
| No atomic rollback for partial native allocation failure | Every owner constructor allocates through one mapped wrapper and disposes the just-created plus all prior arrays in reverse order before rethrow | program 2 points, input 4 points, instance point, and pass 14 points; allocation probe is zero after each |
| Abort was only prose/fault | `Aborted` is a distinct scheduled terminal outcome, separate from diagnostic-bearing `Faulted` | separate abort and fault tests assert terminal state, callback, no commit/publication, lease release, disposal |
| Capacity plan/tests incomplete | Separate immutable fields for all program/config/instance/input/completion/command/Shared/diagnostic/trace records/payload, alignment, work, and scratch | parameterized rejection and representation-overflow cases for every field; all AIBT4301–4312 exercised |
| Release probe had missing script and did not execute exact path | Empty build scene plus `RuntimeInitializeOnLoadMethod` bootstrap; build script launches the non-development Player and requires post-cleanup marker | no missing-script/compiler warnings; Burst AOT request; Player marker and exit 0 after exact create/schedule/complete/commit/dispose path |
| No rollback when `Job.Schedule` throws | All three leases acquired inside one guarded schedule block and released input→instance→program on any scheduling exception; pass returns to Building | injected schedule failure asserts zero callbacks, every owner initialized, instance readable, and successful reuse |

No acceptance criterion, diagnostic severity, or test expectation was weakened.
