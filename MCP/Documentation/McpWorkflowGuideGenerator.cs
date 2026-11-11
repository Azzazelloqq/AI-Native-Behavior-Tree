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
            builder.Append("Use CLI as the command entry point: see [CLI and MCP setup](../cli.md). Open the selected Unity project, then run `aibt --project <UnityProject> mcp ensure`; ");
            builder.Append("commands start/recover its Editor bridge on demand. MCP clients can launch the built CLI with `mcp serve`, or retain direct server configuration with `AIBT_PROJECT_PATH` and explicit `AIBT_MCP_PERMISSIONS`. ");
            builder.Append("The client and Editor must be on the same machine. No manual Start is needed for this path; closed or unresponsive Unity produces a bounded error.\n\n");

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
            builder.Append(Tool("generate_node_tests_and_manifest")).Append(" (stages expected-outcome cases consumed by native test-node), ");
            builder.Append(Tool("analyze_and_compile_node")).Append(" (two-call, non-blocking compile check -- call with `mode='start'`, then poll with `mode='check'`), ");
            builder.Append(Tool("test_node")).Append(" (reports registry/dispatch proof separately from expected-outcome behaviorProven), and finally ");
            builder.Append(Tool("apply_node")).Append(" (the only step that persists into the real project). ");
            builder.Append("Before `apply_node`, changes stay in the quarantined staging directory.\n\n");

            builder.Append("## 6. Custom project tools\n\n");
            builder.Append("Call ").Append(Tool("list_custom_tools")).Append(" to discover any project-registered custom tools, and ");
            builder.Append(Tool("call_custom_tool")).Append(" to invoke one by name.\n");

            builder.Append("\n## 7. Native consumer workflow\n\n");
            builder.Append("Use ").Append(Tool("list_operations")).Append(" and ").Append(Tool("get_operation_schema")).Append(" for bridge argument schemas and permissions. ");
            builder.Append(Tool("inspect_tree")).Append(" returns the source and current contentHash; ").Append(Tool("runtime_catalogs")).Append(" lists generated catalogs. ");
            builder.Append(Tool("prepare_runtime")).Append(" prepares a native asset under Assets with source-hash, dry-run and explicit overwrite preconditions. ");
            builder.Append("After starting the game's own integration, ").Append(Tool("runtime_hosts")).Append(" identifies hosts and ").Append(Tool("read_trace")).Append(" reads bounded native history without completing jobs. ");
            builder.Append("Reference compile/simulate are separate from native preparation/execution. ").Append(Tool("migrate_document")).Append(" persists registered document migrations. ");
            builder.Append("Project documentation is generated under Assets/AIBT-Documentation; runtime resources resolve the installed package root.\n");

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
