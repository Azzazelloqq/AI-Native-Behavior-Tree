using System;
using System.Diagnostics;
using System.IO;
using AIBT.Authoring.BehaviorCases;
using AIBT.Authoring.Benchmarking;
using AIBT.Mcp.Authoring;
using AIBT.Runtime.Scheduling;
using Newtonsoft.Json.Linq;
using Unity.Collections;

namespace AIBT.Mcp.Testing
{
    /// <summary>
    /// Implements the 2 tools this card owns (P6-008, narrowed 2026-08-29 to run-tests/
    /// run-benchmark -- see this card's own "Scope correction" and P6-015): run-tests wraps
    /// the promoted Phase 1 behavior-case runner (<see cref="BehaviorCaseRunner"/>, P1-017,
    /// promoted from Tests/BehaviorCases/Framework/ into AIBT.Authoring.BehaviorCases by this
    /// card), and run-benchmark wraps the promoted Phase 4 scheduling driver
    /// (<see cref="SchedulingPolicyDriver"/>, P4-001, promoted from Tests/Runtime/Benchmarking/
    /// into AIBT.Runtime.Scheduling by this card) and its approved scenario catalog
    /// (<see cref="SchedulingScenarios"/>, promoted from Benchmarks~/Phase4/Scheduling/Unity/ into
    /// AIBT.Authoring.Benchmarking by this card). No second runner/driver/catalog exists here --
    /// both promotions moved the real, already-accepted logic unchanged; this file only resolves
    /// arguments and serializes results. Called from McpToolDispatcher only after permission
    /// enforcement.
    /// </summary>
    internal static class McpTestingToolDispatcher
    {
        // ---- run-tests -------------------------------------------------------------------------

        internal static JObject RunTests(string projectRoot, JObject args)
        {
            var casePath = RequireString(args, "casePath");
            var fullCasePath = ResolveUnderRoot(projectRoot, casePath, McpTestingDiagnostics.CaseNotFound, "case");
            if (!File.Exists(fullCasePath))
            {
                throw new McpToolException(McpTestingDiagnostics.CaseNotFound, "No behavior case was found at '" + casePath + "'.");
            }

            var treeRoot = Path.GetDirectoryName(fullCasePath);
            var factory = new AuthoringBehaviorCaseExecutorFactory(treeRoot);
            var bytes = File.ReadAllBytes(fullCasePath);
            var result = BehaviorCaseRunner.Run(bytes, casePath, factory, BehaviorCaseRegisteredValueRegistry.Empty);

            return McpTestingJson.WriteRunResult(result);
        }

        // ---- run-benchmark ----------------------------------------------------------------------

        private const uint DefaultBudgetStepLimit = 4;
        private const uint DefaultBatchSize = 32;

        internal static JObject RunBenchmark(string projectRoot, JObject args)
        {
            var scenarioName = RequireString(args, "scenario");
            var agentCount = RequireInt(args, "agentCount");
            var policy = RequireString(args, "policy");
            if (agentCount < 1)
            {
                throw new McpToolException(McpTestingDiagnostics.MalformedArguments, "'agentCount' must be at least 1.");
            }

            SchedulingScenarios.ScenarioDefinition? found = null;
            foreach (var definition in SchedulingScenarios.Catalog)
            {
                if (definition.Name == scenarioName)
                {
                    found = definition;
                    break;
                }
            }

            if (found == null)
            {
                throw new McpToolException(McpTestingDiagnostics.UnknownScenario, "'" + scenarioName + "' is not a scenario in the approved P4-001 catalog.");
            }

            var definitionValue = found.Value;
            if (!definitionValue.Implemented)
            {
                throw new McpToolException(McpTestingDiagnostics.ScenarioNotImplemented, "'" + scenarioName + "' is a documented catalog placeholder with no implementation yet -- refusing rather than substituting an implemented scenario.");
            }

            var compiled = definitionValue.Build();
            if (!SchedulingPolicyDriver.TryCreateAgents(compiled.Program, compiled.NodeKinds, agentCount, Allocator.Persistent, out var agents, out var createFailure))
            {
                throw new McpToolException(McpTestingDiagnostics.MalformedArguments, "Agent construction failed: " + createFailure.Code + ".");
            }

            try
            {
                ulong totalSteps;
                bool ran;
                NativeRuntimeFailureV1 runFailure;
                var stopwatch = Stopwatch.StartNew();
                switch (policy)
                {
                    case "immediate":
                        ran = SchedulingPolicyDriver.TryRunImmediate(agents, 1, compiled.LeafStatusByRuntimeIndex, out totalSteps, out runFailure);
                        break;
                    case "budgeted":
                        var stepBudget = OptionalUInt(args, "stepBudget") ?? DefaultBudgetStepLimit;
                        var budgetStates = new NativeBudgetStateV1[agentCount];
                        ran = SchedulingPolicyDriver.TryRunBudgeted(agents, budgetStates, 1, stepBudget, compiled.LeafStatusByRuntimeIndex, out totalSteps, out runFailure);
                        break;
                    case "batched-jobs-same-frame":
                        var batchSize = OptionalUInt(args, "batchSize") ?? DefaultBatchSize;
                        ran = SchedulingPolicyDriver.TryRunBatchedJobsSameFrame(agents, 1, batchSize, compiled.LeafStatusByRuntimeIndex, Allocator.Persistent, out totalSteps, out runFailure);
                        break;
                    default:
                        throw new McpToolException(McpTestingDiagnostics.UnknownPolicy, "'" + policy + "' is not one of the three fixed same-frame policies this tool may run: immediate, budgeted, batched-jobs-same-frame.");
                }
                stopwatch.Stop();

                if (!ran)
                {
                    throw new McpToolException(McpTestingDiagnostics.MalformedArguments, "Benchmark run failed: " + runFailure.Code + ".");
                }

                return McpTestingJson.WriteBenchmarkResult(
                    scenarioName,
                    definitionValue.Isolates,
                    policy,
                    agentCount,
                    totalSteps,
                    stopwatch.Elapsed.Ticks / 10.0);
            }
            finally
            {
                foreach (var agent in agents)
                {
                    agent.Dispose();
                }
            }
        }

        // ---- shared plumbing --------------------------------------------------------------------

        private static string ResolveUnderRoot(string projectRoot, string relativePath, DiagnosticCode notFoundCode, string kind)
        {
            if (Path.IsPathRooted(relativePath))
            {
                throw new McpToolException(McpTestingDiagnostics.MalformedArguments, "'" + kind + "Path' must be relative to the project.");
            }

            var fullPath = Path.GetFullPath(Path.Combine(projectRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            var prefix = Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new McpToolException(notFoundCode, "'" + relativePath + "' escapes the project root.");
            }

            return fullPath;
        }

        private static string RequireString(JObject json, string property)
        {
            var value = json[property]?.Value<string>();
            if (string.IsNullOrEmpty(value))
            {
                throw new McpToolException(McpTestingDiagnostics.MalformedArguments, "Missing required string property '" + property + "'.");
            }

            return value;
        }

        private static int RequireInt(JObject json, string property)
        {
            var token = json[property];
            if (token == null || token.Type != JTokenType.Integer)
            {
                throw new McpToolException(McpTestingDiagnostics.MalformedArguments, "Missing required integer property '" + property + "'.");
            }

            return token.Value<int>();
        }

        private static uint? OptionalUInt(JObject json, string property)
        {
            var token = json[property];
            if (token == null)
            {
                return null;
            }

            if (token.Type != JTokenType.Integer)
            {
                throw new McpToolException(McpTestingDiagnostics.MalformedArguments, "'" + property + "' must be an integer.");
            }

            return token.Value<uint>();
        }
    }
}
