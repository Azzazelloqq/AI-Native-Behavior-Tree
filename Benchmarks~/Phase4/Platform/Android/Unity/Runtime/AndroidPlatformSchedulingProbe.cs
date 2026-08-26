using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using AIBT.Benchmarks.Phase4.Scheduling;
using AIBT.Tests.Runtime.Benchmarking;
using Unity.Collections;
using UnityEngine;

namespace AIBT.Benchmarks.Phase4.Platform.Android
{
    /// <summary>
    /// P4-008: runs `P4-001`'s exact scenario/policy sweep inside a real, non-development,
    /// IL2CPP, Burst-enabled Android ARM64 build on genuine hardware (not an x86_64 emulator).
    /// Android is a full "Native backend" target per
    /// `Documentation~/specifications/platform-backends-v1.md` (unlike single-thread Web), so all
    /// three fixed policies (`Immediate`, `Budgeted`, `BatchedJobsSameFrame`) are measured, same as
    /// the Windows Player probe. Like the Web probe, results are logged rather than written to a
    /// file (Android's Scoped Storage makes arbitrary file writes unreliable across API levels
    /// without extra permission ceremony this card does not need) -- read via `adb logcat`. Each
    /// scenario is logged on its own marked line, not one giant combined line, to stay comfortably
    /// under logcat's per-entry truncation limit.
    /// </summary>
    internal static class AndroidPlatformSchedulingProbe
    {
        private const string SuccessMarker = "AIBT_P4_008_ANDROID_OK|";
        private const string ScenarioMarker = "AIBT_P4_008_ANDROID_SCENARIO|";
        private const string FailureMarker = "AIBT_P4_008_ANDROID_FAIL|";
        private const int WarmupSamples = 3;
        private const int MeasuredSamples = 7;
        private static readonly int[] AgentCounts = { 16, 64, 256 };
        private static readonly string[] Policies = { "Immediate", "Budgeted", "BatchedJobsSameFrame" };

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
            UnityEngine.Debug.Log(SuccessMarker + "unityVersion=" + Application.unityVersion
                + " platform=" + Application.platform
                + " burstEnabled=" + Unity.Burst.BurstCompiler.IsEnabled
                + " is64Bit=" + (IntPtr.Size == 8)
                + " deviceModel=" + SystemInfo.deviceModel
                + " processorType=" + SystemInfo.processorType
                + " processorCount=" + SystemInfo.processorCount
                + " systemMemoryMB=" + SystemInfo.systemMemorySize
                + " osVersion=" + SystemInfo.operatingSystem);

            foreach (var definition in SchedulingScenarios.Catalog)
            {
                if (!definition.Implemented) continue;
                var compiled = definition.Build();
                var builder = new StringBuilder();
                builder.Append("{\"name\":\"").Append(definition.Name).Append("\",\"cases\":[");
                var first = true;

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

                        if (!first) builder.Append(',');
                        first = false;
                        builder.Append("{\"policy\":\"").Append(policy).Append("\",\"agentCount\":").Append(agentCount)
                            .Append(",\"medianNsPerAgent\":").Append(median.ToString("F3", CultureInfo.InvariantCulture)).Append('}');
                    }
                }

                builder.Append("]}");
                UnityEngine.Debug.Log(ScenarioMarker + builder);
            }

            UnityEngine.Debug.Log(SuccessMarker + "DONE");
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
                switch (policy)
                {
                    case "Immediate":
                        ran = SchedulingPolicyDriver.TryRunImmediate(agents, 1, compiled.LeafStatusByRuntimeIndex, out totalSteps, out runFailure);
                        break;
                    case "Budgeted":
                        var budgetStates = new NativeBudgetStateV1[agents.Length];
                        ran = SchedulingPolicyDriver.TryRunBudgeted(agents, budgetStates, 1, 4, compiled.LeafStatusByRuntimeIndex, out totalSteps, out runFailure);
                        break;
                    case "BatchedJobsSameFrame":
                        ran = SchedulingPolicyDriver.TryRunBatchedJobsSameFrame(agents, 1, 32, compiled.LeafStatusByRuntimeIndex, Allocator.Persistent, out totalSteps, out runFailure);
                        break;
                    default:
                        throw new ArgumentException("Unknown policy: " + policy);
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
