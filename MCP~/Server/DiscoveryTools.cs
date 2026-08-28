using System.ComponentModel;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;

namespace AibtMcpServer;

// Thin relays to AIBT.Mcp.McpToolDispatcher (P6-005) over the bridge TCP connection. No logic
// of any kind lives here -- every real decision (query computation, permission enforcement)
// happens in the Unity-side bridge.
[McpServerToolType]
public static class DiscoveryTools
{
    [McpServerTool(Name = "aibt_get_project_manifest")]
    [Description("Project capabilities, policy summary, and tree/revision listing for the open AIBT Unity project.")]
    public static string GetProjectManifest()
        => BridgeClient.SendRequest("get_project_manifest", new JsonObject());

    [McpServerTool(Name = "aibt_search_nodes")]
    [Description("Search the AIBT node catalog by keyword (matches type ID, category, or summary). Empty keyword lists every registered node.")]
    public static string SearchNodes(
        [Description("Keyword to search for; empty string lists everything.")] string keyword = "",
        [Description("Zero-based page offset.")] int offset = 0,
        [Description("Maximum entries to return.")] int count = 50)
        => BridgeClient.SendRequest("search_nodes", new JsonObject { ["keyword"] = keyword, ["offset"] = offset, ["count"] = count });

    [McpServerTool(Name = "aibt_get_node_contract")]
    [Description("Look up the full manifest contract (parameters, child policy, reads/writes, cost hint, examples, ...) for one exact AIBT node type ID.")]
    public static string GetNodeContract(
        [Description("The exact canonical node type ID, e.g. 'aibt.core.inverter'.")] string typeId)
        => BridgeClient.SendRequest("get_node_contract", new JsonObject { ["typeId"] = typeId });
}
