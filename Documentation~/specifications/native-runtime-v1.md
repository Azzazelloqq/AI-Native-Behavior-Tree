# Native runtime ownership and bounded capacity v1

This specification defines native-memory ownership for the initialized AIBT runtime. It does not define the production container API or executor implementation.

The words **MUST**, **MUST NOT**, **SHOULD**, **SHOULD NOT**, and **MAY** are normative.

## Goals and scope

The v1 native runtime:

- owns every native allocation exactly once;
- allocates and sizes all job-visible storage before scheduling;
- never resizes, relocates, allocates, or falls back to managed execution during an execution pass;
- prevents host mutation and disposal while a scheduled job can observe a view;
- rejects capacity and lifetime failures with stable structured diagnostics;
- preserves the observable order defined by `update-phases-v1.md`.

This contract covers the native program image, instance arena, collected input snapshot and completions, pass staging storage, commands, diagnostics, trace, and scheduled-job leases. Managed authoring models and persisted compiled artifacts are outside its scope.

## Ownership graph

```text
NativeProgramImageOwner (immutable, shareable)
  | borrowed read-only lease
  +-- NativeInstanceArenaOwner (one committed mutable instance)
        | exclusive execution lease
        +-- NativeExecutionPassOwner (one scheduled pass)

NativeInputFrameOwner (immutable snapshot + collected completion records)
  | borrowed read-only lease
  +-- NativeExecutionPassOwner

NativeExecutionPassOwner
  +-- staged instance bytes
  +-- normalization and execution scratch
  +-- command staging buffer
  +-- normalized-completion staging buffer
  +-- diagnostic staging buffer and one out-of-band rejection slot
  +-- trace staging buffer with one reserved overflow-summary slot
  +-- scheduled JobHandle
```

An owner is the only component allowed to dispose its allocations. A view or lease is non-owning and MUST NOT dispose, resize, replace, or retain storage beyond its declared dependency. A native allocation MUST NOT be transferred between owners in v1.

### Program image

`NativeProgramImageOwner` owns one validated native copy of the compiled header, node records, child indices, access tables, blackboard records, config/default blobs, and required debug identities. The image is immutable after initialization and may be leased concurrently by multiple instances or passes. It uses `Allocator.Persistent` and is disposed explicitly after every read lease completes.

### Instance arena

`NativeInstanceArenaOwner` owns one instance's committed node memory, private blackboard state, lifecycle state, sequence counters, and scheduler state. It uses `Allocator.Persistent`, is bound to exactly one program-image generation, and admits at most one execution lease. The host MUST NOT read or mutate committed storage while that lease is active.

Execution writes pass-owned staging storage. The committed arena is replaced fieldwise or copied from staging only after successful job completion and validation. Preflight, ownership, capacity, integrity, or malformed-result failure marks the transport pass `Faulted`, discards staging, and leaves the committed arena unchanged. A deterministic node or executor fault discovered after a valid callback frame was acquired is instead a validated `Completed` transport result with a semantic-fault tree outcome: required Abort/Exit cleanup, generation changes from earlier completed steps, diagnostics, and earlier valid staged effects commit atomically; the failing callback's private staging is discarded.

### Input frame

`NativeInputFrameOwner` owns one immutable snapshot revision and the collected external events and completion records for that revision. It uses `Allocator.Persistent` in v1 because a pass may outlive Unity's `TempJob` age limit. Multiple passes MAY hold read-only leases. The owner may be disposed only after all dependent jobs complete.

Completion records are input facts. Normalized completion ordering and stale-discard decisions are written to pass-owned staging; jobs never mutate the input frame.

### Execution pass

`NativeExecutionPassOwner` owns every mutable allocation used or produced by one pass: staged instance bytes, work queues, normalized completions, commands, diagnostics, trace, and defensive overflow state. It uses `Allocator.Persistent`; all allocation occurs while building the pass and outside the zero-allocation execution path.

The pass owns the final `JobHandle` for its schedule chain. Borrowed program, instance, and input leases are released only after that handle completes. Completion and disposal MAY be combined by a host operation, but disposal MUST NOT occur first.

## Allocator policy

Only `Allocator.Persistent` is valid for v1 owners. `Allocator.Temp`, `Allocator.TempJob`, `Allocator.None`, and custom aliases are rejected by `AIBT4301`. This conservative rule removes frame-age assumptions from scheduler timing. A later allocator policy requires a versioned decision.

Native allocations MUST be created only by owner initialization or execution-pass building. Jobs, Burst callbacks, capacity-failure handlers, completion, and disposal MUST NOT allocate managed or native memory.

An owner initialization is atomic: either every required allocation succeeds and the owner becomes initialized, or already-created allocations are disposed in reverse order and the owner remains uninitialized. Allocation failure MUST NOT publish a partially initialized owner. An allocator failure after a valid preflight is converted to the capacity diagnostic for the allocation's `resourceKind`; raw allocator exceptions MUST NOT escape the owner factory.

## Lifecycle and leases

Default/uninitialized values are not initialized owners.

| Owner | Legal states | Legal transitions |
| --- | --- | --- |
| Program image | `Uninitialized`, `Initialized`, `Disposed` | `Uninitialized -> Initialized -> Disposed` |
| Instance arena | `Uninitialized`, `Initialized`, `Executing`, `Disposed` | `Uninitialized -> Initialized -> Executing -> Initialized`; `Initialized -> Disposed` |
| Input frame | `Uninitialized`, `Initialized`, `Executing`, `Disposed` | `Uninitialized -> Initialized -> Executing -> Initialized`; `Initialized -> Disposed` |
| Execution pass | `Uninitialized`, `Building`, `Scheduled`, `Completed`, `Aborted`, `Faulted`, `Disposed` | `Uninitialized -> Building -> Scheduled -> Completed|Aborted|Faulted -> Disposed`; preflight rejection is `Building -> Faulted -> Disposed` |

Program and input owners maintain checked unsigned active-reader counts. `Executing` for an input frame means one or more active readers; it returns to `Initialized` when the final dependent job completes. Reader-count increment overflow is `AIBT4303` and rejects scheduling. A program image remains logically `Initialized` while read leases exist, but disposal still requires a zero reader count.

Every owner has a nonzero `ownerId` and `generation`. Every acquisition returns an opaque nonzero lease token containing `(ownerId, generation, leaseId)`. A token is valid only for the issuing owner, that exact owner generation, and its currently active lease ID. An instance records the exact `(programOwnerId, programGeneration)` at initialization and MUST NOT execute with a different program owner or generation. Program binding and all lease tokens are validated before any node callback or job schedule.

The instance execution lease is exclusive. Diagnostic selection is exact and uses this precedence:

1. A default, malformed, wrong-owner, wrong-generation, foreign, already-released, or otherwise stale token is always `AIBT4311`, even when the target owner currently has a different live lease. Program-binding mismatch, double completion, double disposal, and any illegal transition for which no valid live lease owns the target are also `AIBT4311`.
2. `AIBT4312` is used only when a valid currently live lease owns a view and the host attempts a conflicting operation: mutate/resize/replace/dispose the leased storage, release that valid lease before its final dependency completes, schedule a second pass for the leased instance, or dispose/reuse the scheduled pass.

Validation failure occurs before callbacks and leaves all owners and counters unchanged. If acquisition succeeds but `Job.Schedule` throws, the pass releases input, instance, and program leases in reverse acquisition order, returns to `Building`, publishes nothing, and rethrows the scheduling failure. No owner may remain `Executing`.

Unity safety checks are an additional development-time guard, not the contract's only enforcement. Release builds MUST retain explicit state, generation, bounds, and lease validation.

## Capacity plan

Every pass is built from one immutable `NativeCapacityPlanV1`. It records these unsigned 32-bit capacities as separate fields; record and payload fields MUST NOT alias one counter:

- program records;
- program config/default bytes;
- committed and staged instance bytes;
- input snapshot/event records and input payload bytes;
- collected/normalized completion records and completion payload bytes;
- command records and command payload bytes;
- Shared contribution records and contribution payload bytes for each tree-instance stream;
- diagnostic records and diagnostic payload bytes;
- trace records and trace payload bytes, including the reserved summary;
- maximum required alignment;
- execution work items;
- scratch bytes.

Each field is an inclusive maximum, never a growth hint. Required capacities are derived from the validated compiled program, selected instance batch, integration snapshot contract, declared node emission bounds, trace level, and scheduler budget. Unknown or unbounded emission is not schedulable on the Burst backend.

All additions, multiplications, alignments, count-to-byte conversions, counters, lease IDs, generations, and reader increments use explicit checked unsigned arithmetic. Invalid zero/power-of-two alignment or inconsistent field relationships are `AIBT4302`. Any arithmetic or representation overflow, including an overflow thrown by a platform checked operation, is caught and converted to `AIBT4303`; raw `OverflowException` MUST NOT escape.

The preflight computes the complete worst-case reservation. Shared contribution streams are reserved per tree instance in `TreeInstanceId` order. Their record and payload capacities are storage inputs only; reducer operations and reduction semantics are defined by their dedicated contract. An invalid or insufficient contribution stream rejects the whole Shared Reduce before publication using `AIBT4307` with `resourceKind` set to `SharedContributionRecords` or `SharedContributionPayload`.

Insufficient program, instance, snapshot, command/contribution, completion, diagnostic, or trace capacity is diagnosed by the resource-specific code below. No job is scheduled and no owner state, committed instance state, sequence, or published output is changed.

## Bounded execution and publication

Job-visible arrays have fixed base addresses and fixed capacities for the entire dependency chain. A job receives only value-type views and counters; it receives no owning container capable of resize or disposal.

Every append uses checked reservation. Multi-record semantic output reserves the whole group before writing any record. Counters never wrap. A defensive runtime exhaustion, which indicates a violated preflight bound or corrupt input, marks the pass `Faulted`, writes the out-of-band rejection diagnostic, and quarantines all staged semantic output. The host publishes none of the pass's commands, normalized completions, ordinary diagnostics, trace, sequence advances, or staged instance bytes.

The out-of-band rejection slot is a fixed field in the pass control block, not an appended record and not a new allocation. It contains exactly one deterministic diagnostic. If more than one worker reports a failure, the winner is the smallest tuple `(phase, treeInstanceId, nodeIndex, workerOrdinal, diagnosticCode)` selected by an atomic minimum operation. Worker completion timing cannot select the diagnostic.

Trace is the sole non-semantic overflow exception required by `trace-v1.md`. A trace buffer with nonzero capacity reserves its last record for one `TraceDroppedSummary`; ordinary trace may use only `capacity - 1` records. A trace record or payload that does not fit atomically increments the summary's dropped count and never changes tree semantics. A requested trace level with zero capacity or a plan incapable of holding the fixed summary fails preflight with `AIBT4310`. The summary slot does not conceal command, completion, diagnostic, or state overflow.

After the final job completes, the host validates the pass control block. A validated `Completed` transport pass commits staged instance state and publishes buffers in the phase order from `update-phases-v1.md`; its semantic tree outcome may be success, failure, waiting, stopped, or the deterministic semantic-fault outcome defined above. Transport `Aborted` and `Faulted` are distinct terminal paths: abort is an expected host cancellation outcome and need not raise a diagnostic; transport fault carries a rejection diagnostic. Both discard staged instance/output storage, complete the final dependency, release leases, and permit deterministic disposal.

A fixed-capacity generated-dispatch workspace MAY be reused sequentially within one exclusive instance/pass lane. Its borrowed configuration, memory, RNG, resolved-binding, live-value, completion, command, operation, and transaction-control arrays remain owned by the outer pass. That owner MUST freeze them and keep them alive through the registered dependency, result consumption, and workspace reset. Reset clears only request-local workspace scratch; durable command/operation prefixes, cancellation tombstones, and operation sequence high-water remain in the caller-owned transaction arena.

## Mutation, completion, and disposal rules

While any dependent job is scheduled or running, the host MUST NOT:

- mutate, resize, clear, replace, or dispose job-visible storage;
- reuse a pass or its counters for another update;
- mutate the instance, program image, or input snapshot through another alias;
- complete or release a lease using an owner, generation, or lease ID other than the one that issued it.

The host completes the pass's final `JobHandle` before reading outputs, committing state, releasing leases, or disposing pass storage. Abort and fault do not bypass this rule: they signal cancellation/fault to the scheduled chain, complete the final dependency, discard or publish according to the preceding section, then dispose all pass allocations in reverse creation order.

Disposal is deterministic and idempotence is not implied. Calling disposal twice is `AIBT4311`; the second call performs no native operation. A failed initialization disposes its partial allocations internally but does not create a disposable owner.

## Stable diagnostics

All codes below have default severity `Error`. They are stable v1 runtime codes in the core/runtime range. Structured fields use the listed canonical names; absent numeric values are zero and absent identities are the empty string.

| Code | Symbol | Required fields | Meaning |
| --- | --- | --- | --- |
| `AIBT4301` | `NativeAllocatorInvalid` | `ownerKind`, `allocator` | An owner was requested with an allocator other than `Persistent`. |
| `AIBT4302` | `NativeCapacityPlanInvalid` | `resourceKind`, `requested`, `capacity`, `alignment` | A capacity/alignment input is internally invalid or cannot satisfy a declared bound. |
| `AIBT4303` | `NativeCapacityArithmeticOverflow` | `resourceKind`, `operation`, `left`, `right` | Checked capacity, byte-offset, alignment, counter, or lease arithmetic overflowed. |
| `AIBT4304` | `NativeProgramCapacityExceeded` | `resourceKind`, `requested`, `capacity` | Program records or config/default bytes are insufficient. |
| `AIBT4305` | `NativeInstanceCapacityExceeded` | `resourceKind`, `treeInstanceId`, `requested`, `capacity` | Committed/staged instance bytes, work items, or scratch bytes are insufficient. |
| `AIBT4306` | `NativeSnapshotCapacityExceeded` | `resourceKind`, `snapshotRevision`, `requested`, `capacity` | Input records or input payload bytes are insufficient. |
| `AIBT4307` | `NativeOutputCapacityExceeded` | `resourceKind`, `treeInstanceId`, `nodeIndex`, `requested`, `capacity` | Command records/payload or per-instance Shared contribution records/payload are insufficient. |
| `AIBT4308` | `NativeCompletionCapacityExceeded` | `resourceKind`, `treeInstanceId`, `nodeIndex`, `requested`, `capacity` | Completion records or completion payload bytes are insufficient. |
| `AIBT4309` | `NativeDiagnosticCapacityExceeded` | `resourceKind`, `treeInstanceId`, `nodeIndex`, `requested`, `capacity` | Ordinary diagnostic records or diagnostic payload bytes are insufficient. |
| `AIBT4310` | `NativeTraceCapacityExceeded` | `resourceKind`, `treeInstanceId`, `nodeIndex`, `requested`, `capacity`, `droppedCount` | Trace record/payload preflight has no valid summary capacity or its capacity inputs are invalid. Ordinary record or payload overflow uses the reserved summary and does not raise this error. |
| `AIBT4311` | `NativeLifetimeStateInvalid` | `ownerKind`, `ownerId`, `generation`, `leaseId`, `state`, `operation` | A token/binding is default, malformed, wrong-generation, stale, or foreign, or an illegal transition has no valid live lease owner. |
| `AIBT4312` | `NativeLiveJobOwnershipViolation` | `ownerKind`, `ownerId`, `generation`, `leaseId`, `state`, `operation` | A conflicting host mutation, release, schedule, reuse, or disposal targets storage owned by a valid currently live lease. |

Diagnostics are emitted without allocating. Preflight and host-side lifetime failures are returned directly to the caller in a fixed result value. Scheduled-pass failures use the deterministic out-of-band rejection slot. A full ordinary diagnostic buffer therefore cannot suppress its own capacity or lifetime failure.

## Safety and release-mode requirements

With Unity collection checks enabled, invalid aliasing, out-of-bounds access, and early disposal MUST also be caught by Unity's safety system in the native-ownership test harness. A passing safety-check test does not permit relying on an exception in release mode.

With collection checks disabled, the same valid create/schedule/complete ownership path MUST Burst compile and execute in a non-development Player. The probe MUST launch the built Player, require an explicit success marker produced after state commit and complete cleanup, and reject missing-script warnings, Burst errors, warnings for the probe assembly, or managed fallback. Contract validation MUST remain active and return the same diagnostic code for controlled capacity and lifetime failures. Release execution MUST NOT silently select managed callbacks or resizable managed containers.

Leak verification covers successful completion, preflight rejection, scheduled fault/abort, and failed initialization. After the final dependency completes and owners are disposed, no native allocation created by the scenario may remain live.
