# P5-002 — Node identity, program version, and state-layout hashing

Status: `Done`

## Objective

Implement the identity/versioning/state-layout-hash scheme `P5-001`'s
accepted ADR decided, as the inspectable data model every later Phase 5 card
(classification, restart, migration) reads. This card computes and exposes
identity data; it does not decide compatibility or perform any reload.

## Depends on

- `P5-001` (accepted ADR).

## Required reading

- `P5-001`'s accepted ADR and `Planning~/Evidence/P5-001/`.
- `Documentation~/specifications/compiled-program-v1.md` (`CompiledProgram`
  layout, `CompiledContentHash`, the debug/hot-reload map).
- `Documentation~/specifications/identity-and-hashing-v1.md`.
- `Documentation~/data-formats.md` (stable authoring node IDs).

## Allowed changes

- `Runtime/HotReload/` (new) or wherever `P5-001`'s ADR places this -- follow
  the ADR's stated location, do not invent a new one.
- `Authoring/HotReload/` (new, if the ADR's scheme needs authoring-side
  computation).
- `Tests/Runtime/HotReload/`, `Tests/Editor/HotReload/` as applicable (new).

## Forbidden changes

- Any restart or migration mechanism (`P5-004`/`P5-005`/`P5-006`).
- Any compatibility-classification decision logic (`P5-003`) -- this card
  produces the data a classifier reads, it does not classify.
- Weakening `CompiledContentHash`'s existing layout/semantic isolation
  invariant (`P3-007`) to make hashing simpler.

## Deliverables

- A program-version identifier distinct from `CompiledContentHash`, per
  `P5-001`'s ADR, that survives exactly the changes the ADR decided are
  version-compatible and changes for everything else.
- A state-layout hash capturing whatever memory-layout facts
  compatible-migration (`P5-006`) will need to prove two programs
  migration-safe, computed deterministically from `CompiledProgram` alone.
- Per-node identity resolution reusing the existing stable authoring node ID
  and debug/hot-reload map (`compiled-program-v1.md`) rather than introducing
  a second node-identity concept.
- A pure, inspectable API surface: given two `CompiledProgram` instances,
  return their program-version and state-layout-hash comparison, with no
  side effects and no dependency on a live tree instance.

## Acceptance criteria

- Two `CompiledProgram`s produced from byte-identical source (per `P3-007`'s
  existing proof technique) always report identical program version and
  state-layout hash.
- A change `P5-001`'s ADR classified as always-incompatible always produces
  a different program version or state-layout hash (golden-pair tests per
  category, mirroring `P4-004`'s real-data-fixture discipline).
- The hash/version computation introduces no managed allocation on the
  native-execution hot path -- it runs at compile/reload time, never during
  a tree's per-frame update.
- Every constant/derivation rule is documented with its source in `P5-001`'s
  ADR, not an opaque number.

## Required verification

```text
Verify-Static.ps1
Run-UnityTests.ps1 -Mode EditMode -Scope Focused -TestFilter <identity/hashing fixture>
golden-pair tests for every P5-001-decided compatible/incompatible category
```

## Handoff notes

- `P5-003` consumes this card's comparison API directly; it must not
  recompute identity or hashing itself.
- `P5-006` (compatible migration) is the primary consumer of the
  state-layout hash specifically -- keep its contract precise enough that
  `P5-006` can decide "is this migration safe" from the hash alone plus its
  own runtime checks, without re-deriving structural facts this card already
  computed.

## Outcome

`Runtime/HotReload/Identity/HotReloadNodeIdentitySignature.cs` (per-node type ID/version/
instance-memory layout, with `HasSameTypeAndVersion`/`HasCompatibleLayout` as two separate checks)
and `HotReloadProgramIdentityMap.cs` (`NodeId -> (signature, current compiled index)`, built from
`CompiledProgram.DebugMap`+`Nodes`). Scope adapted from the original card once `ADR-P5-001` decided
classification is per-node, not a whole-program version scalar: no hash is introduced (direct field
comparison on the small existing `CompiledNodeRecord` fields is simpler and equally sufficient),
and no `Authoring/HotReload/` code was needed (the scheme is computed entirely from `Runtime`-only
`CompiledProgram` data). 6 tests, all passing, including a direct re-proof (against the real
production type, not spike code) that compiled index shifts across a recompile even when the node
itself is unchanged. Full detail in `Planning~/Evidence/P5-002/`.
