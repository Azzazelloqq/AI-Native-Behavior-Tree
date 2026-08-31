# P7-008 — Per-project leaf-registration mechanism implementation

Status: `Draft`

## Objective

Apply `ADR-P6-017` (Accepted) to production: build the new public authoring surface it decided is
required — a public leaf-behavior contract with a public-safe node-context type, a public
handler-binding equivalent, and a `NodeRegistryBuilder`/`ValidateBinding` change accepting a binding
of this new public kind while still rejecting the internal one — so a consuming project can register
its own reference-executor leaf nodes for real, closing the gap `P3-009`/`P6-007`/`P6-008` each
independently worked around by hardcoding the same fixed Phase 1 fixture/built-in set.

This card also closes the concrete, live-reproduced consequence `P6-012`'s own gate session found:
a custom node an agent just generated, compiled, and applied via `aibt_apply_node` is not
discoverable through `aibt_search_nodes`/`aibt_get_node_contract`, because both discovery tools
query `NodeRegistryBuilder.CreateWithBuiltIns()`'s hardcoded built-in list, which nothing wires an
applied shard into. Wiring a project's registered nodes into that same registry (the natural
consequence of building the new public registration surface) is this card's own acceptance
criterion, not a separate future card.

## Depends on

- `P6-017` (the accepted decision this card implements).

## Required reading

- `Documentation~/decisions/ADR-P6-017-per-project-leaf-registration.md` (the three-layer surface
  this card must build, and why a smaller facade widening does not suffice).
- `Spikes~/PerProjectLeafRegistration/` (the disposable spike's own proof of the closed layers).
- `Authoring/Registry/NodeRegistryBuilder.cs` and its `ValidateBinding`/`AddUserExtension` methods
  (the exact validation rule this card must extend, not replace).
- `Runtime/Execution/Reference/Leaves/` and `IReferenceLeafHandler`'s own internal shape (what the
  new public contract must offer an equivalent of, without exposing `ReferenceNodeContext`'s raw
  span-based internals as-is).
- `Authoring/Discovery/NodeCatalogQuery.cs`/`ProjectManifestQuery.cs` (`P6-003`) and
  `MCP/Authoring`'s `aibt_search_nodes`/`aibt_get_node_contract` (the discovery tools this card must
  make see a project's own registered/applied nodes).

## Allowed changes

- A new public leaf-behavior contract and public-safe node-context type (location per the ADR's own
  reasoning about the `AIBT.Runtime`/`AIBT.Authoring` boundary — confirm before creating a third
  option).
- `Authoring/Registry/NodeRegistryBuilder.cs` (`ValidateBinding` extension, new registration method).
- Whatever registry instance `Authoring/Discovery/`'s query layer reads from, so an applied
  project node becomes visible to it (this is the discoverability-gap fix; do not build a second,
  parallel registry).
- `Tests/Editor/NodeRegistry/`, `Tests/Runtime/ReferenceExecutor/` (new).
- `Planning~/Evidence/P7-008/`.

## Forbidden changes

- Removing or weakening `ValidateBinding`'s existing rejection of the internal handler kind for
  `AddUserExtension` — the new acceptance path is additive, for the new public kind only.
- Any change to the Burst/native leaf-node authoring path (`AibtBurstNode` et al.) — this card is
  reference-executor-backend only, per the ADR's own scope; native per-project leaves remain a
  separate, undecided question if one exists.
- Silently changing `P6-007`/`P6-008`/`P3-009`'s own hardcoded Phase 1 fixture/built-in set as a
  side effect — those callers may adopt the new registration path in a later, dedicated follow-up,
  not silently inside this card.

## Deliverables

- The public leaf-behavior contract, public-safe context type, public binding-registration method,
  and the `ValidateBinding` extension, all real production code (not a spike).
- A project-authored custom leaf node, registered through the new public surface, executing
  correctly through the reference executor in a real behavior-case test.
- The same custom node discoverable through `aibt_search_nodes`/`aibt_get_node_contract` after
  registration, closing the concrete gap the `P6-012` gate reproduced live.

## Acceptance criteria

- A real, external-feeling test (a project-style leaf node defined outside `AIBT.Runtime`'s own
  internal namespaces, using only the new public contract) registers and executes correctly.
- `ValidateBinding` still rejects the internal handler kind exactly as before — proven by an
  unchanged negative test, not merely inspection.
- `aibt_search_nodes` returns the newly-registered node in the same live-Editor session it was
  registered in, without requiring a domain reload beyond what registration itself already needs.
- Regression: the full existing registry/discovery/reference-executor suite passes unchanged.

## Required verification

```text
Verify-Static.ps1
Run-UnityTests.ps1 -Mode EditMode -Scope Full
real project-style leaf node registered and executed via the new public surface
aibt_search_nodes/aibt_get_node_contract discoverability proof via the real MCP server
live interactive proof via Unity MCP against the real open Editor
```

## Handoff notes

- A future card may migrate `P3-009`/`P6-007`/`P6-008`'s own hardcoded fixture registries onto this
  new public surface, but that is explicitly out of this card's own scope unless trivial.
