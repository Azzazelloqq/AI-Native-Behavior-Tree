using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AibtMcpServer;

// P6-010: unlike every other tool group in this project (all fixed [McpServerToolType] methods
// known at this process's own build time), a custom tool is registered by a consuming project and
// discovered on the Unity side via TypeCache (AIBT.Mcp.CustomTools.CustomMcpToolProviderDiscovery)
// -- this external process cannot know its name/schema ahead of time. CustomRelayMcpServerTool
// exposes one such tool as a real, individually-schema'd MCP tool via the SDK's own public
// extension point for this exact scenario: McpServerTool is an abstract class with a protected
// constructor and exactly three abstract members (ProtocolTool, Metadata, InvokeAsync), confirmed
// by reflection against the installed SDK before this was written -- not an undocumented hack.
// This is a startup-time snapshot (see Program.cs), not a live per-tools/list refresh: if Unity's
// bridge is unreachable when this process starts, zero custom tools are registered for that
// session (disclosed limitation, mirroring ADR-P6-001's own "client may need a restart to pick up
// a change" precedent). Startup must never fail because of this.
internal sealed class CustomRelayMcpServerTool : McpServerTool
{
    private readonly string _toolName;

    internal CustomRelayMcpServerTool(string toolName, string description, JsonElement inputSchema, JsonElement? outputSchema)
    {
        _toolName = toolName;
        ProtocolTool = new Tool
        {
            Name = toolName,
            Description = description,
            InputSchema = inputSchema,
            OutputSchema = outputSchema,
        };
    }

    public override Tool ProtocolTool { get; }

    public override IReadOnlyList<object> Metadata { get; } = Array.Empty<object>();

    public override ValueTask<CallToolResult> InvokeAsync(RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken)
    {
        var argsObject = new JsonObject();
        if (request.Params?.Arguments != null)
        {
            foreach (var (key, value) in request.Params.Arguments)
            {
                if (key == "dryRun")
                {
                    continue;
                }

                argsObject[key] = JsonNode.Parse(value.GetRawText());
            }
        }

        var dryRun = request.Params?.Arguments != null
            && request.Params.Arguments.TryGetValue("dryRun", out var dryRunElement)
            && dryRunElement.ValueKind == JsonValueKind.True;

        var requestPayload = new JsonObject
        {
            ["toolName"] = _toolName,
            ["args"] = argsObject,
            ["dryRun"] = dryRun,
        };

        var responseJson = BridgeClient.SendRequest("call_custom_tool", requestPayload);
        var responseNode = JsonNode.Parse(responseJson);
        var isError = responseNode?["error"] != null;

        // The SDK requires real StructuredContent (not just text) when a tool declares an
        // OutputSchema -- found live via the official Inspector CLI ("declares an output schema
        // but returned no structured content") the first time this was exercised end-to-end.
        var resultNode = responseNode?["result"];
        JsonElement? structuredContent = resultNode != null
            ? JsonDocument.Parse(resultNode.ToJsonString()).RootElement.Clone()
            : null;

        var result = new CallToolResult
        {
            Content = new List<ContentBlock> { new TextContentBlock { Text = responseJson } },
            IsError = isError,
            StructuredContent = structuredContent,
        };
        return ValueTask.FromResult(result);
    }
}

// Queries the bridge once for whatever custom tools are currently registered on the Unity side and
// builds one CustomRelayMcpServerTool per entry. Never throws -- an unreachable bridge or a
// malformed response just means zero custom tools for this server session.
internal static class CustomToolsLoader
{
    internal static IReadOnlyList<McpServerTool> LoadFromBridge()
    {
        var tools = new List<McpServerTool>();

        var responseJson = BridgeClient.SendRequest("list_custom_tools", new JsonObject());
        JsonNode? response;
        try
        {
            response = JsonNode.Parse(responseJson);
        }
        catch (JsonException)
        {
            return tools;
        }

        if (response?["result"]?["tools"] is not JsonArray toolsArray)
        {
            return tools;
        }

        foreach (var entry in toolsArray)
        {
            var name = entry?["toolName"]?.GetValue<string>();
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            var description = entry?["description"]?.GetValue<string>() ?? string.Empty;
            var inputSchemaNode = entry?["inputSchema"] as JsonObject ?? new JsonObject();
            var inputSchema = JsonDocument.Parse(inputSchemaNode.ToJsonString()).RootElement.Clone();

            JsonElement? outputSchema = entry?["outputSchema"] is JsonObject outputSchemaNode
                ? JsonDocument.Parse(outputSchemaNode.ToJsonString()).RootElement.Clone()
                : null;

            tools.Add(new CustomRelayMcpServerTool(name, description, inputSchema, outputSchema));
        }

        return tools;
    }
}
