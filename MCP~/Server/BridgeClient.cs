using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;

namespace AibtMcpServer;

// Thin relay to the Unity-side bridge (AIBT.Mcp.McpBridgeListener). All real logic --
// including permission enforcement -- lives in the bridge; this holds only the session's
// granted-permission set (from its own environment) and forwards it with every request.
internal static class BridgeClient
{
    internal static string SendRequest(string tool, JsonObject args)
    {
        var discoveryPath = Environment.GetEnvironmentVariable("AIBT_MCP_DISCOVERY_FILE") ?? "Library/AibtMcp.json";
        if (!File.Exists(discoveryPath))
        {
            return ErrorJson("AIBT9014", "AIBT MCP bridge is not running (no discovery file at '" + discoveryPath +
                "'). Open Unity and start it via AIBT > MCP > Bridge.");
        }

        int port;
        try
        {
            var discovery = JsonNode.Parse(File.ReadAllText(discoveryPath));
            port = discovery!["port"]!.GetValue<int>();
        }
        catch (Exception ex)
        {
            return ErrorJson("AIBT9014", "Could not read the bridge discovery file: " + ex.Message);
        }

        var permissionsEnv = Environment.GetEnvironmentVariable("AIBT_MCP_PERMISSIONS") ?? string.Empty;
        var grantedCategories = new JsonArray();
        foreach (var category in permissionsEnv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            grantedCategories.Add(JsonValue.Create(category));
        }

        var request = new JsonObject
        {
            ["tool"] = tool,
            ["args"] = args,
            ["grantedCategories"] = grantedCategories,
        };

        try
        {
            using var client = new TcpClient();
            client.Connect("127.0.0.1", port);
            using var stream = client.GetStream();
            using var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
            using var reader = new StreamReader(stream, Encoding.UTF8);
            writer.WriteLine(request.ToJsonString());
            return reader.ReadLine() ?? ErrorJson("AIBT9014", "The bridge closed the connection without responding.");
        }
        catch (SocketException ex)
        {
            return ErrorJson("AIBT9014", "Could not connect to the bridge on port " + port + ": " + ex.Message);
        }
    }

    private static string ErrorJson(string code, string message)
    {
        return new JsonObject { ["error"] = new JsonObject { ["code"] = code, ["message"] = message } }.ToJsonString();
    }
}
