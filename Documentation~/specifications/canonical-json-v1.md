# Canonical JSON v1

This specification defines canonical persisted JSON for Phase 1. Schemas define shape; this document defines bytes and typed semantic values.

## Encoding

- UTF-8 without BOM, LF line endings, two-space indentation, and one trailing LF.
- Property names and string values use JSON escaping with control characters escaped, `"` and `\` escaped, and all other valid Unicode emitted as UTF-8. Unicode normalization is not applied.
- Duplicate object properties, invalid Unicode scalar sequences, comments, trailing commas, and non-finite numbers are errors.
- Integers use base-10 digits with no leading zero except `0`. Negative zero is canonicalized to `0`.
- `Float32` and `Float64` use the shortest round-trippable, culture-invariant finite decimal. Exponent marker is lowercase `e`, exponent `+` is omitted, and exponent leading zeros are removed. Parsed negative zero is written as `0`. NaN and infinities are invalid.

## Ordering

Schema-defined properties are written in their order in the schema's `properties` object. Unknown fields are rejected before writing. Map-like semantic objects (`blackboard`, `nodes`, and parameter-object members) are ordered by property name using ordinal UTF-8 byte order. Metadata is a map with the same ordering rule.

Array order is semantic and preserved except `tags`, which are a mathematical set and are written in ascending ordinal UTF-8 order. Duplicate tags are invalid.

## Typed values

- Bool and numeric scalar types use the corresponding JSON primitive.
- `Float2`, `Float3`, and `Quaternion` are objects with ordered fields `x`, `y`, `z`, `w` as applicable.
- `Enum32` is `{ "contract": <type-id>, "value": <Int32> }`.
- fixed strings are JSON strings whose UTF-8 byte count fits their declared capacity.
- AIBT opaque IDs are canonical identity strings in authoring documents.
- `AssetId` is `{ "guid": <32 lowercase hexadecimal>, "localFileId": <optional signed Int64> }`.
- registered unmanaged values use their registered canonical JSON schema. A registry without such a schema cannot be used in persisted authoring data.

Parameters are validated against the node manifest's typed parameter contracts. Untyped arbitrary JSON parameters are not valid compiled input.

## Semantic tree representation

Tree-level blackboard declarations use scopes `tree`, `agent`, or `shared`. `NodeLocal` is node instance memory declared by a node manifest and is never a tree-level key.
An `Enum32` blackboard entry requires `enumContract` immediately after `type`; the property is forbidden for every other blackboard type. Its default value's `contract` must match the entry contract exactly.

`metadata` is optional non-behavioral authoring information. Its values are restricted to null, Boolean, finite number, string, arrays of allowed metadata values, and objects with unique keys. It is included in canonical file bytes but excluded from the semantic hash and compilation.

## Hash inputs

The semantic hash is SHA-256 over canonical JSON bytes after removing `name`, `description`, `displayName`, `tags`, and `metadata` fields recursively where applicable. It includes `format` and `formatVersion`. Tree and node identities, type versions, parameters, children, observers, root, and blackboard contracts remain included.

The policy hash is SHA-256 over the complete canonical policy JSON bytes. Registry canonicalization is defined by `compiled-program-v1.md`.

## Versions

Unsupported future format versions fail. Phase 1 has no best-effort reader and no migration. A future migration must accept old canonical bytes and produce deterministic new bytes and a previewable semantic diff.
