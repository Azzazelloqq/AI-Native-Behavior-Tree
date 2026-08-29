using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AIBT.Burst;
using AIBT.Authoring.BehaviorCases;
using NUnit.Framework;
using Unity.Collections;

namespace AIBT.Tests.Integration.NativeRuntime
{
    public sealed class NativeExecutionEquivalenceTests
    {
        [TestCase("patrol-react.aibtcase.json")]
        [TestCase("parallel-decorator.aibtcase.json")]
        [TestCase("async-completion.aibtcase.json")]
        [TestCase("async-budgeted-abort.aibtcase.json")]
        [TestCase("initial-blackboard.aibtcase.json")]
        public void EveryGoldenBehaviorCasePassesTheNativeAdapter(string fileName)
        {
            var root = SemanticSlice.SemanticSliceTestPackagePaths.Resolve("Tests", "Fixtures", "Golden");
            foreach (NativeGoldenExecutionPolicyV1 policy in Enum.GetValues(typeof(NativeGoldenExecutionPolicyV1)))
            {
                var result = BehaviorCaseRunner.Run(
                    File.ReadAllBytes(Path.Combine(root, "Cases", fileName)),
                    fileName,
                    new NativeBehaviorCaseExecutorFactory(root, policy),
                    new BehaviorCaseRegisteredValueRegistry(new[]
                    {
                        new BehaviorCaseRegisteredValueContract(
                            StableHash.Fnv1A64(SemanticSlice.SemanticSliceNodeContracts.AsyncStartCommandType), 1, 0),
                        new BehaviorCaseRegisteredValueContract(
                            StableHash.Fnv1A64(SemanticSlice.SemanticSliceNodeContracts.AsyncCancelCommandType), 1, 0),
                    }));

                if (!result.Success && result.InputDiagnostics.Count == 0)
                {
                    var parsed = BehaviorCaseJson.Parse(File.ReadAllBytes(Path.Combine(root, "Cases", fileName)), fileName);
                    using var executor = new NativeBehaviorCaseExecutorFactory(root, policy)
                        .Create(new BehaviorCaseExecutorConfiguration(parsed.Document));
                    foreach (var step in parsed.Document.Steps)
                    {
                        var observed = executor.Execute(step);
                        TestContext.Out.WriteLine(policy + " " + string.Join(" | ", observed.Trace.Select(item =>
                            item.Event + ":" + (item.NodeIndex.HasValue ? item.NodeIndex.Value.Value.ToString() : "-")
                            + ":" + (item.Status.HasValue ? item.Status.Value.ToString() : "-")
                            + ":" + (item.AbortReason.HasValue ? item.AbortReason.Value.ToString() : "-")
                            + ":" + (item.ExitReason.HasValue ? item.ExitReason.Value.ToString() : "-")
                            + ":" + (item.SourceNodeIndex.HasValue ? item.SourceNodeIndex.Value.Value.ToString() : "-"))));
                    }
                }
                Assert.That(result.Success, Is.True, policy + ": "
                    + string.Join(" | ", result.Failures.Select(item => item.Kind + " " + item.Pointer + ": " + item.Message)));
                Assert.That(result.InputDiagnostics, Is.Empty, policy.ToString());
            }
        }

        [Test]
        public void ImmediateBudgetedAndBatchedProduceTheSameCompleteSemanticTrace()
        {
            using var immediate = new Scenario();
            using var budgeted = new Scenario();
            using var batched = new Scenario();
            using var pipelined = new Scenario();
            var expected = RunImmediate(immediate);
            var budgetTrace = RunBudgeted(budgeted);
            var batchTrace = RunBatched(batched);
            var pipelinedTrace = RunPipelined(pipelined);

            Assert.That(budgetTrace, Is.EqualTo(expected));
            Assert.That(batchTrace, Is.EqualTo(expected));
            Assert.That(pipelinedTrace, Is.EqualTo(expected));
            Assert.That(immediate.Control[0].RootStatus, Is.EqualTo(NodeStatus.Failure));
            Assert.That(budgeted.Control[0].RootStatus, Is.EqualTo(NodeStatus.Failure));
            Assert.That(batched.Control[0].RootStatus, Is.EqualTo(NodeStatus.Failure));
            Assert.That(pipelined.Control[0].RootStatus, Is.EqualTo(NodeStatus.Failure));
            Assert.That(immediate.Generations.ToArray(), Is.EqualTo(new uint[] { 1, 1, 1 }));
            Assert.That(budgeted.Generations.ToArray(), Is.EqualTo(immediate.Generations.ToArray()));
            Assert.That(batched.Generations.ToArray(), Is.EqualTo(immediate.Generations.ToArray()));
            Assert.That(pipelined.Generations.ToArray(), Is.EqualTo(immediate.Generations.ToArray()));
        }

        [Test]
        public void RetainedParallel_IsInvariantAcrossImmediateBudgetedAndBatchedPartitions()
        {
            using var immediate = new Scenario(parallel: true);
            using var budgeted = new Scenario(parallel: true);
            using var batched = new Scenario(parallel: true);
            using var pipelined = new Scenario(parallel: true);
            var expected = RunImmediate(immediate);
            Assert.That(RunBudgeted(budgeted), Is.EqualTo(expected));
            Assert.That(RunBatched(batched), Is.EqualTo(expected));
            Assert.That(RunPipelined(pipelined), Is.EqualTo(expected));
            Assert.That(immediate.Control[0].RootStatus, Is.EqualTo(NodeStatus.Success));
            Assert.That(budgeted.Generations.ToArray(), Is.EqualTo(immediate.Generations.ToArray()));
            Assert.That(batched.Generations.ToArray(), Is.EqualTo(immediate.Generations.ToArray()));
            Assert.That(pipelined.Generations.ToArray(), Is.EqualTo(immediate.Generations.ToArray()));
        }

        private static List<TraceEntry> RunImmediate(Scenario scenario)
        {
            var trace = new List<TraceEntry>();
            while (true)
            {
                Assert.That(scenario.Machine.TryAdvance(out var step, out var failure), Is.True, failure.Code.ToString());
                trace.Add(new TraceEntry(step));
                if (step.Kind == NativeLifecycleStepKindV1.DispatchRequired) CompleteDispatch(scenario, step);
                if (step.Kind == NativeLifecycleStepKindV1.Waiting) scenario.BeginNextUpdate();
                if (step.Kind == NativeLifecycleStepKindV1.Completed) return trace;
            }
        }

        private static List<TraceEntry> RunBudgeted(Scenario scenario)
        {
            var trace = new List<TraceEntry>();
            var budget = default(NativeBudgetStateV1);
            while (true)
            {
                Assert.That(NativeLifecycleBudgetDriverV1.TryBeginSegment(1, ref budget), Is.True);
                Assert.That(NativeLifecycleBudgetDriverV1.TryAdvance(
                    ref scenario.Machine, ref budget, out _, out var step, out var failure), Is.True, failure.Code.ToString());
                trace.Add(new TraceEntry(step));
                if (step.Kind == NativeLifecycleStepKindV1.DispatchRequired) CompleteDispatch(scenario, step);
                if (step.Kind == NativeLifecycleStepKindV1.Waiting) scenario.BeginNextUpdate();
                if (step.Kind == NativeLifecycleStepKindV1.Completed) return trace;
            }
        }

        private static List<TraceEntry> RunBatched(Scenario scenario)
        {
            var trace = new List<TraceEntry>();
            Assert.That(NativeBatchedLifecycleOwnerV1.TryCreate(
                new[] { scenario.Machine }, Allocator.Persistent, out var owner, out var failure), Is.True, failure.Code.ToString());
            using var results = new NativeArray<NativeLifecycleStepResultV1>(1, Allocator.Persistent);
            using var failures = new NativeArray<NativeRuntimeFailureV1>(1, Allocator.Persistent);
            try
            {
                while (true)
                {
                    Assert.That(owner.TrySchedule(1, default, out var dependency, out failure), Is.True, failure.Code.ToString());
                    dependency.Complete();
                    Assert.That(owner.TryComplete(results, failures, out failure), Is.True, failure.Code.ToString());
                    var step = results[0];
                    trace.Add(new TraceEntry(step));
                    if (step.Kind == NativeLifecycleStepKindV1.DispatchRequired) CompleteDispatch(scenario, step);
                    if (step.Kind == NativeLifecycleStepKindV1.Waiting) scenario.BeginNextUpdate();
                    if (step.Kind == NativeLifecycleStepKindV1.Completed) return trace;
                }
            }
            finally
            {
                Assert.That(owner.TryDispose(out failure), Is.True, failure.Code.ToString());
            }
        }

        /// <summary>
        /// Drives the scenario through <see cref="NativePipelinedPhaseControllerV1"/> with a real,
        /// enforced stage boundary between every round's schedule and completion -- not the same
        /// call, not a bypass -- proving the resulting atomic-step trace is byte-identical to
        /// Immediate's even with genuine cross-call latency inserted on every single step. The
        /// per-step stage advance here is a test-mechanism choice (proving the controller's
        /// scheduling mechanism preserves correctness at the smallest possible granularity, the
        /// hardest case to get right); it is not a claim that production code must advance a
        /// stage every atomic step -- a real caller advances one stage per real frame, which may
        /// span many atomic steps.
        /// </summary>
        private static List<TraceEntry> RunPipelined(Scenario scenario)
        {
            var trace = new List<TraceEntry>();
            Assert.That(NativePipelinedPhaseControllerV1.TryCreate(
                new[] { scenario.Machine }, Allocator.Persistent, out var controller, out var failure), Is.True, failure.Code.ToString());
            using var results = new NativeArray<NativeLifecycleStepResultV1>(1, Allocator.Persistent);
            using var failures = new NativeArray<NativeRuntimeFailureV1>(1, Allocator.Persistent);
            try
            {
                ulong updateId = 1;
                ulong revision = 1;
                Assert.That(controller.TryBeginSnapshot(updateId, out failure), Is.True, failure.Code.ToString());
                Assert.That(controller.TryCompleteSnapshot(revision, out failure), Is.True, failure.Code.ToString());
                while (true)
                {
                    Assert.That(controller.TryScheduleExecuteRound(1, default, out var dependency, out failure), Is.True, failure.Code.ToString());
                    Assert.That(controller.TryCompleteExecuteRound(results, failures, out failure), Is.False,
                        "A round must never complete within the same stage it was scheduled in.");
                    Assert.That(controller.TryAdvanceStage(out failure), Is.True, failure.Code.ToString());
                    dependency.Complete();
                    Assert.That(controller.TryCompleteExecuteRound(results, failures, out failure), Is.True, failure.Code.ToString());
                    var step = results[0];
                    trace.Add(new TraceEntry(step));
                    if (step.Kind == NativeLifecycleStepKindV1.DispatchRequired) CompleteDispatch(scenario, step);
                    if (step.Kind == NativeLifecycleStepKindV1.Waiting)
                    {
                        Assert.That(controller.TrySealExecute(out failure), Is.True, failure.Code.ToString());
                        Assert.That(controller.TryCompleteReduce(out failure), Is.True, failure.Code.ToString());
                        Assert.That(controller.TryCompletePublish(out _, out failure), Is.True, failure.Code.ToString());
                        scenario.BeginNextUpdate();
                        updateId++;
                        revision++;
                        Assert.That(controller.TryBeginSnapshot(updateId, out failure), Is.True, failure.Code.ToString());
                        Assert.That(controller.TryCompleteSnapshot(revision, out failure), Is.True, failure.Code.ToString());
                    }
                    if (step.Kind == NativeLifecycleStepKindV1.Completed)
                    {
                        Assert.That(controller.TrySealExecute(out failure), Is.True, failure.Code.ToString());
                        Assert.That(controller.TryCompleteReduce(out failure), Is.True, failure.Code.ToString());
                        Assert.That(controller.TryCompletePublish(out var metrics, out failure), Is.True, failure.Code.ToString());
                        Assert.That(metrics.StagesElapsed, Is.GreaterThan(0UL));
                        return trace;
                    }
                }
            }
            finally
            {
                Assert.That(controller.TryDispose(out failure), Is.True, failure.Code.ToString());
            }
        }

        private static void CompleteDispatch(Scenario scenario, NativeLifecycleStepResultV1 step)
        {
            var status = scenario.StatusFor(step);
            Assert.That(scenario.Machine.TryCompleteDispatch(
                step.DispatchToken, BurstContextResult.Success, status, out var failure), Is.True, failure.Code.ToString());
        }

        private readonly struct TraceEntry : IEquatable<TraceEntry>
        {
            internal TraceEntry(NativeLifecycleStepResultV1 step)
            { Kind = step.Kind; NodeIndex = step.NodeIndex; Phase = step.Phase; HasRoot = step.HasRootStatus; Root = step.RootStatus; }
            private NativeLifecycleStepKindV1 Kind { get; }
            private uint NodeIndex { get; }
            private BurstCallbackPhase Phase { get; }
            private bool HasRoot { get; }
            private NodeStatus Root { get; }
            public bool Equals(TraceEntry other)
                => Kind == other.Kind && NodeIndex == other.NodeIndex && Phase == other.Phase && HasRoot == other.HasRoot && Root == other.Root;
            public override bool Equals(object obj) => obj is TraceEntry other && Equals(other);
            public override int GetHashCode() => ((int)Kind * 397) ^ (int)NodeIndex ^ (int)Phase;
            public override string ToString() => $"{Kind}:{NodeIndex}:{Phase}:{(HasRoot ? Root.ToString() : "-")}";
        }

        private sealed class Scenario : IDisposable
        {
            private readonly NativeArray<NativeCompiledNodeRecordV1> _nodes;
            private readonly NativeArray<uint> _children;
            private readonly NativeArray<NativeLifecycleNodeBindingV1> _bindings;
            private readonly NativeArray<byte> _memory;
            private readonly NativeArray<byte> _configuration;
            private readonly NativeArray<NativeFrameStateV1> _frames;
            private readonly NativeArray<NativeParallelBranchStateV1> _parallelBranches;
            private readonly int[] _tickCounts;
            private readonly bool _parallel;
            private ulong _updateId;
            internal readonly NativeArray<uint> Generations;
            internal readonly NativeArray<NativeLifecycleControlV1> Control;
            internal NativeLifecycleMachineV1 Machine;

            internal Scenario(bool parallel = false)
            {
                _parallel = parallel;
                _nodes = new NativeArray<NativeCompiledNodeRecordV1>(new[]
                {
                    parallel
                        ? Node("aibt.core.parallel", 0, 8, 0, 2, 0, 16)
                        : Node("aibt.core.memory-sequence", 0, 4, 0, 2),
                    Node(parallel ? "aibt.tests.running" : "aibt.tests.success", 0, 0, 0, 0),
                    Node(parallel ? "aibt.tests.success" : "aibt.tests.failure", 0, 0, 0, 0),
                }, Allocator.Persistent);
                _children = new NativeArray<uint>(new uint[] { 1, 2 }, Allocator.Persistent);
                _bindings = new NativeArray<NativeLifecycleNodeBindingV1>(new[]
                {
                    new NativeLifecycleNodeBindingV1(0, parallel
                        ? NativeLifecycleNodeKindV1.Parallel
                        : NativeLifecycleNodeKindV1.MemorySequence),
                    new NativeLifecycleNodeBindingV1(1, NativeLifecycleNodeKindV1.GeneratedLeaf),
                    new NativeLifecycleNodeBindingV1(2, NativeLifecycleNodeKindV1.GeneratedLeaf),
                }, Allocator.Persistent);
                _memory = new NativeArray<byte>(parallel ? 8 : 4, Allocator.Persistent);
                _configuration = new NativeArray<byte>(parallel ? 16 : 0, Allocator.Persistent);
                _frames = new NativeArray<NativeFrameStateV1>(3, Allocator.Persistent);
                _parallelBranches = parallel
                    ? new NativeArray<NativeParallelBranchStateV1>(2, Allocator.Persistent)
                    : default;
                _tickCounts = new int[3];
                Generations = new NativeArray<uint>(3, Allocator.Persistent);
                Control = new NativeArray<NativeLifecycleControlV1>(1, Allocator.Persistent);
                Assert.That(NativeLifecycleMachineV1.TryCreate(
                    _nodes, _children, _bindings, _memory, _frames, Generations, Control, _configuration,
                    default, _parallelBranches, out var machine, out var failure), Is.True, failure.Code.ToString());
                _updateId = 1;
                Assert.That(machine.TryBeginUpdate(_updateId, out failure), Is.True, failure.Code.ToString());
                Machine = machine;
            }

            internal void BeginNextUpdate()
            {
                _updateId++;
                Assert.That(Machine.TryBeginUpdate(_updateId, out var failure), Is.True, failure.Code.ToString());
            }

            internal NodeStatus StatusFor(NativeLifecycleStepResultV1 step)
            {
                if (step.Phase != AIBT.Burst.BurstCallbackPhase.Tick)
                    return NodeStatus.Running;
                if (!_parallel)
                    return step.NodeIndex == 1 ? NodeStatus.Success : NodeStatus.Failure;

                var tick = _tickCounts[checked((int)step.NodeIndex)]++;
                return step.NodeIndex == 1 && tick == 0 ? NodeStatus.Running : NodeStatus.Success;
            }

            public void Dispose()
            {
                Control.Dispose(); Generations.Dispose(); _frames.Dispose(); _configuration.Dispose();
                if (_parallelBranches.IsCreated) _parallelBranches.Dispose();
                _memory.Dispose(); _bindings.Dispose(); _children.Dispose(); _nodes.Dispose();
            }

            private static NativeCompiledNodeRecordV1 Node(
                string typeId,
                uint memoryOffset,
                uint memorySize,
                uint childOffset,
                uint childCount,
                uint configurationOffset = 0,
                uint configurationSize = 0)
                => new NativeCompiledNodeRecordV1(new CompiledNodeRecord(
                    StableHash.Fnv1A64(typeId), 1, configurationOffset, configurationSize,
                    configurationSize == 0 ? 1u : 4u,
                    memorySize == 0 ? 0u : memoryOffset, memorySize, memorySize == 0 ? 1u : 4u,
                    NodeMemoryLifetime.Activation, new CompiledRange(childOffset, childCount),
                    CompiledNodeFlags.BurstDomain, 0, new CompiledRange(0, 0), new CompiledRange(0, 0)));
        }
    }
}
