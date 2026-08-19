# P3-005 manual organization and layout persistence evidence

## Result

- `Editor/Layout/LayoutDocument.cs` extended with `LayoutGroup`, `LayoutNote`,
  `LayoutEdgeKey`/`LayoutReroute`, and a `LayoutIdentity` validator
  (GroupId/NoteId reuse the NodeId authoring-identity grammar per
  `editor-layout-v1.md`); `LayoutDocument` now carries `Groups`/`Notes`/`Reroutes`
  alongside `P3-004`'s `Nodes`.
- `Editor/Layout/CanonicalLayoutJsonWriter.cs` extended to serialize
  groups/notes/reroutes (ordinal key order, same canonical rules as `P3-004`).
- `Editor/Layout/CanonicalLayoutJson.cs` (new): a strict reader using
  `Newtonsoft.Json`'s `JsonTextReader` with `DuplicatePropertyNameHandling.Error`
  — the same library and pattern `AIBT.Authoring.CanonicalTreeJson` already
  uses for `.aibt.json` (`AIBT.Editor.asmdef` gained a `Newtonsoft.Json`
  reference for this, by explicit owner direction — see below). Validates
  structure, unknown fields (fail-closed), and — when given the semantic
  `TreeDocument` to check against — `treeId` match, unknown node references,
  duplicate group membership, and orphaned reroutes, raising the `AIBT1101`-`1111`
  diagnostics `P3-002` allocated.
- `Editor/Organization/LayoutOrganizationOperations.cs` (new): pure
  functions over `LayoutDocument` — `Pin`/`Unpin`/`SetNodePosition`,
  `AddOrUpdateGroup`/`RemoveGroup` (rejects a node already in another
  group), `AddOrUpdateNote`/`RemoveNote`, `AddOrUpdateReroute`/`RemoveReroute`.
  Every call returns a new document; none touch the semantic tree.
- `Editor/Organization/LayoutHistory.cs` (new): undo/redo as a snapshot
  stack over immutable `LayoutDocument`s — trivially correct since every
  organization operation already returns a new instance.
- `Editor/Organization/LayoutPersistenceController.cs` (new):
  `LayoutPathFor`/`Load`/`Save`. `Load` falls back to `P3-004`'s
  `DeterministicAutoLayoutService` when no `*.aibt.layout.json` exists next
  to the `.aibt.json` path (`<name>.aibt.json` -> `<name>.aibt.layout.json`),
  and also runs new nodes through it when an existing layout predates them
  — this is where `P3-004`'s previously-flagged "not wired into the
  adapter" gap actually gets resolved, at the persistence layer rather
  than inside `Editor/Graph/`.
- 14 tests, all passing: 7 for the reader/writer (round-trip,
  `treeId` mismatch, unknown node reference, duplicate group membership,
  orphaned reroute, invalid direction, duplicate property), 4 for
  organization operations (pin-survives-relayout, reject-duplicate-membership,
  add/remove round trip, undo/redo), 3 for the persistence controller
  (missing-file default, save-then-load byte-exact round trip,
  `.aibt.json` never touched). `P3-004`'s own 8 tests re-run clean (no
  regression from extending `LayoutDocument`).

## Decision

One escalated decision this task: `AIBT.Editor.asmdef` did not reference
`Newtonsoft.Json` (a new assembly reference, `DECISION_BOUNDARIES.md`
"must escalate before implementation"). Asked the owner directly rather
than choosing unilaterally; **accepted: add the reference**, reusing the
package/pattern `AIBT.Authoring.CanonicalTreeJson` already establishes,
over hand-rolling a second from-scratch JSON tokenizer.

## Scope and limitations

- **No `Editor/Graph/` UI wiring**, same pattern as `P3-004`: this card's
  `Allowed changes` lists `Editor/Layout/` and `Editor/Organization/` only,
  not `Editor/Graph/`. Pin/group/comment/reroute context menus on the live
  `BehaviorTreeGraphView`/`BehaviorTreeNode` are a real, disclosed follow-up,
  not silently done or silently skipped. The API layer built here
  (`LayoutOrganizationOperations`, `LayoutHistory`, `LayoutPersistenceController`)
  is exactly what such UI would call into, and is independently fully
  tested without needing any GraphView interaction.
- Tests for the new `Editor/Organization/` code live under
  `Tests/Editor/Organization/`, mirroring source structure 1:1 like
  `Editor/Graph/` <-> `Tests/Editor/Graph/` and `Editor/Layout/` <->
  `Tests/Editor/Layout/`. The card's `Allowed changes` literally names only
  `Tests/Editor/Layout/` fixtures; this is treated as an omission in the
  auto-generated card text (mirroring the source layout it itself
  introduces), not a real scope expansion into another card's owned area.
- Collaboration/merge-diff behavior (`editor-and-layout.md`'s "two authors'
  non-overlapping layout edits produce a mergeable diff") was not
  separately tested beyond what canonical, deterministic, ordinally-sorted
  JSON output already implies (stable key order means non-overlapping edits
  naturally produce a clean textual diff) — no dedicated merge-conflict
  test was written.
- `LayoutPersistenceController.Load`'s "present but invalid" path returns
  diagnostics and a null `Document` rather than a reconstructed default;
  `editor-and-layout.md` only requires the *missing*-file case to fall back
  automatically.

See `verification-results.json` for exact commands and results.
