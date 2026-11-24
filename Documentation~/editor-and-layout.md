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

## Graph editor as implemented (P9-003)

`AIBT > Graph Editor`, or double-click an `.aibt` asset. Opening a tree never blocks: the window
that already shows it is focused; otherwise a window without unsaved changes is reused; otherwise
a second graph window opens. Inside one window, opening another tree while edits are unsaved
shows a banner (Save and open / Discard and open / Cancel) instead of a modal dialog.

**Canvas.** Grid background, minimap, top-down tree layout computed by
`DeterministicAutoLayoutService` and scaled for the card size, so no branch is hidden under the
side panels. Each card shows a kind color accent and tag (composite, decorator, condition,
action), a readable title (display name, else the type name), a one-line parameter summary, a
child-order badge and an error/warning badge. Ports sit on the card's top (parent) and bottom
(children) edges. Child order always comes from the document; moving a card never reorders.

**Editing.** Right-click a node, edge or the canvas for its commands (add child, move earlier or
later, detach, set as root, duplicate, delete, insert node on an edge, create node, auto layout,
frame). Node creation uses a searchable list: composites and decorators are listed directly, conditions
and actions are grouped by manifest category (Movement, Navigation, Blackboard, ...). It opens from the context menu,
Space, or by dropping a dragged edge on empty canvas (the new node becomes the last child).
Dragging an edge onto another node connects or reparents it. Shortcuts: Delete, Ctrl+S (save),
Ctrl+Z / Ctrl+Y (undo/redo), Ctrl+D (duplicate), F (frame), Alt+Left/Right (reorder),
Ctrl+Shift+L (auto layout). Until a card is dragged by hand, the tree is re-laid out after each
edit; after that, new nodes are placed next to their parent and other positions are kept.

**One toolbar, one save, one history.** New, Open, Save, Auto Layout, Preview, Debug, and More
(Check Native Compile, Prepare Runtime Asset, Reload From Disk, Frame All). Save writes the tree
when it changed and the `.aibt.layout.json` sidecar when positions changed. Tree edits and layout
moves share one undo history that is also Unity's Edit > Undo. External changes to an open,
edited tree show a reload banner; saving over them is refused.

**Inspector.** Typed fields per parameter (toggle, number, enum popup, text, blackboard-key popup
filtered by the expected value type). Unset parameters show the contract default greyed with a
`default` tag; right-click a field to reset it. The Blackboard panel lists keys with their type and
Tree-scope default and adds keys. The Problems list under the graph shows diagnostics; clicking one
selects and frames its node. Notes are informational: `AIBT2053 Input key '<key>'` marks a
Tree-scope key that no node declares a write for and no managed node binds as a key parameter, which is expected for keys gameplay code sets through
`BehaviorTreeAgent.Blackboard.Set` or `NativeTreeAgent.SetFact`. The project tree list validates
managed trees with the managed policy. A valid tree with native (generated) nodes is `Valid`; its
tooltip names the prepared runtime asset compiled from it (as its source or as a subtree) or says
that none is prepared yet. It shows `Needs native compile` only when one of its native node types
has no generated code in the project catalog. The list does not check whether a prepared asset is
stale; the asset's inspector does.

**Live debugging.** In Play Mode, selecting a GameObject with a `BehaviorTreeAgent` whose tree is
open attaches the graph automatically (Debug attaches explicitly and opens the agent's tree when
the window is clean). Running nodes pulse, finished nodes flash green (success), red (failure) or
grey (aborted) and fade, edges between running nodes are drawn as the active path, and the
Blackboard panel shows live values. States are sampled about 30 times a second, so a node that
starts and finishes between two samples is not shown.

**Native agents (P9-004).** Selecting a GameObject with a `NativeTreeAgent` (or pressing Debug)
attaches the same way. The view reads the host's activation stack each frame and maps runtime
node indexes through the program's debug map: it shows the active path, nodes that just finished
and the root result. It does not show exit statuses of inner nodes or activations that start and
end within one frame, and it does not read a host while it is driving or while a pipelined job is
in flight. A raw `ProductionTreeHost` keeps the trace path (**AIBT > Attach Selected Host Trace**)
when it has a trace channel, which it has in the Editor unless it was bootstrapped with
`NativeTraceChannelCapacityV1.None` or belongs to a `NativeTreeAgent` set to Off.

**Native blackboard values (P11-005).** With a `NativeTreeAgent` attached, the Blackboard panel shows
`live: <value>` for every Tree-scope key, read from the current run's host by the key's ID five times
a second. Without a run it shows `live: —`; when the next run starts the values are that run's, so
they go back to the defaults. A host that runs `PipelinedJobs` is executing between frames and
refuses reads: the panel then keeps the last value it read and says so in the tooltip, so values
freeze while the host executes. A key the running program does not have (for example renamed after
the asset was prepared), an Agent- or Shared-scope key, or a key of a registered type shows `live: —`
with the reason in the tooltip. In a subtree window a key of the instance reads the instance's own
key, and an interface key reads the parent key it is bound to.

**Runtime assets.** More > Prepare Runtime Asset compiles the tree against a generated catalog into a
`ProductionTreeAsset` and records the source GUID, source hash and catalog type. Saving the source
re-prepares the asset in place during import; a changed catalog fingerprint re-prepares it after the
next script reload. The asset inspector shows the state (Current, SourceChanged, CatalogChanged,
SourceMissing, CatalogMissing, Unknown) with Prepare Now / Prepare Again / Open Source Graph, and the
`NativeTreeAgent` inspector warns about a stale asset. Running hosts keep the program they started
with until they restart. Assets prepared before P9-004 are Unknown until prepared once more.
