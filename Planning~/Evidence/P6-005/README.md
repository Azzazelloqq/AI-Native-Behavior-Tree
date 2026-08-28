# P6-005 MCP server host, discovery tools, and permission enforcement evidence

## Result

Done. The first real, running piece of the MCP layer: `MCP~/Server/` (the external `dotnet`
process, per `ADR-P6-001`), `MCP/` (the Unity-side bridge assembly, `AIBT.Mcp`), the Discovery
tool/resource group wired to `P6-003`'s query layer, and real permission enforcement.

## Design decisions made in this card (not part of any prior ADR)

- **Two locations, matching `ADR-P6-001`'s own split**: `MCP~/Server/` (tilde-suffixed, Unity
  never compiles it, mirroring `CodeGen~/`/`Spikes~/`) for the external process, promoted from
  `P6-001`'s disposable spike shape; `MCP/` (new Unity Editor-only assembly `AIBT.Mcp.asmdef`,
  referencing `AIBT.Runtime`/`AIBT.Authoring`/`AIBT.Editor`) for the bridge.
- **Server-bridge protocol**: a minimal newline-delimited JSON request/response line protocol
  over the TCP connection `P6-001`'s spike already proved (`{"tool","args","grantedCategories"}`
  -> `{"result"}` or `{"error"}`). The external server is a thin relay only -- every real
  decision (query computation, permission enforcement) lives in the bridge, kept testable the
  same way every other P6 card has been (Unity EditMode tests), rather than needing a second,
  separate dotnet-project test pipeline.
- **Permission grant source**: the external server reads `AIBT_MCP_PERMISSIONS`
  (comma-separated category names) from its own process environment once at startup and sends
  it with every request. A session with nothing set is granted nothing (fail-closed, not
  fail-open).
- **Tree discovery**: `AibtTreeDiscovery.Scan` globs `*.aibt.json` under `Application.dataPath`
  recursively, parsing each with the existing `CanonicalTreeJson.Parse`; unparseable files are
  skipped and reported, never fatal to the whole scan. No project-wide tree index exists
  anywhere in AIBT, so this is a genuinely new, disclosed-as-minimal heuristic.

## Real bugs found and fixed during live verification (not caught by unit tests alone)

1. **Static-resource path computation bug.** `GetStaticResource` originally computed the AIBT
   package root as the *parent* of `Application.dataPath` (correct for `.aibt/policy.json`,
   which is a per-consuming-project file expected at the project root) and reused that same
   computation for `Schemas~/`/`Documentation~/` lookups -- wrong, because AIBT itself lives at
   `Assets/AIBT/` in this repository's embedded-package layout, one level *inside* `Assets/`,
   not beside it. Found only by an actual `resources/read` call against the real server
   returning "not found" for a schema that definitely exists; fixed to resolve resource paths
   from `Application.dataPath` directly. This is exactly the kind of defect that would not have
   been caught by a unit test against the dispatcher alone if the test used the same wrong
   assumption -- caught specifically because verification used the real file system through the
   real client-server-bridge path.
2. **MCP resource template vs. concrete resource.** A single `[McpServerResource(UriTemplate =
   "aibt://resource/{key}")]` method compiled and worked for `resources/read`, but
   `resources/list` returned an empty array -- a real MCP client cannot discover a templated
   resource that way; only `resources/templates/list` enumerates templates. Found by an actual
   `resources/list` call, not assumed from the SDK's documentation. Fixed by exposing each of
   the seven allowlisted resources as its own concrete, parameter-free `[McpServerResource]`
   method -- more boilerplate, but each one is now real and independently listed.
3. **Inspector CLI environment-variable passthrough.** Setting `AIBT_MCP_PERMISSIONS` in the
   invoking shell (`export` or inline prefix) before running the official
   `@modelcontextprotocol/inspector --cli` command did **not** reach the spawned `dotnet`
   subprocess -- every discovery tool call was rejected with `AIBT9012` despite the shell
   correctly showing the variable set. The CLI's own `-e KEY=VALUE` flag additionally hit a
   real Windows-specific spawn quirk (`'-e' is not recognized as an internal or external
   command`) when placed inline before the target command. The reliable fix, verified working,
   was a `--config <file> --server <name>` JSON config with an explicit `"env"` map -- the
   *exact* shape a real MCP client's own `.mcp.json` uses (confirmed against the official C#
   SDK quickstart's own documented pattern). This is a genuine, disclosed finding for AIBT's own
   setup documentation: end users must declare `AIBT_MCP_PERMISSIONS` in their AI client's own
   `"env"` config, not rely on shell inheritance.

## Live end-to-end verification (real MCP client, real permanent server, real Unity bridge)

`Spikes~/`-free this time -- everything is the permanent, committed artifact:

1. Bridge started live in the actually-open Unity `6000.5.8f1` Editor via Unity MCP
   `execute_code` (mirroring `P5-001`/`P6-001`'s own methodology): real `Library/AibtMcp.json`
   discovery file written, `process_id` matched the live Editor session.
2. The official `@modelcontextprotocol/inspector` CLI, configured via a `.mcp.json`-shaped
   config file (`"env": {"AIBT_MCP_PERMISSIONS": "Read"}`), against the real, permanent
   `MCP~/Server/` project (`dotnet run --project Assets/AIBT/MCP~/Server`):
   - `tools/list` -> all three real tools (`aibt_get_project_manifest`, `aibt_search_nodes`,
     `aibt_get_node_contract`) with their real declared schemas.
   - `aibt_search_nodes(keyword="inverter")` -> the real `aibt.core.inverter` contract.
   - `aibt_get_project_manifest()` -> real capabilities/policy-read-failure (this host project
     has no root-level `.aibt/policy.json`, honestly reported, not papered over) and real
     scanned/skipped tree files from this actual repository.
   - `aibt_get_node_contract(typeId="aibt.core.repeater")` -> the real full manifest contract.
   - `resources/list` -> all 7 real resources (after fix #2 above).
   - `resources/read(uri="aibt://resource/schema.tree")` -> the exact real
     `Schemas~/tree.schema.json` file content (after fix #1 above).
3. A permission-denial call (no `AIBT_MCP_PERMISSIONS` set) returned `AIBT9012`, proving
   fail-closed-by-default end-to-end, not only at the unit-test level.
4. Bridge stopped cleanly; discovery file removed; temporary Inspector config file deleted.

## Unity EditMode tests (30, all real)

- `McpPermissionEnforcerTests.cs`: one positive + one negative case for all 8
  `McpPermissionCategory` values against the real `McpPermissionEnforcer.Require`, plus a
  case proving one granted category never implies another.
- `AibtTreeDiscoveryTests.cs`: real valid/malformed `.aibt.json` fixtures in an isolated temp
  directory (not this host project's own messy `Assets/`), deterministic ordering, a
  non-existent directory returning empty rather than throwing.
- `McpToolDispatcherTests.cs`: all three discovery tools against a real registry and real
  scanned/temp-directory tree fixtures; the "zero custom nodes returns exactly the Phase 1
  built-in catalog" acceptance criterion, compared directly against
  `BuiltInNodeManifests.All`; a permission-denial case for every tool; an unknown-tool-name
  rejection.
- `McpBridgeListenerTests.cs`: a 3-cycle repeated start/stop test (the card's own acceptance
  criterion) plus a real `TcpClient` connect-and-dispatch round trip.

## Verification

```text
Unity MCP run_tests (EditMode): AIBT.Tests.Editor.Mcp.Discovery.* -- 30/30 passed
Live end-to-end: real bridge + real permanent MCP~/Server/ + official Inspector CLI --
  tools/list, all 3 discovery tools, resources/list (7), one resource read, one permission
  denial -- all passed after the 3 fixes above
Tools~/Verification/Verify-Static.ps1 -- passed, 95 work items
git diff --check -- clean
```

## Scope and limitations

- Single client, single Unity instance at a time (concurrent multi-client/multi-instance
  remains unexercised, per `ADR-P6-001`'s own "Explicitly unverified" section -- unchanged
  here).
- Tree discovery is a recursive glob under `Application.dataPath`; in this dogfood host
  project it also picks up AIBT's own test fixtures and stale generated `TestResults/` copies
  (seen directly in `get_project_manifest`'s own `skippedTreeFiles` output during
  verification) -- a real consuming project without AIBT's own test tree under `Assets/` would
  not see this noise, but it is disclosed here rather than hidden.
- Static-resource path resolution assumes AIBT lives at `Assets/AIBT` (true for this
  repository's embedded-package layout); a real Package Manager registry install would need a
  different root resolution -- not built or claimed here.
- `.aibt/policy.json` is resolved at the *consuming project's* root (sibling to `Assets/`);
  this host project has none there (only a package-internal one under
  `Assets/AIBT/.aibt/policy.json`, used for the package's own P0-era schema validation, not a
  per-project policy) -- `get_project_manifest` honestly reports the read failure rather than
  substituting the package's own internal file.
- No production Play-mode host, no multi-instance discovery routing, no crash/stale-file
  recovery -- all previously disclosed as open in `ADR-P6-001`, unchanged by this card.
