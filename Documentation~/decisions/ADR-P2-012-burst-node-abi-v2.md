# ADR P2-012: Burst node ABI v2 runtime carriers

- Status: Accepted by explicit owner direction on 2026-08-16
- Date: 2026-08-16
- Decision ID: AIBT-022

## Context

The ABI v1 feasibility contract fixed `BurstEnterContext` and
`BurstTickContext` to a 24-byte token-plus-PCG layout while requiring their
public methods to resolve blackboard, snapshot, command, completion, operation,
and time capabilities inside Burst jobs. The production runtime cannot perform
that resolution from the token alone without using either a raw pointer or a
global token registry. Both alternatives violate the accepted ownership and
hot-path rules, and a single global or `SharedStatic` current-context slot is
not safe when jobs execute independent instances concurrently.

P2-012 therefore needs job-safe native views carried directly through the
opaque batch, frame, reader/writer, and phase-context values. That changes their
private physical representation and is a breaking ABI change under the v1
compatibility rules.

## Decision

Adopt [Public Burst node ABI v2](../specifications/burst-node-abi-v2.md) as the
production target for P2-012:

- retain the v1 public type names, callback signatures, generated facade
  signatures, bridge signatures, capability rules, codecs, and observable
  transaction semantics;
- change the shard marker, catalog handshake expectation, and final catalog
  transport stream to unsigned ABI version `2`;
- retain the unchanged configuration, memory, access, value-schema, and three
  catalog-layout fingerprint grammars at their v1 domain tags and `U32 1`
  format version; those storage/schema format versions are independent of the
  transport ABI version;
- allow Runtime to add unmanaged, non-owning, job-safe native views and
  validation state to the private backing of opaque carriers and phase
  contexts;
- preserve `BurstEnterContext` and `BurstTickContext` as sequential, pack-8
  values with their private token/PCG prefix at offsets `0`, `8`, and `16`, but
  remove the exact total-size pin so Runtime may append backing after byte `24`;
- define no stable private physical layout for the remaining opaque carriers or
  contexts, and no stable total-size contract for Enter/Tick;
- keep ownership, allocation, resize, disposal, and native-container access
  entirely outside the public consumer surface;
- use explicit index-plus-generation validation carried with those views;
  validation tokens are never raw pointers;
- forbid a global token registry, `SharedStatic` current-context lookup,
  thread-local side channel, reflection, or managed fallback;
- include the one implicit Runtime built-in metadata authority in the complete
  canonical node-registry hash, while assigning built-ins no catalog case index
  and emitting no generated dispatch case for them.

AIBT-022 supersedes AIBT-020 only for the ABI version and private
carrier/context representation. The generated-shard, explicit catalog-set,
closed-dispatch, capability, codec, and ownership architecture accepted by
AIBT-020 remains in force.

The generated dispatch catalog contains only selected public ABI v2 shard
nodes. Built-in composites and decorators remain Runtime-owned semantic
instructions. Their manifests participate in the registry handshake so the
compiled program and selected public catalog prove one complete registry, but
they do not participate in the public-node switch.

Runtime owns one internal canonical built-in metadata artifact. A narrow
Authoring adapter verifies that artifact against `BuiltInNodeManifests`, and the
catalog generator consumes it as referenced Runtime metadata. This introduces
no public host API and no runtime discovery.

## Migration

ABI v1 remains a historical feasibility contract and is not executable by the
production v2 bridge. Existing node source normally requires no edit because
public names and signatures are unchanged, but every shard, catalog-set facade,
and Burst/AOT consumer assembly must be regenerated and rebuilt with the ABI v2
Runtime and generator. A v1 marker or handshake fails with `AIBT5012` before a
callback or effect. There is no v1 compatibility shim, dynamic dispatch path,
or managed fallback.

The node type version, persisted authoring JSON, compiled-program format
version, execution-semantics version, and canonical manifest-registry format do
not change solely because of this migration.

## Consequences

- Runtime views can reach bounded native storage directly and safely inside
  Immediate and scheduled Burst execution.
- Consumer code remains source-compatible but is not binary-compatible across
  the v1-to-v2 boundary or across unmatched Runtime/generator builds.
- Tests pin public members and observable behavior. They also pin Enter/Tick
  sequential pack `8` and private prefix offsets `0`/`8`/`16`, but not the
  Enter/Tick total size; other opaque carrier/context private layouts remain
  unpinned. Explicitly layout-pinned public values and typed handles that are
  not carrier backing retain their v1 representation.
- Runtime owners and leases remain solely responsible for storage lifetime;
  copying a carrier never transfers ownership or extends liveness.

## Alternatives rejected

- A raw pointer encoded in the validation token violates the accepted ABI and
  release-mode validation rules.
- A global native token registry or `SharedStatic` current-context table adds
  hidden mutable state, lookup cost, lifetime coupling, and cross-job race risk.
- Preserving ABI v1 and silently changing its private representation contradicts
  its accepted compatibility rule and would not fail stale binaries closed.
- Publishing new public container accessors would expose ownership and widen
  the consumer capability surface unnecessarily.
