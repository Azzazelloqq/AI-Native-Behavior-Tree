# ADR P11-001: Subtrees (reusing one tree inside another)

- Status: Accepted (2026-11-19). Proposed the same day; the owner accepted it with all eight
  recommendations of §8 (see "Owner decisions" below) and approved stage 1 of §7, including the
  specification edits of §4.8. Stages 2–4 each need their own go-ahead.
- Decision ID: AIBT-049
- Task: `Planning~/Tasks/P11/P11-001-subtrees.md`
- Origin: feature F1 of the post-container feature list
  (`Documentation~/decisions/ADR-P10-008-simulation-container.md:733-734`), and the long-standing
  but never implemented promise of "subtree references" in `Documentation~/data-formats.md:15`,
  `:50`, `Documentation~/scope.md:12`, `Documentation~/editor-and-layout.md:11`, `:24`, `:42`,
  `:53`, `:59`.

File references are relative to the package root (`Assets/AIBT`) at commit `3e4b09e`. Another
session (P10-014) has uncommitted edits in `Runtime/Execution/Native/` and
`Runtime/State/Native/`; where a line in such a file is cited, it is the line at `3e4b09e`.

Words used here:

- **subtree** — an ordinary `.aibt` / `.aibt.json` tree that another tree uses.
- **reference node** — the node in the *parent* tree that says "put subtree X here".
- **instance** — one use of a subtree at one reference node. A subtree used three times in one
  tree has three instances.
- **expansion** (flattening) — replacing every reference node by a renamed copy of the subtree's
  nodes, producing an ordinary tree with no references.
- **interface** — the subtree's blackboard keys that a caller must connect.

## 1. Goal and user stories

Today a behaviour used in several trees is copied by hand; fixing it means finding and editing
every copy. Subtrees let an author write it once and use it everywhere.

**Tree author (graph editor).**

1. Picks "Subtree…" in the node palette, chooses a tree from the project (e.g. `combat.attack`),
   and gets one node titled "Attack" in the parent graph. It can be placed under any composite or
   decorator, several times in one tree and in many trees.
2. Connects the subtree's interface keys: `target` → parent key `enemy`, `range` → the constant
   `5.0`. Unconnected or wrongly typed keys are shown as validation errors on the node.
3. Double-clicks the node to open the subtree in its own graph window, edits it, saves. Every tree
   that uses it is re-imported and re-baked; nothing else has to be touched.
4. Selects a group of nodes and chooses "Extract to subtree": a new tree file is created and the
   nodes are replaced by one reference node. "Unpack subtree" does the reverse (a plain copy with
   no link).
5. While a game runs, the reference node lights up when anything inside it is active; opening the
   subtree from the live parent graph highlights the active nodes *of that instance*.

**Tree author (`.aibt.json`, MCP, CLI).** The same reference is a small JSON object (§4.2). MCP
and CLI can add/rebind references, extract and unpack, list which trees use a subtree, and show
the expanded tree.

**Game programmer.** Nothing changes in the API they use. A `BehaviorTreeAgent`, `NativeTreeAgent`,
`ProductionTreeHost` or `TreeSimulation` group that runs a tree with subtrees receives an ordinary
compiled program. Debug output (`describe_agent`, active path, trace timeline) names nodes inside
a subtree as `attack:check-range` and shows them as "Attack / Check range".

## 2. What exists today

### 2.1 Subtrees do not exist anywhere in code

- No subtree, include or tree-reference field exists in the tree schema
  (`Schemas~/tree.schema.json:6-38`, top level `additionalProperties: false`; node object
  `:144-170`) or in `TreeDocument`/`NodeDocument` (`Authoring/Model/Tree/TreeDocument.cs:87-111`,
  `Authoring/Model/Tree/NodeDocument.cs:49-67`).
- P6-006 confirmed this and deliberately implemented MCP `extract_subtree`/`inline_subtree` as
  payload moves, "not a live subtree reference" (`Planning~/Evidence/P6-006/README.md:62-67`).
  `inline_subtree` refuses any incoming node ID that already exists, with no remapping
  (`MCP/Authoring/McpAuthoringOperations.cs:100-106`), and leaves blackboard keys untouched. The
  editor's "duplicate subtree" does remap IDs, giving each copy `node-` + 12 hex characters
  (`Editor/Graph/GraphDocumentEdits.cs:38`, `:74-112`).
- No earlier ADR decided or deferred subtrees. The only prior decision touching them is
  `editor-and-layout.md:59`: "A future semantic module concept must use an explicit subtree or
  another runtime construct, never an editor group." `editor-layout-v1.md:139-146` mentions "a
  documented subtree-reference edge" for reroutes, which was never documented.

### 2.2 Tree document, identity, hashing

- Node IDs are the keys of the `nodes` object, so they are unique per document by construction
  (`tree.schema.json:28-32`); the validator also reports `AIBT2017` (`TreeValidator.cs:299-305`).
  The grammar is `^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$`: `:` is allowed, `/` is not, at most 128
  characters (`identity-and-hashing-v1.md:5-9`, `Runtime/Core/Identity/AuthoringId.cs:9`).
  "Copy/paste and merge tools MUST remap collisions explicitly" (`identity-and-hashing-v1.md:11`).
- The reader rejects every unknown property with `AIBT1004` (`CanonicalTreeJson.cs:777-785`;
  top-level allow-list `:157-159`) and every format version other than 1 and 2 with `AIBT1005`
  (`:165-167`). A new property therefore needs a new tree format version; old readers fail
  cleanly on it, they never mis-read it. Version 2 is additive over version 1
  (`agent-shared-blackboard-v1.md:21-27`) — the precedent for an additive version 3.
- The document's semantic hash is SHA-256 of the canonical bytes without names, descriptions,
  tags and metadata; it includes node IDs (`canonical-json-v1.md:40`; `CanonicalTreeJson.cs:57-64`).
  The reference compiler stores exactly this hash as the program's `CanonicalSemanticHash`
  (`Authoring/Compilation/ReferenceCompiler.cs:30-34`, `:56`).
- The semantic hash seeds every native random stream
  (`Runtime/State/Native/NativeInstanceArenaOwner.cs:290-297`) and appears in every trace record
  (`trace-v1.md:7`).

### 2.3 Blackboard

- Keys are declared per tree with scope `tree`, `agent` or `shared` (`canonical-json-v1.md:33`,
  `BlackboardScope.cs:3-9`). A missing key or a type mismatch is a compile error; the runtime never
  creates keys (`blackboard-v1.md:37`).
- Nodes reach keys in four ways: manifest accesses with a fixed key name
  (`TreeValidator.cs:776-805`); managed-only `BlackboardKey` parameters holding a key ID
  (`NodeManifestContracts.cs:48-54`, checked `TreeValidator.cs:712-743`); the v2 node `bindings`
  map from a generated binding ID to a key ID, checked by the compiler for exact scope/type match
  (`agent-shared-blackboard-v1.md:29-46`; `GeneratedScopeCompiler.cs:288-325`); observer
  `watchedKeys` (`TreeValidator.cs:864-894`). An expansion must rename keys in all four.
- Agent and Shared keys belong to a scope contract (`contractId`, `contractVersion`); trees that
  share a context repeat an identical complete contract (`agent-shared-blackboard-v1.md:49-60`).
- Slots are ordered by key ID; `StableKeyId` is the FNV-1a hash of the key ID
  (`ReferenceCompiler.cs:375-418`, `:490`).

### 2.4 Validator and compilers work on one document

- `TreeValidator.Validate(document, registry, options, registeredTypes)` has no resolver for other
  documents (`TreeValidator.cs:8-55`; `ValidationOptions`, `TreeValidationContracts.cs:94-131`).
- `ReferenceCompiler.Compile` takes one `TreeDocument`, validates it first
  (`ReferenceCompiler.cs:11-26`), orders nodes by pre-order DFS from the root (`:799-832`) and
  indexes them with `Dictionary<NodeId,uint>.Add` (`:834-839`).
- The compiled node record has no node ID: only type ID/version, layout, child range, flags and
  `DebugIdentityIndex` (`ReferenceCompiler.cs:682-706`; `CompiledProgramRecords.cs:38-52`;
  native `NativeProgramContracts.cs:293-333`). The authoring ID lives only in the debug map,
  one entry per node with its own `SourcePath` field (`ReferenceCompiler.cs:666-680`;
  `CompiledProgramRecords.cs:315-348`). Today every entry gets the one `options.SourceId`.
- The compiled-content hash includes the debug map: runtime index, authoring ID, source path and
  display name (`Authoring/Compilation/CompiledContentHasher.cs:133-144`).
- The generated V2 compiler (production/native path) validates, projects, then calls the
  reference compiler and joins records back to `NodeDocument`s through
  `DebugMap[i].AuthoringNodeId` with `Dictionary.Add`
  (`Authoring/Compilation/Generated/GeneratedCompiledProgramV2.cs:268-345`, `:617-638`); it
  assumes unique IDs and `DebugMap[i].RuntimeNodeIndex == i`.
- Structural limits: policy `MaxTreeDepth`/`MaxNodesPerTree`, null by default
  (`ReferenceCompilationPolicy.cs:14-15`, `:75`), enforced in `TreeValidator.cs:1010-1011`,
  `:1078-1087`. Every compilation-affecting option is part of the policy hash
  (`compiled-program-v1.md:27`), so a *new* policy field would change every program's policy
  hash.

### 2.5 Capacity

Native instance capacity is derived from the program's counts, not from tree shape: frames =
`NodeCount`, generations = `NodeCount`, parallel branches = `ChildIndexCount`, node memory =
`InstanceNodeMemorySize` (`Runtime/State/Native/NativeInstanceArenaContracts.cs:105-165`, counts at
`:148-150` at `3e4b09e`). The reference executor allocates one frame per node
(`ReferenceExecutionMachine.cs:100`). Node memory is a linear cursor over nodes in compile order
(`ReferenceCompiler.cs:564-637`). **A bigger tree is sized automatically; nothing is per tree
shape.**

### 2.6 Hot reload

Hot reload matches old and new programs by authoring `NodeId` from the debug map
(`ADR-P5-001-hot-reload-compatibility-model.md:43-62`;
`Runtime/HotReload/Identity/HotReloadProgramIdentityMap.cs:44-51`, last-wins on duplicates;
`HotReloadCompatibilityClassifier.cs:37-118`; native `NativeHotReloadStateMigration.cs:90-96`,
`:141-176`). Any expansion therefore must give every inner node an ID that is **unique and stable
across recompiles**.

### 2.7 Trace, active path, debugging

- Trace records carry the runtime node index (and optional debug identity index), never a string
  (`Runtime/Execution/Reference/Core/ReferenceTrace.cs:28-60`;
  `Runtime/Trace/Native/NativeTraceContractsV1.cs:66-92`).
- `TreeGroup.TryGetActivePath` returns `NodeIndex` and `StableNodeId` = FNV-1a of the authoring
  ID (`Runtime/Simulation/TreeGroup.Debug.cs:218-251`, `:487-501`;
  `SimulationDebugTypes.cs:205-222`). `DescribeAgent` labels nodes by display name, else authoring
  ID (`TreeGroup.Debug.cs:425-433`), surfaced by MCP `describe_agent`
  (`MCP/McpSimulationTools.cs:42-80`).
- The graph debugger maps runtime index → `DebugMap.AuthoringNodeId` → graph node
  (`Editor/Graph/GraphLiveDebugger.cs:272-304`). It attaches to a native program only if
  `MatchesProgram` holds: program semantic hash equals the open document's, **and** the debug-map ID
  set equals the document's node ID set (`Editor/Graph/BehaviorTreeGraphExecution.cs:213-225`).
- Golden behaviour cases identify nodes by compiled `nodeIndex`
  (`Tests/Fixtures/Golden/Cases/patrol-react.aibtcase.json:31-41`), and reference their tree by
  path (`:5`).

### 2.8 Native bindings

Inputs, commands and actions are bound per node **type** and global binding ID, not per node:
`SimulationProgramMapV1.TryBuild` walks generated leaves in index order and deduplicates channels by
binding ID (`Runtime/Simulation/SimulationProgramMapV1.cs:107-220`); `[AibtInput(id)]` names a
channel (`Runtime/Nodes/Contracts/NativeNodeShortForms.cs:99-110`). Completion channels are per
node index (`SimulationProgramMapV1.cs:69`, `:223`). Two copies of the same leaf in one program
already work today.

### 2.9 Import, assets, hosts

- `BehaviorTreeImporter` is `[ScriptedImporter(2, "aibt")]` (`Editor/Graph/BehaviorTreeImporter.cs:17`);
  `.aibt.json` files are plain TextAssets. The importer produces a Player-loadable
  `BehaviorTreeSource` holding the JSON (`:20-34`) and declares **no dependencies** (no
  `DependsOnSourceAsset` anywhere in the package).
- `BehaviorTreeSource.TryGetProgram` compiles lazily at runtime (`Agents/BehaviorTreeSource.cs:82-98`);
  `BehaviorTreeAgent` uses it or a raw `TextAsset` (`Agents/BehaviorTreeAgent.cs:39-42`,
  `:438-450`) and compiles via `ManagedTreeProgram.TryCompile`, which binds leaves through
  `documentNodes[entry.AuthoringNodeId]` (`Agents/Runtime/ManagedTreeProgram.cs:84-96`) — it needs
  the *same* document the program was compiled from.
- `ProductionTreeAsset` is baked only by an explicit action
  (`Editor/Graph/ProductionTreeAssetBaker.cs:19-44`); it stores one source GUID and the source
  file's hash (`:40-42`) and takes `_sourceId` from `DebugMap[0].SourcePath`
  (`Runtime/Integration/ProductionTreeAsset.cs:52`). Staleness and re-bake run from an asset
  postprocessor that matches the *source GUID only* (`Editor/Graph/ProductionTreeAssetStaleness.cs:54-87`,
  `:106-191`).
- `NativeTreeAgent` (`Runtime/Integration/NativeTreeAgent.cs:36`, `:184`), `ProductionTreeHost`
  (`ProductionTreeHost.cs:195-241`) and `TreeSimulation`/`TreeGroup`
  (`Runtime/Simulation/TreeSimulation.cs:90`, `TreeGroup.cs:48`) receive a finished program.
- There is no tree-ID → asset registry (`MCP/AibtTreeDiscovery.cs:8-12`). MCP/CLI address trees by
  `treeId` by scanning files (`AibtTreeDiscovery.cs:34-90`; duplicates make the lookup fail,
  `:34-47`); ADR-P7-042 calls duplicate tree IDs ambiguous (`ADR-P7-042-tree-source-assets.md:9-10`).
- No automatic path reloads live agents on a file change; hot reload is an explicit Editor workflow
  (`Editor/HotReload/HotReloadWorkflowWindow.cs:12-19`).

### 2.10 Graph editor

One tree per window, no tabs (`Editor/Graph/BehaviorTreeGraphWindow.cs:21`, `:72-88`);
`GraphEditSession` wraps one `TreeDocument` (`Editor/Graph/GraphEditSession.cs:10-38`). No
collapse, breadcrumbs or tabs exist; breadcrumbs/navigation are allowed as local view state
(`editor-layout-v1.md:160-175`). The palette lists registry manifests (`Editor/Graph/NodeSearchProvider.cs:30-62`).

## 3. Design options

### (a) Authoring-time inlining (expansion) — recommended

A reference node is replaced, before validation of the result and compilation, by a renamed copy
of the subtree. Executors, ABI, compiled format and trace see an ordinary tree.

| Topic | How it works |
|---|---|
| Semantics | Exactly as if the subtree's nodes were pasted at the reference node. Activation, abort, reactive guards, observers whose owning composite is above the reference, timeouts, parallel — all follow from the existing specification on the expanded tree. No new runtime rule. |
| Memory lifetime | Each instance has its own node memory and frames, laid out by the existing cursor (§2.5). Two instances never share state. |
| Repeated use | Allowed: N references give N renamed copies. Cost: program size and per-agent memory grow linearly with the number of instances (same as hand copies today). |
| Recursion | Forbidden. The expander walks the reference graph by tree ID; a cycle is an error naming the cycle (`AIBT3022`). |
| Limits | Fixed nesting depth 8 (a constant of the expansion contract, *not* a policy field, so no policy hash changes, §2.4). The existing `MaxNodesPerTree`/`MaxTreeDepth` policy applies to the expanded tree. Qualified IDs must fit 128 characters (`AIBT3027`). |
| Node identity | Inner node `n` of the instance at reference node `r` gets ID `r:n`; nested, `r1:r2:n`. Reference node IDs may not contain `:` (`AIBT2047`), so the first segment is always the outermost reference. Uniqueness of the whole expanded ID set is checked (`AIBT3027`). IDs are stable across recompiles as long as `r` and `n` are, so hot reload, debug map and `StableNodeId` work unchanged. Debug-map `SourcePath` of inner nodes is the subtree's source ID (the field already exists per entry, §2.4). |
| Trace / golden cases | Trace format unchanged (indices). New golden cases are written against the expanded tree's indices. Existing cases are untouched because a tree without references expands to itself (identity rule, §4.5). |
| Hot reload | Unchanged mechanism on the expanded programs. Editing a subtree changes the nodes `r:*` in every parent; the classifier treats them like ordinary edits (migrate/new/dropped/incompatible). Re-pointing a reference to another tree drops all `r:*` and adds new ones (a localized restart under `r`'s parent). |
| Blackboard | §4.3: interface keys bind to parent Tree keys or constants; private subtree keys become per-instance keys `r:key`; Agent/Shared keys must match the parent's scope contracts exactly. |
| Parameters | Constants are bound to interface keys (`{ "value": … }`). Node parameters inside the subtree are not templated in this version (open question Q3). |
| Native bindings | Unchanged (§2.8): inner generated leaves keep their type and binding IDs; channels are shared per binding ID exactly like two hand-placed leaves; completion channels are per node index. |
| Assets | The expander needs a resolver (tree ID → document + source ID). The importer resolves references, stores the expanded JSON in `BehaviorTreeSource`, and declares `DependsOnSourceAsset` on each subtree file; `ProductionTreeAsset` records every source it was baked from and the staleness postprocessor re-bakes dependants (§5). |
| Content hash | The program's `CanonicalSemanticHash` becomes the semantic hash of the **expanded** document — identical to today for trees without references. A subtree edit changes the hash of every parent program, as it must. |
| Editor | Reference node drawn collapsed; double-click opens the subtree in its window with a breadcrumb; live debug maps `r:*` IDs to the reference node and, in the subtree window, to the inner nodes of that instance (§4.7). `MatchesProgram` compares against the expanded document. |
| Standing constraints | Execution semantics, ABI v2, compiled format and version, trace format, registry hash: **unchanged**. Tree authoring format: new **version 3** (additive). Public API: additive overloads only. Specs: changes listed in §4.8, by this ADR. |

### (b) Runtime call node that enters another compiled program

A built-in `aibt.core.subtree` node holds a reference to a second compiled program; executors push
a frame into that program.

| Topic | Effect |
|---|---|
| Compiled format | A program must name other programs (ID + expected hash), and records/indices become pairs (program, node). Header and record contracts change → new compiled format version (`compiled-program-v1.md:11-43`). Violates a standing constraint. |
| Executors | Both executors and their equivalence suite learn cross-program frames, abort propagation across the boundary, per-program memory arenas and observer queues that cross programs (an observer's owning reactive composite may be in the caller). The native arena is sized from one program's counts (§2.5); it would need a capacity plan per call graph. Large (L+) and risky in the most tested code. |
| Trace | A node index alone no longer identifies a node; records need a program identity → trace format change. Golden cases need it too. |
| ABI v2 | Not touched by leaves, but `BurstNodeAbortReason` and frame semantics are shared; any new abort reason touches `ExpectedPublicAbiV2.txt:269-283`. |
| Registry | A new `aibt.core.` type changes the P7-028 authority and the registry hash of every program (precedent: ADR-P10-013 §3(b)). |
| Hot reload | Per-program reload; a subtree change reloads every caller instance; identity map becomes two-level. |
| Benefits | One copy of the subtree's records per program set (smaller assets); recursion becomes possible; a subtree could be re-baked without its callers. Per-instance memory is still needed, so per-agent memory is not smaller. |
| Verdict | Breaks the compiled format, trace format and executor equivalence for a gain (program size) that no current user needs. Rejected. Could be revisited if program size of heavy reuse is ever measured to matter. |

### (c) Copy-on-insert templates (no live link)

Inserting a "template" pastes a renamed copy; no reference remains.

| Topic | Effect |
|---|---|
| Everything at runtime | Unchanged; nothing new reaches compilers. |
| Edit once | Not possible — the main user story fails. Copies drift. |
| Existing support | Mostly exists: editor "duplicate subtree" with ID remapping (`GraphDocumentEdits.cs:74-112`) and MCP extract/inline (`McpAuthoringOperations.cs:76-120`). |
| Verdict | Not a subtree feature. Kept as the "Unpack subtree" command of option (a) (one-way: reference → plain copy). |

### Recommendation

Option (a). It is the only option that delivers "edit once, use everywhere" without touching
execution semantics, ABI v2, the compiled program format or the trace format. The executors are
already proven equivalent on arbitrary trees; an expanded tree is an arbitrary tree, so equivalence
carries over. Every runtime and debugging facility keyed by authoring node ID (hot reload, debug
map, active path, `describe_agent`) keeps working once inner nodes get qualified IDs, and the only
code that must learn about subtrees is upstream of the compiler (reader/writer, validator, a new
expander, importer/baker dependency tracking) plus the editor and MCP.

## 4. Proposed design (option a)

### 4.1 Tree format version 3

Version 3 is version 2 plus two optional properties: a top-level `interface` and a node-level
`subtree`. Version 1 and 2 bytes keep their meaning; tools never upgrade a file to 3 unless the
author adds one of the two properties. The canonical writer writes `interface` immediately before
`blackboardContracts` (or before `blackboard` if absent).

### 4.2 Example documents

Subtree `Trees/combat-attack.aibt`:

```json
{
  "format": "aibt.tree",
  "formatVersion": 3,
  "treeId": "combat.attack",
  "name": "Attack",
  "interface": {
    "version": 1,
    "keys": ["range", "target"]
  },
  "root": "attack-seq",
  "blackboard": {
    "range": { "type": "Float32", "default": 2 },
    "shots": { "type": "Int32", "default": 0 },
    "target": { "type": "EntityId" }
  },
  "nodes": {
    "attack-seq": { "type": "aibt.core.memory-sequence", "typeVersion": 1, "children": ["in-range", "fire"] },
    "fire": { "type": "game.fire-at", "typeVersion": 1, "bindings": { "game.fire-at.target": "target", "game.fire-at.count": "shots" } },
    "in-range": { "type": "game.in-range", "typeVersion": 1, "bindings": { "game.in-range.target": "target", "game.in-range.range": "range" } }
  }
}
```

Parent `Trees/guard.aibt`:

```json
{
  "format": "aibt.tree",
  "formatVersion": 3,
  "treeId": "guard",
  "name": "Guard",
  "root": "main",
  "blackboard": {
    "boss": { "type": "EntityId" },
    "enemy": { "type": "EntityId" }
  },
  "nodes": {
    "main": { "type": "aibt.core.reactive-selector-retaining", "typeVersion": 1, "children": ["hit-boss", "hit-enemy", "patrol"] },
    "hit-boss": {
      "displayName": "Attack boss",
      "subtree": { "treeId": "combat.attack", "interfaceVersion": 1, "keys": { "range": { "value": 8 }, "target": "boss" } }
    },
    "hit-enemy": {
      "subtree": { "treeId": "combat.attack", "interfaceVersion": 1, "keys": { "range": { "value": 2 }, "target": "enemy" } }
    },
    "patrol": { "type": "game.patrol", "typeVersion": 1 }
  }
}
```

Expanded document (what the compiler sees; format version 2, never written to the project, but
returned by `inspect_tree --expanded`):

```json
{
  "format": "aibt.tree",
  "formatVersion": 2,
  "treeId": "guard",
  "name": "Guard",
  "root": "main",
  "blackboard": {
    "boss": { "type": "EntityId" },
    "enemy": { "type": "EntityId" },
    "hit-boss:range": { "type": "Float32", "default": 8 },
    "hit-boss:shots": { "type": "Int32", "default": 0 },
    "hit-enemy:range": { "type": "Float32", "default": 2 },
    "hit-enemy:shots": { "type": "Int32", "default": 0 }
  },
  "nodes": {
    "hit-boss:attack-seq": { "displayName": "Attack boss / attack-seq", "type": "aibt.core.memory-sequence", "typeVersion": 1, "children": ["hit-boss:in-range", "hit-boss:fire"] },
    "hit-boss:fire": { "type": "game.fire-at", "typeVersion": 1, "bindings": { "game.fire-at.target": "boss", "game.fire-at.count": "hit-boss:shots" } },
    "hit-boss:in-range": { "type": "game.in-range", "typeVersion": 1, "bindings": { "game.in-range.target": "boss", "game.in-range.range": "hit-boss:range" } },
    "hit-enemy:attack-seq": { "type": "aibt.core.memory-sequence", "typeVersion": 1, "children": ["hit-enemy:in-range", "hit-enemy:fire"] },
    "hit-enemy:fire": { "type": "game.fire-at", "typeVersion": 1, "bindings": { "game.fire-at.target": "enemy", "game.fire-at.count": "hit-enemy:shots" } },
    "hit-enemy:in-range": { "type": "game.in-range", "typeVersion": 1, "bindings": { "game.in-range.target": "enemy", "game.in-range.range": "hit-enemy:range" } },
    "main": { "type": "aibt.core.reactive-selector-retaining", "typeVersion": 1, "children": ["hit-boss:attack-seq", "hit-enemy:attack-seq", "patrol"] },
    "patrol": { "type": "game.patrol", "typeVersion": 1 }
  }
}
```

(Display names are shown for illustration; the exact label rule is in §4.4.)

### 4.3 Blackboard interface

- A subtree's `interface.keys` lists Tree-scope keys of the subtree that every caller must
  connect. `interface.version` is a positive integer the author raises when the interface changes
  incompatibly (keys added/removed/retyped). A reference states the `interfaceVersion` it was
  written for (data-formats.md:50, "External subtree … references include expected contract
  versions"); a mismatch is `AIBT3024`.
- A reference's `keys` object has exactly one entry per interface key (`AIBT3025` for missing or
  unknown). An entry is either a parent key ID (string) — the parent key must be Tree scope and
  have the identical type, type version and enum contract (`AIBT2048` in the parent alone,
  `AIBT3026` across documents) — or a constant `{ "value": <canonical typed value> }` checked
  against the interface key's type (`AIBT3026`).
- Expansion: an interface key bound to a parent key is renamed to that key everywhere in the
  instance; one bound to a constant becomes a private key `r:key` with the constant as default.
  Every other Tree key of the subtree becomes a private per-instance key `r:key` with its own
  default. Writes inside the subtree to a parent-bound key are visible to the parent (in-out); to a
  constant-bound key they stay inside the instance.
- Renaming covers all four access paths of §2.3: manifest fixed-name accesses are not renamable,
  so a subtree whose node uses a fixed-name access to a key that is not bound to a same-named parent
  key is rejected (`AIBT3026`, message names the node); `BlackboardKey` parameters, generated
  `bindings` values and observer `watchedKeys` are renamed.
- Agent and Shared keys are not part of the interface. The subtree's `blackboardContracts` must be
  identical (contract ID and version) to the parent's for each scope it uses, and its Agent/Shared
  keys are copied by identity (`AIBT3028` otherwise).
- A subtree compiled on its own (as a normal tree) uses its interface keys' defaults; the
  interface is ignored by the compiler.

### 4.4 Expansion algorithm (normative draft in §4.8)

1. Read and validate the parent alone (single-document rules, §4.6). Resolve each reference's
   `treeId` with the resolver; read and validate the subtree alone.
2. Recursively expand subtrees first (depth-first, stack of tree IDs for cycle detection, depth ≤ 8).
3. For each reference node `r` in the parent (in ordinal ID order): copy the expanded subtree's
   nodes, prefixing every node ID with `r:`; rename keys per §4.3; set the copied root in place of
   `r` in its parent's `children`; drop `r`. Inner display labels become
   `"<r label> / <inner label>"`, where a label is the display name or else the ID.
4. Merge blackboards (§4.3) and check the expanded ID sets (`AIBT3027`).
5. The result is a format-2 document (format 1 if every input is format 1 and it uses no v2
   feature) with the parent's `treeId`, `name`, `root`, `tags`; `interface` is dropped. Compile it
   as today. Debug-map entries of inner nodes carry the subtree's source ID as `SourcePath`.

**Identity rule:** a document with no reference node and no `interface` is returned unchanged (the
same object), so every existing tree compiles to byte-identical programs and hashes.

### 4.5 Hashes

- Document semantic hash (per file): unchanged definition; for a v3 file it includes `interface`
  and `subtree` objects.
- Program `CanonicalSemanticHash`: semantic hash of the expanded document. Equal to today's value
  for every tree without references (identity rule).
- Registry hash, policy hash: unchanged (no new node type, no new policy field).
- `ProductionTreeAsset` staleness: hash over the parent file and every resolved subtree file
  (ordered by tree ID), plus their GUIDs.

### 4.6 Validation rules and diagnostic codes

All codes are unused today (checked across code, docs and planning). Single-document rules belong
to `TreeValidator` (`AIBT2xxx`); rules that need the resolver belong to the new expander, in the
compiler range (`AIBT30xx`).

| Code | Severity | Rule |
|---|---|---|
| `AIBT2047` SubtreeReferenceShape | Error | A node with `subtree` has no `type`, `typeVersion`, `parameters`, `bindings`, `observer` or `children`; its ID contains no `:`; `treeId` is a valid tree ID; `interfaceVersion` ≥ 1. (Schema-level shape errors still report `AIBT1004`.) |
| `AIBT2048` SubtreeKeyTarget | Error | A string entry in `subtree.keys` names a declared Tree-scope key of the parent. |
| `AIBT2049` SubtreeReferenceRoot | Error | The root is not a reference node (keeps `ProductionTreeAsset._sourceId`, taken from `DebugMap[0]`, pointing at the parent, §2.9). |
| `AIBT2050` SubtreeInterface | Error | Every `interface.keys` entry is a declared Tree-scope key; no duplicates; `version` ≥ 1. |
| `AIBT3020` SubtreeNotFound | Error | No document has the referenced tree ID, or no resolver was given and the tree has references. |
| `AIBT3021` SubtreeAmbiguous | Error | More than one document has the referenced tree ID (related locations list them). |
| `AIBT3022` SubtreeCycle | Error | The reference graph has a cycle (message lists it). |
| `AIBT3023` SubtreeDepth | Error | Nesting deeper than 8. |
| `AIBT3024` SubtreeInterfaceVersion | Error | `interfaceVersion` differs from the subtree's `interface.version`, or the subtree has no interface but the reference binds keys. |
| `AIBT3025` SubtreeKeySet | Error | Missing or unknown entries in `subtree.keys`. |
| `AIBT3026` SubtreeKeyType | Error | Type/version/enum mismatch of a binding or constant; a fixed-name manifest access that cannot be renamed. |
| `AIBT3027` SubtreeQualifiedIdentity | Error | An expanded node or key ID exceeds 128 characters or collides with another (including a parent ID that starts with `r:`). |
| `AIBT3028` SubtreeScopeContract | Error | Subtree's Agent/Shared scope contract differs from or is missing in the parent. |

Diagnostics raised inside a subtree carry the subtree's `documentId` and a related location at the
reference node, so the editor can show them on both.

### 4.7 Editor, MCP/CLI, debugging

- **Graph.** The palette gets one "Subtree…" entry opening a tree picker (same list as
  `BehaviorTreeGraphWindow.Chrome.cs:446-451`). A reference node is drawn as one node with the
  subtree's name, a subtree badge and a row per interface key; the inspector edits the key map,
  pre-filling same-named parent keys and offering to create missing ones. Double-click opens the
  subtree's window; a breadcrumb (local view state, allowed by `editor-layout-v1.md:160-175`)
  leads back. Commands: "Extract to subtree", "Unpack subtree". No layout format change: the
  reference node is an ordinary node in the parent's layout; inner nodes use the subtree's own
  layout file.
- **Live debug.** `MatchesProgram` compares with the expanded document. In the parent, an active
  `r:*` node highlights `r`. A subtree window opened from a live parent carries the instance
  prefix (`r:` or `r1:r2:`) and highlights `prefix + id`. Trace timeline shows the two-part label.
- **MCP/CLI.** `add_node` accepts a `subtree` object; new `set_subtree_keys`,
  `extract_to_subtree` (new file + reference; reuses `CaptureSubtree`), `unpack_subtree`,
  `find_subtree_usages`; `inspect_tree` gets `expanded`; `get_project_manifest` lists each
  tree's `interface` and referenced tree IDs; `validate`/`compile`/`prepare_runtime` resolve
  through `AibtTreeDiscovery`. CLI: `aibt tree expand <treeId>`, `aibt tree usages <treeId>`.
  `describe_agent` needs no change (labels and stable IDs come from the debug map).

### 4.8 Normative text drafts (applied only on acceptance)

**`canonical-json-v1.md`, new section "Format version 3 (subtrees, ADR-P11-001)":**

> Format version 3 is additive over version 2. It adds an optional top-level `interface` object,
> written immediately before `blackboardContracts` (or `blackboard`), with ordered properties
> `version` (unsigned 32-bit integer ≥ 1) and `keys` (array of Tree-scope blackboard key IDs,
> written in ascending ordinal order, unique). It adds an optional node property `subtree`; a node
> with `subtree` has no `type`, `typeVersion`, `parameters`, `bindings`, `observer` or `children`.
> `subtree` has ordered properties `treeId`, `interfaceVersion` (unsigned 32-bit ≥ 1) and `keys`, a
> map ordered by key ID whose values are a parent blackboard key ID or an object `{ "value": v }`
> with a canonical typed value. Both properties take part in the semantic hash. Version 1 and 2
> documents are never reinterpreted.

**`identity-and-hashing-v1.md`, new section "Expanded identities":**

> Expanding a subtree reference node `r` gives each inner node and private key the ID `r:` followed
> by its ID in the expanded subtree. Reference node IDs do not contain `:`. Expanded IDs follow the
> authoring grammar; an expansion that would exceed 128 characters or produce a duplicate fails.
> Expanded IDs are stable as long as the reference node ID and inner IDs are stable.

**`compiled-program-v1.md`, "Header", replace "canonical semantic hash" bullet:**

> - canonical semantic hash of the compiled document; for a tree with subtree references this is the
>   semantic hash of its expansion (ADR-P11-001). A tree without references is its own expansion.

and in "Debug and hot-reload map": "Each entry's source path is the source of the document the
node came from; inner nodes of an expanded subtree carry the subtree's source path."

**`blackboard-v1.md`, new section "Subtree interface":** the rules of §4.3, first four bullets.

**`execution-semantics-v1.md`, new paragraph:**

> A subtree reference has no runtime behaviour of its own. A tree with references executes exactly
> as its expansion (ADR-P11-001); no executor, trace record or node ABI observes a reference.

**`diagnostics-v1.md` / catalogs:** the thirteen codes of §4.6.

**`data-formats.md`** (non-normative): replace "subtree references" with a pointer to this ADR;
**`editor-layout-v1.md:145`**: remove "or a documented subtree-reference edge" (references are
nodes, not edges).

## 5. Impact by area

| Area | Files (main) | Size |
|---|---|---|
| Tree model | `TreeDocument.cs` (interface, `LatestFormatVersion = 3`), `NodeDocument.cs` (subtree reference), new `SubtreeReference`, `SubtreeInterface` types | M |
| Schema and JSON | `Schemas~/tree.schema.json`, `CanonicalTreeJson.cs` (allow-lists, v3), `CanonicalTreeJsonWriter.cs`, canonical fixtures | M |
| Validator | `TreeValidator.cs` (skip type checks for reference nodes, four new rules), `TreeValidationDiagnostics.cs` (`Codes` array) | M |
| Expander (new) | `Authoring/Compilation/Subtrees/` — resolver interface, directory and in-memory resolvers, `TreeExpansion`, diagnostics `AIBT3020-3028` | L |
| Compile entry points | additive overloads with a resolver: `ReferenceCompiler`, `GeneratedCompiledProgramV2Compiler`, `ManagedTreeProgram.TryCompile`, `BehaviorTreeSource` (stores expanded JSON) | M |
| Import / bake | `BehaviorTreeImporter.cs` (version 3, `DependsOnSourceAsset`, tree-ID index custom dependency), `TreeCatalogEntry.cs`, `ProductionTreeAssetBaker.cs`, `ProductionTreeAsset.cs` (additive serialized dependency list), `ProductionTreeAssetStaleness.cs` (re-bake dependants) | M |
| Editor | `NodeSearchProvider.cs`, graph view node for references, inspector, `BehaviorTreeGraphWindow*` (open/breadcrumb, extract/unpack), `GraphLiveDebugger.cs`, `BehaviorTreeGraphExecution.MatchesProgram`, `TraceTimelineWindow.cs` | L |
| MCP / CLI | `McpOperationCatalog.cs`, `McpAuthoringToolDispatcher.cs`, `McpAuthoringOperations.cs`, `ProjectManifestQuery.cs`, `MCP~/Server/*`, `MCP~/Cli/Program.cs`, `Documentation~/cli.md` | M |
| Behaviour cases | `BehaviorCaseJson.cs` / case runner: resolve references relative to the case's tree directory | S |
| Specs and docs | §4.8 list; `Documentation~/generated` unchanged (no new node types); user guide page | S |
| Tests | see §7 | L |

**Untouched:** both executors (`Runtime/Execution/**`), native compiled contracts
(`Runtime/Compiled/**`), `ExpectedPublicAbiV2.txt`, code generator (`CodeGen~`), node registry and
P7-028 authority, trace contracts, hot-reload code (`Runtime/HotReload/**`,
`Runtime/Execution/Native/HotReload/**`), simulation container (`Runtime/Simulation/**`),
`NativeTreeAgent`, `ProductionTreeHost`, existing golden cases and benchmarks.

## 6. Effect on the standing constraints

| Constraint | Effect |
|---|---|
| Execution semantics of existing nodes | None. |
| ABI v2 (`ExpectedPublicAbiV2.txt`) | None. |
| Compiled program format and version | None (`CompiledFormatVersion` 1/2 unchanged). The *meaning* of the header semantic hash is restated for trees with references (spec text, §4.8); unchanged for all others. |
| Trace format | None. |
| Package version | None by this ADR. |
| Existing public APIs | Unchanged; only additive overloads/types. Old overloads given a document with references fail with `AIBT3020` (they cannot resolve), never silently compile something else. |
| `.aibt.json` format | **New version 3, additive** — a persisted format change; needs the owner's approval (Q1) per `data-formats.md:11`. |
| Normative specs | Changed by this ADR on acceptance (§4.8). |
| `aibt.core.*` namespace | Not touched (no new node type). |

## 7. Implementation plan

Each stage ends usable and testable and has its own commit(s) and evidence folder
`Planning~/Evidence/P11-001/`. Per the owner's standing rule, stages that change something visible
end with screenshots and a short video.

### Stage 1 — model, validator, expander, managed path (M–L)

Deliverables: v3 model/reader/writer/schema; validator rules `AIBT2047-2050`; expander with
directory and in-memory resolvers and `AIBT3020-3028`; resolver overloads of the reference compiler
and `ManagedTreeProgram`; `BehaviorTreeSource` stores the expanded document when created by the
importer; behaviour-case runner resolves references; spec edits of §4.8.

Acceptance and tests:
- Identity: every existing fixture tree (`Tests/Fixtures/**`, samples) expands to itself and
  compiles to a byte-identical compiled-content hash (`CompiledContentHasher`) — proves no change
  for existing users.
- Equivalence of hand copy: a tree with two references compiles to the same program as the
  hand-written expanded document (same compiled-content hash).
- Each diagnostic has a negative test (missing, ambiguous, cycle of 2 and 3, depth 9, version
  mismatch, key set, key type, fixed-name access, qualified-ID overflow and collision, scope
  contract, root reference).
- Canonical round-trip of v3 documents; v1/v2 bytes unchanged.
- Two new golden behaviour cases (subtree under a reactive composite with an observer owned
  above the reference; same subtree twice with private keys kept apart).
- Reference-executor hot reload: editing a subtree's leaf parameter migrates both instances;
  retargeting a reference restarts only that branch.
- `BehaviorTreeAgent` runs a tree with subtrees in Play Mode.
- Dotnet harness (outside Unity) runs the model/expander tests.

### Stage 2 — native path, import and baking (M)

Deliverables: generated-V2 compiler overload; importer dependencies and tree-ID index;
`ProductionTreeAsset` dependency list and staleness re-bake of dependants.

Acceptance and tests: native/reference equivalence on the stage-1 trees (existing equivalence
harness, no executor change); `NativeTreeAgent` and a `TreeSimulation` group with inputs and an
external action inside a subtree used twice (channels shared, completions per instance); editing
the subtree file re-imports the parent `BehaviorTreeSource` and marks/re-bakes the
`ProductionTreeAsset`; adding a second file with the same tree ID turns dependants into
`AIBT3021` errors; native hot reload of an edited subtree (same expectations as stage 1).

### Stage 3 — graph editor (L)

Deliverables: palette entry and picker, reference node view, key-map inspector, open/breadcrumb,
"Extract to subtree", "Unpack subtree", diagnostics on reference nodes, live-debug mapping in
parent and subtree windows, `MatchesProgram` on expanded documents.

Acceptance: EditMode tests for the edit operations (undo/redo through `SemanticEditHistory`);
window screenshots (`Save-UnityWindow.ps1`) of a parent and an opened subtree; a Play Mode video
showing the reference node and the inner nodes highlighted for the right instance.

### Stage 4 — MCP, CLI, documentation (S–M)

Deliverables: the tools of §4.7, CLI commands, manifest fields, user guide page "Subtrees".

Acceptance: dispatcher tests per tool including permission negatives; live CLI session that
extracts a subtree, rebinds a key, expands and validates; `describe_agent` on a simulation agent
inside a subtree shows two-part labels.

### Risks and mitigations

| Risk | Mitigation |
|---|---|
| Qualified IDs grow past 128 characters in deep nesting | Depth ≤ 8; `AIBT3027` with the offending path; editor generates short reference IDs. |
| Program size and per-agent memory grow with heavy reuse | Same as hand copies today; measure in stage 2 with a "same subtree ×16" benchmark; option (b) stays documented if it ever matters. |
| Stale parents after a subtree edit (missed dependency) | Importer declares source dependencies and a tree-ID index dependency; staleness covers all sources; a test edits the subtree and checks the parent. |
| Duplicate tree IDs in a project | Hard error `AIBT3021` instead of picking one (matches ADR-P7-042). |
| Runtime sources (`FromJson`, raw `TextAsset`) cannot resolve | Clear `AIBT3020`; optional resolver overload (Q5). |
| Editor debug mismatch | `MatchesProgram` on the expanded document; test attaches to a program built from subtrees. |
| Conflicts with parallel sessions (P10-014 in native runtime) | This feature does not touch `Runtime/Execution/**` or hot-reload code. |

## 8. Open questions for the owner

1. **New `.aibt.json` format version 3.** Subtrees need two new properties, and today's reader
   rejects any unknown property, so files that use subtrees must say `formatVersion: 3`. Old files
   stay as they are and keep working. *Recommendation: approve.*
2. **How a reference names its subtree: by tree ID or by Unity asset GUID.** Tree ID works in the
   Editor, CLI, MCP, tests outside Unity and the Player (MCP already addresses trees by ID); GUID
   survives renaming a tree ID but exists only inside Unity. *Recommendation: tree ID; duplicates
   are an error.*
3. **Parameters.** In this version a caller passes values only through interface keys (connect a
   key or give a constant). Writing "wait N ticks where N comes from the caller" requires the node
   to read N from a key. Templating node parameters directly (`"ticks": {"$param": "n"}`) is a later
   extension. *Recommendation: keys only now; parameters as a follow-up if authors ask.*
4. **Recursion and depth.** A subtree may not use itself, directly or indirectly; nesting depth is
   limited to 8. *Recommendation: approve both; depth is a fixed constant, not a policy setting,
   because a new policy field would change every compiled program's policy hash.*
5. **Trees loaded from raw JSON at runtime** (`BehaviorTreeSource.FromJson`, the `TextAsset` field of
   `BehaviorTreeAgent`) have no project to look subtrees up in. *Recommendation: they report
   `AIBT3020` unless the caller passes a resolver through a new overload; imported `.aibt` assets
   already contain the expanded tree.*
6. **Editor: open the subtree in its own window vs. expand it in place inside the parent graph.**
   *Recommendation: own window with breadcrumb first (stage 3); in-place read-only expansion later
   if wanted.*
7. **Private subtree keys are per instance.** Two uses of the same subtree in one tree never share
   private keys (`hit-boss:shots` vs `hit-enemy:shots`); to share, connect both to one parent key.
   *Recommendation: approve.*
8. **Interface keys bind only to parent Tree keys**, not Agent/Shared keys (those are shared by
   contract identity instead). *Recommendation: approve; revisit if needed.*

## Owner decisions (2026-11-19)

The owner accepted every recommendation of §8:

1. Tree format version 3, additive over version 2; version 1 and 2 files are read as before.
2. A reference names its subtree by tree ID; duplicate tree IDs are an error (`AIBT3021`).
3. A caller passes values only through interface keys (a parent key or a constant). Templated node
   parameters are not part of this decision.
4. A subtree may not use itself, directly or indirectly (`AIBT3022`); nesting depth is at most 8
   (`AIBT3023`), a fixed constant and not a policy field.
5. A tree loaded from raw JSON at runtime reports `AIBT3020` unless the caller passes a resolver
   through a new overload; imported `.aibt` assets hold the expanded tree.
6. The editor opens a subtree in its own window with a breadcrumb (stage 3); in-place expansion is
   not part of this decision.
7. Private subtree keys are per instance.
8. Interface keys bind only to the parent's Tree-scope keys.

9. Added 2026-11-19 after stage 1: the values of a node's generated `bindings` map (blackboard
   key IDs) may use the authoring ID grammar of blackboard keys, so that a generated binding can
   name an expanded private key `r:key`. Binding IDs (the map's property names) keep the canonical
   member identity grammar. `agent-shared-blackboard-v1.md`, the tree schema and `NodeBindingMap`
   change accordingly in stage 2; existing documents keep their meaning.

Stage 1 of §7 was approved on 2026-11-19 and accepted the same day (evidence, "Acceptance run").
Stage 2 was approved on 2026-11-19 and accepted the same day (`c02b42b`). Stage 3 was approved on
2026-11-19 together with two follow-ups of stage 2: a tree with subtrees whose node types are not
in the project catalog imports with the status `Needs registry` (as a tree without subtrees does),
and the Editor's tree-ID index also covers trees under `Packages/`. Stage 3 was accepted the same
day (`7794cb0`). Stage 4 (MCP, CLI, user guide) was approved on 2026-11-19; with it the stage may
change `MCP/**`, `MCP~/Server/**`, `MCP~/Cli/**` and the user documentation. Stage 4 was accepted
the same day (`6b7e141`). Found in stage 4: a project `blackboardNaming` policy rejected expanded
private keys `r:key` (`AIBT2038`) in MCP validate and compile.

10. Added 2026-11-19 after stage 4: the project `blackboardNaming` policy applies to the part of a
    blackboard key name after the last `:`, so an expanded private key `r:key` follows the policy
    when `key` does. Names without `:` are checked as before (`34e80f0`).

## 9. Noticed while reading (not part of F1)

- Diagnostic codes `AIBT2042` and `AIBT2043` are defined twice with different meanings: validator
  `MigrationApplied`/`TreeScopeSlotNeverWritten` (`Authoring/Validation/TreeValidationDiagnostics.cs:47`,
  `:55`) and compiler `MissingScopeContract`/`ScopeContractMismatch`
  (`Authoring/Compilation/ReferenceCompilerDiagnostics.cs:17-18`). `diagnostics-v1.md:34` requires
  one catalog entry per code. Resolved by P11-008 (review F11): the validator's codes, the later
  ones, became `AIBT2052` and `AIBT2053`.
- `HotReloadProgramIdentityMap.Build` silently keeps the last entry if two debug-map entries share an
  ID (`Runtime/HotReload/Identity/HotReloadProgramIdentityMap.cs:44-51`); `CompiledProgram`
  validation does not check debug-map ID uniqueness (`Runtime/Compiled/Model/CompiledProgram.cs:325-350`).
  Harmless today (IDs are unique per document); the expander guarantees uniqueness, but a check
  would be cheap.
- ADR-P7-042 says the importer produces Editor metadata only (`ADR-P7-042-tree-source-assets.md:12-13`);
  the importer now produces the Player-loadable `BehaviorTreeSource` (`BehaviorTreeImporter.cs:20-34`).
