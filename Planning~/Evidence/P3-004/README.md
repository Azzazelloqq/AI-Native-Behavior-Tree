# P3-004 deterministic auto-layout service evidence

## Result

- `Editor/Layout/LayoutDocument.cs`: in-memory model of a `.aibt.layout.json`
  document scoped to what this service produces — header (`treeId`,
  `direction`) plus the `nodes` map only. Groups/notes/reroutes are
  user-authored and owned by `P3-005`; this model deliberately omits them
  rather than adding unused fields.
- `Editor/Layout/CanonicalLayoutJsonWriter.cs`: canonical serializer
  implementing `editor-layout-v1.md`'s encoding rules (UTF-8 no BOM, LF,
  2-space indent, ordinal key order, Float32 shortest-round-trip
  formatting). Duplicates `AIBT.Authoring.CanonicalTreeJsonWriter`'s
  structure rather than reusing it: `AIBT.Authoring` does not grant
  `InternalsVisibleTo` to `AIBT.Editor`, and this card's allowed changes are
  scoped to `Editor/Layout/` only.
- `Editor/Layout/DeterministicAutoLayoutService.cs`: `Layout(TreeDocument,
  LayoutDocument existingLayout = null) -> LayoutDocument`. Post-order tidy
  tree: a leaf gets the next sequential horizontal slot in semantic
  (traversal) order; an internal node is centered (mean) over its
  children's x; y is fixed by depth. Any node already present in
  `existingLayout` keeps its exact recorded position — the "scoped
  re-layout" / "does not reposition previously-placed nodes" requirement —
  only nodes absent from it are freshly placed.
- `Tests/Editor/Layout/DeterministicAutoLayoutServiceTests.cs` (8 test
  cases, all passing):
  - `MatchesGoldenLayoutBytes` (3 cases: shallow-wide, deep-chain, mixed) —
    the service's output is byte-identical to a hand-derived, committed
    `*.expected.aibt.layout.json` golden fixture.
  - `RunningTwiceProducesByteIdenticalOutput` (3 cases) — determinism.
  - `SupersetTreeDoesNotRepositionPreviouslyPlacedNodes` — appending a node
    to a previously-laid-out tree leaves every existing node's position
    exactly unchanged.
  - `LargeSyntheticTreeLaysOutWithoutOverlappingPositions` — a synthetic
    240-node tree (same scale class as `P3-001`'s spike fixture, branching
    factor 3) lays out with no two nodes sharing the exact same position
    (the proxy used for "not visually degenerate").

## Decision

No new decision. Implements the algorithm contract `P3-002` already
specified; does not redesign it.

## Scope and limitations

- **Not wired into `P3-003`'s adapter yet.** The `P3-004` card's Deliverables
  text says this service is "consumed by `P3-003`'s adapter," but its
  Allowed changes list only `Assets/AIBT/Editor/Layout/` and
  `Tests/Editor/Layout/` — not `Editor/Graph/`. `BehaviorTreeGraphView`'s own
  placeholder `AssignDefaultPositions` (a simpler, non-centered column
  layout) was deliberately left untouched rather than overstepping this
  card's scope. Swapping it for a call to
  `DeterministicAutoLayoutService.Layout` is a small, real follow-up that
  needs its own (tiny) task-card authorization, since it touches a file
  owned by `P3-003`.
- "Not visually degenerate" for the 240-node case is checked via exact
  position non-collision, not a crossing-count or edge-length metric —
  `editor-and-layout.md`'s crossing-minimization goals are explicitly
  out of this contract's testable determinism scope per `editor-layout-v1.md`
  itself ("algorithm-quality goals... not part of this document's testable
  determinism contract").
- Golden fixture byte content was hand-derived from the algorithm's
  arithmetic (clean spacing constants — 160/120 units — were chosen so all
  positions are exact integers, avoiding float-precision noise in the
  golden files) and confirmed by the actual test run, not assumed.

See `verification-results.json` for exact commands and results.
