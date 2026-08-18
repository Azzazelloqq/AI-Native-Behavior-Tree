# ADR P2-001: generated public Burst node ABI

Status: Accepted; independently accepted by the P2-001 round-5 review on
2026-08-14.

## Context

Phase 1 intentionally left the concrete public Burst C# ABI undefined. Phase 2
needs custom nodes without reflection, virtual dispatch, managed fallback,
runtime assembly discovery, or a Runtime dependency on consumer assemblies.
Unity compiles each assembly definition independently, so a generator cannot
construct one implicit project-global registry.

## Decision

- Public ABI v1 supports fieldless static callback containers for custom
  Condition and Action nodes only.
- Authoring configuration v1 uses only Bool/UInt32/UInt64 scalars plus generated
  typed handles so it projects exactly into the existing P1 manifest schema.
  Memory, registered values, and payloads retain the broader strict unmanaged
  portability allowlist and generated versioned layouts, but ABI v1 rejects all
  user CLR enum/StringEnum fields; enum-like values use a registered one-field
  fixed-primitive wrapper.
- Generated typed handles enforce declared blackboard, snapshot, completion,
  command, and cancellation capabilities.
- Lifecycle phases receive distinct capability views. Observer evaluation is a
  separate read-only condition callback.
- Every node assembly emits a public deterministic metadata shard. A host
  explicitly selects shards in a catalog-set assembly; that assembly receives
  the generated closed execution facade and job types.
- Consumer facades cross the assembly boundary through a public reserved
  Runtime bridge with opaque validated batch handshake/request/result, frames,
  phase contexts, readers, and staging writers. Runtime owns the batch cursor
  and metrics; generated facades loop requests but never manufacture results.
  Scheduled execution atomically claims a Ready Runtime-owned native shared
  record and gives the job only a non-owning view, so the caller's original
  opaque batch observes the terminal result after dependency completion.
  The bridge exposes no pointer or native-container ownership.
- Config, memory, registered values, and command payloads use generated
  fieldwise canonical codecs. Runtime never reflects or copies arbitrary
  consumer structs; writes and commands publish only through atomic commits.
- Async start/cancel uses one paired operation handle and captures the
  fault-cancel payload at start. ABI v1 permits Shared blackboard reads but
  rejects Shared writes until a reducer ABI is accepted independently.
- Runtime owns data and context primitives but never references consumer node
  assemblies. There is no reflection, mutable function-pointer registry,
  delegate, interface, or virtual dispatch in the execution path.
- ABI, catalog, registry, layout, and access fingerprints must match before
  scheduling. Registry hashing uses the unchanged P1 canonical manifest JSON;
  no Burst-specific alternate registry hash exists. Typed bindings remain in
  AccessLayout and are not silently reinterpreted as P1 manifest
  reads/writes/sideEffects.
- Public validation and execution results store their closed logical U8 code in
  the low byte of a private U16 word whose high byte is reserved zero. Public
  constructors do not throw or normalize; undefined codes are unusable and
  bridge boundaries return `InvalidStatus` without mutation. This pins one
  canonical physical representation that the selected Burst compiler accepts.
- Node random streams use the domain-separated derivation and PCG sequence in
  `time-and-random-v1.md`. Mutable Enter/Tick contexts carry an opaque token and
  private transactional state copy; only successful matching completion
  publishes it. The catalog fingerprint records RandomStream as a closed node
  capability bit.

## Alternatives rejected

Instance interfaces were rejected because they add a dispatch hazard and the
pinned Unity-compatible C# surface cannot express the desired static abstract
contract. Runtime reflection and mutable function-pointer registration were
rejected for determinism, AOT/WASM, ownership, and reviewability. A single
project-global generator was rejected because Unity assembly definitions are
independent compilations. Public custom composites/decorators were deferred
because no accepted child-transition ABI exists.

## Consequences

Projects explicitly own their catalog-set facade and can have more than one
catalog set. Adding or removing a shard changes its catalog fingerprint.
Downstream production tasks must implement versioned access bindings rather
than reinterpret Phase 1 literal manifest reads/writes. Breaking ABI surface
changes require a new ABI version and decision.
