# P8-001 — Editable graph with saved drafts

Status: `In Progress`

## Objective

Let a human author create and edit a behavior tree in the existing graph window, save an
incomplete but representable tree as a draft, reopen it, and see the actual compiler diagnostics.
An invalid draft must never be treated as an executable program.

## Dependencies and decisions

- P3-003, P3-005, P3-006 and P7-025 are complete.
- ADR AIBT-032 integrates document operations into the graph window without changing runtime
  ownership. P3-006 permits invalid edits to be flagged using the same validation diagnostics.
- Owner decision, 2026-11-02: a human may save an incomplete draft; compilation and execution
  require errors to be corrected. Preserve the existing canonical format and compilation gates.
- Canonical serialization still requires a representable document with a valid root ID and at
  least one node. A new tree therefore starts with a root node rather than an unrepresentable file.

## Scope

- Create and open a tree, add and remove nodes, connect and disconnect them, edit parameters,
  control semantic child order, and undo/redo edits in the graph window.
- Display validation/compiler diagnostics for drafts using existing authoring contracts.
- Save and reopen canonical, representable drafts; retain semantic and layout separation.
- Persist node positions without making them affect child order or execution.
- Protect unsaved changes and avoid overwriting external changes silently.
- Preserve the existing strict `SemanticEditTransaction` behavior for existing callers.

## Outside this card

Project registry discovery, blackboard editing, live debugging, gameplay integration, and changing
runtime or persisted schemas require subsequent scoped work. Existing project-specific unresolved
nodes must not be silently discarded or represented as valid.

## Acceptance and verification

- Build a tree with an incomplete decorator, save and reopen it with the original diagnostic;
  connect a suitable child and verify that the diagnostic clears and compilation succeeds.
- Invalid drafts cannot compile or execute. Valid authoring matches canonical hand-authored data.
- Editing, undo/redo, save/reopen, explicit child order and persisted layout are verified in the
  actual Unity window, including external-file and unsaved-change handling.
- Run focused graph and editing behavior tests, relevant existing regression tests, static checks,
  and `git diff --check`; report unavailable checks rather than asserting completion.
- Preserve unrelated work, including the existing local `BehaviorTreeImporter.cs` change.

## Current verification

- Isolated Unity 6000.7.0b1 EditMode run: 1481/1481 passed on the integrated snapshot.
- Static/schema verification passed; targeted graph, preview and project-catalog checks passed.
- Swarm Arena's project-generated tree compiled in a separate consumer project; changing a
  blackboard binding in the actual EditorWindow and saving it passed an automated check.
- Unity 6000.5.8f1 and a manual interactive editor acceptance remain unverified in this environment.
  This card remains in progress until its applicable acceptance gates are completed.
