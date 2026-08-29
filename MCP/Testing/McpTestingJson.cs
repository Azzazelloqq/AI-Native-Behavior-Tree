using AIBT.Authoring.BehaviorCases;
using Newtonsoft.Json.Linq;

namespace AIBT.Mcp.Testing
{
    /// <summary>JSON shapes for <see cref="McpTestingToolDispatcher"/>'s two tools.</summary>
    internal static class McpTestingJson
    {
        internal static JObject WriteRunResult(BehaviorCaseRunResult result)
        {
            var failures = new JArray();
            foreach (var failure in result.Failures)
            {
                failures.Add(new JObject
                {
                    ["stepIndex"] = failure.StepIndex,
                    ["kind"] = failure.Kind.ToString(),
                    ["pointer"] = failure.Pointer,
                    ["message"] = failure.Message,
                });
            }

            return new JObject
            {
                ["success"] = result.Success,
                ["executedStepCount"] = result.ExecutedStepCount,
                ["inputDiagnostics"] = McpDiagnosticJson.WriteDiagnostics(result.InputDiagnostics),
                ["failures"] = failures,
            };
        }

        internal static JObject WriteBenchmarkResult(
            string scenario,
            string isolates,
            string policy,
            int agentCount,
            ulong totalSteps,
            double elapsedMicroseconds)
        {
            return new JObject
            {
                ["scenario"] = scenario,
                ["isolates"] = isolates,
                ["policy"] = policy,
                ["agentCount"] = agentCount,
                ["totalSteps"] = totalSteps,
                ["elapsedMicroseconds"] = elapsedMicroseconds,
                // UnityEngine.Application's properties (unityVersion/platform/isBatchMode) are
                // main-thread-only -- found live (AIBT9013) when this handler ran on the bridge's
                // background TCP thread. System.Environment's own properties carry no such
                // restriction, so environment metadata is limited to those.
                ["environment"] = new JObject
                {
                    ["machineName"] = System.Environment.MachineName,
                    ["osVersion"] = System.Environment.OSVersion.ToString(),
                    ["clrVersion"] = System.Environment.Version.ToString(),
                },
            };
        }
    }
}
