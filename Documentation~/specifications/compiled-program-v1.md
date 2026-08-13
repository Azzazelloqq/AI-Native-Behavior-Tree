# Compiled program contract v1

This document fixes the logical layout. Concrete C# structs are introduced by their assigned work item and MUST preserve these fields and invariants.

## Properties

The compiled program is immutable, pointer-free authoring output shared by many tree instances. Runtime binding may copy it into native storage, but may not change semantics.

All indices, counts, and blob offsets are unsigned 32-bit values for WASM32 compatibility. Invalid/sentinel indices use one named constant rather than multiple magic values. Byte order of persisted compiled artifacts is little-endian; v1 may rebuild instead of loading an incompatible artifact.

## Header

The header contains:

- magic and compiled-format version;
- execution-semantics version;
- compiler version;
- canonical semantic hash;
- node-registry hash;
- canonical policy hash and policy-format version;
- root node index;
- node, child-index, blackboard-slot, and debug-map counts;
- config blob size;
- per-instance node-memory size and required maximum alignment;
- capability flags and deterministic-mode compatibility.

The canonical policy is a typed compiler input, not an independently supplied hash. Its canonical bytes and hash are derived from one `ReferenceCompilationPolicy` value. Every compilation-affecting validation option is represented: execution domains, determinism, side effects, unreachable-node handling, Agent/Shared scope support, warning promotion, structural limits, naming/description rules, forbidden node types, and performance rules. Callers cannot override those values outside the recorded policy hash.

## Node record

Each node record contains:

- stable numeric node type ID resolved by `identity-and-hashing-v1.md` with collision detection;
- node type version;
- config offset, size, and required alignment;
- instance-memory offset, size, alignment, and explicit `Activation` or `Instance` lifetime;
- first child-list offset and child count;
- node flags including execution domain and tracing capability;
- source/debug identity index.

Each node also references contiguous read-slot and write-slot index ranges derived from its manifest. Reference handlers cannot perform runtime string lookup.

Children are stored in a separate contiguous index array in semantic order. Config and memory regions are non-overlapping and validated for bounds and alignment.

## Blackboard schema

Each slot records stable key identity, type ID/version, Enum32 contract ID, scope, offset, size, alignment, default-value offset, access flags, and observer metadata. The Enum32 contract ID is the FNV-1a 64-bit identity of the canonical authoring contract and is nonzero only for Enum32 slots; every other slot stores zero. An Enum32 slot without an explicit authoring default still stores that contract ID and integer value zero in its compiled default bytes. Runtime keys compile to slot indices.

Observer records contain observer node index, owning reactive-composite index, mode, and watched-slot range. Watched slot indices are stored in a contiguous table in ordinal key-ID order. `None` is represented by absence of a record. The last condition result is mutable per-instance execution state and is never stored in the shared immutable compiled program.

## Debug and hot-reload map

The debug map associates runtime node index with stable authoring node ID, source path, and optional display metadata. Presentation layout is not included. Release builds may strip display strings while retaining stable IDs required by configured hot reload and diagnostics.

## Validation

Compilation fails on cycles, invalid roots, unreachable nodes according to policy, duplicate IDs, unknown types, registry collisions, child-policy violations, blackboard errors, unsupported capabilities, offset overflow, or incompatible alignment.

Compilation is deterministic: identical canonical input, registry, policy, and compiler version produce structurally identical logical records and byte-identical blobs on the same supported toolchain. Phase 1 does not define a persisted binary container, so record object serialization is not part of this guarantee.

Semantic and policy hashes follow `canonical-json-v1.md`. The registry hash is SHA-256 over canonical JSON containing manifests sorted by canonical type ID and version; each manifest uses `node-manifest.schema.json` property order, ordinal-sorted set fields, and canonical typed configuration schemas. The compiled-content hash is SHA-256 over a canonical little-endian field stream: header fields excluding the compiled-content hash itself, followed by node records, child indices, access tables, blackboard records, config/default blobs, and debug identities in stored order. Length prefixes are unsigned 32-bit. Compiler version is three unsigned 16-bit components plus an unsigned 32-bit build revision.

Phase 1 built-in canonical type IDs are `aibt.core.memory-sequence`, `aibt.core.reactive-sequence`, `aibt.core.memory-selector`, `aibt.core.reactive-selector`, `aibt.core.parallel`, `aibt.core.inverter`, `aibt.core.succeeder`, `aibt.core.failer`, `aibt.core.repeater`, `aibt.core.timeout`, `aibt.core.cooldown`, and internal fixture leaves under `aibt.test.*`. Their exact parameter schemas and memory descriptors are registry fixtures owned by P1-004 and are golden compiler inputs after acceptance.
