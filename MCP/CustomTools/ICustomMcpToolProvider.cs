using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace AIBT.Mcp.CustomTools
{
    /// <summary>
    /// P6-010's IoC contract for a project-owned high-level MCP tool (ai-and-mcp.md's "Custom MCP
    /// tools" section). A consuming project implements this on a concrete class with a public
    /// parameterless constructor and AIBT discovers it via <see cref="CustomMcpToolProviderDiscovery"/>
    /// -- no AIBT assembly ever references the implementing assembly directly.
    ///
    /// "Owning assembly" is not a member here: the host derives it itself from the concrete type's
    /// own <see cref="System.Reflection.Assembly"/> at discovery time, so a provider never has to
    /// restate a fact the host can always compute.
    ///
    /// <see cref="SupportsCancellation"/> is a declared capability only. The MCP bridge's wire
    /// protocol (<c>McpBridgeListener</c>/<c>BridgeClient</c>) is a single blocking request/response
    /// with no cancellation transport for any tool, built-in or custom -- this flag is surfaced for
    /// documentation, never mechanically enforced. Disclosed explicitly, not silently implied.
    /// </summary>
    public interface ICustomMcpToolProvider
    {
        /// <summary>Stable MCP tool name, e.g. "aibt_custom_create_combat_subtree". Must be unique
        /// among all discovered providers and must not collide with a built-in AIBT MCP tool name.</summary>
        string ToolName { get; }

        string Description { get; }

        /// <summary>Raw JSON Schema for the tool's input arguments.</summary>
        JObject InputSchema { get; }

        /// <summary>Raw JSON Schema for the tool's output, or null if unspecified.</summary>
        JObject OutputSchema { get; }

        /// <summary>The single ADR-P6-001 category this tool is checked against -- the same
        /// McpPermissionEnforcer mechanism every built-in tool uses, not a parallel one.</summary>
        McpPermissionCategory PermissionCategory { get; }

        /// <summary>Free-form declared side-effect labels for documentation (e.g. "writes-tree-file").</summary>
        IReadOnlyList<string> SideEffects { get; }

        /// <summary>Declared only -- see the type-level remarks. Not mechanically enforced.</summary>
        bool SupportsCancellation { get; }

        /// <summary>Whether a dryRun=true call is expected to validate/report without persisting.
        /// Enforcement of the tool's own no-persistence contract in dry-run mode is best-effort:
        /// the host cannot see into project-owned code, so this is honored by the provider's own
        /// implementation, not verified by the host beyond passing the flag through.</summary>
        bool SupportsDryRun { get; }

        /// <summary>Invoked only after the host has confirmed the calling session was granted
        /// <see cref="PermissionCategory"/>. <paramref name="projectRoot"/> is the same Assets-folder
        /// path every built-in tool dispatcher receives.</summary>
        JObject Invoke(string projectRoot, JObject args, bool dryRun);
    }
}
