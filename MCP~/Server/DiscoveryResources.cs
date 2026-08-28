using System.ComponentModel;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;

namespace AibtMcpServer;

// One concrete resource per allowlisted key (not one URI-templated resource) so every one is
// enumerable via resources/list -- an MCP resource template with a {placeholder} is not listed
// that way, only via resources/templates/list, which does not satisfy this card's own
// "list resources" acceptance requirement.
[McpServerResourceType]
public static class DiscoveryResources
{
    [McpServerResource(UriTemplate = "aibt://resource/ai-and-mcp", Name = "AIBT AI-and-MCP contract", MimeType = "text/plain")]
    [Description("Documentation~/ai-and-mcp.md -- the normative AI/MCP contract this server itself implements.")]
    public static string AiAndMcp() => Fetch("ai-and-mcp");

    [McpServerResource(UriTemplate = "aibt://resource/schema.behavior-case", Name = "AIBT behavior-case schema", MimeType = "text/plain")]
    [Description("Schemas~/behavior-case.schema.json")]
    public static string SchemaBehaviorCase() => Fetch("schema.behavior-case");

    [McpServerResource(UriTemplate = "aibt://resource/schema.layout", Name = "AIBT layout schema", MimeType = "text/plain")]
    [Description("Schemas~/layout.schema.json")]
    public static string SchemaLayout() => Fetch("schema.layout");

    [McpServerResource(UriTemplate = "aibt://resource/schema.node-manifest", Name = "AIBT node-manifest schema", MimeType = "text/plain")]
    [Description("Schemas~/node-manifest.schema.json")]
    public static string SchemaNodeManifest() => Fetch("schema.node-manifest");

    [McpServerResource(UriTemplate = "aibt://resource/schema.policy", Name = "AIBT policy schema", MimeType = "text/plain")]
    [Description("Schemas~/policy.schema.json")]
    public static string SchemaPolicy() => Fetch("schema.policy");

    [McpServerResource(UriTemplate = "aibt://resource/schema.tree", Name = "AIBT tree schema", MimeType = "text/plain")]
    [Description("Schemas~/tree.schema.json")]
    public static string SchemaTree() => Fetch("schema.tree");

    [McpServerResource(UriTemplate = "aibt://resource/schema.work-item-index", Name = "AIBT work-item-index schema", MimeType = "text/plain")]
    [Description("Schemas~/work-item-index.schema.json")]
    public static string SchemaWorkItemIndex() => Fetch("schema.work-item-index");

    private static string Fetch(string key)
    {
        var responseLine = BridgeClient.SendRequest("get_static_resource", new JsonObject { ["key"] = key });
        var response = JsonNode.Parse(responseLine);

        if (response!["error"] != null)
        {
            return responseLine;
        }

        var result = response["result"]!;
        return result["found"]!.GetValue<bool>()
            ? result["content"]!.GetValue<string>()
            : "Resource key '" + key + "' not found.";
    }
}
