# CLI and MCP for agents

The CLI is the primary entry point for command-driven agents and scripts. MCP remains available
for clients that use protocol tools/resources and project custom-tool discovery. Both call the
same Editor dispatcher; no second implementation of tree semantics exists.

## Setup

Install the .NET **10 SDK**, open the intended Unity project with AIBT installed, and build:

```powershell
dotnet build "<AIBT>/MCP~/Cli/AibtCli.csproj"
dotnet build "<AIBT>/MCP~/Server/AibtMcpServer.csproj"
```

In these examples, `aibt` means `dotnet "<AIBT>/MCP~/Cli/bin/Debug/net10.0/aibt.dll"`.
PowerShell users may instead run `<AIBT>/Tools~/aibt.ps1`, which builds the CLI before invoking it.
Use the built DLL directly for an MCP stdio client so build output cannot enter the protocol.

```powershell
aibt --project "C:/Projects/MyGame" --json status
aibt --project "C:/Projects/MyGame" --json mcp ensure
aibt --project "C:/Projects/MyGame" --json nodes search cooldown
aibt --project "C:/Projects/MyGame" --json nodes contract aibt.core.cooldown
aibt --project "C:/Projects/MyGame" --permissions Read,Compilation --json tree validate tree.enemy
```

`status` only probes. All other calls ensure the bridge first. The Editor update hook starts a
listener only after an explicit local request; opening Unity alone does not start one. A healthy
listener is reused, domain reload restores the same service ownership, and stale discovery can
be replaced. Unity must already be running and responsive. A closed Editor, compilation or modal
dialog can prevent acknowledgement; the default wait is 15 seconds (`--timeout 1..300`). The CLI
does not launch/kill Unity, close dialogs or alter AI-client configuration.

`--project` is a Unity **project root**, not Assets or the package directory. It can also be set
as `AIBT_PROJECT_PATH`. Each project has its own endpoint under Library. Endpoint and live health
responses must match that project. Do not use an endpoint from another open project.

## Operations and permissions

CLI defaults to `Read`. Use `--permissions` or `AIBT_MCP_PERMISSIONS` to explicitly grant the
categories needed by the request. No permission implies another. Validation/compilation need
`Compilation`, tree writes `SemanticEdit`, layout writes `LayoutEdit`, node generation/apply
`CodeGeneration`, tests `TestExecution`, benchmarks `BenchmarkExecution`. Bridge health/startup
does not grant any operation permissions. An empty explicit permission set grants nothing.

`call <operation> --args-file request.json` exposes every existing bridge operation, including
authoring, dry runs, node staging/test/apply and custom project tools. Operation names may use
the `aibt_` prefix. The argument file contains the **bridge JSON object** (nested objects rather
than the JSON-in-string parameters used by some MCP wrappers). For example:

```json
{
  "treeId": "tree.enemy",
  "expectedHash": "<hash returned by the previous accepted operation>",
  "nodeId": "move",
  "parameterName": "speed",
  "value": 1.5,
  "dryRun": true
}
```

Use `aibt operations --json` and `aibt describe configure_node --json` to read argument schemas,
required permissions and side effects. `nodes contract <typeId>` additionally describes native
behavior-case fields/bindings when generated metadata is available. Never invent a hash or infer it from a revision increment. CLI does not
silently refresh a stale hash and overwrite another author's work. After dispatch, a timeout or
lost response is an **unknown outcome**: inspect the tree/staging before issuing another write.
Neither CLI nor MCP automatically replays the request.

Exit codes: **0** success; **1** structured operation rejection/negative result; **2** usage/JSON
error; **3** bridge/connection failure. `--json` emits a single compact result/error envelope.

## MCP through CLI

Configure the MCP client to launch `dotnet` with this argument array (replace both paths):

```json
[
  "<AIBT>/MCP~/Cli/bin/Debug/net10.0/aibt.dll",
  "--project", "<UnityProject>",
  "--permissions", "Read,SemanticEdit,Compilation,TestExecution,CodeGeneration",
  "mcp", "serve"
]
```

`mcp serve` ensures the Editor bridge, then launches the already-built MCP stdio server. The
server lives for that connection; an agent with a shell can use `mcp ensure` to prepare the
endpoint. Registering native tools in an AI client still requires that client's MCP connection
configuration. A detached background stdio process alone cannot attach tools to that client.

Existing direct MCP configurations remain supported. Set `AIBT_PROJECT_PATH` and
`AIBT_MCP_PERMISSIONS`, or retain an absolute `AIBT_MCP_DISCOVERY_FILE`. The server now shares
on-demand connection recovery with CLI. Custom tools are still a startup snapshot: reconnect
after changing registered providers.

## Backend limits

CLI is not a standalone Unity replacement. `tree compile` uses the **reference** compiler.
Native-only nodes require `tree prepare`; reference simulation does not silently execute them.
Discovery, semantic authoring and validation share the project catalog and, when the project has
one, `<project>/.aibt/policy.json`. The file is optional: without it `tree validate`, `tree compile`
and `tree prepare` check a tree as the Graph window's tree list does (the managed policy for a tree
with managed nodes, otherwise the native default with Agent and Shared scope allowed), and
`get_project_manifest` lists the trees with `"policy": null`. A file that exists but cannot be read
is reported as warning AIBT9008 naming the problem and is not applied. What the file is for and a
minimal one: [Data formats](data-formats.md#project-policy-aibtpolicyjson). Native
preparation additionally checks that the selected catalog declares every non-core leaf.

A tree built only from `aibt.core.*` and the package's native node library (`aibt.native.*`)
follows the same path: `call create_tree` with `"formatVersion": 2`, `call add_node` or
`call apply_domain_patch` with `parameters` and `bindings` in the node JSON, `tree validate`,
`tree prepare` against a catalog set that lists `StandardNativeShard`. `tree compile` reports
AIBT3012 for such a tree, and `call simulate` and `call run_tests` do not run it. The step-by-step
recipes are in [generated/recipes.md](generated/recipes.md) ("find native library nodes", "build a
native tree from library nodes", "change a native tree and prepare it again"); see also
[AI-native authoring](ai-and-mcp.md#native-only-nodes).

## Native preparation and trace

```powershell
aibt --project "<UnityProject>" --json tree inspect tree.enemy
aibt --project "<UnityProject>" --json runtime catalogs
aibt --project "<UnityProject>" --permissions Compilation --json tree prepare tree.enemy --catalog Game.EnemyCatalog --output Assets/AI/Enemy.runtime.asset --expected-hash "<contentHash>" --dry-run
```

Create the output directory first. Remove `--dry-run` to persist the derived asset. An existing
asset requires `--overwrite --expected-asset-hash <existingAssetHash>`; obtain that hash from
dry-run. Its GUID is preserved. Dirty destination assets, source/destination changes, escapes
and link/reparse ancestry are rejected. Only the selected asset is saved. The operation does
not assign components or restart hosts.

After starting your game's own integration (see [First production agent](consumer-quickstart.md)):

```powershell
aibt --project "<UnityProject>" --json runtime hosts
aibt --project "<UnityProject>" --json trace read <instanceId> --limit 128 --after-sequence 0
```

The host that a `NativeTreeAgent` creates for itself is not listed by `runtime hosts` (it is a
hidden component that the host search does not find), so `trace read` cannot reach it; watch such
an agent in the AIBT Graph in Play Mode. Trace results identify the host/program, root outcome and source node IDs. Follow `hasMore` and
`nextSequence` for paging. `complete` describes retained-history coverage, not page size;
`droppedCount`/`faulted` disclose incomplete history. `activeNodesReliable=false` means projected
active nodes may be stale because their exits were lost. Root status is reported independently
from the current host. Busy writers return `available=false, retryable=true`; no job is forced
to complete. Disposed owners are not retryable. No compare-trace operation is exposed.

## Subtrees

Commands for subtree references ([Subtrees](subtrees.md), `ADR-P11-001`). Writes need
`SemanticEdit` and the tree's current `contentHash`; `tree expand` and `tree usages` need `Read`.

```powershell
aibt --project "<UnityProject>" --json tree expand game.boss-fight
aibt --project "<UnityProject>" --json tree usages game.strike
aibt --project "<UnityProject>" --permissions SemanticEdit --json tree extract game.boss-fight finisher game.finisher Trees/finisher.aibt --expected-hash "<contentHash>" --args-file extract.json
aibt --project "<UnityProject>" --permissions SemanticEdit --json tree keys game.boss-fight right hits=bonus_hits --expected-hash "<contentHash>"
aibt --project "<UnityProject>" --permissions SemanticEdit --json tree unpack game.boss-fight right --expected-hash "<contentHash>"
```

| Command | Operation |
|---|---|
| `tree expand <treeId>` | `inspect_tree` with `expanded: true`: the source plus `expansion` (the tree the compiler sees, node sources, diagnostics) |
| `tree usages <treeId>` | `find_subtree_usages`: every reference node that uses the tree ID, and the files that declare it |
| `tree extract <treeId> <nodeId> <newTreeId> <path>` | `extract_to_subtree`: moves the branch into a new file (path under Assets) and leaves a reference; `name` and `description` of the new tree go in `--args-file` |
| `tree keys <treeId> <nodeId> <assignment>...` | `set_subtree_keys`: `key=parentKey` connects, `key:=<json>` binds a constant, `key=` disconnects |
| `tree unpack <treeId> <nodeId>` | `unpack_subtree`: replaces the reference by a plain copy of its expansion |

`--dry-run` previews any of the writes. A reference is added with `call add_node --args-file`
and a node `{"id": "left", "subtree": {"treeId": "game.strike"}}`. An edit that would leave the
tree unable to expand or compile is rejected with its diagnostics (for example `AIBT3025` for a
not connected interface key) and nothing is written. `tree validate`, `tree compile` and
`tree prepare` resolve references through the trees under `Assets` and report a missing,
duplicate or recursive subtree as `AIBT3020`–`AIBT3022`.

## Native node behavior cases

`generate_node_tests_and_manifest` writes source-controlled case data under UNITY_EDITOR;
`test_node` executes it through real native dispatch. These are not pretend NUnit assertions.
The condition template supplies success/failure cases from its threshold contract; custom
nodes/actions need caller-authored cases. Optional `cases` overrides the staged cases for a run:

```json
{
  "expectedContentHash": "<successful compile hash>",
  "cases": [{
    "name": "outside stopping radius",
    "configuration": { "speed": 1.5, "stop-distance": 0.25 },
    "inputs": { "distance": 2.0 },
    "expectedStatus": "Running",
    "expectedEffects": { "move-speed": 1.5 }
  }]
}
```

Use the node's `behaviorCaseContract` for configuration/input/effect IDs and types. Cases have
independent fresh memory and execute Enter then one Tick. Supported values are Bool, Int32,
UInt32, Int64, UInt64, Float32 and Float64; async/completion, registered and fixed-byte shapes
are outside this checker. A supplied effect map asserts the exact effect set; an empty map
asserts none. No observation is used to manufacture an expected result.

Check `behaviorProven` and `behavior.caseCount`, not just structural `valid`/`dispatchProven`.
A supplied case mismatch makes `valid=false`. Empty cases do not prove behavior. Generated
case data is included in the staged source hash; regenerate it before compile, not after.
Applied Burst nodes use runtime-capable assemblies, staging remains Editor-only/quarantined,
and case data is excluded from Player. The game still needs an explicit generated catalog.

The Editor command **AIBT > MCP > Regenerate Documentation** writes current project contracts
to `Assets/AIBT-Documentation`; installed package caches remain untouched. MCP resources expose
workflow, recipes, CLI/integration guidance and a live project node catalog in UPM installations.
