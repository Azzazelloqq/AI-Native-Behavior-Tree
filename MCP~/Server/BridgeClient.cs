using System.Text.Json.Nodes;
using AibtTransport;

namespace AibtMcpServer;

internal static class BridgeClient
{
    internal static string SendRequest(string tool, JsonObject args)
    {
        try { return EditorBridgeClient.FromEnvironment().Call(tool, args).ToJsonString(); }
        catch (Exception exception) when (exception is IOException or System.Net.Sockets.SocketException or System.Text.Json.JsonException or InvalidOperationException)
        {
            return new JsonObject { ["error"] = new JsonObject { ["code"] = "AIBT9014", ["message"] = exception.Message } }.ToJsonString();
        }
    }
}
