using System.Collections.Generic;

namespace AIBT.Mcp
{
    /// <summary>
    /// The single real list of bridge-level tool keys McpToolDispatcher's own switch registers --
    /// promoted out of P6-010's CustomMcpToolProviderDiscovery (which needed it first, for
    /// collision detection) so P6-011's generated workflow guide can reference the same list
    /// instead of hand-duplicating it a second time. Keep in sync with McpToolDispatcher.Dispatch's
    /// own switch cases directly, the same way every other P6 card's diagnostics-range comment
    /// tracks the prior card's own allocation.
    /// </summary>
    internal static class McpBuiltInTools
    {
        internal static readonly IReadOnlyList<string> BridgeToolNames = new[]
        {
            "get_project_manifest", "search_nodes", "get_node_contract", "get_static_resource",
            "create_tree", "add_node", "remove_node", "move_node", "replace_node", "configure_node",
            "set_blackboard_keys", "extract_subtree", "inline_subtree", "apply_domain_patch", "request_layout",
            "validate", "compile", "simulate", "explain_diagnostic",
            "run_tests", "run_benchmark",
            "generate_node", "preview_node_diff", "generate_node_tests_and_manifest",
            "analyze_and_compile_node", "test_node", "apply_node",
            "list_custom_tools", "call_custom_tool",
        };
    }
}
