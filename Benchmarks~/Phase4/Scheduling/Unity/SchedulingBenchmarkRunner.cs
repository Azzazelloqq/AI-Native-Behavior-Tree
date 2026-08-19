using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using AIBT.Tests.Runtime.Benchmarking;
using Unity.Collections;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;

namespace AIBT.Benchmarks.Phase4.Scheduling
{
    /// <summary>
    /// P4-001: runs <see cref="SchedulingScenarios.Catalog"/>'s implemented entries against all
    /// three accepted fixed policies (Immediate, Budgeted, BatchedJobsSameFrame) at a
    /// parameter-matrix of agent counts, and records raw per-sample timing/allocation data plus
    /// environment metadata. This produces measurements only -- no threshold, no "Auto" policy,
    /// no adaptation -- per <c>Documentation~/benchmarks.md</c>'s "Scheduler research" step 1.
    /// Each sample creates and disposes a fresh set of native agents (matching
    /// <c>DispatchBenchmarkRunner.RunOneSample</c>'s per-sample isolation), so the timed region is
    /// exactly one tick's scheduling work, never cross-tick restart semantics.
    /// </summary>
    internal static class SchedulingBenchmarkRunner
    {
        private const int DefaultWarmupSamples = 5;
        private const int DefaultMeasuredSamples = 15;
        private const uint DefaultBudgetStepLimit = 4;
        private const int NotApplicable = -1;
        private static readonly int[] DefaultAgentCounts = { 16, 128 };
        private static readonly int[] DefaultBatchSizes = { 8, 32, 128 };
        private static readonly string[] Policies = { "Immediate", "Budgeted", "BatchedJobsSameFrame" };
        private static AllocationCounterKind s_allocationCounter;

        public static void Run()
        {
            var arguments = Environment.GetCommandLineArgs();
            var outputPath = RequiredArgument(arguments, "-aibtBenchmarkOutput");
            var warmupSamples = IntegerArgument(arguments, "-aibtWarmupSamples", DefaultWarmupSamples, 1);
            var measuredSamples = IntegerArgument(arguments, "-aibtMeasuredSamples", DefaultMeasuredSamples, 1);
            var budgetStepLimit = (uint)IntegerArgument(arguments, "-aibtBudgetStepLimit", (int)DefaultBudgetStepLimit, 1);
            var agentCounts = ListArgument(arguments, "-aibtAgentCounts", DefaultAgentCounts);
            var batchSizes = ListArgument(arguments, "-aibtBatchSizes", DefaultBatchSizes);
            var maxWorkerThreadCount = JobsUtility.JobWorkerMaximumCount;
            var defaultWorkerThreadCounts = new[] { 1, JobsUtility.JobWorkerCount };
            var workerThreadCounts = ListArgument(arguments, "-aibtWorkerThreadCounts", defaultWorkerThreadCounts);
            foreach (var count in workerThreadCounts)
            {
                if (count < 1 || count > maxWorkerThreadCount)
                {
                    throw new ArgumentException("-aibtWorkerThreadCounts entry " + count + " is outside [1, " + maxWorkerThreadCount + "] (JobsUtility.JobWorkerMaximumCount on this machine).");
                }
            }

            var originalWorkerThreadCount = JobsUtility.JobWorkerCount;
            var allocationProbe = ProbeManagedAllocationCounter();
            var scenarioReports = new List<ScenarioReport>();
            var notYetImplemented = new List<PlaceholderScenario>();

            try
            {
                foreach (var definition in SchedulingScenarios.Catalog)
                {
                    if (!definition.Implemented)
                    {
                        notYetImplemented.Add(new PlaceholderScenario { name = definition.Name, isolates = definition.Isolates });
                        continue;
                    }

                    var compiled = definition.Build();
                    var cases = new List<ScenarioCase>();
                    foreach (var agentCount in agentCounts)
                    {
                        foreach (var policy in Policies)
                        {
                            if (policy == "BatchedJobsSameFrame")
                            {
                                // batchSize and worker-thread count only affect the one policy that
                                // actually schedules Unity Jobs -- Immediate/Budgeted are plain
                                // managed loops with nothing for either parameter to change.
                                foreach (var batchSize in batchSizes)
                                {
                                    foreach (var workerThreadCount in workerThreadCounts)
                                    {
                                        JobsUtility.JobWorkerCount = workerThreadCount;
                                        cases.Add(RunCase(compiled, policy, agentCount, budgetStepLimit, batchSize, workerThreadCount, warmupSamples, measuredSamples));
                                    }
                                }

                                JobsUtility.JobWorkerCount = originalWorkerThreadCount;
                            }
                            else
                            {
                                cases.Add(RunCase(compiled, policy, agentCount, budgetStepLimit, NotApplicable, NotApplicable, warmupSamples, measuredSamples));
                            }
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
            }
            finally
            {
                JobsUtility.JobWorkerCount = originalWorkerThreadCount;
            }

            var report = new SchedulingBenchmarkReport
            {
                schema = "aibt-p4-001-scheduling-benchmark-v1",
                capturedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                scope = "Fixed-policy scheduling overhead across the P4-001 structural scenario catalog",
                environment = CaptureEnvironment(arguments),
                configuration = new BenchmarkConfiguration
                {
                    agentCounts = agentCounts,
                    warmupSamplesPerCase = warmupSamples,
                    measuredSamplesPerCase = measuredSamples,
                    budgetStepLimit = budgetStepLimit,
                    batchSizes = batchSizes,
                    workerThreadCounts = workerThreadCounts,
                    maxWorkerThreadCount = maxWorkerThreadCount,
                    policies = (string[])Policies.Clone(),
                    timedRegion = "One TryRunImmediate/TryRunBudgeted/TryRunBatchedJobsSameFrame call (one tick) over a freshly created agent set",
                    excludedFromTimedRegion = "Agent construction/disposal, JSON serialization, GC.Collect calls between cases",
                    batchSizeAndWorkerThreadSweepScope = "batchSizes and workerThreadCounts are swept only for BatchedJobsSameFrame -- Immediate and Budgeted are plain managed loops with no Jobs/batching involved, so both fields are -1 (not applicable) on their cases."
                },
                allocationProbe = allocationProbe,
                scenarios = scenarioReports.ToArray(),
                documentedNotYetImplementedScenarios = notYetImplemented.ToArray(),
                documentedNotYetImplementedPolicies = new[]
                {
                    new PlaceholderPolicy { name = "PipelinedJobs", isolates = "Cross-frame pipelined batch scheduling latency/throughput (P4-003 implements the policy itself; this harness will then measure it, not before)." },
                    new PlaceholderPolicy { name = "Auto", isolates = "Adaptive policy selection (P4-005/P4-007; OQ-006 gates whether this is ever built at all)." }
                },
                limitations = new[]
                {
                    "This isolated Windows Editor batchmode run measures one workstation; it is not a cross-hardware-class result (Planning~/USER_ACTIONS.md).",
                    "Only the six structural scenarios that need no blackboard/async/managed-node/cost-tagged leaf semantics are measured; the remainder are documented placeholders (see documentedNotYetImplementedScenarios).",
                    "PipelinedJobs and Auto are documented policy placeholders only (see documentedNotYetImplementedPolicies) -- neither is implemented, and no case in this report ever substitutes one of the three accepted fixed policies for either.",
                    "No performance pass/fail threshold or regression bound is inferred from these numbers.",
                    "Thermal state and power policy are not controlled by the runner."
                }
            };

            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(outputPath, JsonUtility.ToJson(report, true), new UTF8Encoding(false));
            UnityEngine.Debug.Log("AIBT P4-001 scheduling benchmark completed: " + outputPath);
        }

        private static ScenarioCase RunCase(
            SchedulingScenarios.CompiledScenario compiled,
            string policy,
            int agentCount,
            uint budgetStepLimit,
            int batchSize,
            int workerThreadCount,
            int warmupSamples,
            int measuredSamples)
        {
            var effectiveBatchSize = batchSize > 0 ? (uint)batchSize : 0u;
            for (var warmupIndex = 0; warmupIndex < warmupSamples; warmupIndex++)
            {
                RunOneSample(compiled, policy, agentCount, budgetStepLimit, effectiveBatchSize, warmupIndex, false);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var samples = new ScenarioSample[measuredSamples];
            for (var sampleIndex = 0; sampleIndex < measuredSamples; sampleIndex++)
            {
                samples[sampleIndex] = RunOneSample(compiled, policy, agentCount, budgetStepLimit, effectiveBatchSize, sampleIndex, true);
            }

            return new ScenarioCase
            {
                policy = policy,
                agentCount = agentCount,
                batchSize = batchSize,
                workerThreadCount = workerThreadCount,
                samples = samples,
                summary = Summarize(samples)
            };
        }

        private static ScenarioSample RunOneSample(
            SchedulingScenarios.CompiledScenario compiled,
            string policy,
            int agentCount,
            uint budgetStepLimit,
            uint batchSize,
            int sequence,
            bool measure)
        {
            if (!SchedulingPolicyDriver.TryCreateAgents(compiled.Program, compiled.NodeKinds, agentCount, Allocator.Persistent, out var agents, out var createFailure))
            {
                throw new InvalidOperationException("Agent creation failed: " + createFailure.Code);
            }

            try
            {
                long beforeAllocated = 0;
                long started = 0;
                if (measure)
                {
                    beforeAllocated = ReadManagedAllocationCounter();
                    started = Stopwatch.GetTimestamp();
                }

                bool ran;
                ulong totalSteps;
                NativeRuntimeFailureV1 runFailure;
                switch (policy)
                {
                    case "Immediate":
                        ran = SchedulingPolicyDriver.TryRunImmediate(agents, updateId: 1, compiled.LeafStatusByRuntimeIndex, out totalSteps, out runFailure);
                        break;
                    case "Budgeted":
                        (totalSteps, runFailure, ran) = RunBudgeted(agents, budgetStepLimit, compiled.LeafStatusByRuntimeIndex);
                        break;
                    case "BatchedJobsSameFrame":
                        ran = SchedulingPolicyDriver.TryRunBatchedJobsSameFrame(agents, updateId: 1, batchSize, compiled.LeafStatusByRuntimeIndex, Allocator.Persistent, out totalSteps, out runFailure);
                        break;
                    default:
                        throw new ArgumentException("Unknown policy: " + policy);
                }

                long stopped = 0;
                long afterAllocated = 0;
                if (measure)
                {
                    stopped = Stopwatch.GetTimestamp();
                    afterAllocated = ReadManagedAllocationCounter();
                }

                if (!ran)
                {
                    throw new InvalidOperationException("Policy " + policy + " failed for scenario run: " + runFailure.Code);
                }

                if (!measure) return null;

                var elapsedTicks = stopped - started;
                return new ScenarioSample
                {
                    sequence = sequence,
                    totalSteps = totalSteps,
                    elapsedTicks = elapsedTicks,
                    elapsedNanoseconds = elapsedTicks * (1_000_000_000.0 / Stopwatch.Frequency),
                    nanosecondsPerAgent = elapsedTicks * (1_000_000_000.0 / Stopwatch.Frequency) / agentCount,
                    managedAllocatedBytes = afterAllocated - beforeAllocated
                };
            }
            finally
            {
                foreach (var agent in agents) agent.Dispose();
            }
        }

        private static (ulong, NativeRuntimeFailureV1, bool) RunBudgeted(SchedulingAgent[] agents, uint budgetStepLimit, NodeStatus[] leafStatus)
        {
            var budgetStates = new NativeBudgetStateV1[agents.Length];
            var success = SchedulingPolicyDriver.TryRunBudgeted(agents, budgetStates, updateId: 1, budgetStepLimit, leafStatus, out var steps, out var failure);
            return (steps, failure, success);
        }

        private static ScenarioSummary Summarize(ScenarioSample[] samples)
        {
            var elapsed = samples.Select(sample => sample.nanosecondsPerAgent).OrderBy(value => value).ToArray();
            var allocations = samples.Select(sample => sample.managedAllocatedBytes).ToArray();
            return new ScenarioSummary
            {
                sampleCount = elapsed.Length,
                minimumNanosecondsPerAgent = elapsed[0],
                medianNanosecondsPerAgent = Percentile(elapsed, 0.50),
                p95NanosecondsPerAgent = Percentile(elapsed, 0.95),
                maximumNanosecondsPerAgent = elapsed[elapsed.Length - 1],
                minimumManagedAllocatedBytes = allocations.Min(),
                maximumManagedAllocatedBytes = allocations.Max(),
                allSamplesObservedZeroManagedAllocation = allocations.All(value => value == 0)
            };
        }

        private static double Percentile(double[] sorted, double percentile)
        {
            var rank = (sorted.Length - 1) * percentile;
            var lower = (int)Math.Floor(rank);
            var upper = (int)Math.Ceiling(rank);
            if (lower == upper) return sorted[lower];
            return sorted[lower] + (sorted[upper] - sorted[lower]) * (rank - lower);
        }

        private static AllocationProbe ProbeManagedAllocationCounter()
        {
            GC.GetAllocatedBytesForCurrentThread();
            var before = GC.GetAllocatedBytesForCurrentThread();
            var canary = new byte[4_096];
            canary[0] = 0x5a;
            var after = GC.GetAllocatedBytesForCurrentThread();
            GC.KeepAlive(canary);
            var delta = after - before;
            if (delta > 0)
            {
                s_allocationCounter = AllocationCounterKind.CurrentThreadAllocatedBytes;
                return new AllocationProbe { api = "GC.GetAllocatedBytesForCurrentThread", positiveCanaryPayloadBytes = 4_096, positiveCanaryObservedBytes = delta };
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Profiler.GetMonoUsedSizeLong();
            before = Profiler.GetMonoUsedSizeLong();
            canary = new byte[1_048_576];
            for (var offset = 0; offset < canary.Length; offset += 4_096) canary[offset] = 0x5a;
            after = Profiler.GetMonoUsedSizeLong();
            GC.KeepAlive(canary);
            delta = after - before;
            if (delta <= 0)
            {
                throw new InvalidOperationException("Neither GC.GetAllocatedBytesForCurrentThread nor Profiler.GetMonoUsedSizeLong detected a positive managed-allocation canary.");
            }

            s_allocationCounter = AllocationCounterKind.MonoUsedSize;
            return new AllocationProbe { api = "Profiler.GetMonoUsedSizeLong", positiveCanaryPayloadBytes = 1_048_576, positiveCanaryObservedBytes = delta };
        }

        private static long ReadManagedAllocationCounter()
        {
            switch (s_allocationCounter)
            {
                case AllocationCounterKind.CurrentThreadAllocatedBytes: return GC.GetAllocatedBytesForCurrentThread();
                case AllocationCounterKind.MonoUsedSize: return Profiler.GetMonoUsedSizeLong();
                default: throw new InvalidOperationException("Managed-allocation counter was not initialized.");
            }
        }

        private static BenchmarkEnvironment CaptureEnvironment(string[] arguments)
        {
            return new BenchmarkEnvironment
            {
                unityVersion = Application.unityVersion,
                collectionsPackageVersion = PackageVersion(typeof(NativeArray<>).Assembly, "com.unity.collections"),
                editorBatchMode = Application.isBatchMode,
                activeBuildTarget = EditorUserBuildSettings.activeBuildTarget.ToString(),
                applicationPlatform = Application.platform.ToString(),
                operatingSystem = SystemInfo.operatingSystem,
                osArchitecture = RuntimeInformation.OSArchitecture.ToString(),
                processArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
                processorType = SystemInfo.processorType,
                processorCount = SystemInfo.processorCount,
                processorFrequencyMHz = SystemInfo.processorFrequency,
                jobWorkerCount = JobsUtility.JobWorkerCount,
                systemMemoryMB = SystemInfo.systemMemorySize,
                managedRuntime = RuntimeInformation.FrameworkDescription,
                stopwatchHighResolution = Stopwatch.IsHighResolution,
                commandLine = (string[])arguments.Clone(),
                thermalAndPowerConditions = "Not controlled or recorded"
            };
        }

        private static string PackageVersion(Assembly assembly, string packageName)
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(assembly);
            if (package != null) return package.version;
            package = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages()
                .FirstOrDefault(candidate => string.Equals(candidate.name, packageName, StringComparison.Ordinal));
            return package == null ? "unknown" : package.version;
        }

        private static string RequiredArgument(string[] arguments, string name)
        {
            for (var index = 0; index < arguments.Length - 1; index++)
            {
                if (string.Equals(arguments[index], name, StringComparison.Ordinal)) return Path.GetFullPath(arguments[index + 1]);
            }

            throw new ArgumentException("Missing required command-line argument " + name + ".");
        }

        private static int IntegerArgument(string[] arguments, string name, int fallback, int minimum)
        {
            for (var index = 0; index < arguments.Length - 1; index++)
            {
                if (!string.Equals(arguments[index], name, StringComparison.Ordinal)) continue;
                if (!int.TryParse(arguments[index + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < minimum)
                {
                    throw new ArgumentException(name + " must be an integer greater than or equal to " + minimum + ".");
                }

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

        private enum AllocationCounterKind : byte
        {
            None = 0,
            CurrentThreadAllocatedBytes = 1,
            MonoUsedSize = 2
        }

        [Serializable]
        private sealed class SchedulingBenchmarkReport
        {
            public string schema;
            public string capturedAtUtc;
            public string scope;
            public BenchmarkEnvironment environment;
            public BenchmarkConfiguration configuration;
            public AllocationProbe allocationProbe;
            public ScenarioReport[] scenarios;
            public PlaceholderScenario[] documentedNotYetImplementedScenarios;
            public PlaceholderPolicy[] documentedNotYetImplementedPolicies;
            public string[] limitations;
        }

        [Serializable]
        private sealed class BenchmarkEnvironment
        {
            public string unityVersion;
            public string collectionsPackageVersion;
            public bool editorBatchMode;
            public string activeBuildTarget;
            public string applicationPlatform;
            public string operatingSystem;
            public string osArchitecture;
            public string processArchitecture;
            public string processorType;
            public int processorCount;
            public int processorFrequencyMHz;
            public int jobWorkerCount;
            public int systemMemoryMB;
            public string managedRuntime;
            public bool stopwatchHighResolution;
            public string[] commandLine;
            public string thermalAndPowerConditions;
        }

        [Serializable]
        private sealed class BenchmarkConfiguration
        {
            public int[] agentCounts;
            public int warmupSamplesPerCase;
            public int measuredSamplesPerCase;
            public uint budgetStepLimit;
            public int[] batchSizes;
            public int[] workerThreadCounts;
            public int maxWorkerThreadCount;
            public string[] policies;
            public string timedRegion;
            public string excludedFromTimedRegion;
            public string batchSizeAndWorkerThreadSweepScope;
        }

        [Serializable]
        private sealed class AllocationProbe
        {
            public string api;
            public int positiveCanaryPayloadBytes;
            public long positiveCanaryObservedBytes;
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
            public int batchSize;
            public int workerThreadCount;
            public ScenarioSample[] samples;
            public ScenarioSummary summary;
        }

        [Serializable]
        private sealed class ScenarioSample
        {
            public int sequence;
            public ulong totalSteps;
            public long elapsedTicks;
            public double elapsedNanoseconds;
            public double nanosecondsPerAgent;
            public long managedAllocatedBytes;
        }

        [Serializable]
        private sealed class ScenarioSummary
        {
            public int sampleCount;
            public double minimumNanosecondsPerAgent;
            public double medianNanosecondsPerAgent;
            public double p95NanosecondsPerAgent;
            public double maximumNanosecondsPerAgent;
            public long minimumManagedAllocatedBytes;
            public long maximumManagedAllocatedBytes;
            public bool allSamplesObservedZeroManagedAllocation;
        }

        [Serializable]
        private sealed class PlaceholderScenario
        {
            public string name;
            public string isolates;
        }

        [Serializable]
        private sealed class PlaceholderPolicy
        {
            public string name;
            public string isolates;
        }
    }
}
