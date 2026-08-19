# P3-008 validation UX evidence

## Result

- `Editor/Validation/DiagnosticGraphLocation.cs`: classifies one `Diagnostic` into `Document`,
  `Node`, or `Field` by reading its own `Location.NodeId`/`Location.JsonPointer` — never parses
  or re-derives anything from the tree structure. Confirmed by inspecting `TreeValidator.cs`'s
  `Location`/`Create` helpers that every node-scoped diagnostic already sets `Location.NodeId`
  directly, and node+parameter-scoped ones use the `/nodes/<id>/parameters/<name>` pointer shape
  (`TreeValidator.NodePointer`/`ParameterPointer`), so resolution is a pure function of the
  diagnostic alone.
- `Editor/Validation/DiagnosticGraphSummary.cs`: `Build(DiagnosticCollection)` — per-severity
  counts, a classified marker per diagnostic, and `NodesWithMarkers()` (the jump-to-node list).
  Always rebuilt fresh from whatever diagnostics the caller currently has; nothing is cached or
  mutated in place.
- 3 tests, all passing:
  - `ChildPolicyAndParameterTypeDiagnosticsResolveToStableNodeAndFieldLocations` — a `ChildPolicy`
    violation resolves to a `Node` marker, a `ParameterType` violation resolves to a `Field`
    marker naming the exact parameter; no diagnostic in the fixture falls through to `Document`.
  - `ATreeWithZeroDiagnosticsShowsNoMarkers` — a valid tree produces an empty summary.
  - `FixingTheUnderlyingIssueClearsTheMarkerWithoutAnyManualRefreshStep` — a broken tree's marker
    for a node disappears once the same node is fixed and diagnostics are recomputed, with no
    invalidation/refresh call anywhere in the flow (there is nothing to invalidate — `Build` only
    ever reads the diagnostics passed to it that call).
- Diagnostics come from `AIBT.Authoring.ReferenceCompiler.Compile(...).Diagnostics` in every test
  — "the same [pipeline] `P3-006` routes edits through," per this card's `Forbidden changes`, not
  a separate call to `TreeValidator.Validate` alone (which `ReferenceCompiler.Compile` already
  invokes internally, per `P3-006`'s evidence).

## Decision

No new decision.

## Scope and limitations

- **No `Editor/Graph/` UI wiring** — outside this card's `Allowed changes` (`Editor/Validation/`
  only), same pattern as `P3-004` through `P3-007`. Rendering these markers as actual GraphView
  visuals (badges on `BehaviorTreeNode`, a summary panel) is a disclosed follow-up; the
  presentation *model* built here is what that UI would consume, and is independently fully
  tested.
- "Every diagnostic code the Authoring validation pipeline can produce renders with a stable
  location" was verified against a representative sample (`ChildPolicy`, `ParameterType`, plus the
  zero-diagnostics case), not literally every `AIBT2xxx`/`AIBT3xxx` code enumerated one by one —
  the resolver's logic is total (every diagnostic gets exactly one of the three classifications,
  by construction, not by enumerating cases), so this is a structural guarantee rather than one
  needing per-code test coverage to hold.
- Discovered one real, non-obvious system behavior while designing the first test attempt (not a
  bug, fixed by adjusting the test scenario, not production code): `SemanticEditOperations.Disconnect`
  leaves a detached node present but unreachable, which `TreeValidator` flags with its own
  diagnostic distinct from the `ChildPolicy` violation on the former parent — a genuine
  reachability check working as intended, not something this card's presentation layer needed to
  special-case.

See `verification-results.json` for exact commands and results.
