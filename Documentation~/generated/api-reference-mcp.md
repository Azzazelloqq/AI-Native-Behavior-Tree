# AIBT.Mcp -- public API reference (generated)

Source: live reflection over `AIBT.Mcp`'s own compiled public surface (`P7-014`). Regenerate with the `AIBT/MCP/Regenerate Documentation` Editor menu command. Do not hand-edit -- edits are overwritten on the next regeneration.

A type's own summary line is shown where an XML-doc `<summary>` exists in source; member-level doc-comment text is not yet correlated here (see this document's own generator comment for why) -- every member still gets its own full signature line regardless of whether prose exists for it.
7 public type(s).

---

### `AIBT.Mcp.AibtTreeDiscovery`

A minimal, disclosed-as-heuristic project tree scanner: no project-wide tree index exists anywhere in AIBT yet, so this globs for *.aibt.json under a root directory and parses each with the real CanonicalTreeJson.Parse. A file that fails to parse is skipped (recorded, never silently dropped without a trace), not treated as a fatal error for the whole scan.

- `METHOD AIBT.Mcp.AibtTreeDiscovery+ScanResult Scan(System.String)`

---

### `AIBT.Mcp.CustomTools.ICustomMcpToolProvider`

P6-010's IoC contract for a project-owned high-level MCP tool (ai-and-mcp.md's "Custom MCP tools" section). A consuming project implements this on a concrete class with a public parameterless constructor and AIBT discovers it via <see cref="CustomMcpToolProviderDiscovery"/> -- no AIBT assembly ever references the implementing assembly directly. "Owning assembly" is not a member here: the host derives it itself from the concrete type's own <see cref="System.Reflection.Assembly"/> at discovery time, so a provider never has to restate a fact the host can always compute. <see cref="SupportsCancellation"/> is a declared capability only. The MCP bridge's wire protocol (<c>McpBridgeListener</c>/<c>BridgeClient</c>) is a single blocking request/response with no cancellation transport for any tool, built-in or custom -- this flag is surfaced for documentation, never mechanically enforced. Disclosed explicitly, not silently implied.

- `METHOD Newtonsoft.Json.Linq.JObject Invoke(System.String,Newtonsoft.Json.Linq.JObject,System.Boolean)`
- `PROPERTY AIBT.Mcp.McpPermissionCategory PermissionCategory`
- `PROPERTY Newtonsoft.Json.Linq.JObject InputSchema`
- `PROPERTY Newtonsoft.Json.Linq.JObject OutputSchema`
- `PROPERTY System.Boolean SupportsCancellation`
- `PROPERTY System.Boolean SupportsDryRun`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<System.String> SideEffects`
- `PROPERTY System.String Description`
- `PROPERTY System.String ToolName`

---

### `AIBT.Mcp.McpBridgeListener`

The Unity-side half of ADR-P6-001's bridge: a TCP listener with no MCP SDK dependency, discovered by the external MCP~/Server/ process via a discovery file under Library/. Explicit start/stop only (McpBridgeWindow, or a direct caller) -- never auto-started with the Editor itself. <see cref="Start"/>/<see cref="Stop"/> record the running state in <see cref="SessionState"/> (survives a domain reload within the same Editor session, unlike a plain field) so <see cref="McpBridgeAutoRestart"/> can bring a live instance back after a script compile's domain reload destroys this object -- found necessary by P6-009, the first card whose tools write real .cs source the Editor recompiles; every prior P6 tool only ever wrote data files (*.aibt.json/*.aibtcase.json), which never triggers a domain reload, so this gap never surfaced before.

- `METHOD System.Void .ctor(System.String,System.String)`
- `METHOD System.Void Dispose()`
- `METHOD System.Void Start()`
- `METHOD System.Void Stop()`
- `PROPERTY System.Boolean IsRunning`
- `PROPERTY System.Int32 Port`

---

### `AIBT.Mcp.McpBridgeWindow`

Explicit start/stop workflow for the MCP bridge (mirrors P5-008's HotReloadWorkflowWindow own-window, explicit-trigger-only pattern). Never starts the bridge automatically.

- `METHOD AIBT.Mcp.McpBridgeWindow ShowWindow()`
- `METHOD System.Void .ctor()`

---

### `AIBT.Mcp.McpPermissionCategory`

- `FIELD AIBT.Mcp.McpPermissionCategory ArbitraryProjectIntegration`
- `FIELD AIBT.Mcp.McpPermissionCategory BenchmarkExecution`
- `FIELD AIBT.Mcp.McpPermissionCategory CodeGeneration`
- `FIELD AIBT.Mcp.McpPermissionCategory Compilation`
- `FIELD AIBT.Mcp.McpPermissionCategory LayoutEdit`
- `FIELD AIBT.Mcp.McpPermissionCategory Read`
- `FIELD AIBT.Mcp.McpPermissionCategory SemanticEdit`
- `FIELD AIBT.Mcp.McpPermissionCategory TestExecution`
- `FIELD System.Int32 value__`

---

### `AIBT.Mcp.McpPermissionEnforcer`

The real enforcement path every tool dispatch goes through (ADR-P6-001): a call outside the categories granted to the current session is rejected with a structured diagnostic, never silently downgraded or silently allowed.

- `METHOD System.Boolean Require(System.Collections.Generic.ISet`1<AIBT.Mcp.McpPermissionCategory>,AIBT.Mcp.McpPermissionCategory,AIBT.Diagnostic&)`

---

### `AIBT.Mcp.McpToolDispatcher`

Dispatches one relayed tool request from the external MCP~/Server/ process to the real AIBT.Authoring query layer (P6-003), enforcing McpPermissionEnforcer first. The external server holds no logic of its own -- this is where every real decision is made, and where it is tested.

- `METHOD System.String Dispatch(System.String,System.String)`
