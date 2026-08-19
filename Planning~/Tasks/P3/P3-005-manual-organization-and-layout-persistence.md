# P3-005 — Manual organization and layout persistence

Status: `Done`

## Objective

Let a user manually organize a graph (pin, group, comment, sticky-note, reroute) and persist those decisions to `.aibt.layout.json`, distinct from transient local view state.

## Depends on

- `P3-003`.
- `P3-004`.

## Required reading

- `Documentation~/specifications/editor-layout-v1.md`.
- `Documentation~/editor-and-layout.md` (manual organization tool list, collaboration/diff rules).

## Allowed changes

- `Assets/AIBT/Editor/Layout/` (extends `P3-004`'s area).
- `Assets/AIBT/Editor/Organization/` (new: pinning, groups, comments, sticky notes, reroutes UI and persistence).
- `Tests/Editor/Layout/` fixtures.
- `Planning~/Evidence/P3-005/`.

## Forbidden changes

- Persisting local-only view state (pan/zoom, current selection, open/closed panels) into `.aibt.layout.json` — it stays out of the shared file per `P3-002`'s split.
- Any semantic-tree mutation; this card only affects presentation.

## Deliverables

- Pin/unpin, group, comment, sticky-note, and reroute UI wired to the `P3-003` adapter.
- Persistence to `.aibt.layout.json` that round-trips deterministically and merges cleanly (per `Documentation~/editor-and-layout.md`'s collaboration/diff rules — two authors' non-overlapping layout edits produce a mergeable diff).
- Undo/redo for every manual organization action.

## Acceptance criteria

- A pinned node's position survives re-running `P3-004`'s auto-layout on the rest of the tree.
- Saving and reloading a document reproduces the exact manual organization (positions, groups, comments, sticky notes, reroutes) byte-for-byte in `.aibt.layout.json`.
- No manual-organization action changes `.aibt.json`.

## Required verification

```text
Verify-Static.ps1
Run-UnityTests.ps1 -Mode EditMode -Scope Focused -TestFilter <layout persistence fixture>
```

## Handoff notes

- `P3-007`'s isolation proof exercises this card's persistence path as the "layout-only edit" side of the test; keep the persisted diff surface (what changes in `.aibt.layout.json` per action) inspectable for that test to assert against.
- `P3-012`'s large-graph tests exercise this card's UI directly; avoid designs that only perform acceptably on the small fixtures used here.

## Outcome

- `LayoutDocument` extended with `Groups`/`Notes`/`Reroutes`; `CanonicalLayoutJson.cs`
  (new strict reader, `Newtonsoft.Json`-based) and `CanonicalLayoutJsonWriter.cs`
  (extended) round-trip all four presentation concepts byte-for-byte.
- `Editor/Organization/`: `LayoutOrganizationOperations` (pin/group/note/reroute,
  pure functions), `LayoutHistory` (undo/redo snapshot stack),
  `LayoutPersistenceController` (`Load`/`Save`, auto-layout fallback for a
  missing file — resolves `P3-004`'s previously-flagged integration gap at
  the persistence layer).
- One escalated decision: added `Newtonsoft.Json` to `AIBT.Editor.asmdef`
  (owner accepted, reusing `AIBT.Authoring`'s existing pattern) rather than
  hand-rolling a second JSON tokenizer.
- 14/14 new tests passing (7 reader/writer, 4 organization ops, 3
  persistence controller), plus `P3-004`'s 8 tests re-verified with no
  regression.
- **No `Editor/Graph/` UI wiring** — outside this card's `Allowed changes`,
  same as `P3-004`. Flagged as a disclosed follow-up, not silently done or
  silently skipped.
- Full evidence: `Planning~/Evidence/P3-005/README.md`, `verification-results.json`.
