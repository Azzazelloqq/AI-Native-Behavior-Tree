using System;
using AIBT.Mcp.Authoring;
using Newtonsoft.Json.Linq;

namespace AIBT.Mcp.CustomTools
{
    /// <summary>
    /// Implements P6-010's two dispatcher-level operations: listing every discovered custom tool's
    /// declared metadata, and invoking one by name. Discovery re-runs fresh on every call, matching
    /// the "no live session, reload every call" shape every other P6-003..P6-009 tool already uses.
    /// Permission enforcement itself lives in McpToolDispatcher (the same McpPermissionEnforcer path
    /// every built-in tool goes through) -- this class never checks permissions on its own.
    /// </summary>
    internal static class McpCustomToolsToolDispatcher
    {
        internal static CustomMcpToolProviderDiscovery.BuildResult DiscoverAndBuild()
        {
            return CustomMcpToolProviderDiscovery.Build(CustomMcpToolProviderDiscovery.DiscoverViaTypeCache());
        }

        internal static JObject ListCustomTools()
        {
            var build = DiscoverAndBuild();
            var tools = new JArray();
            foreach (var provider in build.ByToolName.Values)
            {
                tools.Add(McpCustomToolsJson.WriteMetadata(provider));
            }

            return new JObject
            {
                ["tools"] = tools,
                ["diagnostics"] = McpDiagnosticJson.WriteDiagnostics(build.Diagnostics),
            };
        }

        internal static JObject Call(ICustomMcpToolProvider provider, string projectRoot, JObject args)
        {
            var nestedArgs = args["args"] as JObject ?? new JObject();
            var dryRun = args["dryRun"]?.Value<bool>() ?? false;

            try
            {
                return provider.Invoke(projectRoot, nestedArgs, dryRun);
            }
            catch (McpToolException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new McpToolException(
                    McpCustomToolsDiagnostics.ProviderInvocationFailed,
                    "Custom tool '" + provider.ToolName + "' threw " + ex.GetType().Name + ": " + ex.Message);
            }
        }
    }
}
