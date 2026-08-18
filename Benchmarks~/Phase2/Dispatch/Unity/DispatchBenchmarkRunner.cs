using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using AIBT.Burst;
using AIBT.Execution.Burst.Dispatch;
using Unity.Burst;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine.Profiling;

namespace AIBT.Benchmarks.Phase2.Dispatch
{
    [BurstCompile]
    internal static partial class DispatchBenchmarkRunner
    {
        private const int DefaultWarmupSamples = 5;
        private const int DefaultMeasuredSamples = 15;
        private const int DefaultDispatchesPerSample = 16_384;
        private const ulong FirstTypeNumericId = 0xa1b7_0000_0000_0001UL;
        private const uint TypeVersion = 1u;
        private static readonly int[] TypeCounts = { 1, 16, 128 };
        private static readonly string[] CompiledEntryPointNames =
        {
            "ExecuteGeneratedDispatchSwitch1",
            "ExecuteGeneratedDispatchSwitch16",
            "ExecuteGeneratedDispatchSwitch128"
        };
        private static AllocationCounterKind s_allocationCounter;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate long DispatchFunction(ref BurstExecutionBatch batch);

        public static void Run()
        {
            var arguments = Environment.GetCommandLineArgs();
            var outputPath = RequiredArgument(arguments, "-aibtBenchmarkOutput");
            var warmupSamples = IntegerArgument(
                arguments, "-aibtWarmupSamples", DefaultWarmupSamples, 1);
            var measuredSamples = IntegerArgument(
                arguments, "-aibtMeasuredSamples", DefaultMeasuredSamples, 1);
            var dispatchesPerSample = IntegerArgument(
                arguments, "-aibtDispatchesPerSample", DefaultDispatchesPerSample, 128);
            if (dispatchesPerSample % 128 != 0)
            {
                throw new ArgumentException(
                    "-aibtDispatchesPerSample must be divisible by 128 so every type is exercised equally.");
            }

            if (!BurstCompiler.IsEnabled)
            {
                throw new InvalidOperationException(
                    "Burst compilation is disabled; this benchmark refuses a managed fallback.");
            }

            var compiledSwitch1 = BurstCompiler.CompileFunctionPointer<DispatchFunction>(
                ExecuteGeneratedDispatchSwitch1);
            var compiledSwitch16 = BurstCompiler.CompileFunctionPointer<DispatchFunction>(
                ExecuteGeneratedDispatchSwitch16);
            var compiledSwitch128 = BurstCompiler.CompileFunctionPointer<DispatchFunction>(
                ExecuteGeneratedDispatchSwitch128);
            var executeFunctions = new[]
            {
                compiledSwitch1.Invoke,
                compiledSwitch16.Invoke,
                compiledSwitch128.Invoke
            };
            var allocationProbe = ProbeManagedAllocationCounter();

            for (var caseIndex = 0; caseIndex < TypeCounts.Length; caseIndex++)
            {
                for (var warmupIndex = 0; warmupIndex < warmupSamples; warmupIndex++)
                {
                    RunOneSample(
                        executeFunctions[caseIndex],
                        CompiledEntryPointNames[caseIndex],
                        TypeCounts[caseIndex],
                        dispatchesPerSample,
                        warmupIndex,
                        false);
                }
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var reports = TypeCounts
                .Select((typeCount, caseIndex) => new DispatchCaseReport
                {
                    typeCount = typeCount,
                    generatedSwitchWidth = typeCount,
                    compiledEntryPoint = CompiledEntryPointNames[caseIndex],
                    samples = new DispatchSample[measuredSamples]
                })
                .ToArray();

            var globalSequence = 0;
            for (var sampleIndex = 0; sampleIndex < measuredSamples; sampleIndex++)
            {
                for (var caseIndex = 0; caseIndex < reports.Length; caseIndex++)
                {
                    reports[caseIndex].samples[sampleIndex] = RunOneSample(
                        executeFunctions[caseIndex],
                        CompiledEntryPointNames[caseIndex],
                        reports[caseIndex].typeCount,
                        dispatchesPerSample,
                        globalSequence++,
                        true);
                }
            }

            for (var caseIndex = 0; caseIndex < reports.Length; caseIndex++)
            {
                PopulateSummary(reports[caseIndex], dispatchesPerSample);
            }

            var report = new DispatchBenchmarkReport
            {
                schema = "aibt-p2-012-generated-dispatch-benchmark-v2",
                capturedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                scenarioRevision = "p2-012-generated-closed-switch-enter-v2",
                scope = "Burst-compiled generated closed-switch ABI v2 dispatch microcase",
                environment = CaptureEnvironment(arguments),
                configuration = new BenchmarkConfiguration
                {
                    typeCounts = (int[])TypeCounts.Clone(),
                    warmupSamplesPerTypeCount = warmupSamples,
                    measuredSamplesPerTypeCount = measuredSamples,
                    dispatchesPerSample = dispatchesPerSample,
                    stopwatchFrequency = Stopwatch.Frequency,
                    sampleOrder = "Warm every case, then measured round-robin 1/16/128",
                    timedRegion =
                        "One matching Burst function-pointer invocation with an exact-width generated switch",
                    dispatchSelection =
                        "Separate 1-, 16-, and 128-case switches; matching function selected before timing",
                    excludedFromTimedRegion =
                        "Owner/input allocation, native copies, disposal, validation, GC, and JSON serialization"
                },
                allocationProbe = allocationProbe,
                cases = reports,
                evidence = new CompilationEvidence
                {
                    burstEnabled = BurstCompiler.IsEnabled,
                    compiledEntryPoints = (string[])CompiledEntryPointNames.Clone(),
                    managedFallbackAllowed = false,
                    burstDiscardSentinelPassed = true,
                    allWarmupsAndSamplesCompleted = true
                },
                limitations = new[]
                {
                    "This isolated Windows Editor batchmode run is a focused microbenchmark, not a Player platform baseline.",
                    "The compiled function pointer proves Burst-native execution in Editor; it does not replace Player AOT artifact or Burst Inspector evidence.",
                    "Thermal state and power policy are not controlled by the runner.",
                    "No performance pass/fail threshold is inferred from this workstation."
                }
            };

            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(outputPath, JsonUtility.ToJson(report, true), new UTF8Encoding(false));
            UnityEngine.Debug.Log("AIBT P2-012 dispatch benchmark completed: " + outputPath);
            GC.KeepAlive(compiledSwitch1);
            GC.KeepAlive(compiledSwitch16);
            GC.KeepAlive(compiledSwitch128);
        }

        private static BurstContextResult TryAcquireBenchmarkFrame(
            ref BurstExecutionBatch batch,
            out uint catalogCaseIndex,
            out BurstDispatchFrame frame,
            out bool hasWork,
            out int failureStage)
        {
            catalogCaseIndex = default;
            frame = default;
            hasWork = false;
            failureStage = 1;
            var gate = BurstGeneratedRuntimeBridge.TryGetExecutionRequest(
                in batch,
                out var instanceOrdinal,
                out var runtimeNodeIndex,
                out catalogCaseIndex,
                out var phase,
                out hasWork);
            if (gate != BurstContextResult.Success || !hasWork)
            {
                return gate;
            }

            failureStage = 2;
            return BurstGeneratedRuntimeBridge.TryAcquireDispatchFrame(
                ref batch,
                instanceOrdinal,
                runtimeNodeIndex,
                catalogCaseIndex,
                phase,
                out frame);
        }

        private static BurstContextResult ExecuteMicroCallback(
            ref BurstExecutionBatch batch,
            in BurstDispatchFrame frame)
        {
            var gate = BurstGeneratedRuntimeBridge.TryCreateConfigurationReader(
                in frame, out var configuration);
            if (gate != BurstContextResult.Success) return gate;

            gate = BurstGeneratedRuntimeBridge.TryReadUInt32(
                ref configuration, 0u, 0u, out var configurationValue);
            if (gate != BurstContextResult.Success) return gate;

            gate = BurstGeneratedRuntimeBridge.TryCreateMemoryAccessor(
                in frame, out var memory);
            if (gate != BurstContextResult.Success) return gate;

            gate = BurstGeneratedRuntimeBridge.TryReadMemoryInt32(
                ref memory, 0u, 0u, out var memoryValue);
            if (gate != BurstContextResult.Success) return gate;

            gate = BurstGeneratedRuntimeBridge.TryWriteMemoryInt32(
                ref memory,
                0u,
                0u,
                unchecked(memoryValue + (int)configurationValue));
            if (gate != BurstContextResult.Success) return gate;

            gate = BurstGeneratedRuntimeBridge.TryCommitMemory(ref memory);
            if (gate != BurstContextResult.Success) return gate;

            gate = BurstGeneratedRuntimeBridge.TryCreateEnterContext(
                in frame, out var context);
            return gate == BurstContextResult.Success
                ? BurstGeneratedRuntimeBridge.TryCompleteEnter(
                    ref batch, in frame, ref context)
                : gate;
        }

        private static long ValidateCompletedExecution(
            in BurstExecutionBatch batch,
            long processed)
        {
            var gate = BurstGeneratedRuntimeBridge.TryGetExecutionResult(
                in batch, out var executionResult);
            if (gate != BurstContextResult.Success)
            {
                return FailureCode(11, gate);
            }

            if (!executionResult.Success
                || executionResult.InstancesVisited != (uint)processed
                || executionResult.SegmentSteps != (ulong)processed)
            {
                return FailureCode(12, BurstContextResult.InvalidStatus);
            }

            return processed;
        }

        private static long FailureCode(int stage, BurstContextResult result)
            => -(((long)stage << 8) + (byte)result + 1L);

        private static bool IsManagedFallback()
        {
            var marker = 0;
            MarkManagedFallback(ref marker);
            return marker != 0;
        }

        [BurstDiscard]
        private static void MarkManagedFallback(ref int marker)
        {
            marker = 1;
        }

        private static DispatchSample RunOneSample(
            DispatchFunction execute,
            string compiledEntryPoint,
            int typeCount,
            int dispatchesPerSample,
            int sequence,
            bool measure)
        {
            NativeBurstDispatchBatchOwnerV2 owner = null;
            using (var input = new InputBuffers(typeCount, dispatchesPerSample))
            {
                var createInput = input.Value;
                if (!NativeBurstDispatchBatchOwnerV2.TryCreate(
                        in createInput,
                        Allocator.Persistent,
                        out owner,
                        out var createFailure))
                {
                    throw new InvalidOperationException(
                        "Native dispatch owner creation failed: " + createFailure);
                }

                try
                {
                    if (!owner.TryAcquireImmediateBatch(out var batch))
                    {
                        throw new InvalidOperationException("Could not acquire immediate dispatch batch.");
                    }

                    long beforeAllocated = 0;
                    long started = 0;
                    if (measure)
                    {
                        beforeAllocated = ReadManagedAllocationCounter();
                        started = Stopwatch.GetTimestamp();
                    }

                    var processed = execute(ref batch);

                    long stopped = 0;
                    long afterAllocated = 0;
                    if (measure)
                    {
                        stopped = Stopwatch.GetTimestamp();
                        afterAllocated = ReadManagedAllocationCounter();
                    }

                    if (processed != dispatchesPerSample)
                    {
                        throw new InvalidOperationException(
                            "Burst dispatch entry returned " + processed
                            + "; expected " + dispatchesPerSample + ".");
                    }

                    VerifyTerminalResult(in batch, dispatchesPerSample);
                    VerifyMemory(owner, typeCount, dispatchesPerSample);

                    if (!measure) return null;

                    var elapsedTicks = stopped - started;
                    return new DispatchSample
                    {
                        sequence = sequence,
                        generatedSwitchWidth = typeCount,
                        compiledEntryPoint = compiledEntryPoint,
                        elapsedTicks = elapsedTicks,
                        elapsedNanoseconds = elapsedTicks * (1_000_000_000.0 / Stopwatch.Frequency),
                        nanosecondsPerDispatch = elapsedTicks
                            * (1_000_000_000.0 / Stopwatch.Frequency)
                            / dispatchesPerSample,
                        dispatchesPerSecond = dispatchesPerSample
                            / (elapsedTicks / (double)Stopwatch.Frequency),
                        managedAllocatedBytes = afterAllocated - beforeAllocated
                    };
                }
                finally
                {
                    if (owner != null && !owner.TryDispose(out var disposeFailure))
                    {
                        throw new InvalidOperationException(
                            "Native dispatch owner disposal failed: " + disposeFailure);
                    }
                }
            }
        }

        private static void VerifyTerminalResult(
            in BurstExecutionBatch batch,
            int dispatchesPerSample)
        {
            var gate = BurstGeneratedRuntimeBridge.TryGetExecutionResult(
                in batch, out var result);
            if (gate != BurstContextResult.Success
                || !result.Success
                || result.DiagnosticNumber != 0
                || result.InstancesVisited != (uint)dispatchesPerSample
                || result.SegmentSteps != (ulong)dispatchesPerSample)
            {
                throw new InvalidOperationException(
                    "Terminal result mismatch: gate=" + gate
                    + ", code=" + result.Code
                    + ", diagnostic=" + result.DiagnosticNumber
                    + ", instances=" + result.InstancesVisited
                    + ", steps=" + result.SegmentSteps + ".");
            }
        }

        private static void VerifyMemory(
            NativeBurstDispatchBatchOwnerV2 owner,
            int typeCount,
            int dispatchesPerSample)
        {
            if (!owner.TryReadMemoryInt32(0u, 0u, out var first)
                || first != InitialMemoryValue(0) + ConfigurationValue(0, typeCount))
            {
                throw new InvalidOperationException("First request memory did not publish exactly once.");
            }

            var lastIndex = dispatchesPerSample - 1;
            if (!owner.TryReadMemoryInt32((uint)lastIndex, 0u, out var last)
                || last != InitialMemoryValue(lastIndex)
                    + ConfigurationValue(lastIndex, typeCount))
            {
                throw new InvalidOperationException("Last request memory did not publish exactly once.");
            }
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
                return new AllocationProbe
                {
                    api = "GC.GetAllocatedBytesForCurrentThread",
                    positiveCanaryPayloadBytes = 4_096,
                    positiveCanaryObservedBytes = delta,
                    sampleObservation =
                        "Each raw sample records current-thread bytes allocated only during the compiled dispatch invocation."
                };
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Profiler.GetMonoUsedSizeLong();
            before = Profiler.GetMonoUsedSizeLong();
            canary = new byte[1_048_576];
            for (var offset = 0; offset < canary.Length; offset += 4_096)
            {
                canary[offset] = 0x5a;
            }
            after = Profiler.GetMonoUsedSizeLong();
            GC.KeepAlive(canary);
            delta = after - before;
            if (delta <= 0)
            {
                throw new InvalidOperationException(
                    "Neither GC.GetAllocatedBytesForCurrentThread nor Profiler.GetMonoUsedSizeLong "
                    + "detected a positive managed-allocation canary.");
            }

            s_allocationCounter = AllocationCounterKind.MonoUsedSize;
            return new AllocationProbe
            {
                api = "Profiler.GetMonoUsedSizeLong",
                positiveCanaryPayloadBytes = 1_048_576,
                positiveCanaryObservedBytes = delta,
                sampleObservation =
                    "Each raw sample records process-wide Mono used-size change only during the compiled dispatch invocation."
            };
        }

        private static long ReadManagedAllocationCounter()
        {
            switch (s_allocationCounter)
            {
                case AllocationCounterKind.CurrentThreadAllocatedBytes:
                    return GC.GetAllocatedBytesForCurrentThread();
                case AllocationCounterKind.MonoUsedSize:
                    return Profiler.GetMonoUsedSizeLong();
                default:
                    throw new InvalidOperationException("Managed-allocation counter was not initialized.");
            }
        }

        private static void PopulateSummary(
            DispatchCaseReport report,
            int dispatchesPerSample)
        {
            var elapsed = report.samples
                .Select(sample => sample.nanosecondsPerDispatch)
                .OrderBy(value => value)
                .ToArray();
            var allocations = report.samples
                .Select(sample => sample.managedAllocatedBytes)
                .ToArray();
            report.summary = new DispatchSummary
            {
                sampleCount = elapsed.Length,
                dispatchesMeasured = (long)elapsed.Length * dispatchesPerSample,
                minimumNanosecondsPerDispatch = elapsed[0],
                medianNanosecondsPerDispatch = Percentile(elapsed, 0.50),
                p95NanosecondsPerDispatch = Percentile(elapsed, 0.95),
                maximumNanosecondsPerDispatch = elapsed[elapsed.Length - 1],
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

        private static BenchmarkEnvironment CaptureEnvironment(string[] arguments)
        {
            return new BenchmarkEnvironment
            {
                unityVersion = Application.unityVersion,
                burstPackageVersion = PackageVersion(
                    typeof(BurstCompileAttribute).Assembly, "com.unity.burst"),
                collectionsPackageVersion = PackageVersion(
                    typeof(NativeArray<>).Assembly, "com.unity.collections"),
                burstEnabled = BurstCompiler.IsEnabled,
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
                graphicsDevice = SystemInfo.graphicsDeviceName,
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

            package = UnityEditor.PackageManager.PackageInfo
                .GetAllRegisteredPackages()
                .FirstOrDefault(candidate => string.Equals(
                    candidate.name, packageName, StringComparison.Ordinal));
            return package == null ? "unknown" : package.version;
        }

        private static string RequiredArgument(string[] arguments, string name)
        {
            for (var index = 0; index < arguments.Length - 1; index++)
            {
                if (string.Equals(arguments[index], name, StringComparison.Ordinal))
                {
                    return Path.GetFullPath(arguments[index + 1]);
                }
            }

            throw new ArgumentException("Missing required command-line argument " + name + ".");
        }

        private static int IntegerArgument(
            string[] arguments,
            string name,
            int fallback,
            int minimum)
        {
            for (var index = 0; index < arguments.Length - 1; index++)
            {
                if (!string.Equals(arguments[index], name, StringComparison.Ordinal)) continue;
                if (!int.TryParse(
                        arguments[index + 1],
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out var value)
                    || value < minimum)
                {
                    throw new ArgumentException(
                        name + " must be an integer greater than or equal to " + minimum + ".");
                }

                return value;
            }

            return fallback;
        }

        private static int ConfigurationValue(int requestIndex, int typeCount)
            => requestIndex % typeCount + 1;

        private static int InitialMemoryValue(int requestIndex)
            => unchecked(requestIndex * 17 + 3);

        private static BurstCatalogHandshake Handshake()
        {
            return new BurstCatalogHandshake(
                2u,
                new BurstCatalogFingerprint(Hash(0x10u)),
                Hash(0x20u),
                1u,
                1u,
                Hash(0x30u),
                Hash(0x40u),
                Hash(0x50u));
        }

        private static BurstHash256 Hash(uint firstWord)
        {
            return new BurstHash256(
                firstWord,
                firstWord + 1u,
                firstWord + 2u,
                firstWord + 3u,
                firstWord + 4u,
                firstWord + 5u,
                firstWord + 6u,
                firstWord + 7u);
        }

        private static void WriteUInt32(NativeArray<byte> destination, int offset, uint value)
        {
            destination[offset] = (byte)value;
            destination[offset + 1] = (byte)(value >> 8);
            destination[offset + 2] = (byte)(value >> 16);
            destination[offset + 3] = (byte)(value >> 24);
        }

        private sealed class InputBuffers : IDisposable
        {
            private readonly NativeArray<NativeBurstDispatchCaseV2> _cases;
            private readonly NativeArray<NativeBurstDispatchRequestV2> _requests;
            private readonly NativeArray<NativeBurstDispatchFieldV2> _configurationFields;
            private readonly NativeArray<NativeBurstDispatchFieldV2> _memoryFields;
            private readonly NativeArray<byte> _configurationBytes;
            private readonly NativeArray<byte> _memoryBytes;
            private readonly NativeArray<ulong> _randomStates;
            private readonly NativeArray<ulong> _randomIncrements;

            internal InputBuffers(int typeCount, int requestCount)
            {
                _cases = new NativeArray<NativeBurstDispatchCaseV2>(
                    typeCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                _requests = new NativeArray<NativeBurstDispatchRequestV2>(
                    requestCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                _configurationFields = new NativeArray<NativeBurstDispatchFieldV2>(
                    typeCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                _memoryFields = new NativeArray<NativeBurstDispatchFieldV2>(
                    typeCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                _configurationBytes = new NativeArray<byte>(
                    checked(requestCount * 4), Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                _memoryBytes = new NativeArray<byte>(
                    checked(requestCount * 4), Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                _randomStates = new NativeArray<ulong>(
                    0, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                _randomIncrements = new NativeArray<ulong>(
                    0, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

                for (var caseIndex = 0; caseIndex < typeCount; caseIndex++)
                {
                    var numericId = FirstTypeNumericId + (ulong)caseIndex;
                    _cases[caseIndex] = new NativeBurstDispatchCaseV2(
                        numericId,
                        TypeVersion,
                        (uint)caseIndex,
                        (uint)caseIndex,
                        1u,
                        4u,
                        (uint)caseIndex,
                        1u,
                        4u,
                        NativeBurstDispatchPhaseMaskV2.Enter,
                        BurstNodeStatusMask.Success
                        | BurstNodeStatusMask.Failure
                        | BurstNodeStatusMask.Running,
                        false);
                    _configurationFields[caseIndex] = new NativeBurstDispatchFieldV2(
                        0u, 0u, 1u, 4u, NativeBurstDispatchFieldEncodingV2.UInt32);
                    _memoryFields[caseIndex] = new NativeBurstDispatchFieldV2(
                        0u, 0u, 1u, 4u, NativeBurstDispatchFieldEncodingV2.Int32);
                }

                for (var requestIndex = 0; requestIndex < requestCount; requestIndex++)
                {
                    var caseIndex = requestIndex % typeCount;
                    var byteOffset = checked((uint)requestIndex * 4u);
                    _requests[requestIndex] = new NativeBurstDispatchRequestV2(
                        (uint)requestIndex,
                        (uint)requestIndex,
                        FirstTypeNumericId + (ulong)caseIndex,
                        TypeVersion,
                        (uint)caseIndex,
                        BurstCallbackPhase.Enter,
                        byteOffset,
                        byteOffset,
                        0u,
                        requestIndex);
                    WriteUInt32(
                        _configurationBytes,
                        (int)byteOffset,
                        (uint)ConfigurationValue(requestIndex, typeCount));
                    WriteUInt32(
                        _memoryBytes,
                        (int)byteOffset,
                        unchecked((uint)InitialMemoryValue(requestIndex)));
                }

                Value = new NativeBurstDispatchCreateInputV2(
                    Handshake(),
                    _cases.AsReadOnly(),
                    _requests.AsReadOnly(),
                    _configurationFields.AsReadOnly(),
                    _memoryFields.AsReadOnly(),
                    _configurationBytes.AsReadOnly(),
                    _memoryBytes.AsReadOnly(),
                    _randomStates.AsReadOnly(),
                    _randomIncrements.AsReadOnly());
            }

            internal NativeBurstDispatchCreateInputV2 Value { get; }

            public void Dispose()
            {
                if (_randomIncrements.IsCreated) _randomIncrements.Dispose();
                if (_randomStates.IsCreated) _randomStates.Dispose();
                if (_memoryBytes.IsCreated) _memoryBytes.Dispose();
                if (_configurationBytes.IsCreated) _configurationBytes.Dispose();
                if (_memoryFields.IsCreated) _memoryFields.Dispose();
                if (_configurationFields.IsCreated) _configurationFields.Dispose();
                if (_requests.IsCreated) _requests.Dispose();
                if (_cases.IsCreated) _cases.Dispose();
            }
        }

        private enum AllocationCounterKind : byte
        {
            None = 0,
            CurrentThreadAllocatedBytes = 1,
            MonoUsedSize = 2
        }

        [Serializable]
        private sealed class DispatchBenchmarkReport
        {
            public string schema;
            public string capturedAtUtc;
            public string scenarioRevision;
            public string scope;
            public BenchmarkEnvironment environment;
            public BenchmarkConfiguration configuration;
            public AllocationProbe allocationProbe;
            public DispatchCaseReport[] cases;
            public CompilationEvidence evidence;
            public string[] limitations;
        }

        [Serializable]
        private sealed class BenchmarkEnvironment
        {
            public string unityVersion;
            public string burstPackageVersion;
            public string collectionsPackageVersion;
            public bool burstEnabled;
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
            public string graphicsDevice;
            public string managedRuntime;
            public bool stopwatchHighResolution;
            public string[] commandLine;
            public string thermalAndPowerConditions;
        }

        [Serializable]
        private sealed class BenchmarkConfiguration
        {
            public int[] typeCounts;
            public int warmupSamplesPerTypeCount;
            public int measuredSamplesPerTypeCount;
            public int dispatchesPerSample;
            public long stopwatchFrequency;
            public string sampleOrder;
            public string timedRegion;
            public string dispatchSelection;
            public string excludedFromTimedRegion;
        }

        [Serializable]
        private sealed class AllocationProbe
        {
            public string api;
            public int positiveCanaryPayloadBytes;
            public long positiveCanaryObservedBytes;
            public string sampleObservation;
        }

        [Serializable]
        private sealed class CompilationEvidence
        {
            public bool burstEnabled;
            public string[] compiledEntryPoints;
            public bool managedFallbackAllowed;
            public bool burstDiscardSentinelPassed;
            public bool allWarmupsAndSamplesCompleted;
        }

        [Serializable]
        private sealed class DispatchCaseReport
        {
            public int typeCount;
            public int generatedSwitchWidth;
            public string compiledEntryPoint;
            public DispatchSample[] samples;
            public DispatchSummary summary;
        }

        [Serializable]
        private sealed class DispatchSample
        {
            public int sequence;
            public int generatedSwitchWidth;
            public string compiledEntryPoint;
            public long elapsedTicks;
            public double elapsedNanoseconds;
            public double nanosecondsPerDispatch;
            public double dispatchesPerSecond;
            public long managedAllocatedBytes;
        }

        [Serializable]
        private sealed class DispatchSummary
        {
            public int sampleCount;
            public long dispatchesMeasured;
            public double minimumNanosecondsPerDispatch;
            public double medianNanosecondsPerDispatch;
            public double p95NanosecondsPerDispatch;
            public double maximumNanosecondsPerDispatch;
            public long minimumManagedAllocatedBytes;
            public long maximumManagedAllocatedBytes;
            public bool allSamplesObservedZeroManagedAllocation;
        }
    }
}
