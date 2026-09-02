using System.Collections.Generic;
using AIBT.Mcp;
using AIBT.Mcp.CustomTools;
using Newtonsoft.Json.Linq;

namespace AIBT.Samples.CustomMcpToolProvider
{
    /// <summary>
    /// A minimal, real <see cref="ICustomMcpToolProvider"/> implementation (`P6-010`'s own IoC
    /// extension point, `Documentation~/ai-and-mcp.md`'s "Custom MCP tools" section) -- a project
    /// registers a tool like this to expose its own high-level workflow (e.g. "create a combat
    /// subtree") through the MCP server, alongside AIBT's own built-in generic tools. AIBT never
    /// references this assembly directly; <see cref="CustomMcpToolProviderDiscovery"/> finds it via
    /// <c>UnityEditor.TypeCache</c> at server-attach time, purely because it implements the
    /// interface and has a public parameterless constructor -- import this sample, and the tool is
    /// discoverable with no further registration step.
    /// </summary>
    public sealed class SampleGreetingToolProvider : ICustomMcpToolProvider
    {
        public string ToolName => "aibt_sample_greeting";

        public string Description => "Sample custom tool: returns a greeting for the given name.";

        public JObject InputSchema => new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject { ["name"] = new JObject { ["type"] = "string" } },
            ["required"] = new JArray("name"),
        };

        public JObject OutputSchema => new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject { ["greeting"] = new JObject { ["type"] = "string" } },
        };

        public McpPermissionCategory PermissionCategory => McpPermissionCategory.Read;

        public IReadOnlyList<string> SideEffects => System.Array.Empty<string>();

        public bool SupportsCancellation => false;

        public bool SupportsDryRun => false;

        public JObject Invoke(string projectRoot, JObject args, bool dryRun)
        {
            var name = args["name"]?.Value<string>() ?? "world";
            return new JObject
            {
                ["greeting"] = "Hello, " + name + "! (from a custom MCP tool provider)",
            };
        }
    }
}
