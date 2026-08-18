# P3-002 layout model v1 contract evidence

`P3-002` is a specification-only deliverable (per its card's `Allowed changes`:
no editor implementation). This evidence records what was produced and how it
was checked, not a spike measurement.

## Result

- `Documentation~/specifications/editor-layout-v1.md` defines `*.aibt.layout.json`:
  header (`format`/`formatVersion`/`treeId`/`direction`), per-node
  position/pinned records, groups, sticky notes, edge reroutes, the explicit
  local-view-state exclusion list, a deterministic auto-layout contract
  stated as a pure function (`Layout(semanticTree, layoutInputs) ->
  layoutOutput`), and the formal `P3-007` isolation invariant statement.
- `Documentation~/specifications/diagnostics-v1.md` gained a sub-range
  annotation splitting the existing `AIBT1000`-`1999` band into `1000`-`1099`
  (`.aibt.json`) and `1100`-`1199` (`*.aibt.layout.json`); the new spec
  allocates `AIBT1101`-`AIBT1111` for the initial set of layout diagnostics.
- The spec states explicitly (per its own required acceptance criterion)
  that `UnityEditor.Experimental.GraphView` (accepted in `ADR-P3-014`) has no
  built-in persistence, so every field in the schema is AIBT-owned, not
  framework-inherited — `P3-003` has no ambiguity about what it must
  serialize itself versus get from the framework.

## Decision

No new decision; this task applies the already-accepted `AIBT-012`
(`ADR-P3-001` + `ADR-P3-014`) to a concrete persisted-format design, following
the same canonical-JSON discipline as `canonical-json-v1.md` and the same
identity rules as `identity-and-hashing-v1.md` (`GroupId`/`NoteId` reuse the
existing authoring-identity pattern, which that spec already reserved for
this purpose).

## Scope and limitations

- No code was written or run; this is a documentation deliverable only, per
  the card's `Forbidden changes` ("Editor implementation").
- The deterministic auto-layout contract specifies *what* determinism means
  (byte-identical output for identical input) but does not specify *which*
  algorithm `P3-004` implements — layout quality (crossing minimization,
  diff-noise minimization) is explicitly scoped to `P3-004`, not this
  contract.
- Diagnostic codes `AIBT1101`-`1111` are the initial set covering the
  failure shapes this spec's own text identifies; `P3-003`'s implementation
  may surface the need for additional codes, addable later per
  `diagnostics-v1.md`'s own "Adding a code" rule without breaking this
  document.

See `verification-results.json` for the exact commands run.
