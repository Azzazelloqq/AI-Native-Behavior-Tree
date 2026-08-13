# Node contract v1

This specification fixes semantic requirements shared by reference, Burst, managed, editor, and AI representations. The concrete public Burst C# ABI is a Phase 2 contract and MUST NOT be invented during Phase 1.

## Node identity

Every node type has:

- canonical string type ID using reverse-domain or project-qualified naming;
- positive type version and numeric ID defined by `identity-and-hashing-v1.md`;
- generated or registered numeric ID with collision detection;
- one category and child policy;
- configuration schema and instance-memory descriptor;
- execution domain: `Burst`, `Managed`, or `MainThread`;
- declared blackboard reads/writes, side effects, determinism, and cost hint;
- lifecycle statuses and cancellation behavior;
- summary, intended use, discouraged use, and at least one behavior example.

Authoring node identity is separate from node type identity. Replacing a node type does not silently preserve incompatible runtime state.

## Responsibilities

A node has one behavioral responsibility. It MUST NOT:

- access undeclared blackboard slots;
- retain hidden global mutable state;
- access scene objects from Burst execution;
- allocate managed memory in the supported initialized Burst path;
- start work more than once per activation unless retry behavior is explicit;
- call another tree instance reentrantly;
- depend on worker completion order.

## Execution domains

### Burst

Configuration and memory are unmanaged. Dispatch is generated and direct; runtime reflection, boxing, delegates, and virtual/interface dispatch are forbidden in the hot path. Unity APIs are accessed only through snapshot reads and emitted commands.

### Managed

Managed nodes are an explicit compatibility fallback with visible validation/profiling cost. They run outside Burst jobs in a declared phase. Managed support does not weaken Burst-path guarantees.

### Main thread

Main-thread nodes are explicit adapters for Unity or third-party APIs. They obey the same lifecycle but execute through the integration boundary and cannot be hidden inside a Burst subtree.

## Phase 1 reference nodes

Phase 1 uses an internal explicit registry of test/reference handlers. It does not publish the user-node ABI and does not scan assemblies. Reference handlers MUST obey this semantic contract so Phase 2 behavior cases remain reusable.

## Phase 2 ABI gate

Before custom node implementation, a dedicated accepted contract MUST define exact attributes, generic constraints, callback signatures, context accessors, configuration serialization, analyzer diagnostics, generated dispatch, and compatibility rules. Until then, agents may not expose a public node authoring API.
