# P3-002 — Layout model v1 contract

Status: `Done`

## Objective

Promote `Documentation~/editor-and-layout.md`'s requirements into a normative `Documentation~/specifications/editor-layout-v1.md` defining `*.aibt.layout.json`: shared layout versus ignored local view state, and the deterministic auto-layout contract.

## Depends on

- `P3-001`.
- `P3-014`.

## Required reading

- `Documentation~/editor-and-layout.md`
- `Documentation~/architecture.md` (data-ownership table: semantics vs. shared layout vs. local view state)
- `Documentation~/specifications/canonical-json-v1.md`
- The accepted `P3-001` and `P3-014` ADRs.

## Allowed changes

- `Documentation~/specifications/editor-layout-v1.md`
- Focused allocation of a layout-diagnostic range in `Documentation~/specifications/diagnostics-v1.md`, if the layout document needs its own structured diagnostics.
- `Planning~/Evidence/P3-002/`

## Forbidden changes

- Any change to `*.aibt.json`'s canonical semantic schema; layout is additive and out-of-band.
- Editor implementation.

## Deliverables

- `*.aibt.layout.json` schema: node positions, pinning, groups, comments, sticky notes, reroutes, and their versioning.
- An explicit list of what is shared/persisted layout versus local-only view state (e.g. current pan/zoom, selection) that is never written to the shared file.
- A deterministic auto-layout algorithm contract: same semantic tree plus same layout inputs produce byte-identical output, testable without the editor running.
- The formal statement of the invariant `P3-007` will prove: a layout-only edit changes `*.aibt.layout.json` and never the compiled program.

## Acceptance criteria

- The schema round-trips deterministically, matching the discipline already required of `*.aibt.json` in `canonical-json-v1.md`.
- Invalid layout documents produce stable structured diagnostics, not silent coercion.
- The spec states explicitly which fields the chosen `P3-001` framework serializes natively versus which AIBT must own, so `P3-003` has no ambiguity.

## Required verification

```text
Verify-Static.ps1
Verify-Schemas.ps1
```

## Handoff notes

- `P3-003` cannot start until this spec is accepted; it is the shape `P3-003`'s adapter renders against.

## Outcome

- `Documentation~/specifications/editor-layout-v1.md` defines `*.aibt.layout.json`
  v1: header (`format`/`formatVersion`/`treeId`/`direction`), `nodes`
  (position/pinned), `groups` (title/description/color/locked/memberNodeIds),
  `notes` (free-floating sticky comments), `reroutes` (keyed by
  `fromNodeId|toNodeId`, ordered waypoints), the canonical-encoding rules
  mirrored from `canonical-json-v1.md`, `GroupId`/`NoteId` identity reusing
  `identity-and-hashing-v1.md`'s existing authoring-identity pattern, the
  explicit local-view-state exclusion list, a deterministic auto-layout
  contract stated as a pure function with a testable-without-the-Editor
  determinism requirement, and the formal `P3-007` isolation invariant.
- `Documentation~/specifications/diagnostics-v1.md`'s `AIBT1000`-`1999` band
  is now annotated as split `1000`-`1099` (`.aibt.json`) / `1100`-`1199`
  (`*.aibt.layout.json`); the new spec allocates `AIBT1101`-`1111`.
- Explicitly states (required acceptance criterion) that `GraphView`
  (`ADR-P3-014`) serializes nothing natively — every field in the schema is
  AIBT-owned, so `P3-003` has no ambiguity.
- No code changed; documentation-only, per this card's `Forbidden changes`.
- Full evidence: `Planning~/Evidence/P3-002/README.md`, `verification-results.json`.
