using System;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine.Profiling;
using UnityEngine.TestTools.Constraints;
using GcAllocIs = UnityEngine.TestTools.Constraints.Is;
using Is = NUnit.Framework.Is;

namespace AIBT.Tests.Runtime.NativeExecution.Allocation
{
    public sealed class NativeExecutionAllocationTests
    {
        [Test]
        public void InitializedImmediateBudgetedAndBatchedWindowsAllocateNoManagedMemory()
        {
            WarmBatchedScheduler();

            using var immediate = new Scenario();
            var immediateSuccess = false;
            Assert.That(() => { immediateSuccess = immediate.Machine.TryAdvance(out _, out _); }, GcAllocIs.Not.AllocatingGCMemory());
            Assert.That(immediateSuccess, Is.True);

            using var budgeted = new Scenario();
            var budget = default(NativeBudgetStateV1);
            Assert.That(NativeLifecycleBudgetDriverV1.TryBeginSegment(1, ref budget), Is.True);
            var budgetSuccess = false;
            Assert.That(() =>
            {
                budgetSuccess = NativeLifecycleBudgetDriverV1.TryAdvance(
                    ref budgeted.Machine, ref budget, out _, out _, out _);
            }, GcAllocIs.Not.AllocatingGCMemory());
            Assert.That(budgetSuccess, Is.True);

            using var batched = new Scenario();
            Assert.That(NativeBatchedLifecycleOwnerV1.TryCreate(
                new[] { batched.Machine }, Allocator.Persistent, out var owner, out var failure), Is.True, failure.Code.ToString());
            using var results = new NativeArray<NativeLifecycleStepResultV1>(1, Allocator.Persistent);
            using var failures = new NativeArray<NativeRuntimeFailureV1>(1, Allocator.Persistent);
            try
            {
                var batchSuccess = false;
                Assert.That(() =>
                {
                    batchSuccess = owner.TrySchedule(1, default, out var dependency, out _);
                    dependency.Complete();
                    batchSuccess &= owner.TryComplete(results, failures, out _);
                }, GcAllocIs.Not.AllocatingGCMemory());
                Assert.That(batchSuccess, Is.True);
            }
            finally
            {
                Assert.That(owner.TryDispose(out failure), Is.True, failure.Code.ToString());
            }

            Assert.That(() =>
            {
                var canary = new string('x', 128);
                GC.KeepAlive(canary);
            }, GcAllocIs.AllocatingGCMemory(), "allocation instrumentation canary");
        }

        [Test]
        public void RawInitializedWindowSamplesExposeZeroBytesAndControlledCanary()
        {
            WarmBatchedScheduler();
            var recorder = Recorder.Get("GC.Alloc");
            recorder.enabled = false;
            using (var immediate = new Scenario())
            {
                for (var index = 0; index < 4; index++)
                {
                    recorder.FilterToCurrentThread();
                    recorder.enabled = true;
                    var success = immediate.Machine.TryAdvance(out _, out _);
                    recorder.enabled = false;
                    recorder.CollectFromAllThreads();
                    var events = recorder.sampleBlockCount;
                    Assert.That(success, Is.True);
                    Assert.That(events, Is.Zero, "Immediate sample " + index);
                    Sample("Immediate", index, events);
                }
            }

            using (var budgeted = new Scenario())
            {
                var budget = default(NativeBudgetStateV1);
                for (var index = 0; index < 4; index++)
                {
                    Assert.That(NativeLifecycleBudgetDriverV1.TryBeginSegment(1, ref budget), Is.True);
                    recorder.FilterToCurrentThread();
                    recorder.enabled = true;
                    var success = NativeLifecycleBudgetDriverV1.TryAdvance(
                        ref budgeted.Machine, ref budget, out _, out _, out _);
                    recorder.enabled = false;
                    recorder.CollectFromAllThreads();
                    var events = recorder.sampleBlockCount;
                    Assert.That(success, Is.True);
                    Assert.That(events, Is.Zero, "Budgeted sample " + index);
                    Sample("Budgeted", index, events);
                }
            }

            using (var batched = new Scenario())
            using (var results = new NativeArray<NativeLifecycleStepResultV1>(1, Allocator.Persistent))
            using (var failures = new NativeArray<NativeRuntimeFailureV1>(1, Allocator.Persistent))
            {
                Assert.That(NativeBatchedLifecycleOwnerV1.TryCreate(
                    new[] { batched.Machine }, Allocator.Persistent, out var owner, out var failure), Is.True);
                try
                {
                    for (var index = 0; index < 4; index++)
                    {
                        recorder.FilterToCurrentThread();
                        recorder.enabled = true;
                        var success = owner.TrySchedule(1, default, out var dependency, out _);
                        dependency.Complete();
                        success &= owner.TryComplete(results, failures, out _);
                        recorder.enabled = false;
                        recorder.CollectFromAllThreads();
                        var events = recorder.sampleBlockCount;
                        Assert.That(success, Is.True);
                        Assert.That(events, Is.Zero, "Batched sample " + index);
                        Sample("BatchedJobsSameFrame", index, events);
                    }
                }
                finally
                {
                    Assert.That(owner.TryDispose(out failure), Is.True, failure.Code.ToString());
                }
            }

            recorder.FilterToCurrentThread();
            recorder.enabled = true;
            var canary = new byte[4096];
            GC.KeepAlive(canary);
            recorder.enabled = false;
            recorder.CollectFromAllThreads();
            var canaryEvents = recorder.sampleBlockCount;
            Assert.That(canaryEvents, Is.GreaterThan(0));
            Sample("ControlledCanary", 0, canaryEvents);
        }

        [Test]
        public void SuccessAbortFaultRestartAndCapacityFailureDisposeWithoutNativeLeaks()
        {
            using (var success = new Scenario()) Complete(ref success.Machine);

            using (var aborted = new Scenario())
            {
                Assert.That(aborted.Machine.TryAdvance(out _, out var failure), Is.True, failure.Code.ToString());
                Assert.That(aborted.Machine.TryRequestAbort(AIBT.Burst.BurstNodeAbortReason.Explicit, out failure), Is.True, failure.Code.ToString());
                Complete(ref aborted.Machine);
            }

            using (var fault = new LeafScenario())
            {
                Assert.That(fault.Machine.TryAdvance(out var enter, out var failure), Is.True, failure.Code.ToString());
                Assert.That(enter.Kind, Is.EqualTo(NativeLifecycleStepKindV1.DispatchRequired));
                Assert.That(fault.Machine.TryCompleteDispatch(
                    enter.DispatchToken, AIBT.Burst.BurstContextResult.TypeMismatch,
                    NodeStatus.Running, out failure), Is.False);
                Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeLifetimeStateInvalid));
            }

            using (var first = new Scenario()) Complete(ref first.Machine);
            using (var restarted = new Scenario()) Complete(ref restarted.Machine);

            using var nodes = new NativeArray<NativeCompiledNodeRecordV1>(new[]
            {
                new NativeCompiledNodeRecordV1(new CompiledNodeRecord(
                    StableHash.Fnv1A64("aibt.core.memory-sequence"), 1, 0, 0, 1,
                    0, 4, 4, NodeMemoryLifetime.Activation,
                    new CompiledRange(0, 0), CompiledNodeFlags.BurstDomain, 0,
                    new CompiledRange(0, 0), new CompiledRange(0, 0)))
            }, Allocator.Persistent);
            using var children = new NativeArray<uint>(0, Allocator.Persistent);
            using var bindings = new NativeArray<NativeLifecycleNodeBindingV1>(new[]
            {
                new NativeLifecycleNodeBindingV1(0, NativeLifecycleNodeKindV1.MemorySequence)
            }, Allocator.Persistent);
            using var memory = new NativeArray<byte>(4, Allocator.Persistent);
            using var frames = new NativeArray<NativeFrameStateV1>(0, Allocator.Persistent);
            using var generations = new NativeArray<uint>(1, Allocator.Persistent);
            using var control = new NativeArray<NativeLifecycleControlV1>(1, Allocator.Persistent);
            Assert.That(NativeLifecycleMachineV1.TryCreate(
                nodes, children, bindings, memory, frames, generations, control,
                out _, out var capacityFailure), Is.False);
            Assert.That(capacityFailure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeCapacityPlanInvalid));
        }

        private static void Complete(ref NativeLifecycleMachineV1 machine)
        {
            for (var guard = 0; guard < 16; guard++)
            {
                Assert.That(machine.TryAdvance(out var step, out var failure), Is.True, failure.Code.ToString());
                if (step.Kind == NativeLifecycleStepKindV1.Completed) return;
            }
            Assert.Fail("Lifecycle did not complete within its fixed semantic bound.");
        }

        private static void Sample(string policy, int index, long events)
            => TestContext.Out.WriteLine(
                "AIBT_P2_021_SAMPLE|policy=" + policy + "|index=" + index + "|gcEvents=" + events);

        private static void WarmBatchedScheduler()
        {
            using var scenario = new Scenario();
            Assert.That(NativeBatchedLifecycleOwnerV1.TryCreate(
                new[] { scenario.Machine }, Allocator.Persistent, out var owner, out var failure), Is.True, failure.Code.ToString());
            using var results = new NativeArray<NativeLifecycleStepResultV1>(1, Allocator.Persistent);
            using var failures = new NativeArray<NativeRuntimeFailureV1>(1, Allocator.Persistent);
            try
            {
                Assert.That(owner.TrySchedule(1, default, out var dependency, out failure), Is.True, failure.Code.ToString());
                dependency.Complete();
                Assert.That(owner.TryComplete(results, failures, out failure), Is.True, failure.Code.ToString());
            }
            finally
            {
                Assert.That(owner.TryDispose(out failure), Is.True, failure.Code.ToString());
            }
        }

        private sealed class Scenario : IDisposable
        {
            private readonly NativeArray<NativeCompiledNodeRecordV1> _nodes;
            private readonly NativeArray<uint> _children;
            private readonly NativeArray<NativeLifecycleNodeBindingV1> _bindings;
            private readonly NativeArray<byte> _memory;
            private readonly NativeArray<byte> _configuration;
            private readonly NativeArray<NativeFrameStateV1> _frames;
            private readonly NativeArray<uint> _generations;
            private readonly NativeArray<NativeLifecycleControlV1> _control;
            internal NativeLifecycleMachineV1 Machine;

            internal Scenario()
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
                _configuration = new NativeArray<byte>(0, Allocator.Persistent);
                _frames = new NativeArray<NativeFrameStateV1>(1, Allocator.Persistent);
                _generations = new NativeArray<uint>(1, Allocator.Persistent);
                _control = new NativeArray<NativeLifecycleControlV1>(1, Allocator.Persistent);
                Assert.That(NativeLifecycleMachineV1.TryCreate(
                    _nodes, _children, _bindings, _memory, _frames, _generations, _control, _configuration,
                    out var machine, out var failure), Is.True, failure.Code.ToString());
                Assert.That(machine.TryBeginUpdate(1, out failure), Is.True, failure.Code.ToString());
                Machine = machine;
            }

            public void Dispose()
            {
                _control.Dispose(); _generations.Dispose(); _frames.Dispose(); _configuration.Dispose();
                _memory.Dispose(); _bindings.Dispose(); _children.Dispose(); _nodes.Dispose();
            }
        }

        private sealed class LeafScenario : IDisposable
        {
            private readonly NativeArray<NativeCompiledNodeRecordV1> _nodes;
            private readonly NativeArray<uint> _children;
            private readonly NativeArray<NativeLifecycleNodeBindingV1> _bindings;
            private readonly NativeArray<byte> _memory;
            private readonly NativeArray<NativeFrameStateV1> _frames;
            private readonly NativeArray<uint> _generations;
            private readonly NativeArray<NativeLifecycleControlV1> _control;
            internal NativeLifecycleMachineV1 Machine;

            internal LeafScenario()
            {
                _nodes = new NativeArray<NativeCompiledNodeRecordV1>(new[]
                {
                    new NativeCompiledNodeRecordV1(new CompiledNodeRecord(
                        StableHash.Fnv1A64("aibt.tests.allocation-leaf"), 1, 0, 0, 1,
                        0, 0, 1, NodeMemoryLifetime.Activation,
                        new CompiledRange(0, 0), CompiledNodeFlags.BurstDomain, 0,
                        new CompiledRange(0, 0), new CompiledRange(0, 0)))
                }, Allocator.Persistent);
                _children = new NativeArray<uint>(0, Allocator.Persistent);
                _bindings = new NativeArray<NativeLifecycleNodeBindingV1>(new[]
                {
                    new NativeLifecycleNodeBindingV1(0, NativeLifecycleNodeKindV1.GeneratedLeaf)
                }, Allocator.Persistent);
                _memory = new NativeArray<byte>(0, Allocator.Persistent);
                _frames = new NativeArray<NativeFrameStateV1>(1, Allocator.Persistent);
                _generations = new NativeArray<uint>(1, Allocator.Persistent);
                _control = new NativeArray<NativeLifecycleControlV1>(1, Allocator.Persistent);
                Assert.That(NativeLifecycleMachineV1.TryCreate(
                    _nodes, _children, _bindings, _memory, _frames, _generations, _control,
                    out var machine, out var failure), Is.True, failure.Code.ToString());
                Assert.That(machine.TryBeginUpdate(1, out failure), Is.True, failure.Code.ToString());
                Machine = machine;
            }

            public void Dispose()
            {
                _control.Dispose(); _generations.Dispose(); _frames.Dispose(); _memory.Dispose();
                _bindings.Dispose(); _children.Dispose(); _nodes.Dispose();
            }
        }
    }
}
