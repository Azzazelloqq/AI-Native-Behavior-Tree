using Newtonsoft.Json.Linq;

namespace AIBT.Mcp.CustomTools
{
    /// <summary>Serializes a discovered provider's declared metadata for list_custom_tools.</summary>
    internal static class McpCustomToolsJson
    {
        internal static JObject WriteMetadata(ICustomMcpToolProvider provider)
        {
            return new JObject
            {
                ["toolName"] = provider.ToolName,
                ["description"] = provider.Description,
                ["inputSchema"] = provider.InputSchema,
                ["outputSchema"] = provider.OutputSchema,
                ["permissionCategory"] = provider.PermissionCategory.ToString(),
                ["sideEffects"] = new JArray(provider.SideEffects ?? System.Array.Empty<string>()),
                ["supportsCancellation"] = provider.SupportsCancellation,
                ["supportsDryRun"] = provider.SupportsDryRun,
                ["owningAssembly"] = CustomMcpToolProviderDiscovery.OwningAssemblyName(provider),
            };
        }
    }
}
