using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using AIBT.Authoring.Benchmarking;
using AIBT.Runtime.Scheduling;
using Unity.Collections;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;

namespace AIBT.Benchmarks.Phase4.AutoComparison
{
    /// <summary>
    /// P4-006: measures <c>Auto</c> (P4-005) against the best fixed policy per scenario across
    /// P4-001's implemented scenario catalog. Scoped to <see cref="NativeAutoLatencyModeV1.SameFrame"/>
    /// only -- P4-001's benchmark harness (<see cref="SchedulingPolicyDriver"/>) was never wired
    /// to measure `PipelinedJobs` (built separately in P4-003), so restricting to same-frame
    /// latency means `Auto` never selects an unmeasurable policy here, by construction, not by
    /// omission. Reuses <see cref="SchedulingScenarios"/> and <see cref="SchedulingPolicyDriver"/>
    /// unchanged (copied in by <c>Run-AutoComparisonBenchmark.ps1</c> alongside this file) --
    /// this card measures, it does not modify either.
    /// </summary>
    internal static class AutoComparisonRunner
    {
        private const int DefaultWarmupSamples = 5;
        private const int DefaultMeasuredSamples = 15;
        private const uint BudgetStepLimit = 4;
        private const uint FixedBatchSize = 32;
        private static readonly int[] DefaultAgentCounts = { 16, 64, 256, 1024 };
        private static readonly string[] FixedPolicies = { "Immediate", "Budgeted", "BatchedJobsSameFrame" };

        public static void Run()
        {
            var arguments = Environment.GetCommandLineArgs();
            var outputPath = RequiredArgument(arguments, "-aibtBenchmarkOutput");
            var warmupSamples = IntegerArgument(arguments, "-aibtWarmupSamples", DefaultWarmupSamples, 1);
            var measuredSamples = IntegerArgument(arguments, "-aibtMeasuredSamples", DefaultMeasuredSamples, 1);
            var agentCounts = ListArgument(arguments, "-aibtAgentCounts", DefaultAgentCounts);
            var minimumJobWorkloadNanoseconds = 50_000.0;
            var targetBatchWorkNanoseconds = 50_000.0;

            var scenarioReports = new List<ScenarioComparison>();
            foreach (var definition in SchedulingScenarios.Catalog)
            {
                if (!definition.Implemented) continue;
                var compiled = definition.Build();
                var cases = new List<ComparisonCase>();
                foreach (var agentCount in agentCounts)
                {
                    var measured = new List<PolicyMeasurement>();
                    ulong totalSteps = 0;
                    foreach (var policy in FixedPolicies)
                    {
                        var (median, steps) = MeasurePolicy(compiled, policy, agentCount, warmupSamples, measuredSamples);
                        measured.Add(new PolicyMeasurement { policy = policy, medianNanosecondsPerAgent = median });
                        totalSteps = steps;
                    }

                    var best = measured.OrderBy(entry => entry.medianNanosecondsPerAgent).First();

                    var estimator = new NativeWorkEstimatorV1();
                    if (!estimator.TryObserve((uint)agentCount, totalSteps, out var estimatorFailure))
                        throw new InvalidOperationException("Estimator observe failed: " + estimatorFailure.Code);
                    if (!estimator.TryEstimateWorkPerAgentNanoseconds(out var estimatedNs, out estimatorFailure))
                        throw new InvalidOperationException("Estimator estimate failed: " + estimatorFailure.Code);

                    var configuration = new NativeAutoConfigurationV1(
                        NativeAutoSupportedPoliciesV1.Immediate | NativeAutoSupportedPoliciesV1.Budgeted | NativeAutoSupportedPoliciesV1.BatchedJobsSameFrame,
                        NativeAutoLatencyModeV1.SameFrame,
                        null,
                        minimumJobWorkloadNanoseconds,
                        targetBatchWorkNanoseconds,
                        1, 256, 256,
                        (uint)JobsUtility.JobWorkerCount,
                        null, 1);
                    var workload = new NativeAutoWorkloadV1(estimator.SmoothedStepsPerAgent, estimatedNs, 1);
                    if (!NativeAutoSelectionV1.TrySelect(configuration, workload, (uint)agentCount, out var explanation, out var selectFailure))
                        throw new InvalidOperationException("Auto selection failed: " + selectFailure.Code);

                    var autoPolicyName = explanation.ChosenPolicy.ToString();
                    var autoMeasured = measured.First(entry => entry.policy == autoPolicyName);
                    var gap = autoMeasured.medianNanosecondsPerAgent - best.medianNanosecondsPerAgent;
                    var gapPercent = best.medianNanosecondsPerAgent > 0 ? gap / best.medianNanosecondsPerAgent * 100.0 : 0.0;

                    cases.Add(new ComparisonCase
                    {
                        agentCount = agentCount,
                        allMeasuredPolicies = measured.ToArray(),
                        bestFixedPolicy = best.policy,
                        bestFixedMedianNanosecondsPerAgent = best.medianNanosecondsPerAgent,
                        autoChosenPolicy = autoPolicyName,
                        autoReason = explanation.Reason.ToString(),
                        autoConfidence = explanation.Confidence.ToString(),
                        autoMeasuredMedianNanosecondsPerAgent = autoMeasured.medianNanosecondsPerAgent,
                        gapNanosecondsPerAgent = gap,
                        gapPercent = gapPercent,
                        outcome = gap <= 0 ? "MatchesOrBeats" : "Underperforms",
                        explanation = new ExplanationRecord
                        {
                            expectedNodeStepsPerAgent = explanation.ExpectedNodeStepsPerAgent,
                            estimatedWorkPerAgentNanoseconds = explanation.EstimatedWorkPerAgentNanoseconds,
                            estimatedTotalWorkNanoseconds = explanation.EstimatedTotalWorkNanoseconds,
                            batchSize = explanation.BatchSize,
                            batchCount = explanation.BatchCount,
                            workerUtilizationProxy = explanation.WorkerUtilizationProxy,
                            hasConfiguredBudget = explanation.HasConfiguredBudget,
                            exceedsConfiguredBudget = explanation.ExceedsConfiguredBudget,
                        }
                    });
                }

                scenarioReports.Add(new ScenarioComparison
                {
                    name = definition.Name,
                    isolates = definition.Isolates,
                    cases = cases.ToArray()
                });
            }

            var report = new AutoComparisonReport
            {
                schema = "aibt-p4-006-auto-comparison-v1",
                capturedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                scope = "Auto policy (LatencyMode=SameFrame) vs best fixed policy across P4-001's implemented scenario catalog",
                environment = CaptureEnvironment(arguments),
                configuration = new ConfigurationRecord
                {
                    agentCounts = agentCounts,
                    warmupSamples = warmupSamples,
                    measuredSamples = measuredSamples,
                    minimumJobWorkloadNanoseconds = minimumJobWorkloadNanoseconds,
                    targetBatchWorkNanoseconds = targetBatchWorkNanoseconds,
                    latencyMode = "SameFrame"
                },
                scenarios = scenarioReports.ToArray(),
                limitations = new[]
                {
                    "PipelinedJobs is excluded from this comparison by design -- P4-001's benchmark harness was never wired to measure it (built separately in P4-003); restricting to LatencyMode=SameFrame means Auto never selects it here, so no unmeasurable choice can occur.",
                    "Auto's own measured cost for a chosen policy is that same policy's already-measured cost at this scenario/agentCount in this same run -- Auto invents no new execution semantics (P4-005's own forbidden-changes bars that), so there is no separate 'run Auto itself' execution to measure.",
                    "One run on one workstation; not generalized to other hardware.",
                    "No regression threshold or shipping recommendation is drawn from any gap recorded here -- P4-007 interprets these results, this file only records them.",
                    "The Auto configuration used here (minimum job workload, target batch work, batch/memory bounds) is this measurement run's own choice, not a claimed shipped default."
                }
            };

            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(outputPath, JsonUtility.ToJson(report, true), new UTF8Encoding(false));
            UnityEngine.Debug.Log("AIBT P4-006 Auto-vs-fixed comparison completed: " + outputPath);
        }

        private static (double median, ulong totalSteps) MeasurePolicy(
            SchedulingScenarios.CompiledScenario compiled, string policy, int agentCount, int warmupSamples, int measuredSamples)
        {
            for (var warmupIndex = 0; warmupIndex < warmupSamples; warmupIndex++)
                RunOneSample(compiled, policy, agentCount, warmupIndex, false, out _);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var samples = new double[measuredSamples];
            var totalSteps = 0UL;
            for (var sampleIndex = 0; sampleIndex < measuredSamples; sampleIndex++)
                samples[sampleIndex] = RunOneSample(compiled, policy, agentCount, sampleIndex, true, out totalSteps);

            Array.Sort(samples);
            var median = samples.Length % 2 == 1
                ? samples[samples.Length / 2]
                : (samples[samples.Length / 2 - 1] + samples[samples.Length / 2]) / 2.0;
            return (median, totalSteps);
        }

        private static double RunOneSample(
            SchedulingScenarios.CompiledScenario compiled, string policy, int agentCount, int sequence, bool measure, out ulong totalSteps)
        {
            if (!SchedulingPolicyDriver.TryCreateAgents(compiled.Program, compiled.NodeKinds, agentCount, Allocator.Persistent, out var agents, out var createFailure))
                throw new InvalidOperationException("Agent creation failed: " + createFailure.Code);

            try
            {
                long started = 0;
                if (measure) started = Stopwatch.GetTimestamp();

                bool ran;
                NativeRuntimeFailureV1 runFailure;
                switch (policy)
                {
                    case "Immediate":
                        ran = SchedulingPolicyDriver.TryRunImmediate(agents, 1, compiled.LeafStatusByRuntimeIndex, out totalSteps, out runFailure);
                        break;
                    case "Budgeted":
                        var budgetStates = new NativeBudgetStateV1[agents.Length];
                        ran = SchedulingPolicyDriver.TryRunBudgeted(agents, budgetStates, 1, BudgetStepLimit, compiled.LeafStatusByRuntimeIndex, out totalSteps, out runFailure);
                        break;
                    case "BatchedJobsSameFrame":
                        ran = SchedulingPolicyDriver.TryRunBatchedJobsSameFrame(agents, 1, FixedBatchSize, compiled.LeafStatusByRuntimeIndex, Allocator.Persistent, out totalSteps, out runFailure);
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

        private static ReportEnvironment CaptureEnvironment(string[] arguments)
        {
            return new ReportEnvironment
            {
                unityVersion = Application.unityVersion,
                editorBatchMode = Application.isBatchMode,
                operatingSystem = SystemInfo.operatingSystem,
                processorType = SystemInfo.processorType,
                processorCount = SystemInfo.processorCount,
                jobWorkerCount = JobsUtility.JobWorkerCount,
                commandLine = (string[])arguments.Clone(),
                thermalAndPowerConditions = "Not controlled or recorded"
            };
        }

        private static string RequiredArgument(string[] arguments, string name)
        {
            for (var index = 0; index < arguments.Length - 1; index++)
                if (string.Equals(arguments[index], name, StringComparison.Ordinal))
                    return Path.GetFullPath(arguments[index + 1]);
            throw new ArgumentException("Missing required command-line argument " + name + ".");
        }

        private static int IntegerArgument(string[] arguments, string name, int fallback, int minimum)
        {
            for (var index = 0; index < arguments.Length - 1; index++)
            {
                if (!string.Equals(arguments[index], name, StringComparison.Ordinal)) continue;
                if (!int.TryParse(arguments[index + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < minimum)
                    throw new ArgumentException(name + " must be an integer >= " + minimum + ".");
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
        private sealed class AutoComparisonReport
        {
            public string schema;
            public string capturedAtUtc;
            public string scope;
            public ReportEnvironment environment;
            public ConfigurationRecord configuration;
            public ScenarioComparison[] scenarios;
            public string[] limitations;
        }

        [Serializable]
        private sealed class ReportEnvironment
        {
            public string unityVersion;
            public bool editorBatchMode;
            public string operatingSystem;
            public string processorType;
            public int processorCount;
            public int jobWorkerCount;
            public string[] commandLine;
            public string thermalAndPowerConditions;
        }

        [Serializable]
        private sealed class ConfigurationRecord
        {
            public int[] agentCounts;
            public int warmupSamples;
            public int measuredSamples;
            public double minimumJobWorkloadNanoseconds;
            public double targetBatchWorkNanoseconds;
            public string latencyMode;
        }

        [Serializable]
        private sealed class ScenarioComparison
        {
            public string name;
            public string isolates;
            public ComparisonCase[] cases;
        }

        [Serializable]
        private sealed class ComparisonCase
        {
            public int agentCount;
            public PolicyMeasurement[] allMeasuredPolicies;
            public string bestFixedPolicy;
            public double bestFixedMedianNanosecondsPerAgent;
            public string autoChosenPolicy;
            public string autoReason;
            public string autoConfidence;
            public double autoMeasuredMedianNanosecondsPerAgent;
            public double gapNanosecondsPerAgent;
            public double gapPercent;
            public string outcome;
            public ExplanationRecord explanation;
        }

        [Serializable]
        private sealed class PolicyMeasurement
        {
            public string policy;
            public double medianNanosecondsPerAgent;
        }

        [Serializable]
        private sealed class ExplanationRecord
        {
            public double expectedNodeStepsPerAgent;
            public double estimatedWorkPerAgentNanoseconds;
            public double estimatedTotalWorkNanoseconds;
            public uint batchSize;
            public uint batchCount;
            public double workerUtilizationProxy;
            public bool hasConfiguredBudget;
            public bool exceedsConfiguredBudget;
        }
    }
}
