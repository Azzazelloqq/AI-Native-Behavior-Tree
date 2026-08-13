# Identity and hashing v1

## Authoring identities

`TreeId`, authoring `NodeId`, blackboard key ID, group ID, and note ID are case-sensitive UTF-8 strings matching:

```text
^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$
```

They are stable opaque identities, not display names. Tools preserve existing IDs. New IDs MAY be human-readable when uniqueness is guaranteed; copy/paste and merge tools MUST remap collisions explicitly.

## Runtime identities

- `TreeInstanceId`, `AgentId`, and `EntityId` are unsigned 64-bit values; zero is invalid.
- `Revision` is an unsigned 64-bit monotonically increasing value within its owning document or runtime object; zero represents an uninitialized revision.
- Runtime node index is unsigned 32-bit; `0xFFFFFFFF` is the only invalid node index.
- `OperationId` contains `TreeInstanceId` (64-bit), runtime node index (32-bit), activation generation (32-bit), and per-instance operation sequence (64-bit). All fields participate in equality and serialization.

Overflow is a structured error. Values never wrap silently.

## Type IDs

Canonical node, value, command, reducer, and event type strings are converted to numeric IDs with 64-bit FNV-1a over their UTF-8 bytes:

```text
offset basis = 14695981039346656037
prime        = 1099511628211
```

Registries retain the canonical string and MUST reject collisions between distinct strings. Numeric IDs are an optimization, not sufficient proof of identity when importing untrusted compiled data.

## Content hashes

Semantic, node-registry, policy, and compiled-content hashes use SHA-256 over their defined canonical byte representation. Hash input MUST include the relevant format/contract versions and MUST exclude layout, timestamps, machine paths, local view state, and runtime state.

Hashes are lowercase hexadecimal when represented as text.
