using System.ComponentModel;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;

namespace AibtMcpServer;

// Thin relays to AIBT.Mcp.Testing.McpTestingToolDispatcher (P6-008, narrowed 2026-08-29) over the
// bridge TCP connection, mirroring VerificationTools.cs/AuthoringTools.cs's exact shape. No logic
// of any kind lives here.
[McpServerToolType]
public static class TestingTools
{
    [McpServerTool(Name = "aibt_run_tests")]
    [Description("Runs one .aibtcase.json behavior case through the real, promoted Phase 1 headless runner (BehaviorCaseRunner, driving ReferenceExecutionMachine against ReferencePreviewFixtureEnvironment's registries -- the same Phase 1 fixture/built-in node set aibt_simulate uses), returning pass/fail plus per-step diagnostics. Never mutates anything.")]
    public static string RunTests(
        [Description("Path to the .aibtcase.json case file, relative to the project's Assets folder.")] string casePath)
        => BridgeClient.SendRequest("run_tests", new JsonObject { ["casePath"] = casePath });

    [McpServerTool(Name = "aibt_run_benchmark")]
    [Description("Runs one of the 6 P4-001-approved scheduling benchmark scenarios through the real, promoted SchedulingPolicyDriver under one fixed same-frame policy (immediate/budgeted/batched-jobs-same-frame), returning raw measured numbers (elapsed time, total steps) plus environment metadata -- no threshold, default, or performance claim. A placeholder (documented but not yet implemented) or unknown scenario name is refused with a structured diagnostic, never silently substituted.")]
    public static string RunBenchmark(
        [Description("Scenario name from the P4-001 catalog, e.g. 'scheduling-baseline-empty-job'.")] string scenario,
        [Description("Number of independent native tree-instance agents to construct and drive.")] int agentCount,
        [Description("One of: immediate, budgeted, batched-jobs-same-frame.")] string policy,
        [Description("Optional step budget for the 'budgeted' policy (default 4, matching P4-001's own harness default).")] int? stepBudget = null,
        [Description("Optional batch size for the 'batched-jobs-same-frame' policy (default 32, matching P4-001's own harness default).")] int? batchSize = null)
    {
        var args = new JsonObject
        {
            ["scenario"] = scenario,
            ["agentCount"] = agentCount,
            ["policy"] = policy,
        };
        if (stepBudget.HasValue) args["stepBudget"] = stepBudget.Value;
        if (batchSize.HasValue) args["batchSize"] = batchSize.Value;
        return BridgeClient.SendRequest("run_benchmark", args);
    }
}
