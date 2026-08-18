# Agent and Shared blackboard contract v1

## Scope

This specification defines the persisted and runtime-neutral contract for
`Agent` and `Shared` blackboard scopes. It does not define a production native
container or reducer implementation.

The base value, equality, identifier, phase, and determinism rules are defined
by `blackboard-v1.md`, `identity-and-hashing-v1.md`,
`update-phases-v1.md`, and `determinism-v1.md`. Where this document narrows a
Shared operation, this document is authoritative for that operation.

## Versioned authoring representation

Agent and Shared contracts require `aibt.tree` format version `2`. Version `1`
bytes are never reinterpreted. The version-2 proposal is fixed by the schema
and canonical fixtures under `Spikes~/BlackboardScopes/`; production schema and
serializer implementation belong to a later work item.

Format version 2 is additive-complete over the version-1 tree shape: every v1
top-level, node, observer, child, tag, metadata, and Tree-scope blackboard field
remains available with its v1 meaning. `blackboardContracts` is absent when a
tree declares neither extended scope. A Tree-scope entry may retain the v1
shape with omitted `scope`, `typeVersion`, and `default`; omitted `scope` means
Tree. The new `typeVersion`, explicit `scope`, and `default` requirements apply
only to Agent and Shared entries.

Format version 2 also permits an optional node `bindings` object immediately
after `parameters` in canonical property order. Each property maps one
canonical generated blackboard `bindingId` to one existing blackboard `keyId`.
Both strings use the accepted canonical field/binding identity grammar and
object properties are ordered by unsigned UTF-8 `bindingId`. This object only
parameterizes generated blackboard handle fields; it does not extend, merge,
or reinterpret the Phase-1 manifest `reads` or `writes` arrays. The generated
shard descriptor remains authoritative for binding kind, scope, access,
canonical value type, and positive type version.

Compilation requires exactly one mapping for every generated blackboard handle
binding declared by the selected node version and forbids every unknown or
non-blackboard mapping. `bindings` may be absent only when that node version
declares no generated blackboard handles. The target key must exist and its
scope, type identity/version, and permitted direction must match the generated
descriptor exactly before an access ordinal is produced. Snapshot, command,
async-operation, and completion bindings are never parameterized by this
property. ABI v1 generated Shared handles remain read-only; reducer metadata
does not broaden that ABI.

The top-level `blackboardContracts` object occurs immediately before
`blackboard` in canonical property order. It can contain `agent`, then `shared`.
Each present descriptor contains, in order:

1. `contractId`: a canonical authoring identity;
2. `contractVersion`: an unsigned 32-bit integer greater than zero.

Every Agent or Shared key remains a complete entry in the tree's `blackboard`
map. A tree therefore remains self-contained. A scope contract is the complete
set of entries of that scope, including entries unused by that tree's nodes.
Trees intended to share one context repeat an identical complete scope
contract.

An Agent key requires the `agent` descriptor and a Shared key requires the
`shared` descriptor. A descriptor with no corresponding keys is invalid. Each
Agent and Shared entry has this canonical property order:

1. `type`;
2. `typeVersion`, an unsigned 32-bit integer greater than zero;
3. `enumContract`, present only for `Enum32`;
4. `scope`;
5. `reduction`, present only for a Shared key;
6. `description`, when present;
7. `default`.

`default` is required for Agent and Shared keys. It must be a valid canonical
typed value. A reduction contains only `kind`, whose v1 token is one of `min`,
`max`, `sum`, `any`, `all`, `first`, or `last`. A read-only Shared key may omit
`reduction`; any node manifest that declares a write to such a key is a
compilation error. `reduction` is forbidden for Tree and Agent keys.

Default validation happens before canonical bytes or hashes are produced.
Integer ranges are checked in their declared widths; floats and every vector
component must be finite; fixed strings must fit their declared UTF-8 capacity;
opaque IDs and AssetId use their canonical forms. Full-width Int64 values are
parsed and carried as exact signed 64-bit integers through authoring,
canonicalization, hashing, and compiled-default encoding; they never pass
through a binary64 number. `Float32` text is the shortest canonical decimal
that round-trips in Float32 precision. Shortest means the fewest UTF-8 bytes
across canonical plain and exponent forms (`1e-6`, not `0.000001`); equal-length
forms are resolved by ordinal UTF-8 order. Float vectors use exact member order
`x,y` or `x,y,z`; Quaternion uses exact member order `x,y,z,w`, with every
component formatted as Float32. AgentId and EntityId are canonical nonzero
UInt64 decimal strings. OperationId is the accepted canonical four-field
`treeInstanceId:runtimeNodeIndex:generation:sequence` grammar: nonzero UInt64
tree instance, non-sentinel UInt32 runtime node index, UInt32 generation, and
UInt64 sequence. An Enum32 default contains exactly `contract` and `value`, its
contract equals the entry's `enumContract`, and its value fits Int32. A
registered default has the exact registered type version and canonical schema:
every required member occurs once in schema order and no unknown member is
accepted. A missing registered schema, member, or fieldwise codec is an error.

Changing a contract ID starts a distinct contract. Changing any semantic
member of an existing contract requires an increased `contractVersion`.
Reusing one `(contractId, contractVersion)` with different schema bytes is an
error, never a compatible revision. Description changes do not change schema
bytes and do not require a version increase.

## Scope schema bytes and hashes

Agent and Shared schema hashes are SHA-256 over the following canonical byte
stream. Multibyte integers are little-endian. `Utf8` and `Bytes` are an
unsigned 32-bit byte length followed by that many bytes. Text is valid UTF-8
without normalization.

```text
Utf8("aibt.blackboard-scope")
U32(1)                                  // scope-contract format version
U8(scope)                              // Agent=1, Shared=2
Utf8(contractId)
U32(contractVersion)
U32(keyCount)
for key in ordinal-UTF8 key-ID order:
    Utf8(keyId)
    Utf8(canonicalTypeId)
    U32(typeVersion)
    Utf8(enumContractOrEmpty)
    Bytes(canonicalTypedDefaultJson)
    U8(reduction)                       // none=0, min=1, max=2, sum=3,
                                        // any=4, all=5, first=6, last=7
```

`canonicalTypedDefaultJson` is the compact semantic token derived from the
canonical typed value: no insignificant whitespace and the scalar/member
rules of `canonical-json-v1.md`. Object members use their registered canonical
schema order. Descriptions, tree/node identities, layout, policy, registry
order, machine paths, and runtime state are excluded.

The compiler retains the canonical type string and rejects distinct strings
with the same FNV-1a numeric ID. A schema hash is not a substitute for
validating the contract identity, version, type identities, or defaults.

Canonicalizing a tree reorders map entries but never array-valued typed data.
Reordering blackboard object properties therefore cannot change a scope hash.
Any type, type version, enum contract, default, reduction, key identity,
contract identity, or contract version change must change the applicable
scope hash.

## Compiled representation v2

Agent or Shared scope requires compiled-program format version `2`; a version-1
compiled program cannot claim either capability. The v2 logical header extends
the v1 header with one descriptor for each present scope, in Agent then Shared
order:

- contract ID and its 64-bit FNV-1a ID;
- contract version;
- 32-byte scope schema hash;
- 32-byte physical layout hash;
- first scope-slot index and scope-slot count.

Node-registry capability bits `0..6` retain their exact v1 meanings and values.
Compiled-v2 header bit `7` declares Agent-scope records and bit `8` declares
Shared-scope records; these two header bits are not registry capabilities.
The scope-descriptor count is `U32`, and the later raw-layout-stream count is
also `U32`.

The physical layout hash is SHA-256 over `Utf8("aibt.blackboard-layout")`,
`U32(1)`, the scope code, contract identity/version, the raw 32-byte schema
hash, and the slot count, followed by each scope-local slot in key-ID order:
`Utf8(keyId)`, slot index, numeric type ID, type version, enum-contract numeric
ID, 32-bit offset, 32-bit size, 32-bit alignment, canonical default bytes, and
the closed reduction code. Integers and length prefixes use the scope-stream
encoding above. Offsets are relative to their owning scope arena; Tree, Agent,
and Shared arenas never alias.

Compiled defaults use the accepted P1 fieldwise codec rather than CLR/raw JSON
storage. Float2, Float3, and Quaternion write respectively two, three, and four
canonical Float32 words in `x,y,z,w` order. Enum32 writes its numeric contract
U64, signed value I32, then zero padding through its descriptor size. AgentId
and EntityId write one U64. OperationId writes tree U64, runtime-node U32,
activation-generation U32, and sequence U64. AssetId writes GUID-high U64,
GUID-low U64, local-file I64, Bool8 presence, then zero padding; an absent local
file writes zero I64 and false presence. Every multibyte field is little-endian,
and non-finite or noncanonical input is rejected before bytes or hashes exist.

Each compiled slot records its owning scope-local slot index. Each compiled
node access record retains the declared scope and maps its access ordinal to
that scope-local slot. A Shared write access also records the reduction code;
it cannot silently select or override a reducer.

The compiled-content stream is the complete v1 logical field stream, not a
scope-only digest. The exact v2 order is:

1. v1 header fields: magic, compiled/execution/compiler versions, raw semantic,
   registry, and policy hashes, policy version, root, record counts, blob and
   memory sizes, alignment, capability flags, and deterministic compatibility;
2. a scope-descriptor count and the present descriptors in Agent then Shared
   order; absent scopes emit no descriptor;
3. complete node records and child indices in stored order;
4. access records in stored order, each adding scope, scope-local slot, access
   mode, and the closed reduction code;
5. complete blackboard records in stored scope/slot order, including every v1
   Tree record and then the present Agent and Shared records, with retained key
   identity, type/version, enum contract, scope, offset/size/alignment, default
   range, access flags, and observer range;
6. complete v1 observer records in stored order, including observer node,
   owning reactive composite, mode, and watched-slot range;
7. the complete watched-slot table in stored order, each entry retaining scope
   and scope-local slot;
8. length-prefixed config and default blobs;
9. a count plus the raw length-prefixed physical-layout streams for the present
   scopes in Agent then Shared order;
10. complete debug identities in stored order, including `runtimeNodeIndex`,
    node identity, source location, and display name.

All counts and every v1 record/blob/debug field remain hash-covered even when
only Agent or only Shared is present. Multibyte values use the little-endian
and length-prefix rules above. The executable full, Agent-only, and Shared-only
byte streams and independent SHA-256 pins live under
`Spikes~/BlackboardScopes/`. Consequently a header, node, child, access,
blackboard record, blob, scope descriptor, raw layout, reducer, or debug change
changes the compiled-content hash. An Agent-only or Shared-only fixture removes
or rewrites any node observer and observer/watched-slot record whose watched key
was removed; no emitted observer may reference an absent key or slot.

The logical inner `CompiledProgram` remains a valid v1 semantic program: its
header content hash is `CompiledProgramContentHashV1.Compute(inner)` over the
complete inner v1 tables. The containing v2 artifact has a separate outer
content hash, SHA-256 over the exact v2 stream above; the hashes may differ and
neither hash value is included in that outer preimage. A native-v2 header
projection carries the outer hash. This projection does not itself bind or own
a native program image.

## Agent context

An Agent context is a host-created mutable runtime object with exactly one
nonzero `AgentId`, one Agent contract descriptor, one physical layout, slot
values and versions, and one context revision. Live contents are runtime state
and are not a persistent save format.

`AgentId` and every `TreeInstanceId` entering registration, binding, pass
eligibility, lease acquisition/release, or reset coordination is validated as
an exact nonzero UInt64 before state lookup or any callback. Negative, zero,
non-integral, non-canonical, and greater-than-UInt64 values are rejected without
mutation.

Creation validates the complete compiled Agent descriptor and initializes
every slot from its compiled default in scope-slot order. Slot versions and the
context revision start at zero. Creation publishes nothing on failure.

A tree instance that declares Agent access must bind one context before it can
execute. The binding is valid only when all of these match exactly:

- contract ID and its numeric ID;
- contract version;
- schema hash;
- physical layout hash;
- every bound access's ordinal, mode, scope-local slot, type identity, and type
  version.

Scope-schema subset/superset, implicit type conversion, same-version migration,
and best-effort binding are forbidden. Different trees may declare different
access subsets of the same complete context schema; every access ordinal,
mode, slot, and type/version is validated independently. A tree with no Agent
access does not acquire an Agent context implicitly.

One live Agent context owns one AgentId within an execution scheduler. Creating
or registering a second live context with the same AgentId is rejected. A tree
binding is immutable while that tree instance is active. Rebinding, migration,
reset, or disposal requires the context and every bound tree to be quiescent.
The host owns the context and disposes it only after all scheduled users and
bindings have been released.

Many compatible tree instances may bind the same Agent context. At most one of
them owns its Execute lease at a time. Eligible owners are serialized by
ascending `TreeInstanceId`, never by scheduling, batching, or job-completion
order. Agent writes become visible immediately to later nodes in the current
owner and to the next owner after the lease is released. A concurrent or
out-of-order lease attempt is rejected before any node callback runs.

Agent reads, writes, equality, slot-version changes, and observer queuing use
the ordinary blackboard rules. Reset is a transaction: validate every default,
equality operation, required slot-version increment, and the possible context
revision increment in scope-slot order; then publish all changes, or none.
Changed slots increment once each. If at least one slot changes, the context
revision increments once after the slot commits. An unchanged reset is a
no-op. Overflow or equality failure rejects the entire reset without mutation.
Observer reevaluation is queued for bound tree instances in
`(TreeInstanceId, runtimeNodeIndex, watchedSlotIndex)` order and runs only at
the next eligible observer point.

## Shared context and contributions

A Shared context is host-owned and has one exact Shared contract descriptor,
physical layout, slot values and versions, and one context revision. It uses
the same creation, default, quiescence, binding-compatibility, reset, migration,
and version rules as an Agent context, except it has no AgentId.

Execute never mutates a Shared slot. A declared Shared write appends one typed
contribution to a bounded per-tree-instance stream owned by the Shared
context's current update. Each selected instance receives its exclusive stream
and nonzero unsigned 32-bit capacity before scheduling, in ascending
`TreeInstanceId` order. Capacity is an explicit initialized native capacity-plan
input and is recorded in trace metadata; it is not inferred from worker count
or append timing. The aggregate reservation is validated before any Execute
lease. Capacity exhaustion invalidates that instance's stream and rejects the
affected execution operation without resize, wrap, partial contribution,
managed fallback, or direct Shared mutation. Reduce rejects the whole Shared
context update if any participating stream is invalid.

Every accepted contribution stores:

- scope-local Shared slot index and exact type identity/version;
- the capacity copied from its owning preflighted instance stream;
- a canonical value;
- its stable semantic key `(TreeInstanceId, ContributionSequence)`.

`TreeInstanceId` is nonzero. `ContributionSequence` is an unsigned 64-bit
counter reset to zero at the start of that tree instance's Execute phase and
assigned at the semantic write commit point. It increments only after a
contribution is accepted. Counter overflow rejects the contribution without
mutation. Stream validity, owner TreeInstanceId, capacity, count, and every
record's copied capacity/type metadata travel with the Reduce input. A stream
invalidated by any append/capacity failure cannot be repaired by dropping its
bad record. Duplicate semantic keys across the entire Shared context input,
including different slots or streams, are malformed. The key is independent
of worker, batch, append, and completion order.

The stream owner and every contribution `TreeInstanceId` are independently
validated as exact canonical nonzero UInt64 values before contribution
callbacks or reduction. Negative, zero, non-integral, non-canonical, and
overflowing identifiers invalidate the stream and therefore the whole Shared
context update.

The Reduce phase first validates every participating stream and all
contribution records, capacities, types, reducers, globally unique ordering
keys, canonical values, prospective results, equality calls,
slot-version increments, and the possible context-revision increment. It sorts
each slot's contributions lexicographically by unsigned `TreeInstanceId`, then
unsigned `ContributionSequence`. Only after every Shared slot succeeds are
changes committed in scope-slot order. Any failure rejects the whole Shared
context Reduce phase and publishes no slot, version, revision, or observer
change.

For a registered Shared type, v1 uses the sole equality policy accepted by
`burst-node-abi-v1`: `CanonicalBytesEqualityContractId =
0x69e3a80e385e338e`. Replacement comparison is exact canonical-byte equality;
v1 does not register or invoke a custom equality callback. The registered
schema authority, field layout, and every candidate value are validated before
comparison. A missing or mismatched equality contract, malformed schema
authority, or non-canonical candidate rejects the whole Shared context update
without publishing any earlier staged slot, version, revision, or observer
change.

No contributions for a slot means no write and no version change. When at
least one contribution exists, the reducer produces a replacement value from
those contributions only; the previous slot value is not an implicit operand.
The replacement uses canonical equality. An equal result is a no-op. Changed
slots increment once and the Shared context revision increments once after all
slot commits. Queued Shared observers become eligible only in the next update,
because Reduce follows Execute.

## Built-in reductions

All folds use the stable sorted contribution order. Implementations must not
parallelize, reassociate, or vectorize a fold in a way that changes its
declared scalar result.

| Kind | Accepted values | Exact result |
| --- | --- | --- |
| `min` | `Int32`, `Int64`, `Float32`, `Float64` | strict left fold selecting the numerically smaller value |
| `max` | `Int32`, `Int64`, `Float32`, `Float64` | strict left fold selecting the numerically larger value |
| `sum` | `Int32`, `Int64`, `Float32`, `Float64` | strict checked left fold in the declared precision |
| `any` | `Bool` | Boolean OR left fold |
| `all` | `Bool` | Boolean AND left fold |
| `first` | any canonical built-in or registered blackboard value | value with the smallest stable semantic key |
| `last` | any canonical built-in or registered blackboard value | value with the largest stable semantic key |

Every integer `sum` addition is checked in the declared width. Any overflow
rejects the whole Reduce phase; saturation and wrapping are forbidden.
Int64 parsing, comparison, and addition use exact signed 64-bit integers; an
intermediate is never routed through Float64 or another lossy representation.

Every floating input is canonicalized from `-0` to `+0` before use. NaN and
infinity inputs are invalid. `sum` executes one scalar operation at a time in
the declared precision and canonicalizes zero after each operation. Any
non-finite intermediate or result rejects the whole Reduce phase. `min` and
`max` compare canonical finite numeric values; equal values retain the earlier
stable contribution, which is observationally identical under canonical
equality. These rules guarantee independence from worker and partition order,
not cross-architecture bit identity beyond `determinism-v1.md`.

`first` and `last` validate every contribution's complete typed canonical value
before selecting by key. An invalid unselected contribution still rejects the
whole Reduce phase. For Enum32 they retain the slot's declared enum contract
through validation and selection; every contribution's `contract` must match
that contract, and the selected result preserves it exactly.

## Custom reducers

Custom reducers are deliberately absent from persisted contract v1. The token
`custom`, a reducer ID, or custom reducer payload is invalid. A future version
must reuse the accepted public Burst ABI's unmanaged layout, fieldwise codec,
identity/collision, atomic publication, fingerprint, and analyzer rules. It
must also define an ordering and validation model at least as strict as this
document. Associativity or determinism declarations alone are insufficient.

## Stable compilation failures

The following semantic errors are stable v2 compilation results. All have
default severity `Error`, require a JSON Pointer, and use the optional document,
tree, node, and related-location fields defined by `diagnostics-v1.md`.
Production catalog wiring is owned by the implementation work item.

| Code | Condition |
| --- | --- |
| `AIBT2042` | a required Agent or Shared contract descriptor is missing, empty, or has an invalid identity/version |
| `AIBT2043` | the same contract identity/version resolves to different scope schema bytes, hashes, or layouts in one compilation set |
| `AIBT2044` | a node declares a Shared write whose key has no reduction |
| `AIBT2045` | a reduction is forbidden for the scope or incompatible with the key type |
| `AIBT2046` | a custom or unknown reduction is requested in contract v1 |

Errors are emitted in the global diagnostic order. A failed contract or access
does not produce a runnable compiled program, partial scope layout, or fallback
reducer.
