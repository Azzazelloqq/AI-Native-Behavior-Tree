using System;
using System.Collections.Generic;
using System.IO;
using AIBT.Mcp;
using AIBT.Mcp.CustomTools;
using Newtonsoft.Json.Linq;

namespace AIBT.SampleCustomTool
{
    /// <summary>
    /// P6-010's second sample provider: declares SemanticEdit and, unless dryRun is true, writes a
    /// real marker file under the project. Used by the permission-negative test (a session granted
    /// only Read must never reach Invoke, proven by the file never existing) and the dry-run test
    /// (a session granted SemanticEdit with dryRun=true must not persist anything either). The
    /// caller supplies exactly where the marker goes ("markerRelativePath") so the test can assert
    /// on that path without any compile-time reference to this type.
    /// </summary>
    public sealed class SampleMarkerFileCustomToolProvider : ICustomMcpToolProvider
    {
        public string ToolName => "aibt_custom_sample_write_marker";

        public string Description => "P6-010 sample custom tool: writes a marker file under the project (a stand-in for a real semantic-edit-shaped workflow).";

        public JObject InputSchema => new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject { ["markerRelativePath"] = new JObject { ["type"] = "string" } },
            ["required"] = new JArray("markerRelativePath"),
        };

        public JObject OutputSchema => new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject { ["written"] = new JObject { ["type"] = "boolean" } },
        };

        public McpPermissionCategory PermissionCategory => McpPermissionCategory.SemanticEdit;

        public IReadOnlyList<string> SideEffects => new[] { "writes-marker-file" };

        public bool SupportsCancellation => false;

        public bool SupportsDryRun => true;

        public JObject Invoke(string projectRoot, JObject args, bool dryRun)
        {
            var relativePath = args["markerRelativePath"]?.Value<string>();
            if (string.IsNullOrEmpty(relativePath))
            {
                throw new ArgumentException("'markerRelativePath' is required.");
            }

            var fullPath = Path.Combine(projectRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (dryRun)
            {
                return new JObject { ["written"] = false, ["dryRun"] = true, ["wouldWriteTo"] = relativePath };
            }

            File.WriteAllText(fullPath, "P6-010 sample marker.");
            return new JObject { ["written"] = true, ["dryRun"] = false };
        }
    }
}
