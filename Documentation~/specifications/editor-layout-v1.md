# Editor layout v1

This specification defines `*.aibt.layout.json`: shared, committed presentation
data for one semantic tree document. It promotes `Documentation~/editor-and-layout.md`'s
requirements into a normative, versioned persisted format and a deterministic
auto-layout contract. It does not define editor implementation.

## Scope and the three-model split

Per `Documentation~/editor-and-layout.md` and `Documentation~/architecture.md`'s
core data-ownership table, the editor works with three independent
representations. This document governs only the second:

1. **Semantic tree** — `.aibt.json`, governed by `canonical-json-v1.md`. Out of
   scope here.
2. **Shared presentation layout** — `*.aibt.layout.json`, defined below.
   Mutable during authoring, committed to source control, owned by Editor.
3. **Local view state** — never persisted to any shared file. Listed
   exhaustively in [Local view state](#local-view-state).

A layout document decorates exactly one semantic tree document (`treeId`) and
MUST NOT be interpreted by the compiler. Semantic compilation MUST succeed
identically whether a layout document exists, is missing, or fails to parse;
the editor reconstructs a readable default layout in that case (per
`editor-and-layout.md`'s Collaboration section). Per `identity-and-hashing-v1.md`,
the semantic hash excludes layout entirely.

## Encoding

`*.aibt.layout.json` uses the same canonical encoding discipline as
`canonical-json-v1.md`, restated here because this is an independently
versioned format:

- UTF-8 without BOM, LF line endings, two-space indentation, one trailing LF.
- Property names and string values use JSON escaping identical to
  `canonical-json-v1.md`; unpaired surrogates and non-finite numbers are
  errors.
- Integers use base-10 digits with no leading zero except `0`; negative zero
  is canonicalized to `0`.
- `Float32` fields (all positions, sizes, and waypoints in this document) use
  the shortest round-trippable, culture-invariant finite decimal, matching
  `canonical-json-v1.md`'s `Float32`/`Float64` rule. Parsed negative zero is
  written as `0`. NaN and infinities are invalid.
- Duplicate object properties, comments, and trailing commas are errors.
- Schema-defined properties are written in the order given in this document.
  Unknown fields are rejected before writing (fail closed, not silently
  dropped) — this MUST produce a diagnostic (see [Diagnostics](#diagnostics)),
  never silent coercion.
- Map-like collections (`nodes`, `groups`, `notes`, `reroutes`) are ordered by
  key using ordinal UTF-8 byte order, identical to `canonical-json-v1.md`'s
  rule for `blackboard`/`nodes`. Array fields (`memberNodeIds`, `waypoints`)
  are semantic order and are preserved as authored.

## Identities

`GroupId` and `NoteId` follow the `identity-and-hashing-v1.md` authoring
identity pattern (`^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$`), stable opaque
identities, never display names. A reroute has no independent ID; it is
identified by its owning edge (`fromNodeId`/`toNodeId` pair), since Phase 1/2
trees do not support multiple parallel edges between the same two nodes.

## Document shape

```jsonc
{
  "format": "aibt.layout",
  "formatVersion": 1,
  "treeId": "<TreeId>",
  "direction": "topToBottom",
  "nodes": {
    "<NodeId>": { "position": { "x": 0, "y": 0 }, "pinned": false }
  },
  "groups": {
    "<GroupId>": {
      "title": "string",
      "description": "string",
      "color": "#RRGGBB",
      "locked": false,
      "memberNodeIds": ["<NodeId>", "..."]
    }
  },
  "notes": {
    "<NoteId>": {
      "text": "string",
      "position": { "x": 0, "y": 0 },
      "size": { "x": 0, "y": 0 },
      "color": "#RRGGBB"
    }
  },
  "reroutes": {
    "<fromNodeId>|<toNodeId>": {
      "waypoints": [{ "x": 0, "y": 0 }]
    }
  }
}
```

### Header

- `format`: fixed string `"aibt.layout"`.
- `formatVersion`: `Int32`, currently `1`. Unsupported future versions MUST
  fail (no best-effort reader), matching `canonical-json-v1.md`'s Phase 1
  policy. A future migration MUST accept old canonical bytes and produce
  deterministic new bytes.
- `treeId`: the `TreeId` (per `identity-and-hashing-v1.md`) of the semantic
  document this layout decorates. A layout document whose `treeId` does not
  match the currently open `.aibt.json` MUST be rejected with a diagnostic,
  never silently applied.
- `direction`: `"topToBottom"` or `"leftToRight"`, per `editor-and-layout.md`'s
  "Default layout direction is consistent per tree and configurable" rule.

### `nodes` (map, keyed by `NodeId`)

- `position`: `Float2`, the node's canvas position.
- `pinned`: `Bool`. Pinned nodes are excluded from movement by auto-layout
  (`editor-and-layout.md`'s "preserve pinned nodes").
- A `NodeId` referenced here that does not exist in the semantic document is
  a structural error (diagnostic, not silent drop) — layout MUST NOT
  introduce nodes.
- Semantic nodes absent from `nodes` are valid (auto-layout assigns a
  position on next generation); this is not an error.

### `groups` (map, keyed by `GroupId`)

- `title`, `description`: strings, authoring-only, never affect compilation.
- `color`: `"#RRGGBB"` (uppercase or lowercase hex, six digits).
- `locked`: `Bool`, per `editor-and-layout.md`'s "explicitly locked groups"
  exclusion from auto-layout movement.
- `memberNodeIds`: ordered list of `NodeId`. Every referenced ID MUST exist
  in the semantic document. A `NodeId` MAY belong to at most one group;
  overlapping membership is a structural error.

### `notes` (map, keyed by `NoteId`)

Free-form sticky notes/comments, per `editor-and-layout.md`. `position` and
`size` are `Float2`; `text` and `color` as above. Notes are never associated
with a `NodeId` — they are free-floating canvas elements.

### `reroutes` (map, keyed by `"<fromNodeId>|<toNodeId>"`)

- `waypoints`: ordered, non-empty list of `Float2` the edge routes through
  between its two node endpoints.
- Both `fromNodeId` and `toNodeId` MUST reference an edge that exists in the
  semantic document's execution structure (i.e., `toNodeId` is a child of
  `fromNodeId` in the compiled tree, or a documented subtree-reference edge).
  An orphaned reroute (edge no longer exists) is a structural error.

## Framework-native vs. AIBT-owned fields

Per the accepted `ADR-P3-014-editor-graph-framework.md`,
`UnityEditor.Experimental.GraphView` has **no built-in persistence** — it
does not serialize positions, groups, notes, or reroutes on its own (unlike,
e.g., Shader Graph's asset format, which is Shader Graph's own addition on
top of the same base class). Every field defined in this document is
therefore entirely AIBT's own design and ownership; `P3-003`'s adapter reads
this document to position `GraphView` elements it constructs, and nothing in
this schema is inherited from or dictated by the framework. `P3-003` has no
framework-imposed layout format to reconcile against.

## Local view state

The following are never written to `*.aibt.layout.json` or any other shared,
committed file, per `editor-and-layout.md`'s "Local view state" model and its
"View-only state is never committed" rule:

- pan and zoom;
- current selection;
- open inspector panels;
- navigation history / breadcrumb stack;
- temporary filters and search state;
- minimap visibility and focus-mode toggle state.

These MAY be persisted per-user outside source control (e.g. `EditorPrefs` or
a local, gitignored file) at the implementing task's discretion; this
specification only constrains the shared file.

## Deterministic auto-layout contract

Auto-layout is a pure function:

```text
Layout(semanticTree, layoutInputs) -> layoutOutput
```

where `layoutInputs` is the subset of the current `*.aibt.layout.json` that
constrains the algorithm (existing pinned node positions, locked group
bounds, and the `direction` setting) plus the requested scope (whole tree,
subtree, or selected region). `layoutOutput` is a new `nodes`/`groups`
position assignment for every non-pinned, non-locked element in scope.

Requirements, restating `editor-and-layout.md`'s Auto-layout section as a
testable contract:

- **Determinism**: identical `semanticTree` and `layoutInputs` MUST produce
  byte-identical canonical `*.aibt.layout.json` output. This MUST be testable
  headlessly, without the Editor GUI running or any `GraphView` instance
  constructed — pure data in, canonical bytes out.
- **Locality**: positions of nodes and groups outside the requested scope
  are unchanged in the output.
- **Invariant preservation**: pinned nodes and locked groups keep their
  existing `position`/bounds exactly; sibling execution order (semantic
  order) is never reordered by layout.
- Crossing/edge-length minimization and Git-diff-noise minimization
  (`editor-and-layout.md`'s "keep stable positions where possible") are
  algorithm-quality goals for `P3-004`'s implementation, not part of this
  document's testable determinism contract — determinism is about
  reproducibility of whatever the algorithm produces, not the algorithm's
  layout quality.

## The `P3-007` isolation invariant

Stated formally for `P3-007` to prove: for any edit classified as
layout-only (moving/pinning a node, editing a group/note, adding/removing a
reroute, running auto-layout, or changing `direction`), the resulting
`.aibt.json` byte content and its semantic hash (`identity-and-hashing-v1.md`)
MUST be unchanged, and the compiled program produced from that unchanged
`.aibt.json` MUST be unchanged. Only `*.aibt.layout.json` MAY change. The
converse also holds: a semantic-only edit (e.g. an AI domain operation) MUST
NOT alter `*.aibt.layout.json` except through the explicit `editor-and-layout.md`
"AI editing behavior" placement rules (new nodes placed near their semantic
parent, affected region re-laid-out), which are themselves layout-document
writes attributable to that same transaction, not silent side effects.

## Diagnostics

Invalid layout documents produce stable structured diagnostics
(`diagnostics-v1.md`), never silent coercion or default substitution for a
document that fails to parse or validate — a missing document falls back to
a reconstructed default (per `editor-and-layout.md`), but an invalid,
present document does not.

`diagnostics-v1.md` allocates `AIBT1100`-`1199` for layout document
diagnostics, a sibling sub-range to `.aibt.json`'s existing
`AIBT1001`-`1008` (`TreeJsonDiagnostics`) within the shared
`AIBT1000`-`1999` "syntax, schema, and canonical serialization" band:

| Code | Condition |
| --- | --- |
| `AIBT1101` | Invalid UTF-8 byte sequence. |
| `AIBT1102` | Invalid JSON syntax. |
| `AIBT1103` | Duplicate object property. |
| `AIBT1104` | Schema violation (unknown field, wrong type, missing required field). |
| `AIBT1105` | Unsupported `formatVersion`. |
| `AIBT1106` | Invalid Unicode scalar sequence. |
| `AIBT1107` | `treeId` does not match the open semantic document. |
| `AIBT1108` | `NodeId` referenced by a `nodes`/`groups`/reroute entry does not exist in the semantic document. |
| `AIBT1109` | A `NodeId` belongs to more than one group. |
| `AIBT1110` | A reroute references node pair with no corresponding edge in the semantic document (orphaned reroute). |
| `AIBT1111` | Invalid `direction` value. |

Each entry follows `diagnostics-v1.md`'s record shape (stable code,
severity, optional JSON Pointer/line/column, related locations) and its
authoring-JSON property order. Adding a code beyond this initial set
requires one catalog entry, per `diagnostics-v1.md`'s "Adding a code"
rule.

## Versions

This is `editor-layout-v1`. Breaking changes require a new `formatVersion`
and a deterministic migration plus fixtures, per `conventions.md`'s
Versioning rules.
