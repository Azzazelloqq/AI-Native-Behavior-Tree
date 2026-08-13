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
- root node index;
- node, child-index, blackboard-slot, and debug-map counts;
- config blob size;
- per-instance node-memory size and required maximum alignment;
- capability flags and deterministic-mode compatibility.

## Node record

Each node record contains:

- stable numeric node type ID resolved by `identity-and-hashing-v1.md` with collision detection;
- node type version;
- config offset and size;
- instance-memory offset, size, and alignment;
- first child-list offset and child count;
- node flags including execution domain and tracing capability;
- source/debug identity index.

Children are stored in a separate contiguous index array in semantic order. Config and memory regions are non-overlapping and validated for bounds and alignment.

## Blackboard schema

Each slot records stable key identity, type ID/version, scope, offset, size, alignment, default-value offset, access flags, and observer metadata. Runtime keys compile to slot indices.

## Debug and hot-reload map

The debug map associates runtime node index with stable authoring node ID, source path, and optional display metadata. Presentation layout is not included. Release builds may strip display strings while retaining stable IDs required by configured hot reload and diagnostics.

## Validation

Compilation fails on cycles, invalid roots, unreachable nodes according to policy, duplicate IDs, unknown types, registry collisions, child-policy violations, blackboard errors, unsupported capabilities, offset overflow, or incompatible alignment.

Compilation is deterministic: identical canonical input, registry, policy, and compiler version produce byte-identical compiled artifacts on the same supported toolchain.

Semantic and registry hashes and textual representations follow `identity-and-hashing-v1.md`.
