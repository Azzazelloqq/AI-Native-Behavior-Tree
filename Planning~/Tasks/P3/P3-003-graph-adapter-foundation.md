# P3-003 — Graph adapter foundation (read-only)

Status: `Done`

## Objective

Bind the accepted `P3-001` framework to the canonical Authoring model so an existing `.aibt.json` tree renders as a graph. No editing.

## Depends on

- `P3-001`.
- `P3-002`.

## Required reading

- The accepted `P3-001` ADR.
- `Documentation~/specifications/editor-layout-v1.md`.
- `Documentation~/specifications/node-contract-v1.md`.
- `AGENTS.md` architectural boundaries (`Editor` depends on `Authoring`, never the reverse).

## Allowed changes

- `Assets/AIBT/Editor/` (new subdirectories for the graph adapter).
- `Assets/AIBT/Editor/AIBT.Editor.asmdef` (add the accepted framework's package reference).
- `Tests/Editor/` graph-adapter fixtures.
- `Planning~/Evidence/P3-003/`.

## Forbidden changes

- Any write path back to `.aibt.json` or `.aibt.layout.json` — this card is read-only rendering.
- `Runtime` or `Authoring` changes; the adapter consumes their existing public surface only.

## Deliverables

- An `Editor` window/view that opens an existing `.aibt.json` document and renders every node and connection using the framework selected in `P3-001`.
- Node visuals driven by the versioned node registry (manifests), not hardcoded per node type.

## Acceptance criteria

- Every node kind in a representative fixture tree (composites, decorators, conditions, actions) renders with correct connections.
- Opening a document never mutates `.aibt.json` on disk (byte-identical before/after).
- Node coordinates/colors/groups/comments used for rendering come only from `.aibt.layout.json` (or a transient default when absent) and never influence what is asked of the compiler — visually confirms the `AGENTS.md` rule that presentation data cannot affect semantics.

## Required verification

```text
Verify-Static.ps1
Run-UnityCompile.ps1
Run-UnityTests.ps1 -Mode EditMode -Scope Focused -TestFilter <graph adapter fixture>
```

## Handoff notes

- `P3-004`, `P3-005`, and `P3-006` all build on this adapter's rendering surface; keep its public shape (how a document maps to graph elements) stable once accepted, since three downstream cards depend on it directly.

## Outcome

- `Editor/Graph/BehaviorTreeNode.cs`, `BehaviorTreeGraphView.cs`,
  `BehaviorTreeGraphWindow.cs`: read-only `GraphView` adapter over
  `AIBT.Authoring.TreeDocument`, node visuals driven by `NodeRegistry`
  lookups (`NodeManifest.Kind`/`ChildPolicy`), never a per-type switch.
- Positions are a transient depth/breadth default, not `P3-002`'s
  deterministic auto-layout contract — no `*.aibt.layout.json` reader
  exists yet (`P3-005`'s scope).
- `Tests/Editor/Graph/BehaviorTreeGraphAdapterTests.cs` (3/3 passing) proves
  all four `NodeBehaviorKind`s render with correct port/edge topology, that
  opening a document never mutates it on disk or in memory, and that
  unresolved node types render without throwing.
- Two real failures were hit and fixed before green: a `NodeRegistry`
  unqualified-name ambiguity against a sibling test namespace (CS0118), and
  using the reserved `aibt.test.` prefix with `AddUserExtension` instead of
  `AddTestFixtures`. Both are recorded in `Planning~/Evidence/P3-003/verification-results.json`.
- Full evidence: `Planning~/Evidence/P3-003/README.md`, `verification-results.json`.
