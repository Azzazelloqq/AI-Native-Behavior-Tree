# P5-002 node identity and state-layout hashing evidence

## Result

- `Runtime/HotReload/Identity/HotReloadNodeIdentitySignature.cs` (new, public readonly struct):
  the per-node facts `ADR-P5-001` classifies from -- `NodeTypeId`, `NodeTypeVersion`,
  `InstanceMemorySize`, `InstanceMemoryAlignment`, `MemoryLifetime` -- computed directly from an
  existing `CompiledNodeRecord`. Exposes `HasSameTypeAndVersion` and `HasCompatibleLayout` as two
  separate checks (not one combined boolean), since `ADR-P5-001`'s classifier needs to distinguish
  "type/version changed" from "same type/version but layout differs" as different diagnostic
  outcomes.
- `Runtime/HotReload/Identity/HotReloadProgramIdentityMap.cs` (new, public sealed class): builds a
  `NodeId -> (signature, current compiled index)` map from a `CompiledProgram`'s `DebugMap` +
  `Nodes`, immutable once built. `TryGetSignature`/`TryGetRuntimeIndex` mirror the `TryXxx` pattern
  used throughout this codebase (e.g. `NativeWorkEstimatorV1.TryObserve`) rather than throwing on a
  missing node ID.
- `Tests/Editor/HotReload/Identity/HotReloadProgramIdentityMapTests.cs` (new, 6 tests, all passing):
  null-rejection, correct mapping of every debug-map entry, `TryGetXxx` returning `false` for an
  unknown ID, `HasSameTypeAndVersion` true/false cases, `HasCompatibleLayout` true on a real
  parameter-edit case (`aibt.core.repeater` count 3 vs. 5 -- config bytes differ, layout does not),
  and a direct re-proof that compiled index shifts across a recompile even when the node itself is
  unchanged (the same load-bearing fact `P5-001`'s spike established, now proven against the real
  production type instead of throwaway spike code).

## Scope adaptation from the original card

The original `P5-002` card (written before `ADR-P5-001` existed) described "a program-version
identifier distinct from `CompiledContentHash`" and "a state-layout hash." Once the ADR decided
classification is **per-node**, not a single whole-program version scalar, a hash was unnecessary:
every field `HotReloadNodeIdentitySignature` needs (type ID, type version, memory size/alignment/
lifetime) is small, already present on `CompiledNodeRecord`, and cheaper and more debuggable to
compare directly than to hash and compare hashes. No hash is introduced. `P3-007`'s existing
`CompiledContentHash` remains the whole-program "did anything change at all" signal (per
`Documentation~/hot-reload.md`) that decides whether to run this per-node comparison in the first
place -- this card does not duplicate or replace it.

`Authoring/HotReload/` (listed as a possible location in the original card, "if the ADR's scheme
needs authoring-side computation") was not created: the scheme is computed entirely from
`CompiledProgram`, a `Runtime`-only type, with no authoring-side input required.

Tests live in `Tests/Editor/HotReload/Identity/` rather than `Tests/Runtime/HotReload/` (both were
listed as possible locations in the original card, "as applicable"): `CompiledProgram`'s public
constructor is heavily cross-validated (header counts, non-overlapping memory/config ranges,
debug-map back-references, etc.), so hand-constructing a valid instance directly in a
`Runtime`-only test without going through the real compiler would be fragile and would not test a
realistic program shape. Every test instead compiles a real `TreeDocument` through the real
`ReferenceCompiler`, the same pattern `Tests/Editor/Compilation/ReferenceCompilerTests.cs` and
`P5-001`'s own spike already use.

## Verification

Live Unity MCP test run: 6/6 passed. `Verify-Static.ps1`: 83 work items, unchanged. Full detail in
`verification-results.json`.

## Scope and limitations

- This card is a read-only view of one compiled program at a time. It does not compare two
  programs (`P5-003`'s job) and does not touch any live tree instance's own state (`P5-004`'s job).
- The composite-cursor-reset rule (`ADR-P5-001`) is not implemented here -- it operates on
  structural child-order facts, not per-node identity, and belongs to `P5-003`'s classifier.
