using System.ComponentModel;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;

namespace AibtMcpServer;

[McpServerToolType]
public static class RuntimeTools
{
    [McpServerTool(Name = "aibt_list_operations")]
    [Description("List shared CLI/bridge operation schemas, explicit permissions and side effects. These schemas describe bridge JSON, not JSON-in-string MCP wrapper parameters.")]
    public static string ListOperations() => BridgeClient.SendRequest("list_operations", new JsonObject());

    [McpServerTool(Name = "aibt_get_operation_schema")]
    [Description("Read one operation's actual bridge argument schema, permission and side effect.")]
    public static string GetOperationSchema(string operation) => BridgeClient.SendRequest("get_operation_schema", new JsonObject { ["operation"] = operation });

    [McpServerTool(Name = "aibt_inspect_tree")]
    [Description("Read an authored tree and current contentHash before editing/preparing. Reports reference backend availability.")]
    public static string InspectTree(string treeId) => BridgeClient.SendRequest("inspect_tree", new JsonObject { ["treeId"] = treeId });

    [McpServerTool(Name = "aibt_runtime_catalogs")]
    [Description("List Player generated catalog identities for native runtime preparation.")]
    public static string RuntimeCatalogs() => BridgeClient.SendRequest("runtime_catalogs", new JsonObject());

    [McpServerTool(Name = "aibt_prepare_runtime")]
    [Description("Prepare a native v2 runtime asset using the existing Editor baker. Requires Compilation permission, current source expectedHash and explicit catalog/outputPath. Dry-run reports existingAssetHash; overwrite requires that hash and overwrite=true. Preserves existing asset GUID. Does not restart hosts or modify authored source.")]
    public static string PrepareRuntime(string treeId, string expectedHash, string catalog, string outputPath,
        bool dryRun = false, bool overwrite = false, string expectedAssetHash = "")
        => BridgeClient.SendRequest("prepare_runtime", new JsonObject
        {
            ["treeId"] = treeId, ["expectedHash"] = expectedHash, ["catalog"] = catalog, ["outputPath"] = outputPath,
            ["dryRun"] = dryRun, ["overwrite"] = overwrite, ["expectedAssetHash"] = expectedAssetHash,
        });

    [McpServerTool(Name = "aibt_runtime_hosts")]
    [Description("List scene AIBT hosts with stable instance IDs, program hashes and root results. Read-only.")]
    public static string RuntimeHosts() => BridgeClient.SendRequest("runtime_hosts", new JsonObject());

    [McpServerTool(Name = "aibt_read_trace")]
    [Description("Read bounded native trace for an exact runtime host instance. Never completes jobs. Busy returns available=false; complete=false discloses dropped/faulted history. Limit 1..512, default 128.")]
    public static string ReadTrace(ulong instanceId, ulong afterSequence = 0, int limit = 128)
        => BridgeClient.SendRequest("read_trace", new JsonObject { ["instanceId"] = instanceId, ["afterSequence"] = afterSequence, ["limit"] = limit });

    [McpServerTool(Name = "aibt_runtime_simulations")]
    [Description("List simulation containers (AIBT.Simulation.TreeSimulation) the game registered with TreeSimulationDebug.Register: groups, agent counts, traced agents. Read-only; empty with a hint when none is registered.")]
    public static string RuntimeSimulations() => BridgeClient.SendRequest("runtime_simulations", new JsonObject());

    [McpServerTool(Name = "aibt_describe_agent")]
    [Description("Explain one container agent: state, active path (root first, node names), root result, fault, last think tick and reasons, next think, live actions, and readable text. Read-only; busy=true during a step.")]
    public static string DescribeAgent(string simulation, int group, int agent, long generation = 0)
    {
        var payload = new JsonObject { ["simulation"] = simulation, ["group"] = group, ["agent"] = agent };
        if (generation > 0) payload["generation"] = generation;
        return BridgeClient.SendRequest("describe_agent", payload);
    }

    [McpServerTool(Name = "aibt_read_agent_trace")]
    [Description("Read a traced container agent's trace (the game selects agents with TreeGroup.TrySetTraced): native lifecycle records plus think reasons, inputs, commands, action events and faults, each with its tick. Limit 1..512, default 128; complete=false when records were dropped.")]
    public static string ReadAgentTrace(string simulation, int group, int agent, long generation = 0, ulong afterSequence = 0, int limit = 128)
    {
        var payload = new JsonObject { ["simulation"] = simulation, ["group"] = group, ["agent"] = agent, ["afterSequence"] = afterSequence, ["limit"] = limit };
        if (generation > 0) payload["generation"] = generation;
        return BridgeClient.SendRequest("read_agent_trace", payload);
    }
}
