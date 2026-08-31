using System.Collections.Generic;
using AIBT.Mcp;
using AIBT.Mcp.CustomTools;
using Newtonsoft.Json.Linq;

namespace AIBT.SampleCustomTool
{
    /// <summary>
    /// P6-010's sample custom tool provider, deliberately living in its own assembly (no AIBT
    /// production or test assembly references it) to prove AIBT.Mcp discovers it purely via
    /// TypeCache. Declares Read and has no side effect -- the positive-path/dry-run-is-honored
    /// fixture, paired with SampleMarkerFileCustomToolProvider's permission-negative fixture.
    /// </summary>
    public sealed class SampleEchoCustomToolProvider : ICustomMcpToolProvider
    {
        public string ToolName => "aibt_custom_sample_echo";

        public string Description => "P6-010 sample custom tool: echoes its 'message' argument back.";

        public JObject InputSchema => new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject { ["message"] = new JObject { ["type"] = "string" } },
            ["required"] = new JArray("message"),
        };

        public JObject OutputSchema => new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject { ["echoed"] = new JObject { ["type"] = "string" } },
        };

        public McpPermissionCategory PermissionCategory => McpPermissionCategory.Read;

        public IReadOnlyList<string> SideEffects => System.Array.Empty<string>();

        public bool SupportsCancellation => false;

        public bool SupportsDryRun => true;

        public JObject Invoke(string projectRoot, JObject args, bool dryRun)
        {
            var message = args["message"]?.Value<string>() ?? string.Empty;
            return new JObject
            {
                ["echoed"] = message,
                ["dryRun"] = dryRun,
            };
        }
    }
}
