using System;
using System.Linq;
using System.Text;

namespace AIBT.Mcp.Documentation
{
    /// <summary>
    /// Generates the short agent workflow guide. Content is static (the workflow shape doesn't vary
    /// per project), but every tool name referenced is validated against the real
    /// McpBuiltInTools.BridgeToolNames list at generation time -- a typo or a tool renamed out from
    /// under this document fails loudly instead of silently drifting.
    /// </summary>
    internal static class McpWorkflowGuideGenerator
    {
        internal static string Generate()
        {
            var builder = new StringBuilder();
            builder.Append("# AIBT MCP agent workflow guide (generated)\n\n");
            builder.Append("Reflects the actual registered MCP tools (see `AIBT.Mcp.McpBuiltInTools`), not an idealized set. ");
            builder.Append("Regenerate with the `AIBT/MCP/Regenerate Documentation` Editor menu command.\n\n");

            builder.Append("## 1. Connect\n\n");
            builder.Append("Start the Unity-side bridge from the open Editor (`AIBT/MCP/Bridge` -> Start), then launch the AI client ");
            builder.Append("with the external server configured (`dotnet run --project <path to>/MCP~/Server`), per `ADR-P6-001`. ");
            builder.Append("The client and the Editor must be on the same machine; the bridge must be running before the server process starts.\n\n");

            builder.Append("## 2. Discover\n\n");
            builder.Append("Call ").Append(Tool("get_project_manifest")).Append(" for capabilities, project policy, and the tree/revision listing; ");
            builder.Append(Tool("search_nodes")).Append(" to search the node catalog by keyword; ");
            builder.Append(Tool("get_node_contract")).Append(" for one node type's full contract. ");
            builder.Append("Also see the generated node catalog document for every node's contract at once.\n\n");

            builder.Append("## 3. Author\n\n");
            builder.Append("Create a tree with ").Append(Tool("create_tree")).Append(", then edit it with ");
            builder.Append(Tool("add_node")).Append(", ").Append(Tool("remove_node")).Append(", ").Append(Tool("move_node")).Append(", ");
            builder.Append(Tool("replace_node")).Append(", ").Append(Tool("configure_node")).Append(", or ");
            builder.Append(Tool("set_blackboard_keys")).Append(". Use ").Append(Tool("extract_subtree")).Append("/");
            builder.Append(Tool("inline_subtree")).Append(" to move a subtree between trees, ");
            builder.Append(Tool("apply_domain_patch")).Append(" to apply several operations as one atomic transaction, and ");
            builder.Append(Tool("request_layout")).Append(" afterward to lay out the affected region. ");
            builder.Append("Every mutating call takes the target's current `expectedHash`/`contentHash` -- always use the value the last accepted call returned, never assume a fixed increment (`ADR-P6-002`).\n\n");

            builder.Append("## 4. Verify\n\n");
            builder.Append("Run ").Append(Tool("validate")).Append(" and ").Append(Tool("compile")).Append(" against a tree, ");
            builder.Append(Tool("simulate")).Append(" to step it through the Phase 1 reference executor, ");
            builder.Append(Tool("run_tests")).Append(" against a `.aibtcase.json` behavior case, and ");
            builder.Append(Tool("run_benchmark")).Append(" against a real P4-001 scheduling scenario. ");
            builder.Append("Use ").Append(Tool("explain_diagnostic")).Append(" to look up a returned diagnostic code's stable meaning.\n\n");

            builder.Append("## 5. Add a custom node\n\n");
            builder.Append("The generate-compile-apply gate: ").Append(Tool("generate_node")).Append(" (stages a new node from a template), ");
            builder.Append(Tool("preview_node_diff")).Append(" (inspect the staged source before compiling), ");
            builder.Append(Tool("generate_node_tests_and_manifest")).Append(" (stages a paired test scaffold), ");
            builder.Append(Tool("analyze_and_compile_node")).Append(" (two-call, non-blocking compile check -- call with `mode='start'`, then poll with `mode='check'`), ");
            builder.Append(Tool("test_node")).Append(" (proves the compiled shard is registry-materializable), and finally ");
            builder.Append(Tool("apply_node")).Append(" (the only step that persists into the real project). ");
            builder.Append("Nothing before `apply_node` touches the real project.\n\n");

            builder.Append("## 6. Custom project tools\n\n");
            builder.Append("Call ").Append(Tool("list_custom_tools")).Append(" to discover any project-registered custom tools, and ");
            builder.Append(Tool("call_custom_tool")).Append(" to invoke one by name.\n");

            return builder.ToString();
        }

        private static string Tool(string bridgeName)
        {
            if (!McpBuiltInTools.BridgeToolNames.Contains(bridgeName, StringComparer.Ordinal))
            {
                throw new InvalidOperationException("'" + bridgeName + "' is not a real registered MCP tool -- the workflow guide must never reference an invented tool name.");
            }

            return "`aibt_" + bridgeName + "`";
        }
    }
}
