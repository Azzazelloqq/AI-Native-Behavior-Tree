using System.ComponentModel;
using ModelContextProtocol.Server;

namespace AibtMcpSpikeServer;

// Disposable P6-001 spike resource. Proves resource listing/reading round-trips
// end-to-end against a real MCP client, mirroring SpikeTools.cs.
[McpServerResourceType]
public static class SpikeResources
{
    [McpServerResource(UriTemplate = "aibt-spike://status", Name = "AIBT MCP spike status", MimeType = "text/plain")]
    [Description("Static status text proving the AIBT MCP transport spike (P6-001) exposes a real resource.")]
    public static string Status()
        => "AIBT MCP transport spike (P6-001): external dotnet process, Candidate A'.";
}
