# Subtrees

A subtree is an ordinary tree file used as one node of another tree. The parent tree holds a
**reference node** that names the subtree by its tree ID; the subtree file stays the only copy of
its nodes. Change the subtree once and every tree that uses it gets the change at its next import,
compile or preparation.

The design and its rules are in [ADR-P11-001](decisions/ADR-P11-001-subtrees.md). This page is the
user guide.

## How it works

Subtrees exist only while authoring. Before a tree is compiled, each reference node is replaced by
a copy of the subtree's nodes (the **expansion**). The compiler, both executors, the compiled
program, traces and hot reload see one ordinary tree. There is no call node at run time and no
per-reference cost beyond the copied nodes themselves.

For a reference node with ID `r`:

- every node `n` of the subtree becomes the node `r:n` in the expanded tree, with the label
  `"<reference label> / <node label>"` (a label is the display name, else the ID);
- every interface key connected to a parent key is renamed to that parent key;
- every other Tree key of the subtree, and an interface key bound to a constant, becomes a private
  key `r:key` of this instance, so two references to the same subtree do not share them.

A tree without reference nodes and without an `interface` is not touched: it imports, validates,
compiles and prepares exactly as before.

## Interface keys and constants

A subtree lists the Tree-scope keys a caller must supply in `interface.keys`, and gives the list a
version:

```json
"interface": { "version": 1, "keys": ["hits"] }
```

Raise `interface.version` when you add, remove or retype an interface key. A reference records the
`interfaceVersion` it was written for; a reference written for another version is an error
(`AIBT3024`) until it is updated.

A reference has exactly one entry per interface key in `subtree.keys`:

| Entry | Meaning |
|---|---|
| `"hits": "hits"` | Connected to the parent's Tree key `hits`. Same type, type version and enum contract required. Writes inside the subtree are visible to the parent. |
| `"hits": { "value": 5 }` | A constant. The instance gets a private key `r:hits` with this default; writes stay inside the instance. |

A subtree without an `interface` can still be used, but its references bind no keys. Agent- and
Shared-scope keys are never part of an interface; a subtree that uses them needs the same scope
contract as the parent (`AIBT3028`).

## Private keys

Every Tree key of the subtree that is not connected to a parent key belongs to one instance. Two
references `left` and `right` to the same subtree with a key `swings` get two keys, `left:swings`
and `right:swings`, each starting from the subtree's default. The parent cannot connect to them;
the live blackboard panel and traces show them under their `r:key` names.

## Example

A subtree `Trees/strike.aibt` that counts hits (interface key `hits`) and its own swings (private
key `swings`):

```json
{
  "format": "aibt.tree",
  "formatVersion": 3,
  "treeId": "game.strike",
  "name": "Strike",
  "description": "Wind up, hit, count the swing.",
  "interface": { "version": 1, "keys": ["hits"] },
  "root": "combo",
  "blackboard": {
    "hits": { "type": "Int32", "default": 0 },
    "swings": { "type": "Int32", "default": 0 }
  },
  "nodes": {
    "combo": { "type": "aibt.core.memory-sequence", "typeVersion": 1, "children": ["windup", "hit", "count"] },
    "count": { "displayName": "Count swing", "type": "aibt.unity.increment-int", "typeVersion": 1, "parameters": { "amount": 1, "key": "swings" } },
    "hit": { "displayName": "Hit", "type": "aibt.unity.increment-int", "typeVersion": 1, "parameters": { "amount": 1, "key": "hits" } },
    "windup": { "displayName": "Wind up", "type": "aibt.unity.wait-seconds", "typeVersion": 1, "parameters": { "seconds": 0.8 } }
  }
}
```

A parent `Trees/boss-fight.aibt` that uses it twice:

```json
{
  "format": "aibt.tree",
  "formatVersion": 3,
  "treeId": "game.boss-fight",
  "name": "Boss Fight",
  "root": "fight",
  "blackboard": { "hits": { "type": "Int32", "default": 0 } },
  "nodes": {
    "fight": { "type": "aibt.core.memory-sequence", "typeVersion": 1, "children": ["left", "right"] },
    "left": { "displayName": "Left strike", "subtree": { "treeId": "game.strike", "interfaceVersion": 1, "keys": { "hits": "hits" } } },
    "right": { "displayName": "Right strike", "subtree": { "treeId": "game.strike", "interfaceVersion": 1, "keys": { "hits": "hits" } } }
  }
}
```

The expansion has the nodes `fight`, `left:combo`, `left:windup`, `left:hit`, `left:count` and the
same four under `right:`; the keys `hits` (shared by both instances and the parent),
`left:swings` and `right:swings`. A reference node has no `type`, `typeVersion`, `parameters`,
`bindings`, `observer` or `children`. Adding the first reference raises a format 1 or 2 file to
format 3; files without subtree features keep their format. A format 3 file without reference
nodes and without an `interface` (after Unpack, or after its last reference was deleted) compiles
and prepares for the native runtime as the same tree in format 2, with the same semantic hash.

## Graph editor

- **Add**: "Subtree…" in the node palette, or "Add Subtree…" in the canvas context menu, opens a
  picker over the project's trees. The new reference gets a short ID from the subtree's name, and
  interface keys are connected to parent keys of the same name and type at once.
- **Reference card**: a SUBTREE badge, the subtree's name and tree ID, one row per interface key
  (`key → parent key`, `key = constant`, `key: not connected`). Problems of the expansion, also
  those located inside the subtree file, are shown on the card.
- **Inspector**: per interface key choose a parent key of the same type, a constant (Bool, Int32,
  Int64, Float32, Float64) or not connected; "Create parent key" adds a missing key and connects
  it; "Connect same-named keys"; Open Subtree; Unpack.
- **Open Subtree** (or double-click) opens the subtree in its own window with a breadcrumb back to
  the parent, for example `Boss Fight › Strike (instance left)`.
- **Extract to Subtree…** (context menu on a non-root node) moves the node and its descendants into
  a new tree and leaves a reference in their place; the interface is the Tree keys they use. The new
  file is written by the next Save, before the parent, so Undo never leaves a file behind.
- **Unpack Subtree** replaces a reference by a plain copy of its expansion (`r:n` nodes, `r:key`
  keys). A name collision is refused with `AIBT3027`.

See [Visual editor and layout](editor-and-layout.md) for the window itself.

## MCP and CLI

The MCP tools (`aibt_*` in an MCP client, plain names over the CLI's `call`) and their permission
categories:

| Tool | Permission | What it does |
|---|---|---|
| `add_node` with a node `{id?, subtree: {treeId, interfaceVersion?, keys?}, displayName?}` | SemanticEdit | Adds a reference. A missing `id` is made from the tree ID; a missing `interfaceVersion` is the subtree's current one; interface keys not listed are connected to same-named parent keys (`connectSameNamedKeys: false` turns that off); a key given as `null` stays not connected. `apply_domain_patch` accepts the same node in an `add` operation. |
| `set_subtree_keys` | SemanticEdit | Rebinds interface keys of a reference: `{"key": "parentKey" \| {"value": constant} \| null}`. Optional `interfaceVersion`, `connectSameNamedKeys`. |
| `extract_to_subtree` | SemanticEdit | Moves a branch into a new tree file (`newTreeId`, `path` under Assets, optional `name`, `description`) and leaves a reference. Both files are checked first; on any failure neither file is written and no new file is left behind. |
| `unpack_subtree` | SemanticEdit | Replaces a reference by a plain copy of its expansion; returns `innerRootId`. |
| `find_subtree_usages` | Read | Every reference to a tree ID (tree, file, node ID, key map) and the files that declare it; `status` is `Found`, `NotFound` or `Ambiguous`. |
| `inspect_tree` with `expanded: true` | Read | Adds `expansion`: the expanded tree the compiler sees, its hash, the source file of every node, diagnostics. |
| `get_project_manifest` | Read | Each tree also lists its `interface` and `referencedTreeIds`. |

`validate`, `compile` and `prepare_runtime` resolve references through the trees under the
project's `Assets` folder and report a missing, duplicate or recursive subtree as a diagnostic
(`AIBT3020`–`AIBT3022`), not as an exception. `validate` of a tree with references checks the
expanded tree and adds `subtrees: {expanded, referencedTreeIds}` to its result.

Every MCP edit of a tree with references is accepted only if the tree still expands and compiles,
so an edit cannot leave a reference with a not connected key or a missing subtree; the graph editor
allows such states while you work and shows them on the card. `extract_subtree` and
`inline_subtree` are a different, older pair: they move node payloads between calls and create no
reference.

CLI commands (see [CLI and MCP setup](cli.md)):

```powershell
aibt --project "<UnityProject>" --json tree expand game.boss-fight
aibt --project "<UnityProject>" --json tree usages game.strike
aibt --project "<UnityProject>" --permissions SemanticEdit --json tree extract game.boss-fight finisher game.finisher Trees/finisher.aibt --expected-hash "<contentHash>"
aibt --project "<UnityProject>" --permissions SemanticEdit --json tree keys game.boss-fight left hits:=5 --expected-hash "<contentHash>"
aibt --project "<UnityProject>" --permissions SemanticEdit --json tree unpack game.boss-fight left --expected-hash "<contentHash>"
aibt --project "<UnityProject>" --permissions Compilation --json tree validate game.boss-fight
```

In `tree keys`, `key=parentKey` connects, `key:=<json>` binds a constant and `key=` disconnects.
Extra arguments (a description for `tree extract`, `connectSameNamedKeys`) go in `--args-file`.

## Limits

- No recursion: a subtree may not use itself, directly or through other subtrees (`AIBT3022`).
- Nesting depth is at most 8 (`AIBT3023`); this is fixed, not a policy setting.
- A reference node's ID contains no `:` (`:` separates the reference from the inner ID).
- Expanded IDs (`r:n`, `r1:r2:n`, `r:key`) are at most 128 characters and must not collide with
  other IDs of the parent (`AIBT3027`).
- The root of a tree cannot be a reference node.
- Each subtree is validated on its own before it is combined with the parent. Its root then has
  no parent, so an `observer` block on a subtree's root is refused (`AIBT2034`: an observer must be a
  condition child of a reactive composite), even when the reference sits under one. Put the guard
  in the parent, or wrap the subtree's root condition in a reactive composite inside the subtree.
- Tree IDs must be unique in the project; a second file with the same tree ID makes every reference
  to it an error (`AIBT3021`).
- Interface keys bind only to the parent's Tree-scope keys. Node parameters are not passed through
  references; pass values through interface keys.
- A project `blackboardNaming` policy applies to a key's own name, the part after the last `:`.
  An expanded private key `r:key` (a non-interface key of the subtree, or an interface key bound to
  a constant) follows the policy when `key` does; the reference IDs in front of it are node IDs and
  are not checked. A key whose own name breaks the policy reports `AIBT2038` as before.
- A tree loaded from raw JSON at run time (not through an imported asset) reports `AIBT3020` unless
  the caller passes a subtree resolver. Imported `.aibt` assets already hold the expanded tree.

## Diagnostics

| Code | Meaning |
|---|---|
| `AIBT2047` | A reference node has a `type`, `parameters`, `bindings`, `observer` or children, an ID with `:`, a bad tree ID, or `interfaceVersion` below 1. |
| `AIBT2048` | A key entry names a key the parent does not declare at Tree scope. |
| `AIBT2049` | The root is a reference node. |
| `AIBT2050` | `interface.keys` names an undeclared or non-Tree key, repeats a key, or `version` is below 1. |
| `AIBT3020` | No tree has the referenced tree ID (or no resolver was given). |
| `AIBT3021` | More than one file has the referenced tree ID. |
| `AIBT3022` | The references form a cycle. |
| `AIBT3023` | Subtrees are nested deeper than 8. |
| `AIBT3024` | `interfaceVersion` differs from the subtree's `interface.version`, or the subtree has no interface and the reference binds keys. |
| `AIBT3025` | A key entry is missing (also a not connected key) or names an unknown interface key. |
| `AIBT3026` | A connected key or constant has another type, type version or enum contract; or a node accesses a key by fixed name that cannot be renamed. |
| `AIBT3027` | An expanded node or key ID is longer than 128 characters or collides with another. |
| `AIBT3028` | The subtree's Agent or Shared scope contract differs from the parent's or is missing there. |

A problem inside a subtree file is reported with that file's document ID and a related location at
the reference node, so it shows on both.

## Hot reload and live debugging

- Editing a subtree file re-imports every tree that uses it, and re-prepares runtime assets that
  were baked from it; the prepared asset records each subtree file it used.
- [Hot reload](hot-reload.md) matches nodes by authoring ID. Inner nodes keep the stable IDs `r:n`
  across recompiles, so a parameter edit in a subtree migrates every instance and running leaves
  resume. Retargeting one reference restarts only the nodes of that branch.
- In the parent's graph window a running `r:n` node lights its reference card `r`. A subtree window
  opened from a reference follows that instance only (nodes and blackboard values, private keys as
  `r:key`).
- Trace timeline, `describe_agent` and `read_agent_trace` label inner nodes with the two-part label,
  for example `Left strike / Hit`; the node's stable ID is the hash of its prefixed ID `left:hit`,
  and the program's debug map names the subtree file as the node's source.
