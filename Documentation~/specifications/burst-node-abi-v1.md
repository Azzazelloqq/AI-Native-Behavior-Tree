# Public Burst node ABI v1

Status: historical feasibility contract. Production dispatch is superseded by
[`burst-node-abi-v2.md`](burst-node-abi-v2.md). ABI v1 generated artifacts and
handshakes fail closed against the production v2 Runtime before any callback or
effect; there is no compatibility fallback.

This specification defines the public C# contract for generated Burst nodes.
It specializes the backend-neutral semantics in `node-contract-v1.md`; it does
not replace them. ABI v1 is identified by unsigned value `1` independently of
every node type version, manifest version, compiled-program version, and
generator version.

## Supported public node kinds

ABI v1 supports user-authored `Condition` and `Action` nodes. Public custom
`Composite` and `Decorator` declarations are errors because no public
child-transition contract is accepted. Built-in composites and decorators
remain executor-owned.

A condition used as an abort observer implements the separate read-only
observer callback below. Observer evaluation cannot return `Running` and cannot
reuse ordinary `Tick` as an implicit observer callback.

## Declaration

A node is declared as a public, non-nested, non-generic `partial struct` whose
declaring type is also non-generic. The declaration struct has no fields of any
kind, including const, static, or readonly fields. It is a compile-time identity
and callback container, not runtime storage.

Exactly one `AibtBurstNodeAttribute` supplies:

```text
string canonicalTypeId
uint nodeTypeVersion
BurstNodeKind kind                 // Condition or Action
Type configurationType
Type memoryType
NodeMemoryLifetime memoryLifetime
bool deterministic
BurstCancellationMode cancellation
BurstNodeCost cost
BurstNodeStatusMask possibleStatuses
```

All new types below live in namespace `AIBT.Burst`. Existing Runtime types are
referenced with their `AIBT` names. The Burst-specific kind, cancellation, and
cost enums are distinct because their current model equivalents live in
`AIBT.Authoring`, which Runtime cannot reference; generated manifests map equal
semantic names without introducing duplicate types in namespace `AIBT`.

The exact declaration metadata API is:

```csharp
using System;

namespace AIBT.Burst
{
    public enum BurstNodeKind : byte { Condition = 0, Action = 1 }
    public enum BurstCancellationMode : byte { NotApplicable = 0, AbortOnly = 1, Command = 2 }
    public enum BurstNodeCost : byte { Trivial = 0, Low = 1, Medium = 2, High = 3, Variable = 4 }
    [Flags]
    public enum BurstNodeStatusMask : byte
    {
        None = 0, Success = 1, Failure = 2, Running = 4
    }
    public enum ConditionResult : byte { Success = 0, Failure = 1 }
    public enum BurstNodeExitReason : byte { Success = 0, Failure = 1, Aborted = 2 }
    public enum BurstNodeAbortReason : byte
    {
        Explicit = 0,
        ObserverSelf = 1,
        ObserverLowerPriority = 2,
        TreeStopped = 3,
        HotReload = 4,
        Timeout = 5
    }

    [AttributeUsage(AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
    public sealed class AibtBurstNodeAttribute : Attribute
    {
        public AibtBurstNodeAttribute(
            string canonicalTypeId, uint nodeTypeVersion, BurstNodeKind kind,
            Type configurationType, Type memoryType,
            AIBT.NodeMemoryLifetime memoryLifetime, bool deterministic,
            BurstCancellationMode cancellation, BurstNodeCost cost,
            BurstNodeStatusMask possibleStatuses) { }
    }

    [AttributeUsage(AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
    public sealed class AibtNodeDocumentationAttribute : Attribute
    {
        public AibtNodeDocumentationAttribute(
            string summary, string category, string whenToUse,
            string whenNotToUse, params string[] exampleIds) { }
    }

    [AttributeUsage(AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
    public sealed class AibtObserverConditionAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
    public sealed class AibtRandomStreamAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public sealed class AibtConfigFieldAttribute : Attribute
    {
        public AibtConfigFieldAttribute(string fieldId, string valueTypeId, uint valueTypeVersion) { }
    }

    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public sealed class AibtMemoryFieldAttribute : Attribute
    {
        public AibtMemoryFieldAttribute(string fieldId, string valueTypeId, uint valueTypeVersion) { }
    }

    [AttributeUsage(AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
    public sealed class AibtBurstValueAttribute : Attribute
    {
        public AibtBurstValueAttribute(
            string canonicalTypeId, uint valueTypeVersion, string canonicalSchemaId) { }
    }

    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public sealed class AibtValueFieldAttribute : Attribute
    {
        public AibtValueFieldAttribute(string fieldId, string valueTypeId, uint valueTypeVersion) { }
    }

    public enum BurstBlackboardAccess : byte { Read = 0, Write = 1, ReadWrite = 2 }
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public sealed class AibtBlackboardBindingAttribute : Attribute
    {
        public AibtBlackboardBindingAttribute(
            string bindingId, BurstBlackboardAccess access,
            AIBT.BlackboardScope scope, string valueTypeId, uint valueTypeVersion) { }
    }

    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public sealed class AibtSnapshotBindingAttribute : Attribute
    {
        public AibtSnapshotBindingAttribute(string bindingId, string valueTypeId, uint valueTypeVersion) { }
    }

    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public sealed class AibtCommandBindingAttribute : Attribute
    {
        public AibtCommandBindingAttribute(
            string bindingId, string payloadTypeId, uint payloadTypeVersion) { }
    }

    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public sealed class AibtAsyncOperationBindingAttribute : Attribute
    {
        public AibtAsyncOperationBindingAttribute(
            string bindingId,
            string startPayloadTypeId, uint startPayloadTypeVersion,
            string cancelPayloadTypeId, uint cancelPayloadTypeVersion) { }
    }

    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public sealed class AibtCompletionBindingAttribute : Attribute
    {
        public AibtCompletionBindingAttribute(string bindingId, string payloadTypeId, uint payloadTypeVersion) { }
    }

    [AttributeUsage(AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
    public sealed class AibtCatalogShardAttribute : Attribute
    {
        public AibtCatalogShardAttribute(string shardId, uint shardVersion) { }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class AibtCatalogSetAttribute : Attribute
    {
        public AibtCatalogSetAttribute(string catalogId, uint catalogVersion, params Type[] shardTypes) { }
    }
}
```

The catalog attributes apply respectively to a public top-level non-generic
fieldless `partial struct` and a public top-level non-generic `static partial
class`. A node assembly has exactly one shard, containing all valid nodes in
that assembly. Catalog-set shard types are explicit and unique; argument order
does not affect generated order or fingerprints. `shardVersion` and
`catalogVersion` are positive unsigned 32-bit values; zero is `AIBT5009` at the
version argument and makes the owning shard or catalog atomically unusable.

User node, registered value, payload, canonical schema, shard, and catalog IDs
use the exact existing P1 `NodeTypeIdRules`: length `1`-`255`, lowercase ASCII
letters/digits/hyphens in non-empty dot-separated segments, at least one dot,
and no segment may begin or end with a hyphen. Closed built-in value IDs in the
primitive/fixed/opaque tables plus `GeneratedHandle` are the only explicit
exceptions and must match their published spelling exactly. Field and binding IDs use the same segment
rules and length but do not require a dot. Thus empty segments, leading or
trailing dots, and strings such as `a..b`, `a.-b`, or `a-.b` are invalid.

`canonicalTypeId` and its FNV-1a 64 numeric ID follow
`identity-and-hashing-v1.md`. A generator rejects duplicate canonical identity
and version pairs and rejects different canonical IDs with the same numeric ID.
The canonical string remains authoritative.

Documentation metadata is supplied by one `AibtNodeDocumentationAttribute`
containing non-empty summary, category, intended use, discouraged use, and at
least one distinct example identifier. Example IDs use the existing Authoring
identity grammar `^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$` and are sorted by strict
UTF-8 bytes. The examples array itself is non-null. The generated manifest is the only manifest
authority for a generated node. A project cannot merge handwritten behavior
metadata or layout values into that manifest.

All node capability arguments are closed: kind is `Condition` or `Action`,
memory lifetime is `Activation` or `Instance`, cancellation and cost are named
members of their published enums, and `possibleStatuses` is a nonzero subset
of `Success|Failure|Running`. Any other underlying enum value or reserved status
bit is `AIBT5004` at that exact argument and makes the shard unusable.

The generated P1-compatible `NodeManifest` projection is exact:

| Manifest field | ABI v1 projection |
| --- | --- |
| `typeId`, `version` | node attribute canonical ID and positive version |
| `summary`, `category`, `whenToUse`, `whenNotToUse` | documentation attribute strings, unchanged |
| `kind` | `condition` or `action` from `BurstNodeKind` |
| `parameters` | every non-handle config field, sorted by canonical field ID; `bool`=`boolean`, `uint`=`uint32`, `ulong`=`uint64`; `required=true`; packing is its canonical native-config offset/size/alignment; no implicit minimum or conditional rule |
| `childPolicy` | `minimum=0`, `maximum=0`, `ordered=true` |
| `reads`, `writes`, `sideEffects` | empty arrays in this P1-compatible projection |
| `possibleStatuses` | declared mask in `success`, `failure`, `running` order |
| `memory` | generated canonical size/alignment and declared lifetime |
| `configuration` | full generated native-config size/alignment, including generated-handle ordinals |
| `cancellation` | exact declared cancellation enum text |
| `executionDomain` | `burst` |
| `deterministic`, `costHint` | exact declared values |
| `examples` | one record per distinct non-empty example ID, sorted by strict UTF-8 ID: `title` is the ID, `parameters` contains every projected required scalar parameter with `false` or unsigned `0`, and `expectedBehavior` is `summary` |
| optional deprecation/replacement | absent in ABI v1 |

Typed binding IDs are intentionally not reinterpreted as P1 manifest
`reads`/`writes`/`sideEffects`: their exact kind/scope/type/capabilities are in
the separate AccessLayout fingerprint. A later versioned schema owned by the
binding/compiler workstream may project them only through an accepted
compatibility decision. The registry hash uses the unmodified
`compiled-program-v1.md` canonical manifest JSON writer and sorts complete
manifests by canonical type ID then version; no Burst-specific alternate
registry domain or hash is permitted. Its existing root object is exactly
`{"format":"aibt-node-registry","formatVersion":1,"manifests":[...]}` in
that property order, followed by the existing per-manifest property order and
canonical JSON rules. Byte formatting is the existing writer's two-space
indentation, `: ` separator, LF newlines, and exactly one trailing LF; UTF-8 has
no BOM. SHA-256 covers those exact bytes, including formatting and final LF.

Open generic nodes, nested nodes, generic declaring types, multiple node
attributes, inheritance-based declarations, and runtime assembly scanning are
forbidden.

## Storage types and layouts

Configuration and memory are distinct public top-level non-generic unmanaged
`partial struct` types. They and
all recursively contained fields MUST use the ABI allowlist:

- fixed-width integers, `bool` encoded as one byte, and finite `float`/`double`;
- the closed P1 vector, quaternion, fixed-string, and opaque-ID table below;
- generated typed handles defined below;
- fixed buffers whose element type is otherwise allowed;
- registered unmanaged types with canonical type ID, positive type version,
  canonical schema ID, generated fieldwise serialization/equality, and an
  accepted layout descriptor.

The following are forbidden even when C# reports the containing type as
unmanaged: raw pointers, function pointers, `IntPtr`, `UIntPtr`, native-sized
integers, every user CLR enum and StringEnum, owned native
containers, `NativeReference`, safety handles, platform handles, explicit
overlapping layouts, reference fields, strings, delegates, tasks,
`UnityEngine.Object`, and types whose size or field offsets differ across the
supported toolchain targets.

Primitive CLR types have this closed canonical field identity mapping. The
version is exactly `1` in every row; an attribute with any other ID or version
is `AIBT5007`.

| CLR type | Canonical value type ID | Encoding |
| --- | --- | --- |
| `bool` | `Bool` | `Bool8` |
| `sbyte` | `Int8` | `Int8` |
| `byte` | `UInt8` | `UInt8` |
| `short` | `Int16` | `Int16LE` |
| `ushort` | `UInt16` | `UInt16LE` |
| `int` | `Int32` | `Int32LE` |
| `uint` | `UInt32` | `UInt32LE` |
| `long` | `Int64` | `Int64LE` |
| `ulong` | `UInt64` | `UInt64LE` |
| `float` | `Float32` | `Float32BitsLE` |
| `double` | `Float64` | `Float64BitsLE` |

The only non-scalar built-ins are these exact CLR-to-identity pairs; every row
has version `1`, canonical size/alignment from the matching accepted
`BuiltInBlackboardTypes` descriptor, and `FixedBytes` field encoding. `Enum32`
is intentionally absent.

| CLR type | Canonical value type ID |
| --- | --- |
| `AIBT.Float2Value` | `Float2` |
| `AIBT.Float3Value` | `Float3` |
| `AIBT.QuaternionValue` | `Quaternion` |
| `Unity.Collections.FixedString32Bytes` | `FixedString32` |
| `Unity.Collections.FixedString64Bytes` | `FixedString64` |
| `Unity.Collections.FixedString128Bytes` | `FixedString128` |
| `Unity.Collections.FixedString512Bytes` | `FixedString512` |
| `AIBT.AgentId` | `AgentId` |
| `AIBT.EntityId` | `EntityId` |
| `AIBT.OperationId` | `OperationId` |
| `AIBT.AssetId` | `AssetId` |

Their canonical bytes are the existing P1 built-in codec grammar, emitted and
read fieldwise in little-endian order rather than copied from CLR storage:
Float2/Float3/Quaternion are respectively 2/3/4 canonical finite Float32
components with `-0` normalized to `+0`; AgentId and EntityId are one U64;
OperationId is tree U64, runtime-node U32, activation U32, sequence U64;
AssetId is GUID-high U64, GUID-low U64, local-file I64, Bool8-present, then zero
padding, and absent local-file ID requires the I64 to be zero. A fixed string is
U16 byte length, strict UTF-8 payload within its named P1 capacity, then zeros
through the complete descriptor size. Nonzero trailing bytes, invalid UTF-8,
noncanonical bools, non-finite floats, or another CLR/type-ID pairing are
`AIBT5002`/`AIBT5007` as applicable.

`Bool`, `Int32`, `Int64`, `Float32`, and `Float64` reuse the existing P1
built-in identities. `Int8`, `UInt8`, `Int16`, `UInt16`, `UInt32`, and
`UInt64` are ABI storage/payload field identities only; ABI v1
does not add them to `BlackboardValueType` or make them legal public Tree
blackboard slot types. Existing approved fixed vectors, quaternion, fixed
strings, and opaque AIBT IDs retain the canonical ID/version from their Runtime
descriptors. `GeneratedHandle`, version `1`, is the closed reserved config-field
identity for every typed handle and uses `GeneratedHandle` encoding, canonical
size `4`, and alignment `4`. It stores only the generated access ordinal. The
matching binding attribute is the sole authority for the bound semantic kind,
scope, and value/payload type records; a handle CLR type name or one of an async
pair's payload IDs is never used as its `AibtConfigField` identity.

ABI v1 configuration narrows the general storage allowlist: a non-handle
`TConfig` field is exactly `bool`/`Bool`, `uint`/`UInt32`, or `ulong`/`UInt64`,
all at version `1`. Generated typed handles are also legal configuration
fields under their exact binding attributes. `sbyte`, `byte`, `short`,
`ushort`, `int`, `long`, `float`, `double`, fixed vectors/strings/IDs,
and registered values remain legal where otherwise allowed in `TMemory` and
payload/value codecs, but are `AIBT5002` in
configuration. Supporting another authoring configuration type requires a
separately accepted node-manifest schema projection; ABI v1 does not reinterpret
one or expand the persisted schema.

ABI v1 has no user enum registration contract. Enum-like user data in memory,
payloads, or registered values is represented by a registered public partial
struct wrapper containing one fixed primitive field with ordinary
`AibtBurstValue` and `AibtValueField` metadata. Framework callback/result enums
listed by this specification are closed ABI control values, not user storage
fields. No `AibtBurstEnum` or StringEnum metadata is inferred.

Configuration, memory, and registered value structs use the C# default
sequential layout. Explicit `StructLayout` attributes, custom Pack/Size/CharSet,
explicit offsets, overlapping fields, and static mutable fields are forbidden.
Every instance storage field is declared directly on the struct and carries
the one exact field attribute required below; inherited, compiler-generated
auto-property backing, or unannotated storage is invalid. Computed properties
and const values contribute no storage or schema.

Configuration is immutable during callbacks. Its generated serializer writes
each declared field in canonical field-ID order using explicit little-endian
encoding. Every padding byte is zero. It does not persist or hash raw CLR struct
bytes. A field has a stable canonical field ID; source declaration order and
field name are not identities. Unknown, missing, duplicate, out-of-range,
non-finite, size, alignment, and offset-overflow cases are compilation errors.
Every configuration field has exactly one `AibtConfigFieldAttribute`. A
generated-handle field additionally has exactly one matching binding attribute;
no binding attribute is valid on a non-handle field. The field's concrete
handle generic arguments, binding kind, scope, logical type IDs/versions, and
binding attribute metadata must match exactly. For every handle field,
`AibtConfigFieldAttribute` must contain that field's stable field ID plus exact
`GeneratedHandle`, version `1`; any semantic type ID there is `AIBT5007`. The
binding attribute independently carries its exact one or two semantic type
records, including both start and cancel payloads for an async operation.

Canonical config, memory, and registered-value layout places fields in unsigned
UTF-8 canonical field-ID order. Starting at offset zero, each field offset is
the smallest offset aligned to that field's required alignment; total alignment
is the maximum field alignment (or `1` when empty), and total size is rounded up
to that alignment. Maximum alignment is `16`. This algorithm, not CLR/source
declaration order or `SyntaxTree.FilePath`, defines every fingerprint offset and
padding range.

Memory is runtime-only. Its generated layout descriptor records total size,
alignment, each stable field ID, field type ID/version, offset, and size. The
entire range, including padding, is zeroed at the lifecycle point required by
its declared lifetime. A typed memory value is read from and written back to
the arena only after the layout fingerprint handshake succeeds.
Every memory field has exactly one `AibtMemoryFieldAttribute`; binding
attributes are forbidden on memory fields.

Within each config, memory, or registered value schema, canonical field IDs are
unique. Different field ID strings with the same FNV-1a 64 numeric ID are
`AIBT5010`; source field name or declaration order never disambiguates them.

A registered binding/payload value is a non-empty public top-level non-generic unmanaged
`partial struct` with exactly one `AibtBurstValueAttribute`. Its non-empty
canonical schema ID string is authoritative versioned metadata distinct from
the type ID. Every instance
field has exactly one `AibtValueFieldAttribute`; static mutable fields,
properties that contribute storage, unannotated fields, explicit overlap, and
custom serialization/equality hooks are forbidden. Its canonical type ID and
positive version are the sole type identity. Fields recursively use this same
registered contract or the closed scalar/fixed-value allowlist. Field order is
canonical field-ID order, encoding is the table below, padding is always zero,
and equality is exact equality of canonical encoded bytes. Thus `-0` equals
`+0`, non-finite floats are rejected, and Runtime can compare staged bytes
without knowing consumer `T`. A different semantic equality policy requires a
new ABI decision.

The generator derives the registered value's full Burst schema/equality
fingerprint as the SHA-256 stream defined below. That fingerprint is distinct
from the existing 64-bit descriptor IDs. P2 registration must bind it to an
existing `RegisteredUnmanagedTypeDescriptor` with: numeric `TypeId` equal to
FNV-1a of the canonical type ID, matching `Version`, canonical byte `Size` and
`Alignment`, `CanonicalSchemaId` equal to FNV-1a 64 over the authoritative
schema ID's strict UTF-8 bytes, `EqualityContractId = 0x69e3a80e385e338e`
(independently verified FNV-1a 64 of
`aibt.equality.canonical-bytes.v1`), and both migration fields zero. Canonical
schema IDs are unique across registered values; an exact duplicate is
`AIBT5010` just like a numeric schema-ID collision. The full
schema fingerprint remains in generated metadata and the catalog/layout
handshake; it is never truncated into either descriptor ID. A numeric schema-ID
collision between different canonical schema ID strings is `AIBT5010`. Layout
changes without a value version change are `AIBT5002`; custom equality,
migration, or serialization callbacks are forbidden in ABI v1.

For each config, memory, or registered value type `T`, generation augments its
partial struct with one reserved nested `public static class BurstCodec`.
`AIBT5002` rejects a handwritten member with that name. The class exposes only
the applicable direct static fieldwise methods:

```csharp
public static BurstContextResult TryDecodeConfiguration(ref BurstConfigurationReader reader, out T value);
public static BurstContextResult TryReadMemory(ref BurstMemoryAccessor accessor, out T value);
public static BurstContextResult TryStageMemory(ref BurstMemoryAccessor accessor, in T value);
public static BurstContextResult TryReadValue(ref BurstValueReader reader, out T value);
public static BurstContextResult TryWriteValue(ref BurstValueWriter writer, in T value);
```

Config emits only `TryDecodeConfiguration`, memory emits only the two memory
methods, and a registered value emits only the two value methods. Each method
is a closed direct-call codec over bridge scalar operations; nesting calls the
nested registered type's generated `BurstCodec`. The catalog facade and shard
access helpers call these methods statically. No codec interface, delegate,
reflection lookup, Runtime generic serializer, or raw struct copy exists.

Maximum ABI v1 alignment is 16 bytes. All sizes, offsets, counts, and generated
handle ordinals are unsigned 32-bit values and use `0xffffffff` as the sole
invalid value where a sentinel is required.

## Stable typed bindings

Blackboard, snapshot, effect-command, async-operation, and completion access is declared
on configuration fields with an explicit canonical binding ID. Field names and
source order are not binding identities, and a binding ID is independent from
its configuration field ID. Binding IDs are unique within a node so one ID cannot
alias two handle fields. Each binding descriptor records:

```text
canonical binding ID and FNV-1a 64 numeric ID
binding kind
value or payload canonical type ID and positive version
blackboard scope where applicable
read/write/reduction capability where applicable
execute/cancel/completion phase capabilities where applicable
```

Different canonical binding IDs that collide numerically are errors. A binding
is compiled to a generated typed handle containing only a validated numeric
ordinal. Constructors and raw ordinal access are not public. Default, forged,
out-of-range, wrong-kind, wrong-scope, and wrong-type handles are rejected
safely; they never perform an access.

ABI v1 permits only `Tree`, `Agent`, and read-only `Shared` blackboard scopes.
`NodeLocal`, undefined underlying scope values, and `Shared` with write capability
are `AIBT5007` at the scope argument.
`Shared` `Write` or `ReadWrite` is `AIBT5007`: `blackboard-v1.md` requires an
accepted deterministic reducer, and P2-001 does not invent that public
contract. A later reducer work item must make an explicit ABI compatibility
decision; it cannot silently broaden v1.

Cancellation and async bindings use this closed declaration matrix:

| Cancellation | Running status | AsyncOperation binding | Abort capability |
| --- | --- | --- | --- |
| `NotApplicable` | forbidden | forbidden | callback exists for uniform dispatch but has no cancel capability |
| `AbortOnly` | optional | forbidden | local deterministic cleanup only; no cancel emission |
| `Command` | required | at least one paired binding required | cancellation only through a declared paired async handle |

A status/cancellation declaration mismatch is `AIBT5004` at the cancellation
or status-mask argument. An async binding under the wrong mode is `AIBT5007` at
that binding; an undeclared/mismatched cancel use is `AIBT5007` at the
invocation. `CommandHandle<T>` remains an independent fire-and-forget effect and
does not satisfy or alter this matrix. Machine-fault cancellation uses only the
paired contracts/payloads captured by successful async starts.

The binding model is additive. Existing Phase 1 manifest `reads` and `writes`
retain their literal-key meaning. A later versioned manifest/compiler task must
introduce parameter-bound access descriptors before generated bindings are
used in production. This ABI does not reinterpret persisted tree JSON or the
Phase 1 manifest schema.

## Callback signatures

All callbacks are public static, non-generic, non-overloaded, and declared
directly on the node struct. `TConfig` and `TMemory` are exactly the types named
by the node attribute.

```csharp
public static void Enter(
    in TConfig config,
    ref TMemory memory,
    ref BurstEnterContext context);

public static NodeStatus Tick(
    in TConfig config,
    ref TMemory memory,
    ref BurstTickContext context);

public static void Abort(
    in TConfig config,
    ref TMemory memory,
    ref BurstAbortContext context,
    BurstNodeAbortReason reason);

public static void Exit(
    in TConfig config,
    ref TMemory memory,
    ref BurstExitContext context,
    BurstNodeExitReason reason);
```

`BurstNodeAbortReason` has exactly `Explicit`, `ObserverSelf`,
`ObserverLowerPriority`, `TreeStopped`, `HotReload`, and `Timeout` in that
stable semantic order. `BurstNodeExitReason` has exactly `Success`, `Failure`,
and `Aborted`.

An observer-capable condition additionally declares:

```csharp
public static ConditionResult Evaluate(
    in TConfig config,
    ref BurstObserverContext context);
```

`ConditionResult` has exactly `Success` and `Failure`. `Evaluate` is a complete
atomic observer step and receives no node memory. A condition may be used by an
observer only when its declaration has `AibtObserverConditionAttribute`. The
attribute requires the exact callback; an unmarked condition declaring it is
also an error.

The generated caller invokes `Enter`, `Tick`, `Abort`, `Exit`, and `Evaluate`
only at the semantic points defined by `execution-semantics-v1.md` and
`reference-executor-machine-v1.md`. Budget exhaustion and resume invoke no node
callback and consume no random value.

The closed callback matrix is:

| Node declaration | Enter | Tick | Abort | Exit | Evaluate | Legal callback result |
| --- | --- | --- | --- | --- | --- | --- |
| Condition without observer marker | required | required | required | required | forbidden | Tick returns only a status present in the declared non-empty mask |
| Condition with `AibtObserverConditionAttribute` | required | required | required | required | required | Tick uses its declared mask; Evaluate returns only Success or Failure |
| Action | required | required | required | required | forbidden | Tick returns only a status present in the declared non-empty mask |

All four lifecycle callbacks are required even when their body is empty; this
keeps generated dispatch uniform and makes cancellation/cleanup reviewable.

## Phase-specific capability views

Context structs have private runtime state and no public mutable fields,
storage pointers, native containers, or constructors. They expose only
generated typed-handle operations.

The exact public opaque handles, result shapes, and phase-context operations
are:

```csharp
namespace AIBT.Burst
{
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 4, Size = 8)]
    public readonly struct BlackboardReadHandle<T> where T : unmanaged
    { private readonly uint _ordinal; private readonly uint _accessToken;
      internal BlackboardReadHandle(uint ordinal, uint token) { _ordinal = ordinal; _accessToken = token; } }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 4, Size = 8)]
    public readonly struct BlackboardWriteHandle<T> where T : unmanaged
    { private readonly uint _ordinal; private readonly uint _accessToken;
      internal BlackboardWriteHandle(uint ordinal, uint token) { _ordinal = ordinal; _accessToken = token; } }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 4, Size = 8)]
    public readonly struct BlackboardReadWriteHandle<T> where T : unmanaged
    { private readonly uint _ordinal; private readonly uint _accessToken;
      internal BlackboardReadWriteHandle(uint ordinal, uint token) { _ordinal = ordinal; _accessToken = token; } }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 4, Size = 8)]
    public readonly struct SnapshotReadHandle<T> where T : unmanaged
    { private readonly uint _ordinal; private readonly uint _accessToken;
      internal SnapshotReadHandle(uint ordinal, uint token) { _ordinal = ordinal; _accessToken = token; } }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 4, Size = 8)]
    public readonly struct CommandHandle<T> where T : unmanaged
    { private readonly uint _ordinal; private readonly uint _accessToken;
      internal CommandHandle(uint ordinal, uint token) { _ordinal = ordinal; _accessToken = token; } }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 4, Size = 8)]
    public readonly struct AsyncOperationHandle<TStart, TCancel>
        where TStart : unmanaged where TCancel : unmanaged
    { private readonly uint _ordinal; private readonly uint _accessToken;
      internal AsyncOperationHandle(uint ordinal, uint token) { _ordinal = ordinal; _accessToken = token; } }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 4, Size = 8)]
    public readonly struct CompletionHandle<T> where T : unmanaged
    { private readonly uint _ordinal; private readonly uint _accessToken;
      internal CompletionHandle(uint ordinal, uint token) { _ordinal = ordinal; _accessToken = token; } }
    public struct BurstValueReader { }
    public struct BurstValueWriter { }

    public enum BurstContextResult : byte
    {
        Success = 0,
        InvalidHandle = 1,
        TypeMismatch = 2,
        PhaseViolation = 3,
        CapacityExceeded = 4,
        StaleCompletion = 5,
        Overflow = 6,
        InvalidEncoding = 7,
        IncompleteValue = 8,
        AlreadyCommitted = 9,
        InvalidStatus = 10
    }
    public enum BurstCompletionOutcome : byte
    {
        Succeeded = 0, Failed = 1, Cancelled = 2
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 8, Size = 24)]
    public struct BurstEnterContext
    {
        private readonly ulong _validationToken;
        private ulong _randomState;
        private readonly ulong _randomIncrement;
        public BurstContextResult TryGetTimeMicroseconds(out long value);
        public BurstContextResult TryNextUInt32(out uint value);
        public BurstContextResult TryNextUInt32(uint boundExclusive, out uint value);
        public BurstContextResult TryNextFloat32(out float value);
        public BurstContextResult TryBeginBlackboardRead<T>(BlackboardReadHandle<T> handle, out BurstValueReader reader) where T : unmanaged;
        public BurstContextResult TryBeginBlackboardRead<T>(BlackboardReadWriteHandle<T> handle, out BurstValueReader reader) where T : unmanaged;
        public BurstContextResult TryBeginBlackboardWrite<T>(BlackboardWriteHandle<T> handle, out BurstValueWriter writer) where T : unmanaged;
        public BurstContextResult TryBeginBlackboardWrite<T>(BlackboardReadWriteHandle<T> handle, out BurstValueWriter writer) where T : unmanaged;
        public BurstContextResult TryBeginSnapshotRead<T>(SnapshotReadHandle<T> handle, out BurstValueReader reader) where T : unmanaged;
        public BurstContextResult TryBeginConsume<T>(CompletionHandle<T> handle, AIBT.OperationId operationId, out BurstCompletionOutcome outcome, out BurstValueReader reader) where T : unmanaged;
        public BurstContextResult TryBeginEffect<T>(CommandHandle<T> handle, out BurstValueWriter writer) where T : unmanaged;
        public BurstContextResult TryBeginStart<TStart, TCancel>(AsyncOperationHandle<TStart, TCancel> handle, out BurstValueWriter startWriter, out BurstValueWriter faultCancelWriter) where TStart : unmanaged where TCancel : unmanaged;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 8, Size = 24)]
    public struct BurstTickContext
    {
        private readonly ulong _validationToken;
        private ulong _randomState;
        private readonly ulong _randomIncrement;
        public BurstContextResult TryGetTimeMicroseconds(out long value);
        public BurstContextResult TryNextUInt32(out uint value);
        public BurstContextResult TryNextUInt32(uint boundExclusive, out uint value);
        public BurstContextResult TryNextFloat32(out float value);
        public BurstContextResult TryBeginBlackboardRead<T>(BlackboardReadHandle<T> handle, out BurstValueReader reader) where T : unmanaged;
        public BurstContextResult TryBeginBlackboardRead<T>(BlackboardReadWriteHandle<T> handle, out BurstValueReader reader) where T : unmanaged;
        public BurstContextResult TryBeginBlackboardWrite<T>(BlackboardWriteHandle<T> handle, out BurstValueWriter writer) where T : unmanaged;
        public BurstContextResult TryBeginBlackboardWrite<T>(BlackboardReadWriteHandle<T> handle, out BurstValueWriter writer) where T : unmanaged;
        public BurstContextResult TryBeginSnapshotRead<T>(SnapshotReadHandle<T> handle, out BurstValueReader reader) where T : unmanaged;
        public BurstContextResult TryBeginConsume<T>(CompletionHandle<T> handle, AIBT.OperationId operationId, out BurstCompletionOutcome outcome, out BurstValueReader reader) where T : unmanaged;
        public BurstContextResult TryBeginEffect<T>(CommandHandle<T> handle, out BurstValueWriter writer) where T : unmanaged;
        public BurstContextResult TryBeginStart<TStart, TCancel>(AsyncOperationHandle<TStart, TCancel> handle, out BurstValueWriter startWriter, out BurstValueWriter faultCancelWriter) where TStart : unmanaged where TCancel : unmanaged;
    }

    public readonly struct BurstAbortContext
    {
        public BurstContextResult TryBeginCancel<TStart, TCancel>(AsyncOperationHandle<TStart, TCancel> handle, AIBT.OperationId operationId, out BurstValueWriter cancelWriter) where TStart : unmanaged where TCancel : unmanaged;
    }

    public readonly struct BurstExitContext { }

    public readonly struct BurstObserverContext
    {
        public BurstContextResult TryGetTimeMicroseconds(out long value);
        public BurstContextResult TryBeginBlackboardRead<T>(BlackboardReadHandle<T> handle, out BurstValueReader reader) where T : unmanaged;
        public BurstContextResult TryBeginBlackboardRead<T>(BlackboardReadWriteHandle<T> handle, out BurstValueReader reader) where T : unmanaged;
        public BurstContextResult TryBeginSnapshotRead<T>(SnapshotReadHandle<T> handle, out BurstValueReader reader) where T : unmanaged;
    }
}
```

Opaque handles have no public constructor or ordinal accessor. Production
config decoding materializes them from the validated binding table. Default or
forged storage never passes the catalog/access token check. Random operations
are valid only for a node marked `AibtRandomStreamAttribute`; otherwise they
return `PhaseViolation`, record a diagnostic, and consume no value.

Cross-assembly generated facades construct phase views only through this public
validated seam; Runtime does not rely on `internal` access to a consumer
assembly:

```csharp
namespace AIBT.Burst
{
    public enum BurstCallbackPhase : byte
    {
        Enter = 0, Tick = 1, Abort = 2, Exit = 3, Observer = 4
    }

    public readonly struct BurstDispatchFrame { }
    public readonly struct BurstConfigurationReader { }
    public struct BurstMemoryAccessor { }

    public struct BurstExecutionBatch
    {
    }

    // Reserved for generated code. Calls remain safe when invoked manually.
    public static class BurstGeneratedRuntimeBridge
    {
        public static BurstContextResult TryGetCatalogHandshake(
            in BurstExecutionBatch batch, out BurstCatalogHandshake handshake);
        public static BurstContextResult TryRejectBatch(
            ref BurstExecutionBatch batch,
            in BurstCatalogValidationResult validationResult);
        public static BurstContextResult TryGetExecutionRequest(
            in BurstExecutionBatch batch,
            out uint instanceOrdinal, out uint runtimeNodeIndex,
            out uint catalogCaseIndex, out BurstCallbackPhase phase,
            out bool hasWork);
        public static BurstContextResult TryGetExecutionResult(
            in BurstExecutionBatch batch, out BurstExecutionResult result);
        public static BurstContextResult TryPrepareSchedule(
            ref BurstExecutionBatch batch,
            out BurstExecutionBatch scheduledView);
        public static BurstContextResult TryAcquireDispatchFrame(
            ref BurstExecutionBatch batch,
            uint instanceOrdinal, uint runtimeNodeIndex, uint catalogCaseIndex,
            BurstCallbackPhase phase, out BurstDispatchFrame frame);
        public static BurstContextResult TryCreateConfigurationReader(
            in BurstDispatchFrame frame, out BurstConfigurationReader reader);
        public static BurstContextResult TryCreateMemoryAccessor(
            in BurstDispatchFrame frame, out BurstMemoryAccessor accessor);

        public static BurstContextResult TryReadBoolean(
            ref BurstConfigurationReader reader, uint fieldOrdinal, uint elementIndex, out bool value);
        public static BurstContextResult TryReadUInt32(
            ref BurstConfigurationReader reader, uint fieldOrdinal, uint elementIndex, out uint value);
        public static BurstContextResult TryReadUInt64(
            ref BurstConfigurationReader reader, uint fieldOrdinal, uint elementIndex, out ulong value);

        public static BurstContextResult TryReadBlackboardReadHandle<T>(
            ref BurstConfigurationReader reader, uint fieldOrdinal,
            ulong valueTypeNumericId, uint valueTypeVersion,
            out BlackboardReadHandle<T> value) where T : unmanaged;
        public static BurstContextResult TryReadBlackboardWriteHandle<T>(
            ref BurstConfigurationReader reader, uint fieldOrdinal,
            ulong valueTypeNumericId, uint valueTypeVersion,
            out BlackboardWriteHandle<T> value) where T : unmanaged;
        public static BurstContextResult TryReadBlackboardReadWriteHandle<T>(
            ref BurstConfigurationReader reader, uint fieldOrdinal,
            ulong valueTypeNumericId, uint valueTypeVersion,
            out BlackboardReadWriteHandle<T> value) where T : unmanaged;
        public static BurstContextResult TryReadSnapshotHandle<T>(
            ref BurstConfigurationReader reader, uint fieldOrdinal,
            ulong valueTypeNumericId, uint valueTypeVersion,
            out SnapshotReadHandle<T> value) where T : unmanaged;
        public static BurstContextResult TryReadCommandHandle<T>(
            ref BurstConfigurationReader reader, uint fieldOrdinal,
            ulong payloadTypeNumericId, uint payloadTypeVersion,
            out CommandHandle<T> value) where T : unmanaged;
        public static BurstContextResult TryReadAsyncOperationHandle<TStart, TCancel>(
            ref BurstConfigurationReader reader, uint fieldOrdinal,
            ulong startPayloadTypeNumericId, uint startPayloadTypeVersion,
            ulong cancelPayloadTypeNumericId, uint cancelPayloadTypeVersion,
            out AsyncOperationHandle<TStart, TCancel> value)
            where TStart : unmanaged where TCancel : unmanaged;
        public static BurstContextResult TryReadCompletionHandle<T>(
            ref BurstConfigurationReader reader, uint fieldOrdinal,
            ulong payloadTypeNumericId, uint payloadTypeVersion,
            out CompletionHandle<T> value) where T : unmanaged;

        public static BurstContextResult TryReadMemoryBoolean(
            ref BurstMemoryAccessor accessor, uint fieldOrdinal, uint elementIndex, out bool value);
        public static BurstContextResult TryReadMemoryInt8(
            ref BurstMemoryAccessor accessor, uint fieldOrdinal, uint elementIndex, out sbyte value);
        public static BurstContextResult TryReadMemoryUInt8(
            ref BurstMemoryAccessor accessor, uint fieldOrdinal, uint elementIndex, out byte value);
        public static BurstContextResult TryReadMemoryInt16(
            ref BurstMemoryAccessor accessor, uint fieldOrdinal, uint elementIndex, out short value);
        public static BurstContextResult TryReadMemoryUInt16(
            ref BurstMemoryAccessor accessor, uint fieldOrdinal, uint elementIndex, out ushort value);
        public static BurstContextResult TryReadMemoryInt32(
            ref BurstMemoryAccessor accessor, uint fieldOrdinal, uint elementIndex, out int value);
        public static BurstContextResult TryReadMemoryUInt32(
            ref BurstMemoryAccessor accessor, uint fieldOrdinal, uint elementIndex, out uint value);
        public static BurstContextResult TryReadMemoryInt64(
            ref BurstMemoryAccessor accessor, uint fieldOrdinal, uint elementIndex, out long value);
        public static BurstContextResult TryReadMemoryUInt64(
            ref BurstMemoryAccessor accessor, uint fieldOrdinal, uint elementIndex, out ulong value);
        public static BurstContextResult TryReadMemoryFloat32(
            ref BurstMemoryAccessor accessor, uint fieldOrdinal, uint elementIndex, out float value);
        public static BurstContextResult TryReadMemoryFloat64(
            ref BurstMemoryAccessor accessor, uint fieldOrdinal, uint elementIndex, out double value);

        public static BurstContextResult TryWriteMemoryBoolean(
            ref BurstMemoryAccessor accessor, uint fieldOrdinal, uint elementIndex, bool value);
        public static BurstContextResult TryWriteMemoryInt8(
            ref BurstMemoryAccessor accessor, uint fieldOrdinal, uint elementIndex, sbyte value);
        public static BurstContextResult TryWriteMemoryUInt8(
            ref BurstMemoryAccessor accessor, uint fieldOrdinal, uint elementIndex, byte value);
        public static BurstContextResult TryWriteMemoryInt16(
            ref BurstMemoryAccessor accessor, uint fieldOrdinal, uint elementIndex, short value);
        public static BurstContextResult TryWriteMemoryUInt16(
            ref BurstMemoryAccessor accessor, uint fieldOrdinal, uint elementIndex, ushort value);
        public static BurstContextResult TryWriteMemoryInt32(
            ref BurstMemoryAccessor accessor, uint fieldOrdinal, uint elementIndex, int value);
        public static BurstContextResult TryWriteMemoryUInt32(
            ref BurstMemoryAccessor accessor, uint fieldOrdinal, uint elementIndex, uint value);
        public static BurstContextResult TryWriteMemoryInt64(
            ref BurstMemoryAccessor accessor, uint fieldOrdinal, uint elementIndex, long value);
        public static BurstContextResult TryWriteMemoryUInt64(
            ref BurstMemoryAccessor accessor, uint fieldOrdinal, uint elementIndex, ulong value);
        public static BurstContextResult TryWriteMemoryFloat32(
            ref BurstMemoryAccessor accessor, uint fieldOrdinal, uint elementIndex, float value);
        public static BurstContextResult TryWriteMemoryFloat64(
            ref BurstMemoryAccessor accessor, uint fieldOrdinal, uint elementIndex, double value);
        public static BurstContextResult TryCommitMemory(
            ref BurstMemoryAccessor accessor);

        public static BurstContextResult TryReadValue(ref BurstValueReader reader, uint fieldOrdinal, uint elementIndex, out bool value);
        public static BurstContextResult TryReadValue(ref BurstValueReader reader, uint fieldOrdinal, uint elementIndex, out sbyte value);
        public static BurstContextResult TryReadValue(ref BurstValueReader reader, uint fieldOrdinal, uint elementIndex, out byte value);
        public static BurstContextResult TryReadValue(ref BurstValueReader reader, uint fieldOrdinal, uint elementIndex, out short value);
        public static BurstContextResult TryReadValue(ref BurstValueReader reader, uint fieldOrdinal, uint elementIndex, out ushort value);
        public static BurstContextResult TryReadValue(ref BurstValueReader reader, uint fieldOrdinal, uint elementIndex, out int value);
        public static BurstContextResult TryReadValue(ref BurstValueReader reader, uint fieldOrdinal, uint elementIndex, out uint value);
        public static BurstContextResult TryReadValue(ref BurstValueReader reader, uint fieldOrdinal, uint elementIndex, out long value);
        public static BurstContextResult TryReadValue(ref BurstValueReader reader, uint fieldOrdinal, uint elementIndex, out ulong value);
        public static BurstContextResult TryReadValue(ref BurstValueReader reader, uint fieldOrdinal, uint elementIndex, out float value);
        public static BurstContextResult TryReadValue(ref BurstValueReader reader, uint fieldOrdinal, uint elementIndex, out double value);
        public static BurstContextResult TryCompleteValueRead(ref BurstValueReader reader);

        public static BurstContextResult TryWriteValue(ref BurstValueWriter writer, uint fieldOrdinal, uint elementIndex, bool value);
        public static BurstContextResult TryWriteValue(ref BurstValueWriter writer, uint fieldOrdinal, uint elementIndex, sbyte value);
        public static BurstContextResult TryWriteValue(ref BurstValueWriter writer, uint fieldOrdinal, uint elementIndex, byte value);
        public static BurstContextResult TryWriteValue(ref BurstValueWriter writer, uint fieldOrdinal, uint elementIndex, short value);
        public static BurstContextResult TryWriteValue(ref BurstValueWriter writer, uint fieldOrdinal, uint elementIndex, ushort value);
        public static BurstContextResult TryWriteValue(ref BurstValueWriter writer, uint fieldOrdinal, uint elementIndex, int value);
        public static BurstContextResult TryWriteValue(ref BurstValueWriter writer, uint fieldOrdinal, uint elementIndex, uint value);
        public static BurstContextResult TryWriteValue(ref BurstValueWriter writer, uint fieldOrdinal, uint elementIndex, long value);
        public static BurstContextResult TryWriteValue(ref BurstValueWriter writer, uint fieldOrdinal, uint elementIndex, ulong value);
        public static BurstContextResult TryWriteValue(ref BurstValueWriter writer, uint fieldOrdinal, uint elementIndex, float value);
        public static BurstContextResult TryWriteValue(ref BurstValueWriter writer, uint fieldOrdinal, uint elementIndex, double value);
        public static BurstContextResult TryCommitBlackboardWrite(ref BurstValueWriter writer);
        public static BurstContextResult TryCommitEffect(ref BurstValueWriter writer);
        public static BurstContextResult TryCommitStart(
            ref BurstValueWriter startWriter, ref BurstValueWriter faultCancelWriter,
            out AIBT.OperationId operationId);
        public static BurstContextResult TryCommitCancel(ref BurstValueWriter cancelWriter);
        public static BurstContextResult TryCommitConsume(ref BurstValueReader completionReader);
        public static BurstContextResult TryCreateEnterContext(
            in BurstDispatchFrame frame, out BurstEnterContext context);
        public static BurstContextResult TryCreateTickContext(
            in BurstDispatchFrame frame, out BurstTickContext context);
        public static BurstContextResult TryCreateAbortContext(
            in BurstDispatchFrame frame, out BurstAbortContext context);
        public static BurstContextResult TryCreateExitContext(
            in BurstDispatchFrame frame, out BurstExitContext context);
        public static BurstContextResult TryCreateObserverContext(
            in BurstDispatchFrame frame, out BurstObserverContext context);
        public static BurstContextResult TryGetAbortReason(
            in BurstDispatchFrame frame, out BurstNodeAbortReason reason);
        public static BurstContextResult TryGetExitReason(
            in BurstDispatchFrame frame, out BurstNodeExitReason reason);
        public static BurstContextResult TryCompleteEnter(
            ref BurstExecutionBatch batch, in BurstDispatchFrame frame,
            ref BurstEnterContext context);
        public static BurstContextResult TryCompleteTick(
            ref BurstExecutionBatch batch, in BurstDispatchFrame frame,
            ref BurstTickContext context, AIBT.NodeStatus status);
        public static BurstContextResult TryCompleteAbort(
            ref BurstExecutionBatch batch, in BurstDispatchFrame frame);
        public static BurstContextResult TryCompleteExit(
            ref BurstExecutionBatch batch, in BurstDispatchFrame frame);
        public static BurstContextResult TryCompleteObserver(
            ref BurstExecutionBatch batch, in BurstDispatchFrame frame,
            ConditionResult result);
        public static BurstContextResult TryFailDispatch(
            ref BurstExecutionBatch batch, in BurstDispatchFrame frame,
            BurstContextResult failure);
    }
}
```

`BurstExecutionBatch` is initialized only by Runtime and is an opaque non-owning
handle to one Runtime-owned native shared batch record plus its immutable actual
handshake. Runtime alone allocates and disposes that record; consumer code has
no ownership, `Dispose`, pointer, or native-container accessor. Copies resolve
the same record rather than copying its cursor/result state.
`BurstGeneratedRuntimeBridge` is public only because generated consumer
assemblies cannot access Runtime internals; it is not a general host extension
surface. `TryGetCatalogHandshake` is read-only: it validates the Runtime-owned
batch token and returns that batch's immutable actual handshake. A default or
forged batch returns `InvalidHandle` and a default handshake.
`TryRejectBatch` accepts only a valid non-success validation result whose
diagnostic number is `5012`; it atomically closes a valid batch with a retained
`ValidationFailed`/`5012` terminal result, records `AIBT5012`, and publishes no
callback, memory, state, command, completion, or operation effect. Calling it
on invalid storage fails safely without mutation.
An invalid batch returns `InvalidHandle`; a success validation result, a
nonzero code outside `1`-`8`, or a diagnostic number other than `5012` returns
`InvalidStatus` without closing the batch.

`TryGetExecutionRequest` validates the batch view capability and exposes only
the current Runtime-selected request. For a valid exhausted batch it returns `Success`,
`hasWork=false`, and default request outputs. For live work it returns
`Success`, `hasWork=true`, and the exact instance, runtime-node, prebound case,
and phase. After scheduling, only the job-view capability returned by
`TryPrepareSchedule` may advance Scheduled to Running and request work; the
original host view returns `PhaseViolation` and cannot steal execution. Invalid batches return `InvalidHandle`; faulted or validation-closed
batches return `PhaseViolation`, with `hasWork=false` and default outputs.
`TryGetExecutionResult` returns the Runtime-owned terminal
`BurstExecutionResult` only after exhaustion, dispatch fault, or validation
rejection. It returns `InvalidHandle` for a default/forged batch and
`PhaseViolation` for a valid nonterminal batch. Generated code never invents
`InstancesVisited`, `SegmentSteps`, or a terminal code. Terminal reads are
repeatable and read-only while Runtime retains the shared record; this method
never consumes the result or disposes storage. Runtime releases the record only
through its non-public host lifecycle outside the generated ABI. Before physical
release, Runtime invalidates the live generation while the backing remains
allocated, so default, foreign, and logically expired copies return
`InvalidHandle`. A carrier is non-owning and MUST NOT be retained beyond its
final registered dependency or invoked after physical owner disposal; a copy
does not extend the allocation lifetime. The ABI does not require a freed native
allocation to remain addressable through a global tombstone registry.

`TryPrepareSchedule` is called only after the generated facade's handshake
validation succeeds. It atomically claims a valid Ready record as Scheduled
and returns a distinct opaque non-owning job-view capability resolving that
same record for the generated job. The original remains a host-view capability;
neither exposes a public discriminator. Invalid/default storage returns `InvalidHandle`; an already
scheduled, running, terminal, rejected, or otherwise non-Ready record returns
`PhaseViolation`. Failure returns a default view, creates no job, invokes no
callback, and changes no Runtime state. The caller's original handle remains
valid and observes `PhaseViolation` from `TryGetExecutionResult` until the
returned dependency completes the job; afterward it resolves the exact shared
terminal result. A duplicate `Schedule` call therefore returns its input
dependency unchanged and cannot enqueue a second job. Neither host nor job view
can claim the record twice, and the job view becomes stale after terminal
completion.

`TryAcquireDispatchFrame` checks the batch token, instance/node bounds, prebound
case index, requested phase, and all fingerprints. Each `TryCreate*Context`
checks that the frame phase matches exactly. Default, copied-after-expiry,
cross-node, forged, or wrong-phase frames and contexts return `InvalidHandle` or
`PhaseViolation` before exposing storage. The seam returns non-owning validated
views and exposes no raw pointer or native-container ownership.
Enter and Tick contexts are mutable values because each carries an opaque
index-plus-generation validation capability followed by a private copy of the
runtime node's 64-bit PCG state and odd increment. For a node without
`AibtRandomStreamAttribute`, the factory writes the canonical inert pair
`state=0`, `increment=1`; the token still identifies that random capability is
absent. The default token is invalid;
the token is never a raw pointer. Runtime resolves time, bindings, operation
state, and other phase capabilities from that token rather than placing another
state object in the frozen context layout.
When acquisition receives a valid live batch but its current request fails any
of those checks, it atomically closes the Runtime cursor as `Faulted` and
retains the exact `BurstExecutionResult`; the facade reads that result through
`TryGetExecutionResult`. A default or forged batch is rejected earlier by the
handshake/request gates and is never mutated here.

Abort and Exit reasons are Runtime-owned frame input. Generated code obtains
them only through `TryGetAbortReason` or `TryGetExitReason`; it never hardcodes
or infers one. After the direct callback and complete memory staging, it invokes
the exactly matching `TryComplete*` once. Tick completion accepts only
`Success`, `Failure`, or `Running` and validates the node's declared status
mask. Observer completion accepts the closed `ConditionResult` enum and has no
memory accessor. `TryCompleteEnter` and `TryCompleteTick` receive the matching
mutable context by `ref`, validate its token and odd increment against the
frame, and claim that frame transaction exactly once. A phase mismatch, invalid
status, context copied from an already claimed transaction, repeated
completion, or expired frame publishes nothing. `TryFailDispatch` discards every staged value,
memory range, command, completion consumption, and uncommitted operation for
the frame, then closes the Runtime cursor with `Faulted` and records the failure
exactly once. A successful `TryComplete*` publishes the callback transaction,
updates Runtime-owned counters, and advances the Runtime cursor to its next
request; generated code cannot calculate or overwrite metrics. The only
exception to failure rollback is an already committed cancellation tombstone
and any successfully appended Cancel command, which are intentionally
irreversible.
`TryCreateMemoryAccessor` rejects an Observer frame, and generated Evaluate
dispatch contains no memory load, accessor, staging, or write-back call.
After `TryAcquireDispatchFrame` succeeds, every non-success path in generated
code must return through `TryFailDispatch`; a direct return is forbidden because
it could leave the batch transaction live. A failed acquisition has no frame to
fail; its Runtime-owned fault result is read through `TryGetExecutionResult`,
never constructed or mapped by generated code.

The consumer facade constructs each `TConfig` and `TMemory` locally and emits
one closed bridge call per scalar or fixed-buffer element in stable field-ID
order. A fixed vector, quaternion, fixed string, or opaque ID is reconstructed
from those scalar calls. A registered value type is decoded by its generated
codec shard, which recursively emits the same closed calls; Runtime never
discovers that codec. The bridge validates the field ordinal, element index,
encoding, declared type ID/version, frame token, and range before each access.
It decodes and encodes explicit little-endian values and canonicalizes booleans
and finite floating-point values. It never copies raw CLR struct bytes.

The generated facade stages every memory field through `TryWriteMemory*`, then
calls `TryCommitMemory` exactly once after all field writes succeed. Staging
validates but does not mutate the live arena. `TryCommitMemory` seals the
complete zero-padded staging range but still publishes nothing. The matching
`TryCompleteEnter/Tick` receives the original batch, validated frame, and
matching mutable context; `TryCompleteAbort/Exit` receives the batch and frame.
Completion atomically publishes the batch-owned sealed range together with the
lifecycle/state result and, for Enter/Tick, only the context's mutable PCG state,
then expires the transaction token. A failed read, staged write, seal, callback
result, or completion discards the staging range and the context's private RNG
copy; it leaves live memory, runtime RNG state, lifecycle state, and canaries
unchanged. Padding belongs to the
accessor and is never supplied by a CLR struct. The feasibility canary includes
`int`, `ulong`, `bool`, a fixed vector assembled from scalars, deliberately
different CLR padding, a multi-field memory write-back, and a deliberately late
invalid write proving rollback.

`BurstValueReader` and `BurstValueWriter` apply the same rule to blackboard,
snapshot, completion, effect, start, and cancel values. Context `TryBegin*`
methods validate the typed handle and create an opaque reader or staging writer;
they never read or copy `T`. The bridge scalar methods validate field ordinal,
element index, registered type layout, token, and canonical encoding. A reader
must finish with `TryCompleteValueRead`; completion consumption becomes visible
only through `TryCommitConsume`. Except for cancellation tombstones, each
kind-specific commit seals data into the current frame transaction; external
blackboard values, commands, operation ledger entries, and completion
consumption become visible only when the matching lifecycle `TryComplete*`
succeeds. Reads in the same frame observe its sealed blackboard-write overlay.
`TryCommitStart` reserves a never-reused operation sequence and returns the
candidate ID; a later failed frame publishes no active operation or Start
command, but the reserved sequence is not rolled back. `TryCommitCancel` first
commits the tombstone, then immediately attempts the outer Cancel-command
append. A successful append remains observable even if later Abort
callback/finalization fails; append failure leaves the tombstone committed and
records a diagnostic. Neither outcome can reactivate the operation. Wrong commit kind, incomplete codec,
duplicate field, late invalid field, default/forged token, or failure discards
the remaining staging and publishes no other effect.

For a shard declaration `TShard`, generation augments that partial struct with
`public const bool IsUsable = true`, `public const uint AbiVersion = 1u`, and
one reserved nested `public static class BurstAccess`. A handwritten member
named `IsUsable`, `AbiVersion`, or `BurstAccess` is `AIBT5011`. For every
concrete built-in or registered value type
used by a node in the shard, it emits only the applicable overloads below, with
`T`, `TStart`, and `TCancel` replaced by concrete types (the shown type symbols
are generation metavariables, not runtime generic dispatch):

```csharp
public static BurstContextResult TryRead(ref BurstEnterContext context, BlackboardReadHandle<T> handle, out T value);
public static BurstContextResult TryRead(ref BurstTickContext context, BlackboardReadHandle<T> handle, out T value);
public static BurstContextResult TryRead(ref BurstObserverContext context, BlackboardReadHandle<T> handle, out T value);
public static BurstContextResult TryRead(ref BurstEnterContext context, BlackboardReadWriteHandle<T> handle, out T value);
public static BurstContextResult TryRead(ref BurstTickContext context, BlackboardReadWriteHandle<T> handle, out T value);
public static BurstContextResult TryRead(ref BurstObserverContext context, BlackboardReadWriteHandle<T> handle, out T value);
public static BurstContextResult TryWrite(ref BurstEnterContext context, BlackboardWriteHandle<T> handle, in T value);
public static BurstContextResult TryWrite(ref BurstTickContext context, BlackboardWriteHandle<T> handle, in T value);
public static BurstContextResult TryWrite(ref BurstEnterContext context, BlackboardReadWriteHandle<T> handle, in T value);
public static BurstContextResult TryWrite(ref BurstTickContext context, BlackboardReadWriteHandle<T> handle, in T value);
public static BurstContextResult TryReadSnapshot(ref BurstEnterContext context, SnapshotReadHandle<T> handle, out T value);
public static BurstContextResult TryReadSnapshot(ref BurstTickContext context, SnapshotReadHandle<T> handle, out T value);
public static BurstContextResult TryReadSnapshot(ref BurstObserverContext context, SnapshotReadHandle<T> handle, out T value);
public static BurstContextResult TryConsume(ref BurstEnterContext context, CompletionHandle<T> handle, AIBT.OperationId operationId, out BurstCompletionOutcome outcome, out T value);
public static BurstContextResult TryConsume(ref BurstTickContext context, CompletionHandle<T> handle, AIBT.OperationId operationId, out BurstCompletionOutcome outcome, out T value);
public static BurstContextResult TryEmit(ref BurstEnterContext context, CommandHandle<T> handle, in T value);
public static BurstContextResult TryEmit(ref BurstTickContext context, CommandHandle<T> handle, in T value);
public static BurstContextResult TryStart(ref BurstEnterContext context, AsyncOperationHandle<TStart, TCancel> handle, in TStart startPayload, in TCancel faultCancelPayload, out AIBT.OperationId operationId);
public static BurstContextResult TryStart(ref BurstTickContext context, AsyncOperationHandle<TStart, TCancel> handle, in TStart startPayload, in TCancel faultCancelPayload, out AIBT.OperationId operationId);
public static BurstContextResult TryCancel(ref BurstAbortContext context, AsyncOperationHandle<TStart, TCancel> handle, AIBT.OperationId operationId, in TCancel cancelPayload);
```

Each overload is a static direct-call wrapper: begin, generated fieldwise
decode/encode in stable field-ID order, then complete/commit. There are no
interfaces, delegates, reflection, generic Runtime codecs, or raw `T` copies.
The analyzer permits a callback to access a binding only through its own
shard's generated overload for the exact configuration-field symbol. The
feasibility canary uses a registered value/payload whose CLR padding differs
from its canonical bytes, proving this rule beyond config and memory.

| View | Time | Random | Blackboard | Snapshot | Completion | Commands |
| --- | --- | --- | --- | --- | --- | --- |
| `BurstEnterContext` | read | declared stream operations | declared read/write | declared read | declared consume | declared Execute emit |
| `BurstTickContext` | read | declared stream operations | declared read/write | declared read | declared consume | declared Execute emit |
| `BurstAbortContext` | none | none | none | none | none | declared Cancel emit only |
| `BurstExitContext` | none | none | none | none | none | none |
| `BurstObserverContext` | read | none | declared read only | declared read only | none | none |

Observer `Evaluate` cannot use writes, commands, completion consumption, side
effects, or random consumption.
`TryCommitCancel` commits cancellation state before it attempts a cancel
append, as required by `async-and-commands-v1.md`. Exit is cleanup-only; a node
emits terminal commands before returning its terminal `Tick` result.

`TryStart` is the only async-start operation. Its single
`AsyncOperationHandle<TStart, TCancel>` binds one stable contract identity to
both command types. Runtime allocates the
`OperationId` from tree instance, runtime node index, activation generation, and
the monotonic operation sequence; a node cannot supply or forge it. Success
means the candidate ID, Start command, cancel command type, and exact
`faultCancelPayload` bytes are sealed in the frame transaction and the returned
ID must be stored in staged node memory. Matching lifecycle completion publishes
the operation ledger, command, and memory atomically. Runtime then retains the
captured cancel contract/payload so machine fault cleanup can tombstone the
operation and emit its compensating Cancel without running user code. Failure
returns an invalid ID and exposes no active operation. A later frame failure
does not reuse the reserved operation sequence. `TryConsume` accepts only an ID owned by the current node
activation and a declared completion binding with matching payload type/version.
`TryCancel` accepts only the stored active ID and the same async-operation
contract used to start it; mixing two individually valid async handles is
`AIBT5007`. It commits the tombstone before appending the cancel command.
Reticks do not call `TryStart` again unless the node contract explicitly defines
a retry. `TryEmit` is a separate fire-and-forget effect command and never
creates an operation.

Context operations return an explicit success/failure value. Capacity,
bounds, type, phase, stale-completion, overflow, and invalid-handle failures
become stable runtime diagnostics and fault the affected atomic step according
to host policy. The first non-success result is also latched on its frame, so a
callback cannot publish by ignoring a returned error; the matching
`TryComplete*` fails and discards staging. Exceptions cannot cross the boundary.

The analyzer lexically rejects direct Unity API use, managed allocation,
boxing, delegates, tasks, coroutines, reflection, pointer operations,
`throw`, exception construction, and `try`/`catch`/`finally`, and
undeclared handle use inside callbacks. The Burst compiler remains the
authoritative transitive-closure validator: a callback that calls a helper
which Burst cannot compile is invalid even when the forbidden operation is not
lexically present in the callback body. Analyzer success never weakens Burst
compilation requirements.

Lexical capability analysis resolves the callback parameters by method symbol
and fixed ordinal (`config=0`, `memory=1` except Evaluate, `context=2` except
Evaluate where it is `1`). Parameter spelling is not part of the ABI. It then
resolves every accessed configuration field symbol and its declared binding;
identifier text, aliases, source field order, and local variable names cannot
grant a capability. Forbidden Unity/type analysis covers object creation,
`typeof`, conversions, static member access, invocation targets, field/property
types, and generic type arguments; `new UnityEngine.GameObject()` is an
`AIBT5008` error even without a later member access.

The handle argument to a generated `TShard.BurstAccess` operation must be a
direct field access rooted in the resolved `config` parameter. Copying a handle
to a local, returning it from a helper, reconstructing it, or selecting it by
conditional/reflection/dynamic access is `AIBT5006`. This intentionally keeps
the feasibility analyzer lexical; transitive helper code remains Burst's
responsibility and cannot acquire a new binding capability.

## Deterministic random access

Random state consists of a private 64-bit PCG state and private 64-bit odd
stream increment. Nodes never read or write either value directly. Enter and
Tick contexts expose only `TryNextUInt32(out value)`, unbiased
`TryNextUInt32(boundExclusive, out value)`, and `TryNextFloat32(out value)` as
specified by `time-and-random-v1.md`. Each returns a `BurstContextResult`; a
failed call sets its output to the default value and consumes no random word.
`boundExclusive == 0` returns `InvalidStatus`; a valid context without the
node's declared random capability returns `PhaseViolation`; a default, forged,
or expired context returns `InvalidHandle`.

When more than one condition is invalid, random operations validate in this
exact order: token/frame liveness (`InvalidHandle`), declared capability and
expected odd increment (`PhaseViolation`), then a nonzero bound
(`InvalidStatus`). Every failure writes the default output and preserves the
context state.

`TryCreateEnterContext` and `TryCreateTickContext` copy the current node stream
state and increment into a mutable context owned by the live frame. For a
non-random node they instead write the inert pair `0`/`1`; `TryNext*` resolves
the missing capability from the token, returns `PhaseViolation`, and leaves the
pair unchanged. Successful
`TryNext*` calls advance only that context copy. Only a successful matching
`TryCompleteEnter` or `TryCompleteTick` atomically publishes the advanced state
to the Runtime-owned node stream. Completion of a non-random context validates
the exact inert pair but persists no RNG storage. Invalid status, failed or rejected
completion, `TryFailDispatch`, budget suspension before callback execution, and
all failed `TryNext*` operations publish no RNG change. Because a C# value copy
is bit-identical, Runtime accepts at most the first matching completion claim;
every later completion through the original or a copied value is stale and
publishes nothing. A forged/default, cross-frame, cross-node, wrong-phase, or
expired context cannot claim or mutate Runtime RNG state. ABI verification must
assert the 24-byte size, 8-byte packing, and offsets `0`, `8`, and `16` through
both `Marshal` and `UnsafeUtility`, plus default, copied, forged, and stale
context behavior.

Each random-consuming runtime node index owns a stream derived from the root
seed, all 32 raw tree semantic-hash bytes, tree instance ID, and runtime node
index. Abort, Exit, observer evaluation, budget suspension, diagnostics, trace,
and rejected operations consume no random value.

## Generated catalogs and execution ownership

Generation is per Unity assembly definition; generators do not assume a global
compilation.

Each node assembly explicitly declares one stable catalog-shard ID. The
generator emits a public immutable metadata shard containing only canonical
node/binding/layout records and hashes. It emits no registration side effect.

A host assembly explicitly declares one catalog set and lists its selected
public shards. It must reference every selected node assembly. The generator
then:

1. requires each selected shard to have exactly one constant
   `IsUsable=true` and exactly one constant `AbiVersion=1u`;
2. sorts shards and nodes by canonical UTF-8 identity and version;
3. rejects duplicate and numeric-collision identities across all shards;
4. verifies one generated-manifest authority per node;
5. computes the canonical catalog fingerprint;
6. emits the consumer-owned execution facade and Burst job types;
7. emits a closed switch whose cases decode typed configuration, load typed
   memory where legal, call the exact static lifecycle/observer method, stage
   memory back, and publish the exact callback result through the matching
   bridge completion seam. Abort/Exit cases fetch their Runtime-owned reason;
   Observer cases never create a memory accessor.

A selected true shard marker with any ABI value other than `1u` emits
compile-time `AIBT5012` at `Location.None` and only an unusable CatalogSet.
Missing/duplicate/false marker metadata is `AIBT5011`. These checks happen
before any fingerprint or facade member is emitted.

The facade proves heterogeneous selection; it is not a one-node wrapper.
`AIBT.Runtime` supplies native program/state/context primitives but never
references consumer assemblies. The host schedules through its generated
catalog-set facade. Runtime discovery, assembly scanning, reflection,
delegates, virtual/interface dispatch, boxing, and mutable function-pointer
registries are forbidden.

Binding is two-stage. Initialization resolves each compiled `(numeric node type
ID, canonical node type ID, node type version)` to one catalog case index after
collision and fingerprint checks. It separately resolves stable binding IDs to
compiled blackboard, snapshot, command, cancellation, and completion ordinals.
The hot path reads the prebound case index and enters the generated switch; it
performs no string lookup, hash lookup, assembly scan, or manifest selection.

For a public static partial catalog-set class `T`, generation adds these exact
public members. The referenced unmanaged Runtime envelopes are implementation
obligations of the native-runtime workstream, not implementations in P2-001.

```csharp
public const bool IsUsable = true;
public static AIBT.Burst.BurstCatalogFingerprint Fingerprint { get; }
public static AIBT.Burst.BurstCatalogValidationResult Validate(
    in AIBT.Burst.BurstCatalogHandshake handshake);
public static AIBT.Burst.BurstExecutionResult ExecuteImmediate(
    ref AIBT.Burst.BurstExecutionBatch batch);
public static Unity.Jobs.JobHandle Schedule(
    ref AIBT.Burst.BurstExecutionBatch batch,
    Unity.Jobs.JobHandle dependency);
```

Those exact result/envelope declarations are:

```csharp
namespace AIBT.Burst
{
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 4, Size = 32)]
    public readonly struct BurstHash256
    {
        public BurstHash256(
            uint word0, uint word1, uint word2, uint word3,
            uint word4, uint word5, uint word6, uint word7)
        {
            Word0 = word0; Word1 = word1; Word2 = word2; Word3 = word3;
            Word4 = word4; Word5 = word5; Word6 = word6; Word7 = word7;
        }
        public uint Word0 { get; } public uint Word1 { get; }
        public uint Word2 { get; } public uint Word3 { get; }
        public uint Word4 { get; } public uint Word5 { get; }
        public uint Word6 { get; } public uint Word7 { get; }
    }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 4, Size = 32)]
    public readonly struct BurstCatalogFingerprint
    {
        public BurstCatalogFingerprint(BurstHash256 value) { Value = value; }
        public BurstHash256 Value { get; }
    }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 4, Size = 172)]
    public readonly struct BurstCatalogHandshake
    {
        public BurstCatalogHandshake(
            uint abiVersion, BurstCatalogFingerprint catalog,
            BurstHash256 nodeRegistry, uint compiledFormatVersion,
            uint executionSemanticsVersion, BurstHash256 configurationLayout,
            BurstHash256 memoryLayout, BurstHash256 accessLayout)
        {
            AbiVersion = abiVersion; Catalog = catalog;
            NodeRegistry = nodeRegistry; CompiledFormatVersion = compiledFormatVersion;
            ExecutionSemanticsVersion = executionSemanticsVersion;
            ConfigurationLayout = configurationLayout;
            MemoryLayout = memoryLayout; AccessLayout = accessLayout;
        }
        public uint AbiVersion { get; }
        public BurstCatalogFingerprint Catalog { get; }
        public BurstHash256 NodeRegistry { get; }
        public uint CompiledFormatVersion { get; }
        public uint ExecutionSemanticsVersion { get; }
        public BurstHash256 ConfigurationLayout { get; }
        public BurstHash256 MemoryLayout { get; }
        public BurstHash256 AccessLayout { get; }
    }
    public enum BurstCatalogValidationCode : byte
    {
        Success = 0, AbiVersionMismatch = 1, CatalogMismatch = 2,
        RegistryMismatch = 3, CompiledFormatMismatch = 4,
        SemanticsMismatch = 5, ConfigurationLayoutMismatch = 6,
        MemoryLayoutMismatch = 7, AccessLayoutMismatch = 8
    }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 2, Size = 4)]
    public readonly struct BurstCatalogValidationResult
    {
        private readonly ushort _codeWord;
        private readonly ushort _diagnosticNumber;
        public BurstCatalogValidationResult(
            BurstCatalogValidationCode code, ushort diagnosticNumber)
        {
            _codeWord = (ushort)(byte)code;
            _diagnosticNumber = diagnosticNumber;
        }
        public BurstCatalogValidationCode Code =>
            (BurstCatalogValidationCode)(byte)_codeWord;
        public ushort DiagnosticNumber => _diagnosticNumber;
        public bool Success => Code == BurstCatalogValidationCode.Success;
    }
    public enum BurstExecutionCode : byte
    {
        Success = 0, ValidationFailed = 1, Faulted = 2
    }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 4, Size = 16)]
    public readonly struct BurstExecutionResult
    {
        private readonly ushort _codeWord;
        private readonly ushort _diagnosticNumber;
        private readonly uint _instancesVisited;
        private readonly ulong _segmentSteps;
        public BurstExecutionResult(
            BurstExecutionCode code, ushort diagnosticNumber,
            uint instancesVisited, ulong segmentSteps)
        {
            _codeWord = (ushort)(byte)code;
            _diagnosticNumber = diagnosticNumber;
            _instancesVisited = instancesVisited;
            _segmentSteps = segmentSteps;
        }
        public BurstExecutionCode Code => (BurstExecutionCode)(byte)_codeWord;
        public ushort DiagnosticNumber => _diagnosticNumber;
        public uint InstancesVisited => _instancesVisited;
        public ulong SegmentSteps => _segmentSteps;
        public bool Success => Code == BurstExecutionCode.Success;
    }
}
```

Both result structs store the logical U8 code in the low byte of a private U16
word at offset `0`; the high byte is reserved and is always zero in a value
created by the public constructor. `BurstCatalogValidationResult` stores the
diagnostic number at offset `2`, so its canonical binary grammar is U8 code, U8
zero padding, U16 diagnostic. `BurstExecutionResult` stores the diagnostic
number, instance count, and segment-step count at offsets `2`, `4`, and `8`; its
canonical binary grammar is U8 code, U8 zero padding, U16 diagnostic, U32
instances, U64 segment steps. The constructor deliberately does
not throw or normalize: it stores `(ushort)(byte)code`. A defined enum member is
a caller precondition for a usable result, generated facades pass only defined
members, and `Success` is false for every undefined code. Every bridge boundary
that accepts or publishes either result validates the closed code range and the
reserved high byte; an invalid value returns `InvalidStatus` and publishes no
mutation or effect.

`BurstHash256` is exactly 32 bytes. `DiagnosticNumber` is the four-digit numeric
suffix of an `AIBT` diagnostic and zero when absent; formatting into the managed
`DiagnosticCode` value occurs outside jobs. `BurstExecutionBatch` has no public
constructor; Runtime creates its validated non-owning native view. Its private
implementation may contain a Runtime-owned native shared-record handle needed
by the job, but that handle is never exposed or consumer-owned. `Schedule` is rejected
before facade entry when the backend lacks worker-job capability.
`SegmentSteps` is the unsigned 64-bit semantic-step delta produced by exactly
one facade execution call; it is not the instance's cumulative update count.

Generated source, shard records, switch order, and fingerprints are independent
of assembly enumeration, file path, source declaration order, culture,
machine path, and timestamp. Generated products are reviewable and deterministic
for identical source, generator, and pinned compiler inputs.

## Fingerprint handshake

Before creating or scheduling an instance, the generated facade validates:

- ABI version;
- catalog fingerprint;
- node-registry hash;
- compiled-program format and semantic versions;
- configuration layout fingerprint;
- memory layout fingerprint;
- typed access-table fingerprint.

ABI v1 expects unsigned ABI version `1`, compiled-program format version `1`,
and execution-semantics version `1`. `NodeRegistry` is exactly the 32 raw bytes
of the `compiled-program-v1.md` canonical node-registry SHA-256 for the complete
registry represented by the selected shards (including the Runtime-owned
built-in shard); its `BurstHash256` word mapping is the one defined below. It is
not a second generator-specific registry hash.

`Validate` compares in exactly the order listed above, which is the numeric
`BurstCatalogValidationCode` order `1` through `8`, and returns the first
mismatch. Success returns `Success` and diagnostic number zero; every mismatch
returns its exact code and diagnostic number `5012`.

`ExecuteImmediate` and `Schedule` cannot rely on a caller having invoked
`Validate`. Each first calls
`BurstGeneratedRuntimeBridge.TryGetCatalogHandshake`, then its own `Validate`,
before acquiring a dispatch frame or creating a job. For a valid batch with a
mismatch, the facade calls `TryRejectBatch`; `ExecuteImmediate` returns
`ValidationFailed`/`5012`, while `Schedule` returns the input dependency
unchanged. For a default or forged batch, handshake acquisition fails;
`ExecuteImmediate` still returns `ValidationFailed`/`5012` and `Schedule`
returns the input dependency unchanged, without assuming invalid storage can be
mutated. An optional defensive `TryRejectBatch` failure cannot replace or hide
that facade result. The success case, each of the eight single-field mismatch
cases, and default/forged batches all run zero callbacks and effects until this
gate succeeds.

After a successful Schedule gate, the facade calls `TryPrepareSchedule` exactly
once before constructing the job. Only `Success` may enqueue it. Any prepare
failure returns the input dependency unchanged with zero callback/effect; the
facade never captures an unclaimed ordinary batch copy as job-local state.

After a successful gate, `ExecuteImmediate` and the generated job repeatedly
call `TryGetExecutionRequest`. `hasWork=true` is followed by the exact
`TryAcquireDispatchFrame` and closed case/phase switch. A successful matching
`TryComplete*` advances the Runtime cursor; the loop then requests the next
item. `hasWork=false` ends an exhausted batch. An acquisition failure or
post-frame `TryFailDispatch` ends the loop with a Runtime-owned fault. In every
terminal valid-batch case the facade obtains the exact return/captured job
result through `TryGetExecutionResult`; it never derives counters or maps a
bridge error itself. The sole exception is a default/forged batch, for which no
Runtime result exists: `ExecuteImmediate` returns the fixed
`ValidationFailed`/`5012` result with both counters zero, and `Schedule` returns
the input dependency unchanged.

Any mismatch produces a structured error before a callback or job runs. It
never falls back to a managed handler or a different catalog. The canonical
catalog fingerprint is SHA-256 over a domain tag, ABI version, then every shard,
node, callback capability, config/memory field, and binding descriptor in the
sorted order defined above, using unsigned little-endian fixed-width numbers
and unsigned 32-bit length-prefixed UTF-8 strings.

The exact fingerprint domain tags include their final zero byte:

```text
AIBT-CATALOG-V1\0
AIBT-CONFIG-LAYOUT-V1\0
AIBT-MEMORY-LAYOUT-V1\0
AIBT-ACCESS-LAYOUT-V1\0
AIBT-VALUE-SCHEMA-V1\0
AIBT-CATALOG-CONFIG-LAYOUT-V1\0
AIBT-CATALOG-MEMORY-LAYOUT-V1\0
AIBT-CATALOG-ACCESS-LAYOUT-V1\0
```

The closed scalar grammar is `U8` = one byte, `U32` = four unsigned
little-endian bytes, `U64` = eight unsigned little-endian bytes, `H32` = 32 raw
hash bytes, and `S` = `U32 byteLength` followed by strict UTF-8 bytes. Booleans
are `U8` (`0` or `1`); every enum and mask in these streams is `U8`. ABI,
catalog, shard, node, value, payload, and compiled-format versions; counts;
ordinals; byte offsets; byte sizes; and padding offsets/sizes are `U32`.
Numeric canonical IDs, including every FNV-1a 64 result, are `U64`.
Unpaired UTF-16 surrogate input is invalid rather than replacement-encoded.
For `BurstHash256`, `Word0` is raw hash bytes 0-3 interpreted little-endian,
through `Word7` for bytes 28-31; converting to displayed hexadecimal writes the
32 raw bytes in order and never formats the words independently.

Configuration and memory layout streams contain: raw domain tag, `U32` ABI
version, `S` canonical node type ID, `U32` node type version, `U32` total size,
`U8` required alignment, `U32` field count, then fields sorted by canonical
field ID. Each field contains `S` canonical field ID, `U64` numeric field ID,
`S` canonical value type ID, `U32` value type version, `H32`
registered-value schema hash (all-zero for built-ins), `U32` offset, `U32`
size, `U8` alignment, and `U8` encoding. Configuration also
contains padding-range count followed by each zero-padding offset and size;
memory contains the same padding-range grammar and field-encoding table even
though it is runtime-only.

The access stream contains: raw access domain tag, `U32` ABI version, `S`
canonical node type ID, `U32` node type version, `U32` binding count, then
bindings sorted by canonical binding ID. Each binding contains `S` canonical
and `U64` numeric binding ID, `U8` binding kind, `U8` scope, `U8`
phase-capability mask, `U32` generated ordinal, `U32` type-record count, then
type records sorted by role byte. A type record contains `U8` role, `S`
canonical value/payload type ID, `U64` numeric type ID, `U32` positive type
version, and `H32` registered schema hash (all-zero for built-ins).

A registered value schema stream contains: raw value-schema domain tag, `U32`
ABI version, `S` canonical value type ID, `U64` numeric value type ID, `U32`
positive value version, `S` canonical schema ID, its `U64` FNV-1a numeric ID,
`U32` total canonical byte size, `U8` required alignment, `U32` field count,
then fields sorted by canonical field ID. Each field contains `S` canonical and
`U64` numeric field ID, `S` canonical and `U64` numeric value type ID, `U32`
positive type version, `H32` nested registered schema hash (all-zero for
built-ins), `U32` canonical offset, `U32` size, `U8` alignment, and `U8`
field encoding. The SHA-256 result is
the Burst schema/equality fingerprint. It does not replace or resize the
existing numeric descriptor fields.

The three catalog-level layout values used by `BurstCatalogHandshake` are not
an arithmetic combination. Each is SHA-256 over its corresponding raw
`AIBT-CATALOG-*-LAYOUT-V1\0` tag, `U32` ABI version, `S` catalog ID, `U32`
catalog version, `U32` node count, then nodes sorted by canonical type ID and
version. Each node record is `S` canonical node type ID, `U32` node type
version, followed by that node's `H32` configuration, memory, or access hash
respectively. This keeps each handshake
value independently reproducible while retaining the per-node hashes in the
catalog stream.

The catalog stream contains: raw catalog domain tag, `U32` ABI version, `S`
catalog ID, `U32` catalog version, and `U32` shard count. Each shard, sorted by
shard ID then version, contains `S` shard ID, `U32` shard version, `U32` shard
node count, then that shard's nodes sorted by canonical type ID and version.
Each node contains `S` canonical and `U64` numeric type ID, `U32` node version,
`U8` kind, `U8` deterministic flag, `U8` cancellation, `U8` cost, `U8` status
mask, `U8` memory lifetime, `U8` callback-capability mask, `U8` node-capability
mask, and the three `H32`
configuration/memory/access fingerprints. Shard membership is therefore hashed;
moving a node between shards changes the catalog fingerprint. Assembly names,
file paths, metadata tokens, timestamps, and compiler enumeration order are
excluded.

Closed byte tables are:

| Field | Values |
| --- | --- |
| callback capability mask | `Enter=0x01`, `Tick=0x02`, `Abort=0x04`, `Exit=0x08`, `ObserverEvaluate=0x10`; no other bits |
| node capability mask | `RandomStream=0x01`; bits `0x02`-`0x80` must be zero |
| binding kind | `BlackboardRead=0`, `BlackboardWrite=1`, `BlackboardReadWrite=2`, `SnapshotRead=3`, `EffectCommand=4`, `AsyncOperation=5`, `Completion=6` |
| type role | `Value=0`, `EffectPayload=1`, `AsyncStartPayload=2`, `AsyncCancelPayload=3`, `CompletionPayload=4` |
| field encoding | `Bool8=0`, `Int8=1`, `UInt8=2`, `Int16LE=3`, `UInt16LE=4`, `Int32LE=5`, `UInt32LE=6`, `Int64LE=7`, `UInt64LE=8`, `Float32BitsLE=9`, `Float64BitsLE=10`, `FixedBytes=11`, `GeneratedHandle=12`, `Registered=13` |
| scope | existing `AIBT.BlackboardScope` byte (`NodeLocal=0`, `Tree=1`, `Agent=2`, `Shared=3`), or `0xff` when not applicable |
| phase-capability mask | `None=0x00`, `Execute=0x01`, `Cancel=0x02`, `Completion=0x04`; no other bits |
| memory lifetime | existing `AIBT.NodeMemoryLifetime` byte (`Activation=0`, `Instance=1`) |

The same field-encoding table is present in both configuration and memory
layout streams. `Bool8` accepts only zero and one. Floating encodings store canonical IEEE bits
after the blackboard canonicalization rules. `FixedBytes` covers only the
closed composite built-ins above; its recorded size is the matching P1
descriptor size and its type ID selects the exact fieldwise grammar. A compiled `GeneratedHandle` field stores only its unsigned
32-bit access ordinal in little-endian form; `0xffffffff` is invalid. The
reader combines that ordinal with the validated frame access token when it
constructs the opaque handle. A `Registered` field is reconstructed by its
generated codec shard from its registered fields. User CLR enums and StringEnum
fields are invalid; enum-like data uses a registered wrapper with one fixed
primitive field. Unknown table values are errors, never forward-compatible
guesses. Adding or removing `AibtRandomStreamAttribute` changes the catalog
fingerprint through the node-capability byte.

Blackboard bindings use their declared scope, phase mask `None`, and one
`Value` type record. Snapshot bindings use scope `0xff`, phase mask `None`, and
one `Value` record. Effect commands use scope `0xff`, phase mask `Execute`, and
one `EffectPayload` record. Async operations use scope `0xff`, phase mask
`Execute|Cancel`, and exactly the `AsyncStartPayload` and `AsyncCancelPayload`
records. Completion bindings use scope `0xff`, phase mask `Completion`, and one
`CompletionPayload` record. Any other sentinel, mask, role, count, or
combination is invalid.

## Compatibility

- Adding a node type or a new version changes the catalog fingerprint but not
  the ABI version.
- Changing node configuration, memory, bindings, callbacks, kind, capabilities,
  or semantics requires a new node type version and migration guidance.
- Reordering source or renaming a C# field without changing its stable field ID
  is compatible and must not change generated bytes.
- Removing a node version is incompatible with programs that reference it.
- Changing an ABI callback, context capability, handle representation, packing
  rule, fingerprint stream, enum meaning, or random derivation requires a new
  ABI version and an accepted decision.
- An invalid Burst declaration is a build error and has no managed fallback.

## Analyzer diagnostics

The production analyzer reserves `AIBT5001` through `AIBT5099`. Every ABI v1
descriptor below has Roslyn severity `Error`, is enabled by default, and has
`WellKnownDiagnosticTags.NotConfigurable`; source, pragma, ruleset, and
`.editorconfig` suppression are forbidden. The generator treats even a
suppressed diagnostic object as invalid input and emits no usable binding,
shard case, or catalog facade for it. Where the table says "argument", the
primary location is the narrowest offending attribute argument expression,
not the complete attribute or declaration.

| ID | Contract | Primary diagnostic location |
| --- | --- | --- |
| `AIBT5001` | declaration is not a public top-level non-generic fieldless partial struct | offending type or field identifier; the node type identifier for a missing `partial` modifier |
| `AIBT5002` | config, memory, or registered value contains a non-allowlisted or unstable field/layout | offending type/field identifier, or the explicit layout attribute when layout itself is invalid |
| `AIBT5003` | callback is missing, overloaded, generic, or has a wrong signature | offending callback identifier; the node type identifier when the required callback is absent |
| `AIBT5004` | unsupported public node kind/capability, lifetime, cancellation, cost, or status mask | exact offending node-attribute argument or observer-capability attribute |
| `AIBT5005` | duplicate canonical node or registered-value identity/version | canonical type-ID argument of the declaration ordered later by assembly simple name, fully qualified metadata type name, then source span; earlier declarations are additional locations when source is available |
| `AIBT5006` | undeclared or forged context access | outermost invocation/member/type expression performing the access |
| `AIBT5007` | binding kind, scope, payload type/version, or phase mismatch | offending binding attribute argument when declarative; otherwise the outermost operation invocation |
| `AIBT5008` | forbidden API, allocation, pointer, reflection, task, coroutine, or Unity object use | outermost forbidden object-creation, invocation, member-access, type, `typeof`, pointer, or function-pointer syntax |
| `AIBT5009` | invalid canonical identity, version, or documentation contract | offending attribute argument; the attributed type identifier when a required attribute is absent |
| `AIBT5010` | duplicate canonical schema/binding identity or numeric node, value/schema, field, or binding identity collision | argument of the lexicographically greater canonical ID under unsigned UTF-8 order (the later stable declaration for an exact duplicate); the other source declaration is an additional location when available |
| `AIBT5011` | shard/catalog-set reference, authority, null/invalid shard array, generated-name, or global collision error | offending shard/catalog declaration or shard-type argument; for an external shard conflict, the referencing shard-type argument |
| `AIBT5012` | ABI, registry, catalog, layout, or access fingerprint mismatch in generated validation | `Location.None` for a compile-time generated-validation diagnostic; runtime validation returns diagnostic number `5012` with no Roslyn diagnostic |

Assembly/type comparison is ordinal over stable UTF-8 metadata names and never
uses a physical file or machine path. If only one colliding symbol has source
in the current compilation, that local symbol is primary regardless of global
ordering and the external canonical identity is included in the message. This
makes diagnostic ownership deterministic across per-asmdef generation. Each
message includes the violated operation/identity and the expected and actual
fixed metadata needed to reproduce the failure; it never uses a CLR field name,
physical path, or assembly enumeration order as identity.

Compiler and Burst diagnostics remain visible alongside AIBT diagnostics. A
node asmdef is atomic: any `AIBT5001`-`AIBT5011` in it emits only its declared
partial shard with `public const bool IsUsable = false`; it emits no
`BurstAccess`, codecs, metadata records, bindings, or cases. A selected shard
that is missing, invalid, duplicated, globally colliding, or compile-time ABI
mismatched makes its CatalogSet atomic-invalid: generation emits only that
partial catalog class with `public const bool IsUsable = false`, and no
`Fingerprint`, `Validate`, `ExecuteImmediate`, `Schedule`, job, or case. The ABI
marker mismatch is the compile-time `AIBT5012` case. A runtime `AIBT5012`
handshake rejection of an otherwise valid generated facade is separate and
does not change generated usability.

If an invalid node asmdef declares multiple shard types, each receives only the
false marker. If it declares no shard type, no public shard is synthesized; an
internal generated `__AibtUnusableShardMarker` with constant false may be
emitted solely for deterministic test/tool observability and is not ABI.
