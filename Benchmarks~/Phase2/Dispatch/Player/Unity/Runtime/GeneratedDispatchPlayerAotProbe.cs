using System;
using System.IO;
using System.Runtime.InteropServices;
using Stopwatch = System.Diagnostics.Stopwatch;
using AIBT.Burst;
using AIBT.Execution.Burst.Dispatch;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace AIBT.Tests.Runtime.NativeExecution.Dispatch
{
    [BurstCompile(CompileSynchronously = true)]
    internal struct GeneratedDispatchPlayerAotJob : IJob
    {
        public BurstExecutionBatch Batch;
        public NativeArray<BurstExecutionResult> Execution;
        public NativeArray<int> ManagedPathSentinel;

        public void Execute()
        {
            var managedPath = 0;
            MarkManagedPath(ref managedPath);
            ManagedPathSentinel[0] = managedPath;
            Execution[0] = GeneratedDispatchCanaryCatalog.ExecuteImmediate(ref Batch);
        }

        [BurstDiscard]
        private static void MarkManagedPath(ref int value)
        {
            value = 1;
        }
    }

    public static class GeneratedDispatchPlayerAotProbe
    {
        private const string ResultArgument = "-aibtP2PlayerResult";
        private const string SuccessMarker = "AIBT_P2_012_PLAYER_AOT_OK|";
        private const string FailureMarker = "AIBT_P2_012_PLAYER_AOT_FAIL|";
        private const int LiveValue = 37;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Run()
        {
            var resultPath = FindArgument(ResultArgument);
            try
            {
#if !UNITY_WEBGL || UNITY_EDITOR
                Require(!string.IsNullOrWhiteSpace(resultPath), ResultArgument + " is required.");
#endif
                Require(GeneratedDispatchCanaryCatalog.IsUsable, "The generated catalog is not usable.");
                Require(BurstCompiler.IsEnabled, "Burst is disabled in the Player.");
                Require(IsIl2Cpp, "The Player was not compiled with IL2CPP.");
#if UNITY_WEBGL && !UNITY_EDITOR
                Require(IntPtr.Size == 4, "The Web Player process is not wasm32.");
#else
                Require(IntPtr.Size == 8, "The Player process is not x64.");
#endif

                BurstExecutionResult execution;
                NativeBurstDispatchWorkspaceResultV2 runtimeResult;
                var memoryValue = 0u;
                var assetSentinelIsZero = false;
                var managedPathSentinel = -1;
                VerifyLifecyclePartitions(out var lifecycleSteps, out var budgetSegments);
                MeasureGeneratedDispatch(
                    out var rawNanosecondsPerDispatch,
                    out var rawAlternativeNanosecondsPerDispatch,
                    out var measuredHeapDeltaBytes,
                    out var gen0CollectionDelta,
                    out var controlledAllocationBytes);

                using (var scenario = new Scenario())
                using (var executionOutput = new NativeArray<BurstExecutionResult>(
                    1, Allocator.Persistent, NativeArrayOptions.ClearMemory))
                using (var managedPathOutput = new NativeArray<int>(
                    1, Allocator.Persistent, NativeArrayOptions.ClearMemory))
                {
                    var views = scenario.Views;
                    Require(scenario.Owner.TryBeginRequest(
                        in views,
                        out var lease,
                        out var failure),
                        "TryBeginRequest failed: " + failure);
                    Require(scenario.Owner.TryAcquireImmediateBatch(
                        in lease,
                        out var batch,
                        out failure),
                        "TryAcquireImmediateBatch failed: " + failure);
                    Require(BurstGeneratedRuntimeBridge.TryPrepareSchedule(
                        ref batch,
                        out var scheduledView) == BurstContextResult.Success,
                        "TryPrepareSchedule failed.");

                    var dependency = new GeneratedDispatchPlayerAotJob
                    {
                        Batch = scheduledView,
                        Execution = executionOutput,
                        ManagedPathSentinel = managedPathOutput,
                    }.Schedule();
                    Require(scenario.Owner.TryRegisterDependency(
                        in lease,
                        in batch,
                        dependency,
                        out failure),
                        "TryRegisterDependency failed: " + failure);
                    dependency.Complete();
                    Require(scenario.Owner.TryAcquireCompletedBatch(
                        in lease,
                        out _,
                        out failure),
                        "TryAcquireCompletedBatch failed: " + failure);
                    execution = executionOutput[0];
                    Require(scenario.Owner.TryConsumeResult(
                        in lease,
                        out runtimeResult,
                        out failure),
                        "TryConsumeResult failed: " + failure);

                    managedPathSentinel = managedPathOutput[0];
                    memoryValue = ReadUInt32(scenario.MemoryBytes, 0);
                    assetSentinelIsZero = IsZero(scenario.MemoryBytes, 8, 32);

                    Require(managedPathSentinel == 0,
                        "Managed execution reached the BurstDiscard sentinel.");
                    Require(execution.Code == BurstExecutionCode.Success,
                        "Generated ExecuteImmediate returned " + execution.Code + ".");
                    Require(execution.DiagnosticNumber == 0,
                        "Generated ExecuteImmediate returned diagnostic " + execution.DiagnosticNumber + ".");
                    Require(execution.InstancesVisited == 1u && execution.SegmentSteps == 1UL,
                        "Generated ExecuteImmediate returned unexpected counters.");
                    Require(runtimeResult.Execution.Code == BurstExecutionCode.Success,
                        "Runtime-owned execution result was " + runtimeResult.Execution.Code + ".");
                    Require(runtimeResult.FrameAcquired,
                        "Runtime-owned result did not record a generated dispatch frame.");
                    Require(runtimeResult.CallbackFailure == BurstContextResult.Success,
                        "Generated callback failed with " + runtimeResult.CallbackFailure + ".");
                    Require(runtimeResult.Status == NodeStatus.Success,
                        "Generated callback status was " + runtimeResult.Status + ".");
                    Require(memoryValue == (uint)LiveValue + 1u,
                        "Generated callback did not publish the expected memory value.");
                    Require(assetSentinelIsZero,
                        "The absent AssetId memory sentinel or its canonical padding changed.");
                    Require(IsZero(scenario.MemoryBytes, 4, 4)
                        && IsZero(scenario.MemoryBytes, 40, 8),
                        "Generated memory write-back changed unrelated bytes.");
                    Require(scenario.Owner.TryReset(in lease, out failure),
                        "TryReset failed: " + failure);
                }

                var evidence = new PlayerEvidence
                {
                    schema = "aibt-p2-012-player-aot-v1",
                    passed = true,
                    unityVersion = Application.unityVersion,
                    platform = Application.platform.ToString(),
                    operatingSystem = SystemInfo.operatingSystem,
                    processorType = SystemInfo.processorType,
                    logicalProcessorCount = SystemInfo.processorCount,
                    jobWorkerCount = Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobWorkerCount,
                    il2cpp = IsIl2Cpp,
                    process64Bit = IntPtr.Size == 8,
                    burstEnabled = BurstCompiler.IsEnabled,
                    catalogUsable = GeneratedDispatchCanaryCatalog.IsUsable,
                    generatedEntryPoint = "GeneratedDispatchCanaryCatalog.ExecuteImmediate",
                    burstJob = typeof(GeneratedDispatchPlayerAotJob).FullName,
                    dispatchMeasurementMode = DispatchMeasurementMode,
                    alternativeDispatchMeasurementMode = AlternativeDispatchMeasurementMode,
                    managedPathSentinelScope = "ScheduledBurstFeasibilityProbe",
                    managedPathSentinel = managedPathSentinel,
                    executionCode = execution.Code.ToString(),
                    diagnosticNumber = execution.DiagnosticNumber,
                    instancesVisited = execution.InstancesVisited,
                    segmentSteps = checked((long)execution.SegmentSteps),
                    callbackFailure = runtimeResult.CallbackFailure.ToString(),
                    callbackStatus = runtimeResult.Status.ToString(),
                    memoryValue = memoryValue,
                    zeroAssetIdSentinelPreserved = assetSentinelIsZero,
                    immediateBudgetedEquivalent = true,
                    behaviorMatrixPassed = true,
                    behaviorMatrixCases = BehaviorMatrixCases,
                    lifecycleSteps = lifecycleSteps,
                    budgetSegments = budgetSegments,
                    measurementIterationsPerSample = 1024,
                    rawNanosecondsPerDispatch = rawNanosecondsPerDispatch,
                    rawAlternativeNanosecondsPerDispatch = rawAlternativeNanosecondsPerDispatch,
                    measuredHeapDeltaBytes = measuredHeapDeltaBytes,
                    gen0CollectionDelta = gen0CollectionDelta,
                    controlledAllocationBytes = controlledAllocationBytes,
                    allocationMetricLimitation = "GC.GetTotalMemory is a coarse Web/Player signal, not a zero-allocation proof.",
                };
                var json = JsonUtility.ToJson(evidence, true);
                PublishEvidence(resultPath, json);
                Debug.Log(SuccessMarker + JsonUtility.ToJson(evidence));
#if !UNITY_WEBGL || UNITY_EDITOR
                Application.Quit(0);
#endif
            }
            catch (Exception exception)
            {
                var evidence = new PlayerEvidence
                {
                    schema = "aibt-p2-012-player-aot-v1",
                    passed = false,
                    unityVersion = Application.unityVersion,
                    platform = Application.platform.ToString(),
                    operatingSystem = SystemInfo.operatingSystem,
                    processorType = SystemInfo.processorType,
                    logicalProcessorCount = SystemInfo.processorCount,
                    jobWorkerCount = Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobWorkerCount,
                    il2cpp = IsIl2Cpp,
                    process64Bit = IntPtr.Size == 8,
                    burstEnabled = BurstCompiler.IsEnabled,
                    catalogUsable = GeneratedDispatchCanaryCatalog.IsUsable,
                    error = exception.ToString(),
                };
                var json = JsonUtility.ToJson(evidence, true);
                PublishEvidence(resultPath, json);
                Debug.LogError(FailureMarker + JsonUtility.ToJson(evidence));
#if !UNITY_WEBGL || UNITY_EDITOR
                Application.Quit(2);
#endif
            }
        }

        private static void PublishEvidence(string path, string json)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            AIBTWebReport(json);
#else
            TryWriteEvidence(path, json);
#endif
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void AIBTWebReport(string json);
#endif

        private static string FindArgument(string name)
        {
            var arguments = Environment.GetCommandLineArgs();
            for (var index = 0; index < arguments.Length - 1; index++)
            {
                if (string.Equals(arguments[index], name, StringComparison.Ordinal))
                {
                    return Path.GetFullPath(arguments[index + 1]);
                }
            }

            return null;
        }

        private static void WriteEvidence(string path, string json)
        {
            var directory = Path.GetDirectoryName(path);
            Require(!string.IsNullOrEmpty(directory), "The result path has no parent directory.");
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, json);
        }

        private static void TryWriteEvidence(string path, string json)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path))
                {
                    WriteEvidence(path, json);
                }
            }
            catch
            {
                // The Player log retains the failure marker when the evidence path is unavailable.
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private static bool IsZero(NativeArray<byte> bytes, int offset, int length)
        {
            for (var index = 0; index < length; index++)
            {
                if (bytes[offset + index] != 0)
                {
                    return false;
                }
            }

            return true;
        }

        private static uint ReadUInt32(NativeArray<byte> bytes, int offset)
        {
            return bytes[offset]
                | (uint)bytes[offset + 1] << 8
                | (uint)bytes[offset + 2] << 16
                | (uint)bytes[offset + 3] << 24;
        }

        private static void VerifyLifecyclePartitions(out int immediateSteps, out int budgetSegments)
        {
            using (var immediate = new LifecycleScenario())
            using (var budgeted = new LifecycleScenario())
            {
                var expected = new NativeArray<byte>(8, Allocator.Temp, NativeArrayOptions.ClearMemory);
                try
                {
                    immediateSteps = 0;
                    while (true)
                    {
                        Require(immediate.Machine.TryAdvance(out var step, out var failure),
                            "Immediate lifecycle failed: " + failure.Code);
                        Require(immediateSteps < expected.Length, "Immediate lifecycle exceeded its fixed trace capacity.");
                        expected[immediateSteps++] = (byte)step.Kind;
                        if (step.Kind == NativeLifecycleStepKindV1.Completed) break;
                    }

                    var budget = default(NativeBudgetStateV1);
                    budgetSegments = 0;
                    for (var index = 0; index < immediateSteps; index++)
                    {
                        Require(NativeLifecycleBudgetDriverV1.TryBeginSegment(1, ref budget),
                            "Budget segment could not begin.");
                        Require(NativeLifecycleBudgetDriverV1.TryAdvance(
                            ref budgeted.Machine, ref budget, out _, out var step, out var failure),
                            "Budgeted lifecycle failed: " + failure.Code);
                        Require((byte)step.Kind == expected[index], "Immediate and budgeted traces differ.");
                        budgetSegments++;
                    }
                }
                finally
                {
                    expected.Dispose();
                }
            }
        }

        private static void MeasureGeneratedDispatch(
            out double[] rawNanosecondsPerDispatch,
            out double[] rawAlternativeNanosecondsPerDispatch,
            out long measuredHeapDeltaBytes,
            out int gen0CollectionDelta,
            out int controlledAllocationBytes)
        {
            const int warmup = 32;
            const int iterations = 1024;
            const int samples = 7;
            using (var scenario = new Scenario())
            using (var execution = new NativeArray<BurstExecutionResult>(
                1, Allocator.Persistent, NativeArrayOptions.ClearMemory))
            using (var managedPath = new NativeArray<int>(
                1, Allocator.Persistent, NativeArrayOptions.ClearMemory))
            {
                for (var index = 0; index < warmup; index++)
                    ExecuteMeasuredCycle(scenario, execution, managedPath);
                rawNanosecondsPerDispatch = new double[samples];
                GC.Collect();
                var beforeCollections = GC.CollectionCount(0);
                var beforeHeap = GC.GetTotalMemory(false);
                for (var sample = 0; sample < samples; sample++)
                {
                    var started = Stopwatch.GetTimestamp();
                    for (var index = 0; index < iterations; index++)
                        ExecuteMeasuredCycle(scenario, execution, managedPath);
                    var elapsed = Stopwatch.GetTimestamp() - started;
                    rawNanosecondsPerDispatch[sample] = elapsed * 1_000_000_000d
                        / Stopwatch.Frequency / iterations;
                    Require(rawNanosecondsPerDispatch[sample] > 0d,
                        "Generated dispatch measurement returned a non-positive sample.");
                }
                measuredHeapDeltaBytes = GC.GetTotalMemory(false) - beforeHeap;
                gen0CollectionDelta = GC.CollectionCount(0) - beforeCollections;
#if UNITY_WEBGL && !UNITY_EDITOR
                for (var index = 0; index < warmup; index++)
                    ExecuteScheduledCycle(scenario, execution, managedPath);
                rawAlternativeNanosecondsPerDispatch = new double[samples];
                for (var sample = 0; sample < samples; sample++)
                {
                    var started = Stopwatch.GetTimestamp();
                    for (var index = 0; index < iterations; index++)
                        ExecuteScheduledCycle(scenario, execution, managedPath);
                    var elapsed = Stopwatch.GetTimestamp() - started;
                    rawAlternativeNanosecondsPerDispatch[sample] = elapsed * 1_000_000_000d
                        / Stopwatch.Frequency / iterations;
                    Require(rawAlternativeNanosecondsPerDispatch[sample] > 0d,
                        "Scheduled feasibility measurement returned a non-positive sample.");
                }
#else
                rawAlternativeNanosecondsPerDispatch = Array.Empty<double>();
#endif
            }

            var canary = new byte[1024 * 1024];
            canary[0] = 17;
            canary[canary.Length - 1] = 29;
            controlledAllocationBytes = canary.Length;
            Require(canary[0] + canary[canary.Length - 1] == 46,
                "Controlled allocation canary was not observable.");
            GC.KeepAlive(canary);
        }

        private static void ExecuteMeasuredCycle(
            Scenario scenario,
            NativeArray<BurstExecutionResult> execution,
            NativeArray<int> managedPath)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            ExecuteImmediateCycle(scenario);
#else
            ExecuteScheduledCycle(scenario, execution, managedPath);
#endif
        }

        private static void ExecuteImmediateCycle(Scenario scenario)
        {
            var views = scenario.Views;
            Require(scenario.Owner.TryBeginRequest(in views, out var lease, out var failure),
                "Measured TryBeginRequest failed.");
            Require(scenario.Owner.TryAcquireImmediateBatch(in lease, out var batch, out failure),
                "Measured TryAcquireImmediateBatch failed.");
            var execution = GeneratedDispatchCanaryCatalog.ExecuteImmediate(ref batch);
            Require(execution.Code == BurstExecutionCode.Success,
                "Measured direct dispatch failed.");
            Require(scenario.Owner.TryConsumeResult(in lease, out var result, out failure),
                "Measured TryConsumeResult failed.");
            Require(result.Execution.Code == BurstExecutionCode.Success
                && result.CallbackFailure == BurstContextResult.Success,
                "Measured runtime result failed.");
            Require(scenario.Owner.TryReset(in lease, out failure),
                "Measured TryReset failed.");
        }

        private static void ExecuteScheduledCycle(
            Scenario scenario,
            NativeArray<BurstExecutionResult> execution,
            NativeArray<int> managedPath)
        {
            var views = scenario.Views;
            Require(scenario.Owner.TryBeginRequest(in views, out var lease, out var failure),
                "Measured TryBeginRequest failed.");
            Require(scenario.Owner.TryAcquireImmediateBatch(in lease, out var batch, out failure),
                "Measured TryAcquireImmediateBatch failed.");
            Require(BurstGeneratedRuntimeBridge.TryPrepareSchedule(ref batch, out var scheduled)
                == BurstContextResult.Success, "Measured TryPrepareSchedule failed.");
            var dependency = new GeneratedDispatchPlayerAotJob
            {
                Batch = scheduled,
                Execution = execution,
                ManagedPathSentinel = managedPath,
            }.Schedule();
            Require(scenario.Owner.TryRegisterDependency(
                in lease, in batch, dependency, out failure),
                "Measured dependency registration failed.");
            dependency.Complete();
            Require(scenario.Owner.TryAcquireCompletedBatch(in lease, out _, out failure),
                "Measured completed batch acquisition failed.");
            Require(execution[0].Code == BurstExecutionCode.Success && managedPath[0] == 0,
                "Measured Burst dispatch failed.");
            Require(scenario.Owner.TryConsumeResult(in lease, out var result, out failure),
                "Measured TryConsumeResult failed.");
            Require(result.Execution.Code == BurstExecutionCode.Success
                && result.CallbackFailure == BurstContextResult.Success,
                "Measured runtime result failed.");
            Require(scenario.Owner.TryReset(in lease, out failure),
                "Measured TryReset failed.");
        }

        private static void WriteUInt32(NativeArray<byte> bytes, int offset, uint value)
        {
            bytes[offset] = (byte)value;
            bytes[offset + 1] = (byte)(value >> 8);
            bytes[offset + 2] = (byte)(value >> 16);
            bytes[offset + 3] = (byte)(value >> 24);
        }

#if ENABLE_IL2CPP
        private const bool IsIl2Cpp = true;
#else
        private const bool IsIl2Cpp = false;
#endif

#if UNITY_WEBGL && !UNITY_EDITOR
        private const string DispatchMeasurementMode = "DirectSingleThreadImmediate";
        private const string AlternativeDispatchMeasurementMode = "ScheduledIJobFeasibility";
        private static readonly string[] BehaviorMatrixCases =
        {
            "empty-sequence-immediate",
            "empty-sequence-budgeted",
            "generated-user-node-direct",
            "generated-user-node-scheduled-feasibility",
        };
#else
        private const string DispatchMeasurementMode = "ScheduledBurstJob";
        private const string AlternativeDispatchMeasurementMode = "NotApplicable";
        private static readonly string[] BehaviorMatrixCases =
        {
            "empty-sequence-immediate",
            "empty-sequence-budgeted",
            "generated-user-node-scheduled",
        };
#endif

        [Serializable]
        private sealed class PlayerEvidence
        {
            public string schema;
            public bool passed;
            public string unityVersion;
            public string platform;
            public string operatingSystem;
            public string processorType;
            public int logicalProcessorCount;
            public int jobWorkerCount;
            public bool il2cpp;
            public bool process64Bit;
            public bool burstEnabled;
            public bool catalogUsable;
            public string generatedEntryPoint;
            public string burstJob;
            public string dispatchMeasurementMode;
            public string alternativeDispatchMeasurementMode;
            public string managedPathSentinelScope;
            public int managedPathSentinel;
            public string executionCode;
            public int diagnosticNumber;
            public uint instancesVisited;
            public long segmentSteps;
            public string callbackFailure;
            public string callbackStatus;
            public uint memoryValue;
            public bool zeroAssetIdSentinelPreserved;
            public bool immediateBudgetedEquivalent;
            public bool behaviorMatrixPassed;
            public string[] behaviorMatrixCases;
            public int lifecycleSteps;
            public int budgetSegments;
            public int measurementIterationsPerSample;
            public double[] rawNanosecondsPerDispatch;
            public double[] rawAlternativeNanosecondsPerDispatch;
            public long measuredHeapDeltaBytes;
            public int gen0CollectionDelta;
            public int controlledAllocationBytes;
            public string allocationMetricLimitation;
            public string error;
        }

        private sealed class LifecycleScenario : IDisposable
        {
            private readonly NativeArray<NativeCompiledNodeRecordV1> _nodes;
            private readonly NativeArray<uint> _children;
            private readonly NativeArray<NativeLifecycleNodeBindingV1> _bindings;
            private readonly NativeArray<byte> _memory;
            private readonly NativeArray<NativeFrameStateV1> _frames;
            private readonly NativeArray<uint> _generations;
            private readonly NativeArray<NativeLifecycleControlV1> _control;

            internal LifecycleScenario()
            {
                _nodes = new NativeArray<NativeCompiledNodeRecordV1>(new[]
                {
                    new NativeCompiledNodeRecordV1(new CompiledNodeRecord(
                        StableHash.Fnv1A64("aibt.core.memory-sequence"), 1, 0, 0, 1,
                        0, 4, 4, NodeMemoryLifetime.Activation,
                        new CompiledRange(0, 0), CompiledNodeFlags.BurstDomain, 0,
                        new CompiledRange(0, 0), new CompiledRange(0, 0)))
                }, Allocator.Persistent);
                _children = new NativeArray<uint>(0, Allocator.Persistent);
                _bindings = new NativeArray<NativeLifecycleNodeBindingV1>(new[]
                {
                    new NativeLifecycleNodeBindingV1(0, NativeLifecycleNodeKindV1.MemorySequence)
                }, Allocator.Persistent);
                _memory = new NativeArray<byte>(4, Allocator.Persistent);
                _frames = new NativeArray<NativeFrameStateV1>(1, Allocator.Persistent);
                _generations = new NativeArray<uint>(1, Allocator.Persistent);
                _control = new NativeArray<NativeLifecycleControlV1>(1, Allocator.Persistent);
                Require(NativeLifecycleMachineV1.TryCreate(
                    _nodes, _children, _bindings, _memory, _frames, _generations, _control,
                    out var machine, out var failure), "Lifecycle creation failed: " + failure.Code);
                Require(machine.TryBeginUpdate(1, out failure), "Lifecycle update failed: " + failure.Code);
                Machine = machine;
            }

            internal NativeLifecycleMachineV1 Machine;

            public void Dispose()
            {
                _control.Dispose();
                _generations.Dispose();
                _frames.Dispose();
                _memory.Dispose();
                _bindings.Dispose();
                _children.Dispose();
                _nodes.Dispose();
            }
        }

        private sealed class Scenario : IDisposable
        {
            private readonly NativeArray<NativeBurstDispatchCaseV2> _cases;
            private readonly NativeArray<NativeBurstDispatchFieldV2> _configurationFields;
            private readonly NativeArray<NativeBurstDispatchFieldV2> _memoryFields;
            private readonly NativeArray<NativeBurstDispatchBindingV2> _bindings;
            private readonly NativeArray<NativeBurstDispatchFieldV2> _valueFields;
            private readonly NativeArray<NativeBurstDispatchCanonicalRangeV2> _caseRanges;
            private readonly NativeArray<NativeBurstDispatchCanonicalRangeV2> _bindingRanges;
            private readonly NativeArray<NativeBurstDispatchCanonicalRuleV2> _rules;
            private readonly NativeArray<byte> _configurationBytes;
            private readonly NativeArray<ulong> _randomStates;
            private readonly NativeArray<ulong> _randomIncrements;
            private readonly NativeArray<NativeBurstDispatchResolvedBindingV2> _resolvedBindings;
            private readonly NativeArray<byte> _liveValueBytes;
            private readonly NativeArray<NativeBurstDispatchCompletionV2> _completions;
            private readonly NativeArray<byte> _completionPayloadBytes;
            private readonly NativeArray<NativeBurstDispatchCommandV2> _commands;
            private readonly NativeArray<byte> _commandPayloadBytes;
            private readonly NativeArray<NativeBurstDispatchOperationV2> _operations;
            private readonly NativeArray<NativeBurstDispatchTransactionControlV2> _transactionControl;
            private bool _ownerDisposed;

            internal Scenario()
            {
                _cases = Array<NativeBurstDispatchCaseV2>(1);
                _configurationFields = Array<NativeBurstDispatchFieldV2>(2);
                _memoryFields = Array<NativeBurstDispatchFieldV2>(6);
                _bindings = Array<NativeBurstDispatchBindingV2>(1);
                _valueFields = Array<NativeBurstDispatchFieldV2>(1);
                _caseRanges = Array<NativeBurstDispatchCanonicalRangeV2>(2);
                _bindingRanges = Array<NativeBurstDispatchCanonicalRangeV2>(2);
                _rules = Array<NativeBurstDispatchCanonicalRuleV2>(1);

                _cases[0] = new NativeBurstDispatchCaseV2(
                    StableHash.Fnv1A64("aibt.tests.generated-node"),
                    1u,
                    0u,
                    0u,
                    2u,
                    8u,
                    0u,
                    6u,
                    48u,
                    NativeBurstDispatchPhaseMaskV2.Enter
                        | NativeBurstDispatchPhaseMaskV2.Tick
                        | NativeBurstDispatchPhaseMaskV2.Abort
                        | NativeBurstDispatchPhaseMaskV2.Exit
                        | NativeBurstDispatchPhaseMaskV2.Observer,
                    BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure,
                    false,
                    0u,
                    1u);
                _configurationFields[0] = new NativeBurstDispatchFieldV2(
                    0u, 0u, 1u, 1u, NativeBurstDispatchFieldEncodingV2.Boolean);
                _configurationFields[1] = new NativeBurstDispatchFieldV2(
                    1u, 4u, 1u, 4u, NativeBurstDispatchFieldEncodingV2.GeneratedHandle);
                _memoryFields[0] = new NativeBurstDispatchFieldV2(
                    0u, 0u, 1u, 4u, NativeBurstDispatchFieldEncodingV2.UInt32);
                _memoryFields[1] = new NativeBurstDispatchFieldV2(
                    1u, 0u, 8u, 1u, 8u, NativeBurstDispatchFieldEncodingV2.UInt64,
                    NativeBurstDispatchCanonicalRuleKindV2.AssetId);
                _memoryFields[2] = new NativeBurstDispatchFieldV2(
                    1u, 1u, 16u, 1u, 8u, NativeBurstDispatchFieldEncodingV2.UInt64);
                _memoryFields[3] = new NativeBurstDispatchFieldV2(
                    1u, 2u, 24u, 1u, 8u, NativeBurstDispatchFieldEncodingV2.Int64);
                _memoryFields[4] = new NativeBurstDispatchFieldV2(
                    1u, 3u, 32u, 1u, 1u, NativeBurstDispatchFieldEncodingV2.Boolean);
                _memoryFields[5] = new NativeBurstDispatchFieldV2(
                    1u, 4u, 40u, 1u, 4u, NativeBurstDispatchFieldEncodingV2.Int32);
                _bindings[0] = new NativeBurstDispatchBindingV2(
                    0u,
                    1u,
                    NativeBurstDispatchBindingKindV2.BlackboardRead,
                    (byte)BlackboardScope.Agent,
                    NativeBurstDispatchBindingPhaseMaskV2.None,
                    NativeBuiltInBlackboardTypeIdsV1.Int32,
                    1u,
                    0u,
                    1u,
                    4u,
                    0UL,
                    0u,
                    0u,
                    0u,
                    0u);
                _valueFields[0] = new NativeBurstDispatchFieldV2(
                    0u, 0u, 1u, 4u, NativeBurstDispatchFieldEncodingV2.Int32);
                _caseRanges[0] = new NativeBurstDispatchCanonicalRangeV2(0u, 0u);
                _caseRanges[1] = new NativeBurstDispatchCanonicalRangeV2(0u, 1u);
                _bindingRanges[0] = new NativeBurstDispatchCanonicalRangeV2(1u, 0u);
                _bindingRanges[1] = new NativeBurstDispatchCanonicalRangeV2(1u, 0u);
                _rules[0] = new NativeBurstDispatchCanonicalRuleV2(
                    NativeBurstDispatchCanonicalRuleKindV2.AssetId, 8u);

                var canonical = new NativeBurstDispatchCanonicalInputV2(
                    _caseRanges.AsReadOnly(),
                    _bindingRanges.AsReadOnly(),
                    _rules.AsReadOnly());
                var shape = new NativeBurstDispatchWorkspaceShapeV2(
                    GeneratedDispatchCanaryCatalog.HandshakeForPlayerAot(),
                    _cases.AsReadOnly(),
                    _configurationFields.AsReadOnly(),
                    _memoryFields.AsReadOnly(),
                    _bindings.AsReadOnly(),
                    _valueFields.AsReadOnly(),
                    canonical);
                var capacity = new NativeBurstDispatchWorkspaceCapacityV2(
                    48u,
                    new NativeBurstDispatchBindingCapacityV2(1u, 4u, 0u, 0u, 0u, 7UL));
                Require(NativeBurstDispatchWorkspaceOwnerV2.TryCreate(
                    in shape,
                    in capacity,
                    Allocator.Persistent,
                    out var owner,
                    out var failure),
                    "NativeBurstDispatchWorkspaceOwnerV2.TryCreate failed: " + failure);
                Owner = owner;

                _configurationBytes = Bytes(8);
                _configurationBytes[0] = 1;
                WriteUInt32(_configurationBytes, 4, 0u);
                MemoryBytes = Bytes(48);
                _randomStates = Array<ulong>(0);
                _randomIncrements = Array<ulong>(0);
                _resolvedBindings = Array<NativeBurstDispatchResolvedBindingV2>(1);
                _resolvedBindings[0] = new NativeBurstDispatchResolvedBindingV2(0u, 23u, 0u);
                _liveValueBytes = Bytes(4);
                WriteUInt32(_liveValueBytes, 0, LiveValue);
                _completions = Array<NativeBurstDispatchCompletionV2>(0);
                _completionPayloadBytes = Bytes(0);
                _commands = Array<NativeBurstDispatchCommandV2>(0);
                _commandPayloadBytes = Bytes(0);
                _operations = Array<NativeBurstDispatchOperationV2>(0);
                _transactionControl = Array<NativeBurstDispatchTransactionControlV2>(1);
                var transaction = new NativeBurstDispatchTransactionControlV2
                {
                    LedgerToken = 0xd15ca7c1UL,
                    TreeInstanceId = new TreeInstanceId(19UL),
                    NextOperationSequence = 7UL,
                };
                NativeBurstDispatchTransactionLedgerV2.Initialize(ref transaction);
                _transactionControl[0] = transaction;

                var request = new NativeBurstDispatchRequestV2(
                    0u,
                    7u,
                    StableHash.Fnv1A64("aibt.tests.generated-node"),
                    1u,
                    0u,
                    BurstCallbackPhase.Tick,
                    0u,
                    0u,
                    0u,
                    123_456L,
                    new TreeInstanceId(19UL),
                    1u,
                    0u,
                    1u);
                Views = new NativeBurstDispatchWorkspaceRequestViewsV2(
                    request,
                    _configurationBytes,
                    MemoryBytes,
                    _randomStates,
                    _randomIncrements,
                    _resolvedBindings,
                    _liveValueBytes,
                    _completions,
                    _completionPayloadBytes,
                    _commands,
                    _commandPayloadBytes,
                    _operations,
                    _transactionControl);
            }

            internal NativeBurstDispatchWorkspaceOwnerV2 Owner { get; }
            internal NativeBurstDispatchWorkspaceRequestViewsV2 Views { get; }
            internal NativeArray<byte> MemoryBytes { get; }

            public void Dispose()
            {
                if (!_ownerDisposed && Owner != null)
                {
                    Require(Owner.TryDispose(out var failure),
                        "Native dispatch owner disposal failed: " + failure);
                    _ownerDisposed = true;
                }

                Dispose(_transactionControl);
                Dispose(_operations);
                Dispose(_commandPayloadBytes);
                Dispose(_commands);
                Dispose(_completionPayloadBytes);
                Dispose(_completions);
                Dispose(_liveValueBytes);
                Dispose(_resolvedBindings);
                Dispose(_randomIncrements);
                Dispose(_randomStates);
                Dispose(MemoryBytes);
                Dispose(_configurationBytes);
                Dispose(_rules);
                Dispose(_bindingRanges);
                Dispose(_caseRanges);
                Dispose(_valueFields);
                Dispose(_bindings);
                Dispose(_memoryFields);
                Dispose(_configurationFields);
                Dispose(_cases);
            }

            private static NativeArray<T> Array<T>(int length) where T : struct
            {
                return new NativeArray<T>(
                    length, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            }

            private static NativeArray<byte> Bytes(int length)
            {
                return new NativeArray<byte>(
                    length, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            }

            private static void Dispose<T>(NativeArray<T> value) where T : struct
            {
                if (value.IsCreated)
                {
                    value.Dispose();
                }
            }
        }
    }
}
