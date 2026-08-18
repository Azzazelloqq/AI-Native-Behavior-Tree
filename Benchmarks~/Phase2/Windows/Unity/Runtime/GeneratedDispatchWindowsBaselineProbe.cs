using System;
using System.IO;
using Stopwatch = System.Diagnostics.Stopwatch;
using AIBT.Burst;
using AIBT.Execution.Burst.Dispatch;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;

namespace AIBT.Tests.Runtime.NativeExecution.Dispatch
{
    [BurstCompile(CompileSynchronously = true)]
    internal struct WindowsSchedulingBaselineJob : IJob
    {
        public NativeArray<int> Counter;

        public void Execute()
        {
            Counter[0]++;
        }
    }

    internal static class GeneratedDispatchWindowsBaselineProbe
    {
        private const string ResultArgument = "-aibtP2WindowsBaselineResult";
        private const int Warmups = 8;
        private const int Samples = 15;
        private const int Iterations = 128;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Run()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            var output = FindArgument(ResultArgument);
            try
            {
                Require(!string.IsNullOrWhiteSpace(output), ResultArgument + " is required.");
                var behaviorCases = VerifyBehaviorCases();
                var evidence = new WindowsBaselineRawEvidence
                {
                    schema = "aibt-p2-022-windows-baseline-raw-v1",
                    passed = true,
                    unityVersion = Application.unityVersion,
                    platform = Application.platform.ToString(),
                    stopwatchFrequency = Stopwatch.Frequency,
                    warmupCount = Warmups,
                    generatedDispatchProgramPayloadBytes = GeneratedDispatchProgramPayloadBytes,
                    generatedDispatchInstancePayloadBytes = GeneratedDispatchInstancePayloadBytes,
                    behaviorCasesPassed = true,
                    behaviorCases = behaviorCases,
                    samples = new[]
                    {
                        MeasureScheduling(),
                        MeasureCheapTree(),
                        MeasureCommands(),
                        MeasureMixed(),
                    },
                };
                Write(output, JsonUtility.ToJson(evidence, true));
                Debug.Log("AIBT_P2_022_WINDOWS_BASELINE_OK|" + JsonUtility.ToJson(evidence));
            }
            catch (Exception exception)
            {
                var evidence = new WindowsBaselineRawEvidence
                {
                    schema = "aibt-p2-022-windows-baseline-raw-v1",
                    passed = false,
                    unityVersion = Application.unityVersion,
                    platform = Application.platform.ToString(),
                    error = exception.ToString(),
                };
                TryWrite(output, JsonUtility.ToJson(evidence, true));
                Debug.LogError("AIBT_P2_022_WINDOWS_BASELINE_FAIL|" + JsonUtility.ToJson(evidence));
                Application.Quit(3);
            }
#endif
        }

        private static string[] VerifyBehaviorCases()
        {
            using (var sequence = new LifecycleEntry(1, false)) sequence.Execute();
            using (var selector = new LifecycleEntry(1, true)) selector.Execute();

            using (var counter = new NativeArray<int>(1, Allocator.Persistent))
            {
                new WindowsSchedulingBaselineJob { Counter = counter }.Schedule().Complete();
                Require(counter[0] == 1, "Scheduled Burst job did not complete exactly once.");
            }

            var capacity = new NativeCommandAsyncCapacityV1(1, 0, 1, 1, 0, 1, 1, 1, 1, 0);
            Require(NativeCommandAsyncOwnerV1.TryCreate(
                new TreeInstanceId(41), capacity, Allocator.Persistent, out var owner)
                == BurstContextResult.Success, "Command behavior owner creation failed.");
            try
            {
                Require(owner.TryAcquireExecution(out var lease) == BurstContextResult.Success,
                    "Command behavior lease acquisition failed.");
                var commandType = new CommandType(0x8bc97e18bcafe33fUL, 1);
                Require(lease.View.TryEmitEffect(commandType, CommandPhase.Execute,
                    NativePayloadSliceV1.Empty, out var sequenceNumber) == BurstContextResult.Success,
                    "Command behavior emission failed.");
                Require(sequenceNumber == 1, "Command behavior sequence changed.");
                Require(owner.TryRegisterDependency(in lease, default) == BurstContextResult.Success,
                    "Command behavior dependency registration failed.");
                Require(owner.TryGetCommandStream(in lease, out var stream) == BurstContextResult.Success,
                    "Command behavior stream acquisition failed.");
                Require(stream.ExecuteCount == 1 && stream.CancelCount == 0 && stream.Count == 1,
                    "Command behavior stream count changed.");
                Require(stream.TryGetRecord(0, out var record) == BurstContextResult.Success
                    && record.CommandType == commandType && record.Sequence == 1,
                    "Command behavior record changed.");
                Require(owner.TryRelease(in lease) == BurstContextResult.Success,
                    "Command behavior release failed.");
            }
            finally
            {
                Require(owner.TryDispose() == BurstContextResult.Success,
                    "Command behavior owner disposal failed.");
            }

            return new[]
            {
                "empty-sequence-immediate-success",
                "empty-selector-immediate-failure",
                "command-effect-publication",
                "burst-job-schedule-complete",
            };
        }

        private static WindowsScenarioRaw MeasureScheduling()
        {
            using (var counter = new NativeArray<int>(1, Allocator.Persistent))
            {
                for (var index = 0; index < Warmups; index++)
                    new WindowsSchedulingBaselineJob { Counter = counter }.Schedule().Complete();
                var raw = NewRaw("scheduling-overhead", Iterations, 0, 0, 0,
                    UnsafeUtility.SizeOf<int>());
                raw.rawSchedulingTicks = new long[Samples];
                raw.rawCompletionTicks = new long[Samples];
                GC.Collect();
                var beforeHeap = GC.GetTotalMemory(false);
                var beforeCollections = GC.CollectionCount(0);
                for (var sample = 0; sample < Samples; sample++)
                {
                    long scheduling = 0;
                    long completion = 0;
                    for (var index = 0; index < Iterations; index++)
                    {
                        var started = Stopwatch.GetTimestamp();
                        var handle = new WindowsSchedulingBaselineJob { Counter = counter }.Schedule();
                        var scheduled = Stopwatch.GetTimestamp();
                        handle.Complete();
                        var completed = Stopwatch.GetTimestamp();
                        scheduling += scheduled - started;
                        completion += completed - scheduled;
                    }
                    raw.rawSchedulingTicks[sample] = scheduling;
                    raw.rawCompletionTicks[sample] = completion;
                    raw.rawElapsedTicks[sample] = scheduling + completion;
                }
                raw.measuredHeapDeltaBytes = GC.GetTotalMemory(false) - beforeHeap;
                raw.gen0CollectionDelta = GC.CollectionCount(0) - beforeCollections;
                return raw;
            }
        }

        private static WindowsScenarioRaw MeasureCheapTree()
        {
            for (var index = 0; index < Warmups; index++)
                using (var warm = new LifecyclePopulation(1)) warm.ExecuteAll();

            var raw = NewRaw("cheap-tree", Iterations, 4L * Iterations, 0,
                LifecyclePopulation.ProgramBytes, LifecyclePopulation.InstanceBytes);
            GC.Collect();
            var beforeHeap = GC.GetTotalMemory(false);
            var beforeCollections = GC.CollectionCount(0);
            for (var sample = 0; sample < Samples; sample++)
            {
                using (var population = new LifecyclePopulation(Iterations))
                {
                    var started = Stopwatch.GetTimestamp();
                    population.ExecuteAll();
                    raw.rawElapsedTicks[sample] = Stopwatch.GetTimestamp() - started;
                }
            }
            raw.measuredHeapDeltaBytes = GC.GetTotalMemory(false) - beforeHeap;
            raw.gen0CollectionDelta = GC.CollectionCount(0) - beforeCollections;
            return raw;
        }

        private static WindowsScenarioRaw MeasureCommands()
        {
            for (var index = 0; index < Warmups; index++)
                using (var warm = new CommandScenario(1)) warm.EmitAll();
            var raw = NewRaw("command-heavy", Iterations, 0, Iterations,
                0, CommandInstanceBytes(Iterations));
            GC.Collect();
            var beforeHeap = GC.GetTotalMemory(false);
            var beforeCollections = GC.CollectionCount(0);
            for (var sample = 0; sample < Samples; sample++)
            {
                using (var commands = new CommandScenario(Iterations))
                {
                    var started = Stopwatch.GetTimestamp();
                    commands.EmitAll();
                    raw.rawElapsedTicks[sample] = Stopwatch.GetTimestamp() - started;
                }
            }
            raw.measuredHeapDeltaBytes = GC.GetTotalMemory(false) - beforeHeap;
            raw.gen0CollectionDelta = GC.CollectionCount(0) - beforeCollections;
            return raw;
        }

        private static WindowsScenarioRaw MeasureMixed()
        {
            const int population = Iterations / 2;
            for (var index = 0; index < Warmups; index++)
            {
                using (var warm = new LifecyclePopulation(1)) warm.ExecuteAll();
                using (var commands = new CommandScenario(1)) commands.EmitAll();
            }

            var raw = NewRaw("mixed-population", Iterations, 4L * population, population,
                LifecyclePopulation.ProgramBytes,
                LifecyclePopulation.InstanceBytes
                    + (CommandInstanceBytes(population) + population - 1) / population);
            GC.Collect();
            var beforeHeap = GC.GetTotalMemory(false);
            var beforeCollections = GC.CollectionCount(0);
            for (var sample = 0; sample < Samples; sample++)
            {
                using (var machines = new LifecyclePopulation(population))
                using (var commands = new CommandScenario(population))
                {
                    var started = Stopwatch.GetTimestamp();
                    machines.ExecuteAll();
                    commands.EmitAll();
                    raw.rawElapsedTicks[sample] = Stopwatch.GetTimestamp() - started;
                }
            }
            raw.measuredHeapDeltaBytes = GC.GetTotalMemory(false) - beforeHeap;
            raw.gen0CollectionDelta = GC.CollectionCount(0) - beforeCollections;
            return raw;
        }

        private static WindowsScenarioRaw Measure(
            string name, int iterations, long steps, long commands,
            long nativeProgramBytes, long nativeInstanceBytes, Action body)
        {
            var raw = NewRaw(name, iterations, steps, commands, nativeProgramBytes, nativeInstanceBytes);
            GC.Collect();
            var beforeHeap = GC.GetTotalMemory(false);
            var beforeCollections = GC.CollectionCount(0);
            for (var sample = 0; sample < Samples; sample++)
            {
                var started = Stopwatch.GetTimestamp();
                body();
                raw.rawElapsedTicks[sample] = Stopwatch.GetTimestamp() - started;
            }
            raw.measuredHeapDeltaBytes = GC.GetTotalMemory(false) - beforeHeap;
            raw.gen0CollectionDelta = GC.CollectionCount(0) - beforeCollections;
            return raw;
        }

        private static WindowsScenarioRaw NewRaw(
            string name, int iterations, long steps, long commands,
            long nativeProgramBytes, long nativeInstanceBytes)
        {
            return new WindowsScenarioRaw
            {
                name = name,
                iterationsPerSample = iterations,
                stepsPerSample = steps,
                commandsPerSample = commands,
                nativeProgramBytes = nativeProgramBytes,
                nativeBytesPerInstance = nativeInstanceBytes,
                rawElapsedTicks = new long[Samples],
                allocationMetricLimitation = "GC.GetTotalMemory is a coarse Player signal; the P2-021 allocation gate is authoritative.",
            };
        }

        private static long CommandInstanceBytes(int count) =>
            UnsafeUtility.SizeOf<NativeCommandAsyncControlV1>()
            + 2L * UnsafeUtility.SizeOf<NativeOperationRecordV1>()
            + 2L * UnsafeUtility.SizeOf<NativePendingCompletionRecordV1>()
            + 2L * UnsafeUtility.SizeOf<NativeCompletionHighWaterV1>()
            + 2L * UnsafeUtility.SizeOf<NativeCompletionDiagnosticV1>()
            + UnsafeUtility.SizeOf<NativeCompletionInputRecordV1>()
            + count * (long)UnsafeUtility.SizeOf<NativeCommandRecordV1>()
            + UnsafeUtility.SizeOf<NativeCommandRecordV1>();

        private static long GeneratedDispatchProgramPayloadBytes => 2L * (
            UnsafeUtility.SizeOf<NativeBurstDispatchCaseV2>()
            + 9L * UnsafeUtility.SizeOf<NativeBurstDispatchFieldV2>()
            + UnsafeUtility.SizeOf<NativeBurstDispatchBindingV2>()
            + 4L * UnsafeUtility.SizeOf<NativeBurstDispatchCanonicalRangeV2>()
            + UnsafeUtility.SizeOf<NativeBurstDispatchCanonicalRuleV2>());

        private static long GeneratedDispatchInstancePayloadBytes =>
            8L + 48L + 4L
            + UnsafeUtility.SizeOf<NativeBurstDispatchResolvedBindingV2>()
            + UnsafeUtility.SizeOf<NativeBurstDispatchTransactionControlV2>()
            + UnsafeUtility.SizeOf<NativeBurstDispatchControlV2>()
            + sizeof(int)
            + sizeof(long)
            + UnsafeUtility.SizeOf<NativeBurstDispatchRequestV2>()
            + 2L * 48L
            + 1L
            + UnsafeUtility.SizeOf<NativeBurstDispatchValueSessionV2>()
            + 2L * 4L;

        private static string FindArgument(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (var index = 0; index < args.Length - 1; index++)
                if (string.Equals(args[index], name, StringComparison.Ordinal))
                    return Path.GetFullPath(args[index + 1]);
            return null;
        }

        private static void Write(string path, string json)
        {
            var directory = Path.GetDirectoryName(path);
            Require(!string.IsNullOrWhiteSpace(directory), "Baseline result path has no parent.");
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, json);
        }

        private static void TryWrite(string path, string json)
        {
            try { if (!string.IsNullOrWhiteSpace(path)) Write(path, json); } catch { }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        [Serializable]
        private sealed class WindowsBaselineRawEvidence
        {
            public string schema;
            public bool passed;
            public string unityVersion;
            public string platform;
            public long stopwatchFrequency;
            public int warmupCount;
            public long generatedDispatchProgramPayloadBytes;
            public long generatedDispatchInstancePayloadBytes;
            public bool behaviorCasesPassed;
            public string[] behaviorCases;
            public WindowsScenarioRaw[] samples;
            public string error;
        }

        [Serializable]
        private sealed class WindowsScenarioRaw
        {
            public string name;
            public int iterationsPerSample;
            public long stepsPerSample;
            public long commandsPerSample;
            public long nativeProgramBytes;
            public long nativeBytesPerInstance;
            public long[] rawElapsedTicks;
            public long[] rawSchedulingTicks;
            public long[] rawCompletionTicks;
            public long measuredHeapDeltaBytes;
            public int gen0CollectionDelta;
            public string allocationMetricLimitation;
        }

        private sealed class LifecyclePopulation : IDisposable
        {
            internal static readonly long ProgramBytes =
                UnsafeUtility.SizeOf<NativeCompiledNodeRecordV1>()
                + UnsafeUtility.SizeOf<NativeLifecycleNodeBindingV1>();
            internal static readonly long InstanceBytes = 4
                + UnsafeUtility.SizeOf<NativeFrameStateV1>()
                + sizeof(uint)
                + UnsafeUtility.SizeOf<NativeLifecycleControlV1>();

            private readonly LifecycleEntry[] _entries;

            internal LifecyclePopulation(int count)
            {
                _entries = new LifecycleEntry[count];
                for (var index = 0; index < count; index++) _entries[index] = new LifecycleEntry((ulong)index + 1, false);
            }

            internal void ExecuteAll()
            {
                for (var index = 0; index < _entries.Length; index++) _entries[index].Execute();
            }

            public void Dispose()
            {
                for (var index = _entries.Length - 1; index >= 0; index--) _entries[index]?.Dispose();
            }
        }

        private sealed class CommandScenario : IDisposable
        {
            private readonly NativeCommandAsyncOwnerV1 _owner;
            private readonly NativeCommandAsyncLeaseV1 _lease;
            private readonly int _count;

            internal CommandScenario(int count)
            {
                _count = count;
                var capacity = new NativeCommandAsyncCapacityV1(
                    1, 0, 1, 1, 0, 1, 1, (uint)count, 1, 0);
                Require(NativeCommandAsyncOwnerV1.TryCreate(
                    new TreeInstanceId(31), capacity, Allocator.Persistent, out _owner)
                    == BurstContextResult.Success, "Command owner creation failed.");
                Require(_owner.TryAcquireExecution(out _lease) == BurstContextResult.Success,
                    "Command lease acquisition failed.");
            }

            internal void EmitAll()
            {
                for (var index = 0; index < _count; index++)
                    Require(_lease.View.TryEmitEffect(
                        new CommandType(0x8bc97e18bcafe33fUL, 1), CommandPhase.Execute,
                        NativePayloadSliceV1.Empty, out _) == BurstContextResult.Success,
                        "Command emission failed.");
            }

            public void Dispose()
            {
                Require(_owner.TryRegisterDependency(in _lease, default) == BurstContextResult.Success,
                    "Command dependency registration failed.");
                Require(_owner.TryRelease(in _lease) == BurstContextResult.Success,
                    "Command lease release failed.");
                Require(_owner.TryDispose() == BurstContextResult.Success, "Command owner disposal failed.");
            }
        }

        private sealed class LifecycleEntry : IDisposable
        {
            private readonly NativeArray<NativeCompiledNodeRecordV1> _nodes;
            private readonly NativeArray<uint> _children;
            private readonly NativeArray<NativeLifecycleNodeBindingV1> _bindings;
            private readonly NativeArray<byte> _memory;
            private readonly NativeArray<NativeFrameStateV1> _frames;
            private readonly NativeArray<uint> _generations;
            private readonly NativeArray<NativeLifecycleControlV1> _control;
            private NativeLifecycleMachineV1 _machine;

            private readonly bool _selector;

            internal LifecycleEntry(ulong updateId, bool selector)
            {
                _selector = selector;
                _nodes = new NativeArray<NativeCompiledNodeRecordV1>(1, Allocator.Persistent);
                _children = new NativeArray<uint>(0, Allocator.Persistent);
                _bindings = new NativeArray<NativeLifecycleNodeBindingV1>(1, Allocator.Persistent);
                _memory = new NativeArray<byte>(4, Allocator.Persistent);
                _frames = new NativeArray<NativeFrameStateV1>(1, Allocator.Persistent);
                _generations = new NativeArray<uint>(1, Allocator.Persistent);
                _control = new NativeArray<NativeLifecycleControlV1>(1, Allocator.Persistent);
                _nodes[0] = new NativeCompiledNodeRecordV1(new CompiledNodeRecord(
                    StableHash.Fnv1A64(selector ? "aibt.core.memory-selector" : "aibt.core.memory-sequence"), 1, 0, 0, 1,
                    0, 4, 4, NodeMemoryLifetime.Activation,
                    new CompiledRange(0, 0), CompiledNodeFlags.BurstDomain, 0,
                    new CompiledRange(0, 0), new CompiledRange(0, 0)));
                _bindings[0] = new NativeLifecycleNodeBindingV1(0,
                    selector ? NativeLifecycleNodeKindV1.MemorySelector : NativeLifecycleNodeKindV1.MemorySequence);
                Require(NativeLifecycleMachineV1.TryCreate(
                    _nodes, _children, _bindings, _memory, _frames, _generations, _control,
                    out _machine, out var failure), "Lifecycle create failed: " + failure.Code);
                Require(_machine.TryBeginUpdate(updateId, out failure), "Lifecycle begin failed: " + failure.Code);
            }

            internal void Execute()
            {
                var steps = 0;
                var rootStatus = default(NodeStatus);
                var hasRootStatus = false;
                while (true)
                {
                    Require(_machine.TryAdvance(out var result, out _),
                        "Lifecycle advance failed.");
                    steps++;
                    if (result.Kind == NativeLifecycleStepKindV1.Completed)
                    {
                        rootStatus = result.RootStatus;
                        hasRootStatus = result.HasRootStatus;
                        break;
                    }
                }
                Require(steps == 4, "Cheap lifecycle trace changed.");
                Require(hasRootStatus && rootStatus == (_selector ? NodeStatus.Failure : NodeStatus.Success),
                    "Empty composite terminal status changed.");
            }

            public void Dispose()
            {
                if (_control.IsCreated) _control.Dispose();
                if (_generations.IsCreated) _generations.Dispose();
                if (_frames.IsCreated) _frames.Dispose();
                if (_memory.IsCreated) _memory.Dispose();
                if (_bindings.IsCreated) _bindings.Dispose();
                if (_children.IsCreated) _children.Dispose();
                if (_nodes.IsCreated) _nodes.Dispose();
            }
        }
    }
}
