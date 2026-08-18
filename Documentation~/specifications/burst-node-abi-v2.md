# Public Burst node ABI v2

This specification is the normative production delta over
[`burst-node-abi-v1.md`](burst-node-abi-v1.md). Read v1 first. Every v1
declaration, public name and signature, analyzer rule, capability rule, codec,
ordering rule, diagnostic, and observable transaction semantic remains
normative unless this file explicitly replaces it. The accepted decision is
[ADR P2-012](../decisions/ADR-P2-012-burst-node-abi-v2.md).

ABI v1 is retained as historical feasibility documentation. Production
generated dispatch and Runtime bridge execution use ABI v2 only.

## Version and public source surface

ABI v2 is identified by unsigned value `2`:

- every generated shard exposes `public const uint AbiVersion = 2u`;
- a catalog set accepts only selected usable shards whose marker is `2u`;
- `BurstCatalogHandshake.AbiVersion` is `2u`;
- a marker or handshake with any other ABI version is `AIBT5012` and runs zero
  callbacks and effects.

The public attributes, enums, typed handles, node callback signatures,
`BurstGeneratedRuntimeBridge` methods, generated `BurstAccess` methods, and
catalog-set facade members keep the exact v1 names and signatures. ABI v2 adds
no public constructor, native-container accessor, pointer accessor, ownership
operation, or mutable public field to an opaque carrier or context.

The explicitly pinned representations of typed handles, hashes, fingerprints,
handshakes, validation results, and execution results remain as specified by
v1. The carrier/context exception is defined below.

Compiled-program format version `1`, execution-semantics version `1`, node type
versions, and canonical node-registry JSON format version `1` are unchanged.

## Transport ABI and layout/schema formats

ABI v2 separates transport/catalog compatibility from the unchanged
layout/schema serialization formats. Only the final catalog fingerprint stream
changes its domain and leading version value:

```text
AIBT-CATALOG-V2\0
U32 2
```

The remainder of the final catalog stream grammar, sort order, canonical
encodings, and hash-word mapping is unchanged from v1. Its three per-node layout
hashes are computed from the unchanged streams below.

Configuration, memory, access, registered-value schema, and the three
catalog-level layout streams retain their exact v1 domain tags, including the
final zero byte:

```text
AIBT-CONFIG-LAYOUT-V1\0
AIBT-MEMORY-LAYOUT-V1\0
AIBT-ACCESS-LAYOUT-V1\0
AIBT-VALUE-SCHEMA-V1\0
AIBT-CATALOG-CONFIG-LAYOUT-V1\0
AIBT-CATALOG-MEMORY-LAYOUT-V1\0
AIBT-CATALOG-ACCESS-LAYOUT-V1\0
```

In each of these seven streams, the `U32` immediately following the domain is
format version `1`, not the transport ABI version. This overrides the v1
document's use of the term "ABI version" for those fields. With otherwise
identical declarations, their canonical bytes and hashes remain identical
between an ABI v1 build and an ABI v2 build; this is required, not a
compatibility failure.

The shard marker, `BurstCatalogHandshake.AbiVersion`, and final catalog stream
still require transport ABI `2`. A v1 transport marker, handshake, or final
catalog domain/version cannot validate as ABI v2. The handshake's
configuration, memory, and access layout fields continue to compare hashes made
with the retained v1-format layout streams.

## Runtime-private carrier backing

The following public structs are opaque Runtime carriers rather than binary
data contracts:

- `BurstValueReader` and `BurstValueWriter`;
- `BurstDispatchFrame`, `BurstConfigurationReader`, and
  `BurstMemoryAccessor`;
- `BurstExecutionBatch`;
- `BurstEnterContext`, `BurstTickContext`, `BurstAbortContext`,
  `BurstExitContext`, and `BurstObserverContext`.

`BurstEnterContext` and `BurstTickContext` retain
`StructLayout(LayoutKind.Sequential, Pack = 8)`. Each retains this exact private
prefix:

| Field | Type | Offset |
| --- | --- | ---: |
| `_validationToken` | `ulong` | `0` |
| `_randomState` | `ulong` | `8` |
| `_randomIncrement` | `ulong` | `16` |

ABI v2 removes only the exact Enter/Tick total-size pin. Runtime MAY append
job-safe private backing after the preserved 24-byte prefix while maintaining
sequential pack `8`. ABI verification MUST check the layout kind, pack, prefix
field types, and `Marshal.OffsetOf` values, but MUST NOT require
`Marshal.SizeOf` or `UnsafeUtility.SizeOf` to equal `24`.

For every other opaque carrier/context listed above, private field count, order,
offsets, alignment, packing, and total size are Runtime implementation details.
Consumer code MUST NOT serialize, reinterpret, raw-byte compare, or place any
opaque carrier/context in fixed-size storage. ABI tests MUST NOT use
`Marshal`/`UnsafeUtility` to pin an exact total size for Enter/Tick or any
private physical layout for the other opaque carriers/contexts.

Every carrier remains unmanaged, Burst-compatible, non-owning, bounded by the
accepted capacity plan, and constructible only through the validated Runtime
seam. Runtime MAY place private `NativeArray` views, read-only native views,
fixed control records, and index-plus-generation validation state directly in
the carrier. Those fields MUST NOT be publicly exposed, allocated, resized,
disposed, or retained by consumer code.

Storage resolution MUST use only the views and validation state carried by the
current batch/frame/context chain. A validation token is an opaque
index-plus-generation/liveness value and is never a raw pointer. The execution
path MUST NOT use a global token registry, mutable static map, `SharedStatic`
current-context lookup, thread-local side channel, reflection, or managed
fallback.

Each operation validates owner generation, lease/frame liveness, instance and
node identity, phase, declared capability, typed handle, and bounds before
exposing or staging data. Default, forged, foreign, copied-after-expiry, and
wrong-phase values fail exactly as in v1. Carrier copies neither transfer
ownership nor extend liveness, and at most one matching completion may publish
a frame transaction. Here `copied-after-expiry` means logical generation,
frame, or workspace expiry while the Runtime backing is still allocated. It
does not permit invoking a non-owning carrier after physical owner disposal;
the host must first complete the final dependency and prove that no carrier can
be retained, as required by `native-runtime-v1.md`.

Enter and Tick contexts still carry a private transactional PCG state and odd
increment. Only the matching successful completion publishes the advanced PCG
state. The additional job-safe backing does not change random derivation,
failure precedence, or rollback behavior.

## Implicit Runtime built-in metadata authority

Every catalog set implicitly consumes exactly one immutable internal Runtime
built-in metadata authority. It contains the canonical built-in registry
artifact required by the ABI v2 handshake. A deterministic build-time
Authoring adapter MUST reconstruct the same manifest set from
`BuiltInNodeManifests` and reject any byte/hash mismatch. The catalog generator
reads the authority as referenced Runtime metadata and unions it with selected
public shards. Hosts do not list it in `AibtCatalogSetAttribute`, it adds no
public API, and neither Runtime nor generated code discovers it by assembly
scanning or reflection.

The catalog-set `NodeRegistry` handshake hash is the canonical registry JSON
hash over the union of:

1. every Runtime built-in manifest from that authority; and
2. every manifest from the selected valid public ABI v2 shards.

The union uses the unchanged canonical manifest writer and global identity,
version, and numeric-collision checks. A collision between a Runtime built-in
and a public node makes the catalog set unusable. Missing, duplicated, stale,
or mismatched built-in authority makes the catalog set unusable before a facade
or job is emitted. Missing or duplicated authority is `AIBT5011`; ABI or
byte/hash mismatch is `AIBT5012`.

Runtime built-ins are metadata-only for the generated catalog. They contribute
to `NodeRegistry`, but not to selected shard count, public dispatch case count,
case indices, the generated switch, or the configuration/memory/access catalog
layout streams. The Runtime native semantic executor handles them directly.
Generated dispatch MUST NOT synthesize lifecycle callbacks or placeholder cases
for built-ins.

## Migration and fail-closed behavior

All consumer node assemblies, metadata shards, catalog-set facades, and
Burst/AOT products MUST be regenerated and rebuilt when moving from v1 to v2.
Source declarations that used only the public v1 contract require no semantic
rewrite. Persisted trees and compiled programs require no migration solely for
the carrier change, but their runtime catalog handshake must be rebuilt for v2.

The production bridge does not execute ABI v1 catalogs. A v1 shard marker,
catalog fingerprint/domain, or handshake fails before frame acquisition and
returns the existing `AIBT5012` validation result. There is no compatibility
shim, dynamic fallback, or managed execution path.

Precompiled consumer binaries built against another carrier backing are not a
supported distribution boundary. Runtime, generator, generated source, and
consumer Burst/AOT code are one rebuild unit for ABI v2.

## Required verification

ABI v2 verification MUST cover:

- exact public name/signature parity with v1 and marker/handshake value `2`;
- independent final-catalog v2 byte vectors, frozen v1-format
  layout/schema byte vectors, and v1 transport rejection;
- Enter/Tick sequential pack `8` and private prefix offsets `0`/`8`/`16`, with
  no exact total-size assertion;
- direct job-safe carrier access with no raw pointer or global registry;
- default, forged, foreign, stale-copy, wrong-phase, duplicate-completion, and
  reentrancy rejection in Immediate and scheduled jobs;
- transaction rollback and PCG publication through expanded contexts;
- complete registry hashing with the implicit Runtime built-ins, collision
  rejection, and proof that built-ins receive no generated dispatch cases;
- clean consumer regeneration/rebuild and fail-closed v1 migration fixtures;
- Burst/AOT compilation and zero managed allocation after warmup.
