# Visual editor and layout

## Product requirement

The editor must produce readable behavior diagrams, not merely expose graph serialization. A graph must remain understandable after automatic generation, AI edits, reopening, refactoring, and collaboration through Git.

## Separate models

The editor works with three independent representations:

1. **Semantic tree** — node types, parameters, ordered children, blackboard, and subtree references.
2. **Shared presentation layout** — node positions, pinned state, groups, comments, sticky notes, and edge reroutes.
3. **Local view state** — selection, pan, zoom, open inspectors, navigation history, and temporary filters.

Only semantic data affects execution. Shared presentation data is committed. Local view state is ignored and must not create repository noise.

## Readability rules

- The visual child order must match semantic execution order and be numbered when ambiguity is possible.
- Default layout direction is consistent per tree and configurable between top-to-bottom and left-to-right.
- Nodes use predictable widths, spacing, alignment, category styling, status badges, and parameter summaries.
- Edges avoid node bodies and minimize crossings. Reroute points are supported for unavoidable long or crossing connections.
- Sequence and selector flow is visually distinguishable without relying on color alone.
- Collapsed subtrees preserve a meaningful title, status, validation state, and execution summary.
- Validation errors and performance warnings point to exact nodes without destroying layout.
- Runtime highlighting must remain legible without causing nodes to move.

## Auto-layout

The editor supports layout for the entire tree, the current subtree, or the selected region. Auto-layout must be deterministic for identical semantic and layout inputs.

Requirements:

- preserve pinned nodes and explicitly locked groups;
- limit movement to the affected region after a local edit;
- minimize crossings and excessive edge length;
- maintain sibling execution order;
- respect group boundaries and comment blocks;
- keep stable positions where possible to reduce visual and Git diff noise;
- provide preview and undo before applying a large rearrangement.

Tree-specific tidy layout is the default candidate for strict trees. Layered graph layout is evaluated for subtree references and cross-links. The exact algorithm and graph framework are selected through a measured editor spike.

## Manual organization

Required authoring tools:

- align, distribute, snap, compact, and frame selection;
- pin or unpin nodes and regions;
- create titled, colored groups with descriptions;
- sticky notes and free-form comments;
- edge reroutes and route reset;
- minimap, search, filters, breadcrumbs, and subtree navigation;
- bookmarks and focus mode for large trees;
- copy/paste with deterministic ID remapping;
- undo/redo for semantic and layout changes;
- separate semantic and layout diffs.

Groups, comments, colors, and positions are presentation-only. A future semantic module concept must use an explicit subtree or another runtime construct, never an editor group.

## AI editing behavior

AI tools modify semantics through domain operations. After a semantic transaction, the editor:

1. preserves positions of unaffected and pinned nodes;
2. places new nodes near their semantic parent or requested group;
3. lays out only the affected region by default;
4. reports unresolved overlaps or crossings;
5. lets the user preview a full cleanup pass.

AI may add meaningful group titles, descriptions, and comments when requested, but must not manually generate arbitrary canvas coordinates. Layout services own coordinates.

## Collaboration

Layout files use stable IDs, deterministic ordering, normalized numeric precision, and versioned migrations. A missing or invalid layout never prevents semantic compilation; the editor can reconstruct a readable default.

Conflicts in semantic files and layout files are resolved independently. View-only state is never committed.

## Debugging and profiling

- live active path for one or many selected agents;
- node enter/exit/status history with reasons;
- blackboard values and changes;
- breakpoints, pause, step, and subtree isolation;
- abort and cancellation source visualization;
- node and branch cost overlays;
- scheduler policy, batching, budget, and latency display;
- trace comparison between agents or revisions.

Debug instrumentation is optional in player builds and must have an explicit cost profile.
