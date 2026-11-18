using System;
using System.Collections.Generic;
using AIBT.Burst;
using NUnit.Framework;
using Unity.Collections;

namespace AIBT.Tests.Runtime
{
    internal enum RetainingNativeDrive : byte
    {
        Immediate = 0,
        OneStepBudget = 1,
        Batched = 2,
    }

    /// <summary>
    /// ADR-P10-013 stage 2: runs the compiled program of a <see cref="RetainingTree"/> on
    /// <see cref="NativeLifecycleMachineV1"/> (frame array sized exactly NodeCount, as the arena
    /// does) with the same scripted leaf results, and renders the lifecycle records in the same
    /// notation as <see cref="RetainingTree.Render"/>, so a test can require reference and native
    /// traces to be equal record for record.
    /// </summary>
    internal sealed class RetainingNativeTwin : IDisposable
    {
        private readonly Dictionary<uint, string> _names;
        private readonly ScriptedReferenceLeaf[] _leaves;
        private readonly HashSet<uint> _abortedComposites = new HashSet<uint>();
        private readonly RetainingNativeDrive _drive;
        private NativeArray<NativeCompiledNodeRecordV1> _nodes;
        private NativeArray<uint> _children;
        private NativeArray<NativeLifecycleNodeBindingV1> _bindings;
        private NativeArray<byte> _configuration;
        private NativeArray<uint> _generations;
        private NativeArray<byte> _cooldown;
        private NativeArray<NativeParallelBranchStateV1> _parallel;
        private NativeBatchedLifecycleOwnerV1 _batched;
        private NativeArray<NativeLifecycleStepResultV1> _batchedResults;
        private NativeArray<NativeRuntimeFailureV1> _batchedFailures;
        private NativeBudgetStateV1 _budget;
        internal NativeArray<byte> Memory;
        internal NativeArray<NativeFrameStateV1> Frames;
        internal NativeArray<NativeLifecycleControlV1> Control;
        internal NativeLifecycleMachineV1 Machine;

        /// <summary>The node named as the source of an abort requested from outside (the reference tests pass the root).</summary>
        internal uint? ExternalAbortSource { get; set; } = 0;

        internal RetainingNativeTwin(RetainingTree tree, RetainingNativeDrive drive = RetainingNativeDrive.Immediate)
        {
            _drive = drive;
            _names = tree.Names;
            var program = tree.Fixture.Program;
            var count = program.Nodes.Count;
            _leaves = new ScriptedReferenceLeaf[count];
            _nodes = new NativeArray<NativeCompiledNodeRecordV1>(count, Allocator.Persistent);
            _children = new NativeArray<uint>(Math.Max(1, program.ChildIndices.Count), Allocator.Persistent);
            _bindings = new NativeArray<NativeLifecycleNodeBindingV1>(count, Allocator.Persistent);
            Memory = new NativeArray<byte>((int)program.Header.InstanceNodeMemorySize, Allocator.Persistent);
            _configuration = new NativeArray<byte>(program.ConfigBlob.Count, Allocator.Persistent);
            Frames = new NativeArray<NativeFrameStateV1>(count, Allocator.Persistent);
            _generations = new NativeArray<uint>(count, Allocator.Persistent);
            Control = new NativeArray<NativeLifecycleControlV1>(1, Allocator.Persistent);
            uint parallelCount = 0;
            var hasCooldown = false;
            for (var index = 0; index < count; index++)
            {
                _nodes[index] = new NativeCompiledNodeRecordV1(program.Nodes[index]);
                var kind = NativeHotReloadInstance.ClassifyKind(program.Nodes[index].NodeTypeId);
                _bindings[index] = new NativeLifecycleNodeBindingV1((uint)index, kind);
                if (kind == NativeLifecycleNodeKindV1.Parallel) parallelCount += program.Nodes[index].Children.Count;
                if (kind == NativeLifecycleNodeKindV1.Cooldown) hasCooldown = true;
                _leaves[index] = tree.Fixture.Nodes[index].Leaf as ScriptedReferenceLeaf;
            }
            for (var index = 0; index < program.ChildIndices.Count; index++) _children[index] = program.ChildIndices[index];
            for (var index = 0; index < program.ConfigBlob.Count; index++) _configuration[index] = program.ConfigBlob[index];
            _cooldown = new NativeArray<byte>(hasCooldown ? count : 0, Allocator.Persistent);
            _parallel = new NativeArray<NativeParallelBranchStateV1>((int)parallelCount, Allocator.Persistent);
            Assert.That(NativeLifecycleMachineV1.TryCreate(
                _nodes, _children, _bindings, Memory, Frames, _generations, Control,
                _configuration, _cooldown, _parallel, out Machine, out var failure), Is.True, failure.Code.ToString());
            if (drive == RetainingNativeDrive.Batched)
            {
                _batchedResults = new NativeArray<NativeLifecycleStepResultV1>(1, Allocator.Persistent);
                _batchedFailures = new NativeArray<NativeRuntimeFailureV1>(1, Allocator.Persistent);
                Assert.That(NativeBatchedLifecycleOwnerV1.TryCreate(
                    new[] { Machine }, Allocator.Persistent, out _batched, out failure), Is.True, failure.Code.ToString());
            }
        }

        internal readonly struct Outcome
        {
            internal Outcome(NativeLifecycleStepKindV1 end, NodeStatus? root, ulong steps)
            {
                End = end;
                Root = root;
                Steps = steps;
            }

            internal NativeLifecycleStepKindV1 End { get; }
            internal NodeStatus? Root { get; }
            internal ulong Steps { get; }
        }

        internal string[] Update(ulong updateId, long timeMicroseconds = -1) => Update(updateId, out _, timeMicroseconds);

        internal string[] Update(ulong updateId, out Outcome outcome, long timeMicroseconds = -1)
        {
            var lines = new List<string>();
            var before = Control[0].SemanticSteps;
            Begin(updateId, timeMicroseconds);
            var end = RunToRest(lines);
            outcome = new Outcome(end, Control[0].HasRootStatus != 0 ? Control[0].RootStatus : (NodeStatus?)null,
                Control[0].SemanticSteps - before);
            return lines.ToArray();
        }

        internal void Begin(ulong updateId, long timeMicroseconds = -1)
        {
            var time = timeMicroseconds < 0 ? checked((long)updateId * 1000) : timeMicroseconds;
            Assert.That(Machine.TryBeginUpdate(updateId, time, out var failure), Is.True, failure.Code.ToString());
        }

        /// <summary>Advances one machine step and appends its records; returns the step kind.</summary>
        internal NativeLifecycleStepKindV1 Step(List<string> lines)
        {
            NativeLifecycleStepResultV1 step;
            NativeRuntimeFailureV1 failure;
            switch (_drive)
            {
                case RetainingNativeDrive.OneStepBudget:
                    while (true)
                    {
                        NativeLifecycleBudgetDriverV1.TryBeginSegment(1, ref _budget);
                        Assert.That(NativeLifecycleBudgetDriverV1.TryAdvance(
                            ref Machine, ref _budget, out var kind, out step, out failure), Is.True, failure.Code.ToString());
                        if (kind != NativeBudgetAdvanceKindV1.Suspended) break;
                    }
                    break;
                case RetainingNativeDrive.Batched:
                    Assert.That(_batched.TrySchedule(1, default, out var dependency, out failure), Is.True, failure.Code.ToString());
                    dependency.Complete();
                    Assert.That(_batched.TryComplete(_batchedResults, _batchedFailures, out failure), Is.True, failure.Code.ToString());
                    step = _batchedResults[0];
                    break;
                default:
                    Assert.That(Machine.TryAdvance(out step, out failure), Is.True, failure.Code.ToString());
                    break;
            }
            Record(step, lines);
            return step.Kind;
        }

        internal NativeLifecycleStepKindV1 RunToRest(List<string> lines)
        {
            for (var guard = 0; guard < 100000; guard++)
            {
                var kind = Step(lines);
                if (kind == NativeLifecycleStepKindV1.Waiting || kind == NativeLifecycleStepKindV1.Completed) return kind;
            }
            Assert.Fail("The native update never came to rest.");
            return default;
        }

        private void Record(NativeLifecycleStepResultV1 step, List<string> lines)
        {
            var control = Control[0];
            switch (step.Kind)
            {
                case NativeLifecycleStepKindV1.CompositeEntered:
                    lines.Add("E " + Name(step.NodeIndex));
                    break;
                case NativeLifecycleStepKindV1.CompositeAborted:
                    _abortedComposites.Add(step.NodeIndex);
                    lines.Add("A " + Name(step.NodeIndex) + " " + (NodeAbortReason)(byte)control.AbortReason + "@" + Source(control));
                    break;
                case NativeLifecycleStepKindV1.CompositeExited:
                    if (_abortedComposites.Remove(step.NodeIndex))
                    {
                        lines.Add("X(Ab) " + Name(step.NodeIndex));
                        break;
                    }
                    var status = control.Depth == 0 ? control.RootStatus : Frames[(int)control.Depth - 1].PendingStatus;
                    lines.Add("T " + Name(step.NodeIndex) + " " + Letter(status));
                    lines.Add("X " + Name(step.NodeIndex));
                    break;
                case NativeLifecycleStepKindV1.DispatchRequired:
                    Complete(step, lines, control);
                    break;
            }
        }

        private void Complete(NativeLifecycleStepResultV1 step, List<string> lines, NativeLifecycleControlV1 control)
        {
            var status = NodeStatus.Running;
            var name = Name(step.NodeIndex);
            switch (step.Phase)
            {
                case BurstCallbackPhase.Enter:
                    lines.Add("E " + name);
                    break;
                case BurstCallbackPhase.Tick:
                    var leaf = _leaves[step.NodeIndex];
                    Assert.That(leaf, Is.Not.Null, "Only scripted leaves are dispatched.");
                    status = leaf.NextScriptedStatus();
                    lines.Add("T " + name + " " + Letter(status));
                    break;
                case BurstCallbackPhase.Abort:
                    lines.Add("A " + name + " " + (NodeAbortReason)(byte)step.AbortReason + "@" + Source(control));
                    break;
                case BurstCallbackPhase.Exit:
                    lines.Add((step.ExitReason == BurstNodeExitReason.Aborted ? "X(Ab) " : "X ") + name);
                    break;
            }
            Assert.That(Machine.TryCompleteDispatch(step.DispatchToken, BurstContextResult.Success, status, out var failure),
                Is.True, failure.Code.ToString());
        }

        private string Source(NativeLifecycleControlV1 control)
        {
            if (control.HasAbortSource != 0) return Name(control.AbortSourceNode);
            return ExternalAbortSource.HasValue ? Name(ExternalAbortSource.Value) : "-";
        }

        private string Name(uint index) => _names.TryGetValue(index, out var name) ? name : "#" + index;

        private static string Letter(NodeStatus status)
            => status == NodeStatus.Success ? "S" : status == NodeStatus.Failure ? "F" : "R";

        /// <summary>Reads the composite's 8-byte activation memory (offset 0 cursor, offset 4 retained ordinal + 1).</summary>
        internal uint ReadMemory(uint nodeIndex, int offset)
        {
            var start = (int)_nodes[(int)nodeIndex].InstanceMemoryOffset + offset;
            return (uint)(Memory[start] | Memory[start + 1] << 8 | Memory[start + 2] << 16 | Memory[start + 3] << 24);
        }

        public void Dispose()
        {
            if (_batched != null)
            {
                Assert.That(_batched.TryDispose(out var failure), Is.True, failure.Code.ToString());
                _batched = null;
            }
            Dispose(ref _batchedResults);
            Dispose(ref _batchedFailures);
            Dispose(ref Control); Dispose(ref _parallel); Dispose(ref _cooldown); Dispose(ref _generations);
            Dispose(ref Frames); Dispose(ref _configuration); Dispose(ref Memory); Dispose(ref _bindings);
            Dispose(ref _children); Dispose(ref _nodes);
        }

        private static void Dispose<T>(ref NativeArray<T> value) where T : struct
        {
            if (value.IsCreated) value.Dispose();
            value = default;
        }
    }
}
