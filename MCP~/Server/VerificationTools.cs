using System.ComponentModel;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;

namespace AibtMcpServer;

// Thin relays to AIBT.Mcp.Verification.McpVerificationToolDispatcher (P6-007) over the bridge TCP
// connection, mirroring DiscoveryTools.cs/AuthoringTools.cs's exact shape. No logic of any kind
// lives here. Nested payloads (simulate's steps array, explain_diagnostic's diagnostic object)
// are accepted as raw JSON text, matching AuthoringTools.cs's own established convention.
[McpServerToolType]
public static class VerificationTools
{
    [McpServerTool(Name = "aibt_validate")]
    [Description("Validates a tree against the real TreeValidator (including project-policy diagnostics from .aibt/policy.json when present), returning the exact structured diagnostics diagnostics-v1.md defines. Never mutates anything.")]
    public static string Validate(
        [Description("Target tree ID.")] string treeId)
        => BridgeClient.SendRequest("validate", new JsonObject { ["treeId"] = treeId });

    [McpServerTool(Name = "aibt_compile")]
    [Description("Compiles a tree through the real ReferenceCompiler against the project's real registered node types, returning success/diagnostics and the compiled program's content hash -- never a bare boolean. Never mutates anything.")]
    public static string Compile(
        [Description("Target tree ID.")] string treeId)
        => BridgeClient.SendRequest("compile", new JsonObject { ["treeId"] = treeId });

    [McpServerTool(Name = "aibt_simulate")]
    [Description("Steps a tree through the real ReferencePreviewDriver (the Phase 1 reference executor, fixed to the Phase 1 fixture/built-in node set -- a tree using other node types will fail to compile here even if it compiles for real authoring). Only plain 'update' steps ({operation:'update', updateId, snapshotRevision, timeMicroseconds}) are supported: the driver assigns updateId/snapshotRevision itself sequentially starting at 1 (a step must match that), and exposes no event/completion/resume/abort/step-budget injection API at all -- a step requesting any of those is rejected with a structured diagnostic. Returns a step-by-step status/trace summary explicitly labeled with the backend and node set used. Never mutates any file.")]
    public static string Simulate(
        [Description("Target tree ID.")] string treeId,
        [Description("Ordered steps JSON array. Each entry: {operation:'update', updateId, snapshotRevision, timeMicroseconds}.")] string stepsJson)
        => BridgeClient.SendRequest("simulate", new JsonObject { ["treeId"] = treeId, ["steps"] = JsonNode.Parse(stepsJson) });

    [McpServerTool(Name = "aibt_explain_diagnostic")]
    [Description("Given one diagnostic record (as returned by aibt_validate/aibt_compile/aibt_simulate), returns its stable catalog meaning (subsystem, default severity, required/optional field contract) when the code's catalog is reachable from this MCP surface, and echoes back exactly the suggestedOperation the caller supplied (never inventing one). Only 2 of the project's ~12 diagnostic subsystems have a publicly reachable catalog (tree validation AIBT2010-2041, blackboard schema AIBT2001-2008); every other code reports catalogReachable:false, disclosed rather than fabricated.")]
    public static string ExplainDiagnostic(
        [Description("One diagnostic record JSON object, at minimum {code}. May include severity/message/location/suggestedOperation as returned by another tool.")] string diagnosticJson)
        => BridgeClient.SendRequest("explain_diagnostic", new JsonObject { ["diagnostic"] = JsonNode.Parse(diagnosticJson) });
}
