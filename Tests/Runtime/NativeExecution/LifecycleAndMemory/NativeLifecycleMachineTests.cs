using System;
using AIBT.Burst;
using NUnit.Framework;
using Unity.Collections;

namespace AIBT.Tests.Runtime.NativeExecution.LifecycleAndMemory
{
    public sealed class NativeLifecycleMachineTests
    {
        [Test]
        public void MemorySequence_UsesOneAtomicStepPerTransitionAndResumesRunningLeafOncePerUpdate()
        {
            using var nodes = new NativeArray<NativeCompiledNodeRecordV1>(new[]
            {
                Node(StableHash.Fnv1A64("aibt.core.memory-sequence"), 0, 4, 0, 2),
                Node(StableHash.Fnv1A64("aibt.tests.first"), 4, 0, 0, 0),
                Node(StableHash.Fnv1A64("aibt.tests.second"), 4, 0, 0, 0),
            }, Allocator.Persistent);
            using var children = new NativeArray<uint>(new uint[] { 1, 2 }, Allocator.Persistent);
            using var bindings = new NativeArray<NativeLifecycleNodeBindingV1>(new[]
            {
                new NativeLifecycleNodeBindingV1(0, NativeLifecycleNodeKindV1.MemorySequence),
                new NativeLifecycleNodeBindingV1(1, NativeLifecycleNodeKindV1.GeneratedLeaf),
                new NativeLifecycleNodeBindingV1(2, NativeLifecycleNodeKindV1.GeneratedLeaf),
            }, Allocator.Persistent);
            using var memory = new NativeArray<byte>(4, Allocator.Persistent);
            using var frames = new NativeArray<NativeFrameStateV1>(3, Allocator.Persistent);
            using var generations = new NativeArray<uint>(3, Allocator.Persistent);
            using var control = new NativeArray<NativeLifecycleControlV1>(1, Allocator.Persistent);

            Assert.That(NativeLifecycleMachineV1.TryCreate(
                nodes, children, bindings, memory, frames, generations, control,
                out var machine, out var failure), Is.True, failure.Code.ToString());
            Assert.That(machine.TryBeginUpdate(1, out failure), Is.True, failure.Code.ToString());

            AssertStep(ref machine, NativeLifecycleStepKindV1.CompositeEntered, 0);
            AssertStep(ref machine, NativeLifecycleStepKindV1.ChildSelected, 0);
            var enterFirst = AssertStep(ref machine, NativeLifecycleStepKindV1.DispatchRequired, 1, BurstCallbackPhase.Enter);
            Assert.That(machine.TryCompleteDispatch(enterFirst.DispatchToken, BurstContextResult.Success, NodeStatus.Running, out failure), Is.True, failure.Code.ToString());
            var tickFirst = AssertStep(ref machine, NativeLifecycleStepKindV1.DispatchRequired, 1, BurstCallbackPhase.Tick);
            Assert.That(machine.TryCompleteDispatch(tickFirst.DispatchToken, BurstContextResult.Success, NodeStatus.Success, out failure), Is.True, failure.Code.ToString());
            var exitFirst = AssertStep(ref machine, NativeLifecycleStepKindV1.DispatchRequired, 1, BurstCallbackPhase.Exit);
            Assert.That(machine.TryCompleteDispatch(exitFirst.DispatchToken, BurstContextResult.Success, NodeStatus.Success, out failure), Is.True, failure.Code.ToString());
            AssertStep(ref machine, NativeLifecycleStepKindV1.ChildAccepted, 0);
            AssertStep(ref machine, NativeLifecycleStepKindV1.ChildSelected, 0);
            var enterSecond = AssertStep(ref machine, NativeLifecycleStepKindV1.DispatchRequired, 2, BurstCallbackPhase.Enter);
            Assert.That(machine.TryCompleteDispatch(enterSecond.DispatchToken, BurstContextResult.Success, NodeStatus.Running, out failure), Is.True, failure.Code.ToString());
            var tickSecond = AssertStep(ref machine, NativeLifecycleStepKindV1.DispatchRequired, 2, BurstCallbackPhase.Tick);
            Assert.That(machine.TryCompleteDispatch(tickSecond.DispatchToken, BurstContextResult.Success, NodeStatus.Running, out failure), Is.True, failure.Code.ToString());
            AssertStep(ref machine, NativeLifecycleStepKindV1.Waiting, 2);
            Assert.That(machine.TryAdvance(out _, out failure), Is.False);
            Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeLifetimeStateInvalid));

            Assert.That(machine.TryBeginUpdate(2, out failure), Is.True, failure.Code.ToString());
            var resumedTick = AssertStep(ref machine, NativeLifecycleStepKindV1.DispatchRequired, 2, BurstCallbackPhase.Tick);
            Assert.That(machine.TryCompleteDispatch(resumedTick.DispatchToken, BurstContextResult.Success, NodeStatus.Success, out failure), Is.True, failure.Code.ToString());
            var exitSecond = AssertStep(ref machine, NativeLifecycleStepKindV1.DispatchRequired, 2, BurstCallbackPhase.Exit);
            Assert.That(machine.TryCompleteDispatch(exitSecond.DispatchToken, BurstContextResult.Success, NodeStatus.Success, out failure), Is.True, failure.Code.ToString());
            AssertStep(ref machine, NativeLifecycleStepKindV1.ChildAccepted, 0);
            AssertStep(ref machine, NativeLifecycleStepKindV1.CompositeExited, 0);
            var completed = AssertStep(ref machine, NativeLifecycleStepKindV1.Completed, 0);

            Assert.That(completed.HasRootStatus, Is.True);
            Assert.That(completed.RootStatus, Is.EqualTo(NodeStatus.Success));
            Assert.That(generations.ToArray(), Is.EqualTo(new uint[] { 1, 1, 1 }));
            Assert.That(memory.ToArray(), Is.EqualTo(new byte[4]), "Activation memory must clear after Exit.");
            Assert.That(control[0].SemanticSteps, Is.EqualTo(13ul));
        }

        [Test]
        public void Abort_UnwindsDeepestFirstThroughAbortAndExit_ThenClearsActivationState()
        {
            using var nodes = new NativeArray<NativeCompiledNodeRecordV1>(new[]
            {
                Node(StableHash.Fnv1A64("aibt.core.memory-sequence"), 0, 4, 0, 1),
                Node(StableHash.Fnv1A64("aibt.tests.running"), 4, 0, 0, 0),
            }, Allocator.Persistent);
            using var children = new NativeArray<uint>(new uint[] { 1 }, Allocator.Persistent);
            using var bindings = new NativeArray<NativeLifecycleNodeBindingV1>(new[]
            {
                new NativeLifecycleNodeBindingV1(0, NativeLifecycleNodeKindV1.MemorySequence),
                new NativeLifecycleNodeBindingV1(1, NativeLifecycleNodeKindV1.GeneratedLeaf),
            }, Allocator.Persistent);
            using var memory = new NativeArray<byte>(4, Allocator.Persistent);
            using var frames = new NativeArray<NativeFrameStateV1>(2, Allocator.Persistent);
            using var generations = new NativeArray<uint>(2, Allocator.Persistent);
            using var control = new NativeArray<NativeLifecycleControlV1>(1, Allocator.Persistent);

            Assert.That(NativeLifecycleMachineV1.TryCreate(
                nodes, children, bindings, memory, frames, generations, control,
                out var machine, out var failure), Is.True, failure.Code.ToString());
            Assert.That(machine.TryBeginUpdate(1, out failure), Is.True, failure.Code.ToString());
            AssertStep(ref machine, NativeLifecycleStepKindV1.CompositeEntered, 0);
            AssertStep(ref machine, NativeLifecycleStepKindV1.ChildSelected, 0);
            var enter = AssertStep(ref machine, NativeLifecycleStepKindV1.DispatchRequired, 1, BurstCallbackPhase.Enter);
            Assert.That(machine.TryCompleteDispatch(enter.DispatchToken, BurstContextResult.Success, NodeStatus.Running, out failure), Is.True, failure.Code.ToString());
            var tick = AssertStep(ref machine, NativeLifecycleStepKindV1.DispatchRequired, 1, BurstCallbackPhase.Tick);
            Assert.That(machine.TryCompleteDispatch(tick.DispatchToken, BurstContextResult.Success, NodeStatus.Running, out failure), Is.True, failure.Code.ToString());
            AssertStep(ref machine, NativeLifecycleStepKindV1.Waiting, 1);

            Assert.That(machine.TryBeginUpdate(2, out failure), Is.True, failure.Code.ToString());
            Assert.That(machine.TryRequestAbort(BurstNodeAbortReason.TreeStopped, out failure), Is.True, failure.Code.ToString());
            var abort = AssertStep(ref machine, NativeLifecycleStepKindV1.DispatchRequired, 1, BurstCallbackPhase.Abort);
            Assert.That(machine.TryCompleteDispatch(abort.DispatchToken, BurstContextResult.Success, NodeStatus.Running, out failure), Is.True, failure.Code.ToString());
            var exit = AssertStep(ref machine, NativeLifecycleStepKindV1.DispatchRequired, 1, BurstCallbackPhase.Exit);
            Assert.That(machine.TryCompleteDispatch(exit.DispatchToken, BurstContextResult.Success, NodeStatus.Running, out failure), Is.True, failure.Code.ToString());
            AssertStep(ref machine, NativeLifecycleStepKindV1.CompositeAborted, 0);
            AssertStep(ref machine, NativeLifecycleStepKindV1.CompositeExited, 0);
            var completed = AssertStep(ref machine, NativeLifecycleStepKindV1.Completed, 0);

            Assert.That(completed.HasRootStatus, Is.False);
            Assert.That(control[0].RootAborted, Is.EqualTo(1));
            Assert.That(control[0].AbortReason, Is.EqualTo(BurstNodeAbortReason.TreeStopped));
            Assert.That(frames[0].LifecycleState, Is.EqualTo(NativeFrameLifecycleStateV1.Inactive));
            Assert.That(frames[1].LifecycleState, Is.EqualTo(NativeFrameLifecycleStateV1.Inactive));
            Assert.That(memory.ToArray(), Is.EqualTo(new byte[4]));
            Assert.That(generations.ToArray(), Is.EqualTo(new uint[] { 1, 1 }));
        }

        [TestCase((byte)NativeLifecycleNodeKindV1.MemorySequence, NodeStatus.Success)]
        [TestCase((byte)NativeLifecycleNodeKindV1.MemorySelector, NodeStatus.Failure)]
        public void EmptyMemoryComposite_UsesTheCanonicalTerminalResult(
            byte rawKind,
            NodeStatus expectedStatus)
        {
            var kind = (NativeLifecycleNodeKindV1)rawKind;
            var typeId = kind == NativeLifecycleNodeKindV1.MemorySequence
                ? StableHash.Fnv1A64("aibt.core.memory-sequence")
                : StableHash.Fnv1A64("aibt.core.memory-selector");
            using var nodes = new NativeArray<NativeCompiledNodeRecordV1>(new[] { Node(typeId, 0, 4, 0, 0) }, Allocator.Persistent);
            using var children = new NativeArray<uint>(0, Allocator.Persistent);
            using var bindings = new NativeArray<NativeLifecycleNodeBindingV1>(new[] { new NativeLifecycleNodeBindingV1(0, kind) }, Allocator.Persistent);
            using var memory = new NativeArray<byte>(4, Allocator.Persistent);
            using var frames = new NativeArray<NativeFrameStateV1>(1, Allocator.Persistent);
            using var generations = new NativeArray<uint>(1, Allocator.Persistent);
            using var control = new NativeArray<NativeLifecycleControlV1>(1, Allocator.Persistent);

            Assert.That(NativeLifecycleMachineV1.TryCreate(nodes, children, bindings, memory, frames, generations, control,
                out var machine, out var failure), Is.True, failure.Code.ToString());
            Assert.That(machine.TryBeginUpdate(1, out failure), Is.True, failure.Code.ToString());
            AssertStep(ref machine, NativeLifecycleStepKindV1.CompositeEntered, 0);
            AssertStep(ref machine, NativeLifecycleStepKindV1.ChildAccepted, 0);
            AssertStep(ref machine, NativeLifecycleStepKindV1.CompositeExited, 0);
            var completed = AssertStep(ref machine, NativeLifecycleStepKindV1.Completed, 0);
            Assert.That(completed.HasRootStatus, Is.True);
            Assert.That(completed.RootStatus, Is.EqualTo(expectedStatus));
            Assert.That(control[0].SemanticSteps, Is.EqualTo(3ul));
        }

        [TestCase(8u, 4u, NodeMemoryLifetime.Activation)]
        [TestCase(4u, 2u, NodeMemoryLifetime.Activation)]
        [TestCase(4u, 4u, NodeMemoryLifetime.Instance)]
        [TestCase(0u, 1u, NodeMemoryLifetime.Activation)]
        public void InvalidMemoryCompositeDescriptor_RejectsBeforeLifecycle(
            uint size,
            uint alignment,
            NodeMemoryLifetime lifetime)
        {
            using var nodes = new NativeArray<NativeCompiledNodeRecordV1>(new[]
            {
                new NativeCompiledNodeRecordV1(new CompiledNodeRecord(
                    StableHash.Fnv1A64("aibt.core.memory-sequence"), 1, 0, 0, 1,
                    0, size, alignment, lifetime,
                    new CompiledRange(0, 0), CompiledNodeFlags.BurstDomain, 0,
                    new CompiledRange(0, 0), new CompiledRange(0, 0)))
            }, Allocator.Persistent);
            using var children = new NativeArray<uint>(0, Allocator.Persistent);
            using var bindings = new NativeArray<NativeLifecycleNodeBindingV1>(new[]
            {
                new NativeLifecycleNodeBindingV1(0, NativeLifecycleNodeKindV1.MemorySequence)
            }, Allocator.Persistent);
            using var memory = new NativeArray<byte>(8, Allocator.Persistent);
            using var frames = new NativeArray<NativeFrameStateV1>(1, Allocator.Persistent);
            using var generations = new NativeArray<uint>(1, Allocator.Persistent);
            using var control = new NativeArray<NativeLifecycleControlV1>(1, Allocator.Persistent);

            Assert.That(NativeLifecycleMachineV1.TryCreate(nodes, children, bindings, memory, frames, generations, control,
                out _, out var failure), Is.False);
            Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeCapacityPlanInvalid));
            Assert.That(control[0].OwnerId, Is.Zero);
            Assert.That(generations[0], Is.Zero);
        }

        [Test]
        public void RandomStreamDerivation_MatchesAllPublishedPcgVectors()
        {
            AssertRandomVector(
                0x0000000000000000UL,
                new string('0', 64),
                1UL, 0u,
                0xcd0663b1aab38607UL, 0x364142da8f45ed0bUL,
                0x650f0350u, 0x19bf2775u, 0x93792ebdu, 0xf8d15448u, 0x80f1bd3cu, 0x1312f9f2u);
            AssertRandomVector(
                0x0123456789abcdefUL,
                "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f",
                1UL, 42u,
                0xb63505dd96f263beUL, 0x8d9ea478a3f51455UL,
                0x94286b1au, 0x4ff48da5u, 0xce86bc0du, 0x55e6545au, 0x8ba0f814u, 0x83be6712u);
            AssertRandomVector(
                ulong.MaxValue,
                new string('f', 64),
                18364758544493064720UL, 4294967294u,
                0xaa817c070c95253dUL, 0x32bea5d2ab2b8077UL,
                0x56a75281u, 0x2089b2deu, 0x5e76d072u, 0x81b053c5u, 0x0dde67a2u, 0xc869d193u);
        }

        [Test]
        public void ReactiveSequence_ExitsOldRunningSubtreeBeforeReevaluatingFromZero()
        {
            using var nodes = new NativeArray<NativeCompiledNodeRecordV1>(new[]
            {
                Node(StableHash.Fnv1A64("aibt.core.reactive-sequence"), 0, 4, 0, 2),
                Node(StableHash.Fnv1A64("aibt.tests.guard"), 4, 0, 0, 0),
                Node(StableHash.Fnv1A64("aibt.tests.action"), 4, 0, 0, 0),
            }, Allocator.Persistent);
            using var children = new NativeArray<uint>(new uint[] { 1, 2 }, Allocator.Persistent);
            using var bindings = new NativeArray<NativeLifecycleNodeBindingV1>(new[]
            {
                new NativeLifecycleNodeBindingV1(0, NativeLifecycleNodeKindV1.ReactiveSequence),
                new NativeLifecycleNodeBindingV1(1, NativeLifecycleNodeKindV1.GeneratedLeaf),
                new NativeLifecycleNodeBindingV1(2, NativeLifecycleNodeKindV1.GeneratedLeaf),
            }, Allocator.Persistent);
            using var memory = new NativeArray<byte>(4, Allocator.Persistent);
            using var frames = new NativeArray<NativeFrameStateV1>(3, Allocator.Persistent);
            using var generations = new NativeArray<uint>(3, Allocator.Persistent);
            using var control = new NativeArray<NativeLifecycleControlV1>(1, Allocator.Persistent);
            Assert.That(NativeLifecycleMachineV1.TryCreate(nodes, children, bindings, memory, frames, generations, control,
                out var machine, out var failure), Is.True, failure.Code.ToString());

            Assert.That(machine.TryBeginUpdate(1, out failure), Is.True, failure.Code.ToString());
            AssertStep(ref machine, NativeLifecycleStepKindV1.CompositeEntered, 0);
            AssertStep(ref machine, NativeLifecycleStepKindV1.ChildSelected, 0);
            CompleteLeaf(ref machine, 1, NodeStatus.Success);
            AssertStep(ref machine, NativeLifecycleStepKindV1.ChildAccepted, 0);
            AssertStep(ref machine, NativeLifecycleStepKindV1.ChildSelected, 0);
            var enterAction = AssertStep(ref machine, NativeLifecycleStepKindV1.DispatchRequired, 2, BurstCallbackPhase.Enter);
            Assert.That(machine.TryCompleteDispatch(enterAction.DispatchToken, BurstContextResult.Success, NodeStatus.Running, out failure), Is.True);
            var tickAction = AssertStep(ref machine, NativeLifecycleStepKindV1.DispatchRequired, 2, BurstCallbackPhase.Tick);
            Assert.That(machine.TryCompleteDispatch(tickAction.DispatchToken, BurstContextResult.Success, NodeStatus.Running, out failure), Is.True);
            AssertStep(ref machine, NativeLifecycleStepKindV1.Waiting, 2);

            Assert.That(machine.TryBeginUpdate(2, out failure), Is.True, failure.Code.ToString());
            var abort = AssertStep(ref machine, NativeLifecycleStepKindV1.DispatchRequired, 2, BurstCallbackPhase.Abort);
            Assert.That(machine.TryCompleteDispatch(abort.DispatchToken, BurstContextResult.Success, NodeStatus.Running, out failure), Is.True);
            var oldExit = AssertStep(ref machine, NativeLifecycleStepKindV1.DispatchRequired, 2, BurstCallbackPhase.Exit);
            Assert.That(machine.TryCompleteDispatch(oldExit.DispatchToken, BurstContextResult.Success, NodeStatus.Running, out failure), Is.True);
            AssertStep(ref machine, NativeLifecycleStepKindV1.ReactiveReset, 0);
            AssertStep(ref machine, NativeLifecycleStepKindV1.ChildSelected, 0);
            CompleteLeaf(ref machine, 1, NodeStatus.Success);
            AssertStep(ref machine, NativeLifecycleStepKindV1.ChildAccepted, 0);
            AssertStep(ref machine, NativeLifecycleStepKindV1.ChildSelected, 0);
            var secondEnter = AssertStep(ref machine, NativeLifecycleStepKindV1.DispatchRequired, 2, BurstCallbackPhase.Enter);
            Assert.That(machine.TryCompleteDispatch(secondEnter.DispatchToken, BurstContextResult.Success, NodeStatus.Running, out failure), Is.True);
            var secondTick = AssertStep(ref machine, NativeLifecycleStepKindV1.DispatchRequired, 2, BurstCallbackPhase.Tick);
            Assert.That(machine.TryCompleteDispatch(secondTick.DispatchToken, BurstContextResult.Success, NodeStatus.Running, out failure), Is.True);
            AssertStep(ref machine, NativeLifecycleStepKindV1.Waiting, 2);

            Assert.That(generations.ToArray(), Is.EqualTo(new uint[] { 1, 2, 2 }));
            Assert.That(memory[0], Is.EqualTo(1), "The reevaluated guard must advance the cursor back to the running action.");
        }

        [Test]
        public void DeepSequence_UsesTheFixedStackWithoutRecursion()
        {
            const int depth = 2048;
            var nodeRecords = new NativeCompiledNodeRecordV1[depth + 1];
            var childIndices = new uint[depth];
            var bindingRecords = new NativeLifecycleNodeBindingV1[depth + 1];
            for (var index = 0; index < depth; index++)
            {
                nodeRecords[index] = Node(StableHash.Fnv1A64("aibt.core.memory-sequence"), (uint)index * 4u, 4, (uint)index, 1);
                childIndices[index] = (uint)index + 1u;
                bindingRecords[index] = new NativeLifecycleNodeBindingV1((uint)index, NativeLifecycleNodeKindV1.MemorySequence);
            }
            nodeRecords[depth] = Node(StableHash.Fnv1A64("aibt.tests.deep-leaf"), 0, 0, 0, 0);
            bindingRecords[depth] = new NativeLifecycleNodeBindingV1((uint)depth, NativeLifecycleNodeKindV1.GeneratedLeaf);
            using var nodes = new NativeArray<NativeCompiledNodeRecordV1>(nodeRecords, Allocator.Persistent);
            using var children = new NativeArray<uint>(childIndices, Allocator.Persistent);
            using var bindings = new NativeArray<NativeLifecycleNodeBindingV1>(bindingRecords, Allocator.Persistent);
            using var memory = new NativeArray<byte>(depth * 4, Allocator.Persistent);
            using var frames = new NativeArray<NativeFrameStateV1>(depth + 1, Allocator.Persistent);
            using var generations = new NativeArray<uint>(depth + 1, Allocator.Persistent);
            using var control = new NativeArray<NativeLifecycleControlV1>(1, Allocator.Persistent);
            Assert.That(NativeLifecycleMachineV1.TryCreate(nodes, children, bindings, memory, frames, generations, control,
                out var machine, out var failure), Is.True, failure.Code.ToString());
            Assert.That(machine.TryBeginUpdate(1, out failure), Is.True, failure.Code.ToString());
            for (uint index = 0; index < depth; index++)
            {
                AssertStep(ref machine, NativeLifecycleStepKindV1.CompositeEntered, index);
                AssertStep(ref machine, NativeLifecycleStepKindV1.ChildSelected, index);
            }
            CompleteLeaf(ref machine, (uint)depth, NodeStatus.Success);
            for (var index = depth - 1; index >= 0; index--)
            {
                AssertStep(ref machine, NativeLifecycleStepKindV1.ChildAccepted, (uint)index);
                AssertStep(ref machine, NativeLifecycleStepKindV1.CompositeExited, (uint)index);
            }
            Assert.That(AssertStep(ref machine, NativeLifecycleStepKindV1.Completed, 0).RootStatus, Is.EqualTo(NodeStatus.Success));
        }

        [Test]
        public void TerminalPendingExitWinsAbortAndCopiedCompletionIsStale()
        {
            using var nodes = new NativeArray<NativeCompiledNodeRecordV1>(new[]
            {
                Node(StableHash.Fnv1A64("aibt.core.memory-sequence"), 0, 4, 0, 1),
                Node(StableHash.Fnv1A64("aibt.tests.child"), 0, 0, 0, 0),
            }, Allocator.Persistent);
            using var children = new NativeArray<uint>(new uint[] { 1 }, Allocator.Persistent);
            using var bindings = new NativeArray<NativeLifecycleNodeBindingV1>(new[]
            {
                new NativeLifecycleNodeBindingV1(0, NativeLifecycleNodeKindV1.MemorySequence),
                new NativeLifecycleNodeBindingV1(1, NativeLifecycleNodeKindV1.GeneratedLeaf),
            }, Allocator.Persistent);
            using var memory = new NativeArray<byte>(4, Allocator.Persistent);
            using var frames = new NativeArray<NativeFrameStateV1>(2, Allocator.Persistent);
            using var generations = new NativeArray<uint>(2, Allocator.Persistent);
            using var control = new NativeArray<NativeLifecycleControlV1>(1, Allocator.Persistent);
            Assert.That(NativeLifecycleMachineV1.TryCreate(nodes, children, bindings, memory, frames, generations, control,
                out var machine, out var failure), Is.True, failure.Code.ToString());
            Assert.That(machine.TryBeginUpdate(1, out failure), Is.True, failure.Code.ToString());
            AssertStep(ref machine, NativeLifecycleStepKindV1.CompositeEntered, 0);
            AssertStep(ref machine, NativeLifecycleStepKindV1.ChildSelected, 0);
            var enter = AssertStep(ref machine, NativeLifecycleStepKindV1.DispatchRequired, 1, BurstCallbackPhase.Enter);
            Assert.That(machine.TryCompleteDispatch(enter.DispatchToken, BurstContextResult.Success, NodeStatus.Running, out failure), Is.True);
            Assert.That(machine.TryCompleteDispatch(enter.DispatchToken, BurstContextResult.Success, NodeStatus.Running, out _), Is.False);
            var tick = AssertStep(ref machine, NativeLifecycleStepKindV1.DispatchRequired, 1, BurstCallbackPhase.Tick);
            Assert.That(machine.TryCompleteDispatch(tick.DispatchToken, BurstContextResult.Success, NodeStatus.Success, out failure), Is.True);
            Assert.That(machine.TryRequestAbort(BurstNodeAbortReason.Explicit, out failure), Is.True, failure.Code.ToString());
            var exit = AssertStep(ref machine, NativeLifecycleStepKindV1.DispatchRequired, 1, BurstCallbackPhase.Exit);
            Assert.That(machine.TryCompleteDispatch(exit.DispatchToken, BurstContextResult.Success, NodeStatus.Success, out failure), Is.True);
            AssertStep(ref machine, NativeLifecycleStepKindV1.CompositeAborted, 0);
            AssertStep(ref machine, NativeLifecycleStepKindV1.CompositeExited, 0);
            Assert.That(AssertStep(ref machine, NativeLifecycleStepKindV1.Completed, 0).HasRootStatus, Is.False);
        }

        [TestCase(2u, CompiledObserverMode.Self, BurstNodeAbortReason.ObserverSelf)]
        [TestCase(1u, CompiledObserverMode.LowerPriority, BurstNodeAbortReason.ObserverLowerPriority)]
        public void ObserverTransition_AbortsOnlyTheActiveReactiveSubtreeBeforeReset(
            uint observerNodeIndex,
            CompiledObserverMode mode,
            BurstNodeAbortReason reason)
        {
            using var nodes = new NativeArray<NativeCompiledNodeRecordV1>(new[]
            {
                Node(StableHash.Fnv1A64("aibt.core.reactive-sequence"), 0, 4, 0, 2),
                Node(StableHash.Fnv1A64("aibt.tests.guard"), 0, 0, 0, 0),
                Node(StableHash.Fnv1A64("aibt.tests.action"), 0, 0, 0, 0),
            }, Allocator.Persistent);
            using var children = new NativeArray<uint>(new uint[] { 1, 2 }, Allocator.Persistent);
            using var bindings = new NativeArray<NativeLifecycleNodeBindingV1>(new[]
            {
                new NativeLifecycleNodeBindingV1(0, NativeLifecycleNodeKindV1.ReactiveSequence),
                new NativeLifecycleNodeBindingV1(1, NativeLifecycleNodeKindV1.GeneratedLeaf),
                new NativeLifecycleNodeBindingV1(2, NativeLifecycleNodeKindV1.GeneratedLeaf),
            }, Allocator.Persistent);
            using var memory = new NativeArray<byte>(4, Allocator.Persistent);
            using var frames = new NativeArray<NativeFrameStateV1>(3, Allocator.Persistent);
            using var generations = new NativeArray<uint>(3, Allocator.Persistent);
            using var control = new NativeArray<NativeLifecycleControlV1>(1, Allocator.Persistent);
            Assert.That(NativeLifecycleMachineV1.TryCreate(nodes, children, bindings, memory, frames, generations, control,
                out var machine, out var failure), Is.True, failure.Code.ToString());
            Assert.That(machine.TryBeginUpdate(1, out failure), Is.True, failure.Code.ToString());
            AssertStep(ref machine, NativeLifecycleStepKindV1.CompositeEntered, 0);
            AssertStep(ref machine, NativeLifecycleStepKindV1.ChildSelected, 0);
            CompleteLeaf(ref machine, 1, NodeStatus.Success);
            AssertStep(ref machine, NativeLifecycleStepKindV1.ChildAccepted, 0);
            AssertStep(ref machine, NativeLifecycleStepKindV1.ChildSelected, 0);
            var enter = AssertStep(ref machine, NativeLifecycleStepKindV1.DispatchRequired, 2, BurstCallbackPhase.Enter);
            Assert.That(machine.TryCompleteDispatch(enter.DispatchToken, BurstContextResult.Success, NodeStatus.Running, out failure), Is.True);
            var tick = AssertStep(ref machine, NativeLifecycleStepKindV1.DispatchRequired, 2, BurstCallbackPhase.Tick);
            Assert.That(machine.TryCompleteDispatch(tick.DispatchToken, BurstContextResult.Success, NodeStatus.Running, out failure), Is.True);
            Assert.That(machine.TryApplyObserverTransition(
                new NativeObserverTransitionV1(observerNodeIndex, 0, mode, reason), out failure), Is.True, failure.Code.ToString());
            var abort = AssertStep(ref machine, NativeLifecycleStepKindV1.DispatchRequired, 2, BurstCallbackPhase.Abort);
            Assert.That(machine.TryCompleteDispatch(abort.DispatchToken, BurstContextResult.Success, NodeStatus.Running, out failure), Is.True);
            var exit = AssertStep(ref machine, NativeLifecycleStepKindV1.DispatchRequired, 2, BurstCallbackPhase.Exit);
            Assert.That(machine.TryCompleteDispatch(exit.DispatchToken, BurstContextResult.Success, NodeStatus.Running, out failure), Is.True);
            AssertStep(ref machine, NativeLifecycleStepKindV1.ReactiveReset, 0);
        }

        private static void CompleteLeaf(ref NativeLifecycleMachineV1 machine, uint nodeIndex, NodeStatus status)
        {
            var enter = AssertStep(ref machine, NativeLifecycleStepKindV1.DispatchRequired, nodeIndex, BurstCallbackPhase.Enter);
            Assert.That(machine.TryCompleteDispatch(enter.DispatchToken, BurstContextResult.Success, NodeStatus.Running, out var failure), Is.True, failure.Code.ToString());
            var tick = AssertStep(ref machine, NativeLifecycleStepKindV1.DispatchRequired, nodeIndex, BurstCallbackPhase.Tick);
            Assert.That(machine.TryCompleteDispatch(tick.DispatchToken, BurstContextResult.Success, status, out failure), Is.True, failure.Code.ToString());
            var exit = AssertStep(ref machine, NativeLifecycleStepKindV1.DispatchRequired, nodeIndex, BurstCallbackPhase.Exit);
            Assert.That(machine.TryCompleteDispatch(exit.DispatchToken, BurstContextResult.Success, status, out failure), Is.True, failure.Code.ToString());
        }

        private static void AssertRandomVector(
            ulong rootSeed,
            string semanticHash,
            ulong treeInstanceId,
            uint nodeIndex,
            ulong seededState,
            ulong increment,
            params uint[] expected)
        {
            Assert.That(NativeRandomStreamDerivationV1.TryDerive(
                rootSeed,
                new NativeHash256V1(new CompiledHash(semanticHash)),
                treeInstanceId,
                nodeIndex,
                out var stream), Is.True);
            Assert.That(stream.State, Is.EqualTo(seededState));
            Assert.That(stream.Increment, Is.EqualTo(increment));
            var state = stream.State;
            for (var index = 0; index < expected.Length; index++)
                Assert.That(NativeRandomStreamDerivationV1.NextUInt32(ref state, stream.Increment), Is.EqualTo(expected[index]), $"output {index}");
        }

        private static NativeLifecycleStepResultV1 AssertStep(
            ref NativeLifecycleMachineV1 machine,
            NativeLifecycleStepKindV1 expected,
            uint nodeIndex,
            BurstCallbackPhase phase = default)
        {
            Assert.That(machine.TryAdvance(out var actual, out var failure), Is.True, failure.Code.ToString());
            Assert.That(actual.Kind, Is.EqualTo(expected));
            Assert.That(actual.NodeIndex, Is.EqualTo(nodeIndex));
            if (expected == NativeLifecycleStepKindV1.DispatchRequired)
                Assert.That(actual.Phase, Is.EqualTo(phase));
            return actual;
        }

        private static NativeCompiledNodeRecordV1 Node(
            ulong typeId,
            uint memoryOffset,
            uint memorySize,
            uint childOffset,
            uint childCount)
            => new NativeCompiledNodeRecordV1(new CompiledNodeRecord(
                typeId, 1, 0, 0, 1,
                memorySize == 0 ? 0u : memoryOffset, memorySize, memorySize == 0 ? 1u : 4u,
                NodeMemoryLifetime.Activation,
                new CompiledRange(childOffset, childCount),
                CompiledNodeFlags.BurstDomain,
                0,
                new CompiledRange(0, 0),
                new CompiledRange(0, 0)));
    }
}
