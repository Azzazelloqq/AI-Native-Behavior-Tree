using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using AIBT.Benchmarks.Phase4.Scheduling;
using AIBT.Tests.Runtime.Benchmarking;
using Unity.Collections;
using UnityEngine;

namespace AIBT.Benchmarks.Phase4.Platform.Web
{
    /// <summary>
    /// P4-008: runs `P4-001`'s scenario sweep inside a real single-thread Unity Web (WebGL)
    /// build, restricted to `Immediate`/`Budgeted` -- per
    /// `Documentation~/specifications/platform-backends-v1.md`, `BatchedJobsSameFrame` and
    /// `PipelinedJobs` are not claimed-supported Web policies ("unavailable unless a future
    /// verified Unity capability changes this decision"), so this probe does not silently test a
    /// policy the project's own accepted architecture excludes for this backend. A WebGL Player
    /// has no reliable arbitrary filesystem write, so results are logged to the browser console
    /// (readable via the dev-tools/Browser-pane console) rather than written to a file, with a
    /// summary-only report (medians, not every raw sample) to keep the single log line compact.
    /// Scope is deliberately smaller than the Windows Player probe (3 agent counts, fewer samples)
    /// given WebGL's slower single-threaded execution and to keep the console payload manageable.
    /// </summary>
    internal static class WebPlatformSchedulingProbe
    {
        private const string SuccessMarker = "AIBT_P4_008_WEB_PLAYER_OK|";
        private const string FailureMarker = "AIBT_P4_008_WEB_PLAYER_FAIL|";
        private const int WarmupSamples = 3;
        private const int MeasuredSamples = 7;
        private static readonly int[] AgentCounts = { 16, 64, 256 };
        private static readonly string[] Policies = { "Immediate", "Budgeted" };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Run()
        {
            try
            {
                RunInternal();
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogError(FailureMarker + exception);
            }
        }

        private static void RunInternal()
        {
            var builder = new StringBuilder();
            builder.Append('{');
            builder.Append("\"schema\":\"aibt-p4-008-web-player-scheduling-v1\",");
            builder.Append("\"unityVersion\":\"").Append(Application.unityVersion).Append("\",");
            builder.Append("\"applicationPlatform\":\"").Append(Application.platform).Append("\",");
            builder.Append("\"burstEnabled\":").Append(Unity.Burst.BurstCompiler.IsEnabled ? "true" : "false").Append(',');
            builder.Append("\"is64BitProcess\":").Append(IntPtr.Size == 8 ? "true" : "false").Append(',');
            builder.Append("\"scenarios\":[");
            var firstScenario = true;

            foreach (var definition in SchedulingScenarios.Catalog)
            {
                if (!definition.Implemented) continue;
                var compiled = definition.Build();
                if (!firstScenario) builder.Append(',');
                firstScenario = false;
                builder.Append("{\"name\":\"").Append(definition.Name).Append("\",\"cases\":[");
                var firstCase = true;

                foreach (var agentCount in AgentCounts)
                {
                    foreach (var policy in Policies)
                    {
                        for (var warmupIndex = 0; warmupIndex < WarmupSamples; warmupIndex++)
                            RunOneSample(compiled, policy, agentCount, false);

                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                        GC.Collect();

                        var samples = new double[MeasuredSamples];
                        for (var sampleIndex = 0; sampleIndex < MeasuredSamples; sampleIndex++)
                            samples[sampleIndex] = RunOneSample(compiled, policy, agentCount, true);

                        Array.Sort(samples);
                        var median = samples.Length % 2 == 1
                            ? samples[samples.Length / 2]
                            : (samples[samples.Length / 2 - 1] + samples[samples.Length / 2]) / 2.0;

                        if (!firstCase) builder.Append(',');
                        firstCase = false;
                        builder.Append("{\"policy\":\"").Append(policy).Append("\",\"agentCount\":").Append(agentCount)
                            .Append(",\"medianNsPerAgent\":").Append(median.ToString("F3", CultureInfo.InvariantCulture)).Append('}');
                    }
                }

                builder.Append("]}");
            }

            builder.Append("]}");
            UnityEngine.Debug.Log(SuccessMarker + builder);
        }

        private static double RunOneSample(SchedulingScenarios.CompiledScenario compiled, string policy, int agentCount, bool measure)
        {
            if (!SchedulingPolicyDriver.TryCreateAgents(compiled.Program, compiled.NodeKinds, agentCount, Allocator.Persistent, out var agents, out var createFailure))
                throw new InvalidOperationException("Agent creation failed: " + createFailure.Code);

            try
            {
                long started = 0;
                if (measure) started = Stopwatch.GetTimestamp();

                bool ran;
                NativeRuntimeFailureV1 runFailure;
                ulong totalSteps;
                if (policy == "Immediate")
                {
                    ran = SchedulingPolicyDriver.TryRunImmediate(agents, 1, compiled.LeafStatusByRuntimeIndex, out totalSteps, out runFailure);
                }
                else
                {
                    var budgetStates = new NativeBudgetStateV1[agents.Length];
                    ran = SchedulingPolicyDriver.TryRunBudgeted(agents, budgetStates, 1, 4, compiled.LeafStatusByRuntimeIndex, out totalSteps, out runFailure);
                }

                long stopped = measure ? Stopwatch.GetTimestamp() : 0;
                if (!ran) throw new InvalidOperationException("Policy " + policy + " failed: " + runFailure.Code);
                if (!measure) return 0;

                var elapsedTicks = stopped - started;
                return elapsedTicks * (1_000_000_000.0 / Stopwatch.Frequency) / agentCount;
            }
            finally
            {
                foreach (var agent in agents) agent.Dispose();
            }
        }
    }
}
