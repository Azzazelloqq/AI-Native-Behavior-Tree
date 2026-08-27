using System.ComponentModel;
using ModelContextProtocol.Server;

namespace AibtMcpSpikeServer;

// Disposable P6-001 spike tool. Proves an external dotnet MCP server (Candidate A')
// round-trips end-to-end against a real MCP client. Never wired to any AIBT
// Authoring/Runtime/Editor API -- transport proof only.
[McpServerToolType]
public static class SpikeTools
{
    [McpServerTool(Name = "aibt_spike_ping")]
    [Description("No-op spike tool for the AIBT P6-001 MCP transport spike. Echoes the input back.")]
    public static string Ping(
        [Description("Any short text to echo back.")] string message = "")
        => $"AIBT MCP spike pong: {message}";
}
