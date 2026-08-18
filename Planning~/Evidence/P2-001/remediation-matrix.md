# P2-001 independent-review remediation matrix

Status: closed. Independent round-5 review accepted all B1-B5 remediation;
ADR/AIBT-020 are Accepted and P2-001 is Done.

The contract is not weakened to match the disposable spike. Every row requires
the spike to compile and prove the normative surface exactly, followed by a new
full run and independent review.

| ID | Independent blocker | Required remediation | Exact retained proof for closure | Owned paths |
| --- | --- | --- | --- | --- |
| B1 | Compiled ABI differs from the specification | Make both RuntimeStub and Unity harness use one compiled normative ABI source: 8-byte sequential Pack=4 handles containing two `uint` words; 24-byte sequential Pack=8 mutable Enter/Tick contexts containing an opaque `ulong` token, mutable `ulong` PCG state, and readonly odd `ulong` increment; complete `BurstContextResult` values 0-10; exact result storage with a private U16 code word whose low byte is the logical enum and whose reserved high byte is zero; every exact attribute/context/bridge/facade/result signature; and public top-level non-generic unmanaged partial config/memory/value structs with exact field attributes. A binding attribute supplements, never replaces, `AibtConfigField`; every handle field uses reserved `GeneratedHandle`/v1 with canonical U32 size4/align4, while binding metadata owns semantic type records. | Reflection assertions and a closed expected public type/member manifest reject missing and extra `AIBT.Burst` surface; `Marshal` and `UnsafeUtility` assert every pinned size/pack/offset including context offsets 0/8/16 and result offsets 0/2 and 0/2/4/8 while opaque Runtime-owned batch/frame internals remain unpinned; managed and Burst canaries prove defined result codes, canonical zero high byte, undefined-enum `Success=false`, and bridge `InvalidStatus` rejection with no mutation; compile-time positive declarations; exact negative diagnostics for non-public/nested/generic/non-partial storage, missing/duplicate field attributes, binding-without-config-field, semantic ID in a handle config field, and async two-payload mismatch; generated facade and Unity Burst tests run only against that compiled surface. | `Spikes~/BurstNodeAbi/**`; normative corrections, if any, only in the allowed spec/evidence paths |
| B2 | Facade and handshake contract is absent | Generate exact `Fingerprint`, `Validate`, `ExecuteImmediate`, and `Schedule` members. Implement the specified SHA-256 catalog/config/memory/access streams and the unchanged P1 canonical complete-manifest registry hash; validate ABI, catalog, registry, compiled format, execution semantics, config layout, memory layout, and access layout before acquiring a frame or invoking a callback. Use the exact Runtime-owned shared batch handshake/request/result bridge: the facade loops requests, completions advance the opaque cursor, generated code never invents metrics, and `TryPrepareSchedule` atomically claims Ready-to-Scheduled before the job captures a distinct non-owning job-view capability. | One success vector plus eight independently mutated mismatch cases and default/forged batches; each failure emits or returns `AIBT5012`, produces no callback/memory/command effect, and leaves no usable scheduled work. A labeled three-shard feasibility registry subset has exact manifest projections and an independently reconstructed/pinned P1 registry SHA. Exhausted/faulted/rejected result retrieval and multi-request advancement are exact. A real Burst job updates a Runtime-owned native shared record; after dependency completion the original host view reads the same terminal result repeatedly without implicit disposal, while it cannot steal Scheduled work, duplicate Schedule enqueues nothing/returns its input dependency, and the job view is stale after terminal; a feasibility-only Runtime release then makes every view InvalidHandle with no leak. Byte-vector tests independently reconstruct every fingerprint stream, including a random-capability-byte mutation proving that adding/removing `AibtRandomStreamAttribute` changes the catalog hash. | `Spikes~/BurstNodeAbi/**`; `Planning~/Evidence/P2-001/**` records sanitized hashes/results |
| B3 | Unity cannot load the analyzer DLL | Package the analyzer/generator against Unity-compatible Roslyn 4.3 references with all required dependencies resolved according to Unity analyzer guidance. Keep explicit analyzer GUID references and clean UPM topology. Expand log gate to reject analyzer “will not be loaded”, `CS8032`, `AD0001`, generator failure, Burst failure, or managed fallback. | Clean invalid Unity probe emits the expected AIBT diagnostic and no binding; clean valid probe generates/compiles; log scan has zero load/failure markers. Actual Unity Roslyn 4.10 host and Burst 1.8.29 remain triple-gated. | `Spikes~/BurstNodeAbi/**` |
| B4 | RNG proof is prose-only and contexts return zero stubs | Add retained executable implementation/tests for the exact domain bytes, all 32 hash bytes, 63-bit stream mask, SplitMix fold, PCG seed sequence, six outputs for all published vectors, bounded rejection, Float32 conversion, private context-state progression, ordinary re-entry continuity, Restart replay, and abort/budget/observer/rejected-operation no-consume behavior. Enter/Tick completion receives the matching context by `ref` and is the only seam that may atomically publish its mutable state. Non-random contexts use exact inert state/increment `0`/`1`; capability is token-resolved, not encoded as an invalid even increment. | All published intermediate/final words and outputs match exactly; mutation canaries fail for omitted hash bytes/node index/domain terminator/mask; state snapshots prove successful completion advancement and no committed advancement on invalid status, rejected completion, failure, suspension, or failed operation; non-random Enter/Tick returns `PhaseViolation` without mutating `0`/`1` and completion persists no RNG storage; default/forged/cross-frame/copied/stale contexts prove exactly-once frame claim; managed and Burst combined-invalid tables prove precedence InvalidHandle then PhaseViolation then InvalidStatus with default/no mutation; Burst-compiled context calls return the same vectors. | `Spikes~/BurstNodeAbi/**`; normative ambiguity stops for owner decision instead of changing vectors |
| B5 | Generator enforcement is incomplete | Close the storage allowlist to the exact primitive table, exact P1 Float2/Float3/Quaternion, four FixedString types, AgentId/EntityId/OperationId/AssetId, and registered values; validate top-level/public/partial/layout/field contracts; registered value type/version/schema/equality binding; duplicate and numeric node/value/schema/field/binding collisions; callback/capability rules including the exact NotApplicable/AbortOnly/Command-to-Running/AsyncOperation matrix; exact P1 segmented ID grammar for node/value/payload/schema/shard/catalog IDs, its dot-optional field/binding variant, AuthoringId grammar for examples, and positive shard/catalog versions; and exact `AIBT5001`-`AIBT5012` severity/location/suppression. All user CLR enum/StringEnum fields are rejected; enum-like data requires a registered one-field primitive wrapper. Invalid declarations must emit no usable shard, facade, case, or binding. | Table-driven positive/negative suite with exact ID, Error severity, source location, and no-usable-output assertion for every diagnostic family; cancellation matrix mismatches are exact AIBT5004/AIBT5007 with fire-and-forget CommandHandle explicitly independent; every closed built-in CLR-to-ID/version mapping has a positive and mismatch case; fixed-string trailing bytes/UTF-8 and AssetId absent-local zeroing are canaries; malformed empty/hyphen-edge segments, zero/max shard/catalog versions, and invalid/duplicate examples produce the exact result (`0xffffffff` remains valid; zero is `AIBT5009`); raw enum negative plus registered-wrapper positive; explicit field/binding collision injection; generated source forbidden-token scan; two clean deterministic runs and Unity load/Burst rerun. | `Spikes~/BurstNodeAbi/**`; `Planning~/Evidence/P2-001/**` records results |

## Preserved accepted subproofs

- Explicit two-node-asmdef shards and separate consumer catalog assembly.
- Public validated cross-assembly bridge without IVT, reflection, or exposed
  pointer/container ownership.
- Phase-specific contexts, exact lifecycle reason/result seams, and observer
  dispatch without memory access.
- Fieldwise config/memory/registered payload canaries and late-write rollback.
- Cross-shard collision seam and post-acquire `TryFailDispatch` cleanup routing.
- Actual Unity 6000.5.8f1, Roslyn host 4.10.0.0, and Burst 1.8.29 resolution.

These remain useful evidence but do not satisfy P2-001 until B1-B5 all close.

## Independent re-review delta

The green 204-assertion/22-test checkpoint did not close the normative proof.
Remediation is restarted with these exact additional gates:

| Blocker | Live gap | Required next proof |
| --- | --- | --- |
| B1 | The reflection manifest closes type names, bridge member names, and only a few signatures/overload counts. | Enumerate and reject missing/extra constructors, properties, methods, generic constraints, parameter modifiers/types, return types, and overloads for every public `AIBT.Burst` attribute, handle, result, context, reader/writer, batch/frame, bridge member, and exact generated facade member. |
| B2a | The scheduled job view can read a terminal result. | Job view returns `InvalidHandle` after terminal while the host view reads the same retained result repeatedly. |
| B2b | Registered/nested registered H32 inputs are zero in the independent stream; registry and capability checks rely on pinned literals or inequality. | Independently build nonzero registered and nested schema streams, the complete canonical P1 registry byte stream/hash, and the exact catalog byte at the RandomStream capability position. |
| B4 | Unity Burst jobs execute a standalone RNG canary rather than the real Enter/Tick context operations. | Burst-compile and execute actual Enter/Tick context calls for Enter progression and re-entry, validation precedence, and abort/budget/observer/rejected-operation no-consume behavior. |
| B5 | Several primary locations are declaration-wide or inner-symbol locations. | Assert exact spans and additional locations for every diagnostic family, including kind/identity/version/documentation attribute arguments, callback identifiers, the outermost access invocation/expression, and one outermost forbidden syntax without nested duplicates; repeat the NotConfigurable and unusable-output gates. |

## Round-3 candidate checkpoint

The new full command exits zero with two clean Runner invocations of 231
assertions each and Unity 26/26. It adds an explicit 352-record/47,288-byte ABI
manifest fixture; Runtime-owned two-request scheduled cursor with a stale
terminal job view; independent full P1 registry bytes with SHA-256
`7ee137f15483dc75bd251c6469f3f0f189519dfac622a1f8e7498f3f249381a6`;
nonzero recursive registered-schema H32 and exact RandomStream capability byte;
actual Enter/Tick-context Burst jobs; and exact span/count/additional-location,
NotConfigurable, and unusable-output assertions for the reopened diagnostic
families. This is a candidate proof, not acceptance; independent round-3 review
is still required.

## Round-3 independent rejection delta

The round-3 verdict is Reject. The 231-assertion/26-test checkpoint remains
superseded evidence, not acceptance. Normative wording already requires the
following behavior; the Spike must add exact executable proof without changing
the contract:

| Area | Live gap | Required round-4 proof |
| --- | --- | --- |
| B2 all copies | The shared record is attached during `TryPrepareSchedule`, so a host copy made before scheduling retains value-local Ready state. A job-view copy made before terminal returns `PhaseViolation` instead of becoming stale. | Attach the Runtime-owned record at batch creation. Every host copy observes one atomic Ready-to-Scheduled claim and cannot reschedule/steal. Every job-view copy returns `InvalidHandle` after terminal. Host copies read the same retained result repeatedly until release; release invalidates all host/job copies. |
| B5 `AIBT5001`/`5002` | Exact offending-field and storage-type locations are not asserted. | Assert `AIBT5001` on the offending node field identifier and `AIBT5002` on the offending config/memory/registered storage type or field required by the normative table. |
| B5 `AIBT5007`/`5009` | Tests do not close the exact mismatched binding argument and all field/value/type/version/schema argument locations. | Assert the narrowest declarative mismatch argument for `5007` and each applicable offending attribute argument for `5009`, with exact span/count/NotConfigurable/unusable output. |
| B5 `AIBT5010`/`5011` | Non-node collision does not prove the first declaration as an additional location; shard reference failure is not pinned to the shard-type argument. | Inject a non-node numeric collision and assert primary plus first-source additional location; assert `5011` on the exact offending shard-type argument. |

## Round-4 candidate checkpoint

The new exact full command exits zero with two clean Runner invocations of 278
assertions each and Unity 26/26. Both managed and Unity paths now allocate the
Runtime-owned host claim at batch creation, reject scheduling through every
pre-schedule host copy, invalidate every copied job view at terminal, retain
repeatable host-copy result reads, and invalidate all views after release. The
expanded B5 matrix pins the remaining `AIBT5001`, `5002`, `5007`, `5009`,
non-node `5010`, and `5011` primary/additional spans together with Error,
NotConfigurable, and atomic unusable output. This is not acceptance; independent
round-4 review is required.

## Round-4 final rejection delta

Two B5 mismatches remain. An AsyncOperation binding declared under
`NotApplicable` or `AbortOnly` cancellation is a binding incompatibility and
must emit `AIBT5007` at the offending binding attribute. It is not the
declaration/status mismatch `AIBT5004` at the node cancellation argument. The
Spike must prove both incompatible modes with exact span/count, Error,
NotConfigurable, and atomic unusable output. Separately, an external/global
conflict in a selected shard must emit `AIBT5011` at the local CatalogSet
referencing shard-type argument, with the same severity/suppression/atomicity
gates. A new full clean run and independent verdict are required. B2 all-copy
ownership is accepted and must not regress.

## Round-5 candidate checkpoint

The exact full command now exits zero with two clean Runner invocations of 293
assertions each and Unity 26/26. `NotApplicable` and `AbortOnly` async bindings
emit exact `AIBT5007` at the offending binding while declaration/status
mismatches retain `AIBT5004`. A selected external/global shard conflict emits
exact `AIBT5011` at the local CatalogSet `typeof` shard argument. Both gates
assert count, span, Error, NotConfigurable, and atomic unusable output. This is a
candidate proof, not acceptance; independent round-5 review is required.

## Round-5 acceptance

Independent round-5 review accepted the 293-assertion/26-test evidence and the
normative contract on 2026-08-14. All B1-B5 rows are closed; the earlier reject
and remediation entries above remain as audit history.
