# Blackboard contract v1

## Types

Built-in semantic types are:

- `Bool`, `Int32`, `Int64`, `Float32`, `Float64`;
- `Float2`, `Float3`, `Quaternion`;
- `Enum32` with a registered enum contract;
- `FixedString32`, `FixedString64`, `FixedString128`, `FixedString512`;
- `AgentId`, `EntityId`, `OperationId` as AIBT-owned opaque identifiers;
- `AssetId` as Unity asset GUID plus optional local file ID;
- registered unmanaged value types with stable type ID, version, size, alignment, equality, and migration metadata.

`EntityId` is not `Unity.Entities.Entity`. Managed strings, `UnityEngine.Object`, arbitrary object references, delegates, tasks, and containers with owned dynamic lifetime are forbidden in the Burst blackboard.

Implicit type conversion is forbidden. Conversion requires an explicit node.

## Scopes

- `NodeLocal`: private to one node instance; compiled into its memory layout and not addressable by other nodes.
- `Tree`: shared by nodes of one tree instance.
- `Agent`: shared by all tree instances explicitly bound to the same agent context.
- `Shared`: shared by multiple agents through a declared deterministic access policy.

Phase 1 implements `NodeLocal` and `Tree`. `Agent` and `Shared` remain validation-recognized but execution-unsupported until their dedicated work items are complete; attempting to compile them produces a capability diagnostic.

## Keys

Canonical keys have stable opaque IDs, unique human-readable names within their scope, explicit type, optional default value, and description. References compile to slot indices; runtime string lookup is not used in the Burst path.

A missing key, scope mismatch, or type mismatch is a compilation error. The runtime MUST NOT create keys implicitly.

## Reads and writes

Node manifests declare every possible read and write. The compiler rejects undeclared access in generated Burst bindings. A write becomes visible immediately to later nodes in the same instance execution pass for `Tree` scope.

Each slot has a change version. The version increments only when the registered equality operation reports a changed value. Observer reevaluation is queued from version changes.

## Shared writes

Shared scope is read-only during the execute phase unless a key declares one of:

- deterministic `Min`, `Max`, `Sum`, `Any`, or `All` reduction for a compatible built-in type;
- `FirstByInstanceId` or `LastByInstanceId`;
- a generated unmanaged reducer with stable ID and associativity/determinism declaration.

Unconfigured shared writes are compilation errors. Shared reductions occur in the update phase defined by `update-phases-v1.md`.

## Initialization and reset

Defaults are validated and compiled. Tree-instance creation initializes every slot deterministically. Reset restores defaults and increments the tree revision once after the operation; it does not produce individual observer callbacks until the next update phase.

## Serialization

Canonical authoring JSON stores semantic values. Compiled layout uses 32-bit offsets and deterministic alignment suitable for WASM32. Runtime state is not a persistent save format.
