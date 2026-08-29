using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using AIBT.Authoring.Benchmarking;
using AIBT.Runtime.Scheduling;
using Unity.Collections;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;

namespace AIBT.Benchmarks.Phase4.Platform.Windows
{
    /// <summary>
    /// P4-008: runs the exact same scenario/policy sweep as `P4-001`'s
    /// <c>SchedulingBenchmarkRunner</c> (reusing <see cref="SchedulingScenarios"/> and
    /// <see cref="SchedulingPolicyDriver"/> unchanged, copied in by
    /// <c>Run-WindowsPlatformBenchmark.ps1</c>), but inside a real, non-development,
    /// IL2CPP+Burst Windows x64 Standalone Player instead of the Editor batchmode process every
    /// other Phase 4 benchmark used -- proving the same scenarios run on the actual mandatory
    /// pre-1.0 Windows target, not just in-Editor.
    /// </summary>
    internal static class WindowsPlatformSchedulingProbe
    {
        private const string OutputArgument = "-aibtBenchmarkOutput";
        private const string WarmupArgument = "-aibtWarmupSamples";
        private const string MeasuredArgument = "-aibtMeasuredSamples";
        private const string AgentCountsArgument = "-aibtAgentCounts";
        private const string SuccessMarker = "AIBT_P4_008_WINDOWS_PLAYER_OK|";
        private const string FailureMarker = "AIBT_P4_008_WINDOWS_PLAYER_FAIL|";
        private const int DefaultWarmupSamples = 5;
        private const int DefaultMeasuredSamples = 15;
        private static readonly int[] DefaultAgentCounts = { 16, 64, 256, 1024 };
        private static readonly string[] Policies = { "Immediate", "Budgeted", "BatchedJobsSameFrame" };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Run()
        {
            var exitCode = 0;
            try
            {
                RunInternal();
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogError(FailureMarker + exception);
                exitCode = 1;
            }
            finally
            {
                Application.Quit(exitCode);
            }
        }

        private static void RunInternal()
        {
            var arguments = Environment.GetCommandLineArgs();
            var outputPath = RequiredArgument(arguments, OutputArgument);
            var warmupSamples = IntegerArgument(arguments, WarmupArgument, DefaultWarmupSamples);
            var measuredSamples = IntegerArgument(arguments, MeasuredArgument, DefaultMeasuredSamples);
            var agentCounts = ListArgument(arguments, AgentCountsArgument, DefaultAgentCounts);

            var scenarioReports = new System.Collections.Generic.List<ScenarioReport>();
            foreach (var definition in SchedulingScenarios.Catalog)
            {
                if (!definition.Implemented) continue;
                var compiled = definition.Build();
                var cases = new System.Collections.Generic.List<ScenarioCase>();
                foreach (var agentCount in agentCounts)
                {
                    foreach (var policy in Policies)
                    {
                        for (var warmupIndex = 0; warmupIndex < warmupSamples; warmupIndex++)
                            RunOneSample(compiled, policy, agentCount, warmupIndex, false, out _);

                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                        GC.Collect();

                        var samples = new double[measuredSamples];
                        var stepSamples = new double[measuredSamples];
                        var totalSteps = 0L;
                        for (var sampleIndex = 0; sampleIndex < measuredSamples; sampleIndex++)
                        {
                            samples[sampleIndex] = RunOneSample(compiled, policy, agentCount, sampleIndex, true, out var stepsThisRun);
                            totalSteps = (long)stepsThisRun;
                            stepSamples[sampleIndex] = samples[sampleIndex] * agentCount / stepsThisRun;
                        }

                        Array.Sort(samples);
                        var median = samples.Length % 2 == 1
                            ? samples[samples.Length / 2]
                            : (samples[samples.Length / 2 - 1] + samples[samples.Length / 2]) / 2.0;
                        Array.Sort(stepSamples);
                        var medianPerStep = stepSamples.Length % 2 == 1
                            ? stepSamples[stepSamples.Length / 2]
                            : (stepSamples[stepSamples.Length / 2 - 1] + stepSamples[stepSamples.Length / 2]) / 2.0;
                        cases.Add(new ScenarioCase
                        {
                            policy = policy,
                            agentCount = agentCount,
                            medianNanosecondsPerAgent = median,
                            minimumNanosecondsPerAgent = samples[0],
                            maximumNanosecondsPerAgent = samples[samples.Length - 1],
                            totalSteps = totalSteps,
                            medianNanosecondsPerStep = medianPerStep
                        });
                    }
                }

                scenarioReports.Add(new ScenarioReport
                {
                    name = definition.Name,
                    isolates = definition.Isolates,
                    nodeCount = compiled.Program.Nodes.Count,
                    cases = cases.ToArray()
                });
            }

            var report = new PlatformBenchmarkReport
            {
                schema = "aibt-p4-008-windows-player-scheduling-v1",
                capturedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                scope = "P4-001's fixed-policy scheduling scenarios, run inside a real Windows x64 Standalone Player",
                environment = CaptureEnvironment(arguments),
                configuration = new ConfigurationRecord
                {
                    agentCounts = agentCounts,
                    warmupSamples = warmupSamples,
                    measuredSamples = measuredSamples
                },
                scenarios = scenarioReports.ToArray(),
                limitations = new[]
                {
                    "This is the same scenario/policy sweep P4-001/P4-002 already ran in the Editor; it does not add new scenarios or policies.",
                    "One run on one Windows x64 workstation; not generalized to other hardware.",
                    "No regression threshold or 'supported' performance claim is drawn from these numbers, per this card's own forbidden-changes clause.",
                    "totalSteps/medianNanosecondsPerStep were added after the initial P4-008 pass, to compare this Player's real per-step calibration cost against P4-004's Editor-derived CalibratedNanosecondsPerNodeStep constant; see Planning~/Evidence/P4-008/README.md."
                }
            };

            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(outputPath, JsonUtility.ToJson(report, true), new UTF8Encoding(false));
            UnityEngine.Debug.Log(SuccessMarker + "schema=" + report.schema + " scenarios=" + scenarioReports.Count);
        }

        private static double RunOneSample(
            SchedulingScenarios.CompiledScenario compiled, string policy, int agentCount, int sequence, bool measure, out ulong stepsThisRun)
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
                stepsThisRun = totalSteps;
                if (!measure) return 0;

                var elapsedTicks = stopped - started;
                return elapsedTicks * (1_000_000_000.0 / Stopwatch.Frequency) / agentCount;
            }
            finally
            {
                foreach (var agent in agents) agent.Dispose();
            }
        }

        private static PlatformEnvironment CaptureEnvironment(string[] arguments)
        {
            return new PlatformEnvironment
            {
                unityVersion = Application.unityVersion,
                applicationPlatform = Application.platform.ToString(),
                isEditor = Application.isEditor,
                operatingSystem = SystemInfo.operatingSystem,
                osArchitecture = RuntimeInformation.OSArchitecture.ToString(),
                processArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
                processorType = SystemInfo.processorType,
                processorCount = SystemInfo.processorCount,
                jobWorkerCount = JobsUtility.JobWorkerCount,
                systemMemoryMB = SystemInfo.systemMemorySize,
                burstEnabled = Unity.Burst.BurstCompiler.IsEnabled,
                is64BitProcess = IntPtr.Size == 8,
                pinnedBurstVersion = "1.8.30",
                pinnedCollectionsVersion = "6.5.0",
                commandLine = (string[])arguments.Clone(),
                thermalAndPowerConditions = "Not controlled or recorded"
            };
        }

        private static string RequiredArgument(string[] arguments, string name)
        {
            for (var index = 0; index < arguments.Length - 1; index++)
                if (string.Equals(arguments[index], name, StringComparison.Ordinal))
                    return arguments[index + 1];
            throw new ArgumentException("Missing required command-line argument " + name + ".");
        }

        private static int IntegerArgument(string[] arguments, string name, int fallback)
        {
            for (var index = 0; index < arguments.Length - 1; index++)
            {
                if (!string.Equals(arguments[index], name, StringComparison.Ordinal)) continue;
                if (!int.TryParse(arguments[index + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var value))
                    throw new ArgumentException(name + " must be an integer.");
                return value;
            }
            return fallback;
        }

        private static int[] ListArgument(string[] arguments, string name, int[] fallback)
        {
            for (var index = 0; index < arguments.Length - 1; index++)
            {
                if (!string.Equals(arguments[index], name, StringComparison.Ordinal)) continue;
                return arguments[index + 1].Split(',').Select(token => int.Parse(token.Trim(), CultureInfo.InvariantCulture)).ToArray();
            }
            return fallback;
        }

        [Serializable]
        private sealed class PlatformBenchmarkReport
        {
            public string schema;
            public string capturedAtUtc;
            public string scope;
            public PlatformEnvironment environment;
            public ConfigurationRecord configuration;
            public ScenarioReport[] scenarios;
            public string[] limitations;
        }

        [Serializable]
        private sealed class PlatformEnvironment
        {
            public string unityVersion;
            public string applicationPlatform;
            public bool isEditor;
            public string operatingSystem;
            public string osArchitecture;
            public string processArchitecture;
            public string processorType;
            public int processorCount;
            public int jobWorkerCount;
            public int systemMemoryMB;
            public bool burstEnabled;
            public bool is64BitProcess;
            public string pinnedBurstVersion;
            public string pinnedCollectionsVersion;
            public string[] commandLine;
            public string thermalAndPowerConditions;
        }

        [Serializable]
        private sealed class ConfigurationRecord
        {
            public int[] agentCounts;
            public int warmupSamples;
            public int measuredSamples;
        }

        [Serializable]
        private sealed class ScenarioReport
        {
            public string name;
            public string isolates;
            public int nodeCount;
            public ScenarioCase[] cases;
        }

        [Serializable]
        private sealed class ScenarioCase
        {
            public string policy;
            public int agentCount;
            public double medianNanosecondsPerAgent;
            public double minimumNanosecondsPerAgent;
            public double maximumNanosecondsPerAgent;
            public long totalSteps;
            public double medianNanosecondsPerStep;
        }
    }
}
