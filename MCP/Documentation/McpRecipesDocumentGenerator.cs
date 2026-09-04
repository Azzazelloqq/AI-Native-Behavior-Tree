using System.Text;

namespace AIBT.Mcp.Documentation
{
    /// <summary>
    /// Generates the recipes document. Each tool-call JSON snippet is transcribed from the real
    /// MCP~/Server/*.cs source (the schemas a real MCP client actually sees), and every recipe is
    /// live-verified against a real MCP client before this card is accepted -- the "not paraphrased"
    /// guarantee comes from that live proof, not from a mechanical schema import (the external
    /// dotnet server's compiled schemas are not reachable from Unity-compiled code at all; see this
    /// card's own evidence for why). "Inspect a trace" (the card's original fourth recipe) is
    /// replaced by "run a test": P6-008 found no production code wires a real trace channel to a
    /// live pass, so a trace recipe would claim a capability that does not exist.
    /// </summary>
    internal static class McpRecipesDocumentGenerator
    {
        internal static string Generate()
        {
            var builder = new StringBuilder();
            builder.Append("# AIBT MCP recipes (generated)\n\n");
            builder.Append("Verification evidence is recorded under `Planning~/Evidence/P6-011/` and, for the updated node-compilation protocol, `Planning~/Evidence/P7-031/`. ");
            builder.Append("Regenerate with the `AIBT/MCP/Regenerate Documentation` Editor menu command.\n\n");

            builder.Append("## Recipe: create and validate a tree\n\n");
            builder.Append("Goal: create a new tree with one root node, add a child, then validate it.\n\n");
            builder.Append("```json\n");
            builder.Append("1) aibt_create_tree {\"treeId\": \"tree.my-tree\", \"name\": \"My Tree\", \"path\": \"MyTree.aibt.json\",\n");
            builder.Append("   \"rootNodeJson\": \"{\\\"id\\\":\\\"root\\\",\\\"typeId\\\":\\\"aibt.core.memory-sequence\\\",\\\"typeVersion\\\":1}\"}\n");
            builder.Append("   -> {\"accepted\": true, \"contentHash\": \"<hash>\", \"path\": \"MyTree.aibt.json\", \"diagnostics\": []}\n\n");
            builder.Append("2) aibt_add_node {\"treeId\": \"tree.my-tree\", \"expectedHash\": \"<hash from step 1>\", \"parentId\": \"root\",\n");
            builder.Append("   \"nodeJson\": \"{\\\"id\\\":\\\"child-1\\\",\\\"typeId\\\":\\\"aibt.core.memory-sequence\\\",\\\"typeVersion\\\":1}\"}\n");
            builder.Append("   -> {\"accepted\": true, \"contentHash\": \"<new hash>\", ...}\n\n");
            builder.Append("3) aibt_validate {\"treeId\": \"tree.my-tree\"}\n");
            builder.Append("   -> {\"valid\": true, \"policyApplied\": <bool>, \"diagnostics\": []}\n");
            builder.Append("```\n\n");
            builder.Append("Always use the `contentHash` the *previous accepted call* returned as the next call's `expectedHash` -- never assume a fixed increment (`ADR-P6-002`).\n\n");

            builder.Append("## Recipe: generate, compile, and apply a custom node\n\n");
            builder.Append("Goal: scaffold a new Burst condition node, compile it for real, and persist it into the project. The full P6-009 gate.\n\n");
            builder.Append("```json\n");
            builder.Append("1) aibt_generate_node {\"kind\": \"condition\", \"typeId\": \"aibt.myproject.my-condition\", \"ns\": \"MyProject.Nodes\",\n");
            builder.Append("   \"summary\": \"...\", \"category\": \"MyProject/Conditions\", \"whenToUse\": \"...\", \"whenNotToUse\": \"...\",\n");
            builder.Append("   \"blackboardReadKey\": \"someKey\", \"blackboardReadType\": \"Bool\"}\n");
            builder.Append("   -> stages the node into the single reserved staging slot; overwrites any prior pending generation.\n\n");
            builder.Append("2) aibt_preview_node_diff {}\n");
            builder.Append("   -> returns the exact staged file content -- never mutates or compiles anything.\n\n");
            builder.Append("3) aibt_generate_node_tests_and_manifest {}\n");
            builder.Append("   -> stages a paired test scaffold (an honest placeholder pending P6-022).\n\n");
            builder.Append("4) aibt_analyze_and_compile_node {\"mode\": \"start\"}\n");
            builder.Append("   -> {\"status\": \"pending\", \"attemptId\": \"<id>\"}; requests a new compilation of the captured staged content.\n\n");
            builder.Append("5) aibt_analyze_and_compile_node {\"mode\": \"check\", \"attemptId\": \"<id from step 4>\"}\n");
            builder.Append("   -> repeat while status is 'pending'/'still-compiling', including across domain reload; 'compiled' returns contentHash, 'failed' returns diagnostics. Changed staging or an expired attempt requires a fresh start.\n\n");
            builder.Append("6) aibt_test_node {\"expectedContentHash\": \"<contentHash from step 5>\"}\n");
            builder.Append("   -> checks metadata and registry materialization; dispatchProven reports whether native dispatch was exercised for the supported binding types.\n\n");
            builder.Append("7) aibt_apply_node {\"expectedContentHash\": \"<contentHash from step 5>\", \"destinationPath\": \"MyProject/GeneratedNodes/MyCondition\"}\n");
            builder.Append("   -> the only step that persists into the real project; re-verifies the hash and re-runs the checks itself first.\n");
            builder.Append("```\n\n");

            builder.Append("## Recipe: run a scheduling benchmark\n\n");
            builder.Append("Goal: measure the fixed per-tick overhead of the smallest possible tree under the `immediate` policy.\n\n");
            builder.Append("```json\n");
            builder.Append("aibt_run_benchmark {\"scenario\": \"scheduling-baseline-empty-job\", \"agentCount\": 4, \"policy\": \"immediate\"}\n");
            builder.Append("-> {\"scenario\": \"scheduling-baseline-empty-job\", \"agentCount\": 4, \"policy\": \"immediate\",\n");
            builder.Append("    \"totalSteps\": <n>, \"elapsedMicroseconds\": <raw measured number>, ...}\n");
            builder.Append("```\n\n");
            builder.Append("No threshold or performance claim is attached to the result -- it is a raw measurement (`P4-002`'s own discipline).\n\n");

            builder.Append("## Recipe: run a behavior-case test\n\n");
            builder.Append("Goal: run a `.aibtcase.json` fixture through the real headless behavior-case runner.\n\n");
            builder.Append("```json\n");
            builder.Append("aibt_run_tests {\"casePath\": \"AIBT/Tests/Editor/Mcp/Testing/Fixtures/success-then-running.aibtcase.json\"}\n");
            builder.Append("-> {\"success\": true, \"executedStepCount\": 1, \"inputDiagnostics\": [], \"failures\": []}\n");
            builder.Append("```\n");

            return builder.ToString();
        }
    }
}
