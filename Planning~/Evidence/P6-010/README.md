# P6-010 custom MCP tool provider registration and permission model evidence

## Result

Done. `MCP/CustomTools/` implements `ai-and-mcp.md`'s "Custom MCP tools" contract: a consuming
project implements `ICustomMcpToolProvider` on its own class, `AIBT.Mcp` discovers it purely via
`UnityEditor.TypeCache` (no AIBT assembly ever references the implementing assembly), and every
call is enforced by the exact same `McpPermissionEnforcer` mechanism every built-in tool already
uses. Two new dispatcher cases (`list_custom_tools`, `call_custom_tool`) wire this into
`McpToolDispatcher.cs`; `MCP~/Server/CustomTools.cs` exposes each discovered tool as its own
first-class MCP tool with its own real JSON schema, not a generic passthrough.

## Architectural fork found before implementation, resolved by the owner

Every existing MCP tool (`P6-005`-`P6-009`) is a **compile-time** `[McpServerToolType]` method in
`MCP~/Server/`, known at that process's own `dotnet build` time. A consuming project's custom tool
cannot be known then, so a literal reading of `ai-and-mcp.md`'s "a tool declares stable name...
JSON input/output schemas" (a real, individually-schema'd tool a client sees in `tools/list`) is
not achievable with that static pattern. Before writing any code, a throwaway reflection probe
against the installed `ModelContextProtocol 2.2.0` SDK assemblies (not assumed from memory)
confirmed the SDK's own supported extension point for exactly this: `McpServerBuilderExtensions
.WithTools(IMcpServerBuilder, IEnumerable<McpServerTool>)` registers arbitrary tool instances
additively alongside `.WithToolsFromAssembly()`, and `McpServerTool` is public abstract with a
protected constructor and exactly three abstract members (`ProtocolTool`, `Metadata`,
`InvokeAsync`) — a documented dynamic/proxy-tool shape, not a hack. This fork (generic passthrough
tool vs. real dynamic per-tool registration) was put to the owner via `AskUserQuestion`; the owner
chose **dynamic per-tool registration**, which is what was built.

## Decisions disclosed rather than silently assumed

1. **Server-side registration is a startup-time snapshot, not a live per-`tools/list` refresh.**
   `Program.cs` queries the bridge once (`list_custom_tools`) before `builder.Build()`. If the
   bridge is unreachable at that moment, zero custom tools are registered for that server process's
   lifetime — never a startup failure. This mirrors `ADR-P6-001`'s own already-accepted "client may
   need a restart to pick up a change" precedent, extended to: Unity's bridge must be running
   before the MCP server process starts for that session's custom tools to appear. A live-refresh
   `ListToolsHandler` override was considered and rejected: the SDK's low-level `ListToolsHandler`
   is a full override point, not an additive hook, so using it would mean re-implementing the
   already-working attribute-scanned tool list too, a materially bigger change than this card's own
   deliverables require.
2. **Cancellation is declaration-only, never enforced.** `McpBridgeListener`/`BridgeClient`'s
   entire wire protocol is one blocking TCP request/response with no message IDs and no
   cancellation transport, for every tool built in this project so far. `ICustomMcpToolProvider
   .SupportsCancellation` is surfaced in `list_custom_tools` for documentation only. Building real
   cancellation plumbing would mean changing the bridge's wire protocol, `P6-005`-owned and outside
   this card's allowed paths.
3. **Interpretation of "a custom tool declared with `read` permission is rejected when it attempts
   a semantic-edit-shaped operation internally."** Read fully literally this is not mechanically
   enforceable in general: `SemanticPatchTransaction`/`LayoutPatchTransaction` (`Editor/Patching/`)
   are `public` production APIs reachable by any Editor-domain code, including a custom tool
   provider's own assembly, and retrofitting a capability check into them is a cross-assembly
   public-API change outside both this card's allowed paths and `P6-004`'s own gate-accepted scope.
   Built and verified instead: `SampleMarkerFileCustomToolProvider` declares `SemanticEdit` and
   really writes a file when invoked; `CallCustomToolWithoutTheDeclaredCategoryIsRejectedAndNeverTouchesTheFile`
   (and the live Inspector CLI run below) prove a session granted only `Read` gets a structured
   `AIBT9012` rejection **and** the target file never comes to exist — the dispatcher's permission
   gate genuinely prevents the attempt from having any effect, proven by observable effect, not by
   trusting the response's `error` field alone.
4. **One bad/colliding custom tool does not take down every other one.** Unlike
   `NodeRegistryBuilder.Build()` (which fails the whole registry closed on any diagnostic — correct
   for a single canonical compiler input), `CustomMcpToolProviderDiscovery.Build` skips only the
   offending provider(s) and keeps every valid one working. Chosen because custom tools are
   optional, per-project, additive conveniences; one project's misconfigured tool should not disable
   AIBT's entire MCP surface.
5. **"Owning assembly" is derived, not declared.** `ICustomMcpToolProvider` has no `OwningAssembly`
   member; the host computes it itself via `provider.GetType().Assembly.GetName().Name` at
   discovery time, avoiding redundant provider boilerplate for a fact the host can always compute.

## Real bug found live and fixed

The first `tools/call` against the real Inspector CLI failed with `"Tool ... declares an output
schema but returned no structured content"` — the SDK requires real `CallToolResult
.StructuredContent` (not just a text block) whenever a tool declares an `OutputSchema`.
`CustomRelayMcpServerTool.InvokeAsync` now parses the bridge's `result` payload into a
`JsonElement` and sets `StructuredContent` from it; confirmed fixed by re-running the same call.

## Verification

```text
dotnet build (MCP~/Server/) -- 0 warnings, 0 errors
Unity EditMode full regression (host project, 1571 tests) -- 1571/1571 executed, 3 pre-existing
  failures unrelated to this card (2 known "CodeGen test assembly must belong to the AIBT package"
  host-project-noise failures matching every prior gate's own documented pattern, 1 unrelated
  LocalSaveSystem module failure) -- re-run identically after restoring the sample fixture assembly
  post-removal-check, same 3 unrelated failures both times, zero regressions from this card
New tests, all passing (11): CustomMcpToolProviderDiscoveryTests (4, pure Build() logic, no
  TypeCache), McpCustomToolsToolDispatcherTests (7, real end-to-end McpToolDispatcher.Dispatch
  calls pulling in the real, separately-assembled AIBT.SampleCustomTool fixture)
Live end-to-end (real bridge via Unity MCP execute_code, real permanent MCP~/Server/, official
  @modelcontextprotocol/inspector CLI, config-file env-var permissions per P6-005's own established
  workaround):
  - tools/list: aibt_custom_sample_echo and aibt_custom_sample_write_marker appear as first-class
    tools with their own real declared schemas, alongside all 28 static built-in tools (30 total)
  - tools/call aibt_custom_sample_echo (Read granted) -- echoed correctly, real structuredContent
  - tools/call aibt_custom_sample_write_marker (Read only, tool declares SemanticEdit) -- AIBT9012,
    file confirmed never created
  - tools/call aibt_custom_sample_write_marker (SemanticEdit granted, dryRun=true) -- accepted,
    file confirmed not created
  - tools/call aibt_custom_sample_write_marker (SemanticEdit granted, real) -- file confirmed
    created, then cleaned up
  - Provider-assembly-removed regression check: Fixtures/ moved out of Assets/, Unity recompiled
    clean (no errors), bridge auto-restarted (P6-009's McpBridgeAutoRestart) on a new port,
    tools/list showed exactly 28 tools (0 aibt_custom_*), aibt_get_project_manifest still worked
    normally; Fixtures/ restored, Unity recompiled clean again, full regression re-run confirmed
    identical (still 3 pre-existing unrelated failures, all new CustomTools tests passing)
  - Bridge stopped cleanly afterward; discovery file confirmed removed
Tools~/Verification/Verify-Static.ps1 -- passed, 105 work items
git diff --check -- clean
```

## Scope and limitations

- Custom tool discovery on the external server is a startup-time snapshot (Decision 1 above), not
  a live refresh within one server process's lifetime.
- `SupportsCancellation` is declared metadata only; no cancellation transport exists anywhere in
  the bridge protocol for any tool, built-in or custom (Decision 2).
- The permission gate protects the MCP-protocol call boundary, not arbitrary in-process code a
  custom tool provider might call directly (Decision 3) — the same boundary every built-in tool's
  enforcement already operates at.
