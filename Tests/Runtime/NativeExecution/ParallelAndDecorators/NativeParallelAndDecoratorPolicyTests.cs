using NUnit.Framework;
using Unity.Collections;

namespace AIBT.Tests.Runtime.NativeExecution.ParallelAndDecorators
{
    public sealed class NativeParallelAndDecoratorPolicyTests
    {
        [TestCase(0, "SS", true, NodeStatus.Success)]
        [TestCase(0, "SR", false, NodeStatus.Running)]
        [TestCase(0, "SF", true, NodeStatus.Failure)]
        [TestCase(1, "SR", true, NodeStatus.Success)]
        [TestCase(1, "FR", false, NodeStatus.Running)]
        [TestCase(1, "FF", true, NodeStatus.Failure)]
        public void ParallelPoliciesMatchTheReferenceDecisionTable(byte policy, string states, bool terminal, NodeStatus status)
        {
            using var branches = Branches(states);
            var configuration = new NativeParallelConfigurationV1((NativeParallelPolicyV1)policy, 0, 0, NativeParallelTieBreakV1.FailureFirst);
            Assert.That(NativeParallelPolicyEvaluatorV1.TryEvaluate(configuration, branches.AsReadOnly(), 0, (uint)branches.Length, out var actual), Is.True);
            Assert.That(actual.IsTerminal, Is.EqualTo(terminal));
            Assert.That(actual.Status, Is.EqualTo(status));
        }

        [TestCase((byte)NativeParallelTieBreakV1.FailureFirst, NodeStatus.Failure)]
        [TestCase((byte)NativeParallelTieBreakV1.SuccessFirst, NodeStatus.Success)]
        public void ThresholdTieUsesConfiguredStableOrder(byte tieBreak, NodeStatus expected)
        {
            using var branches = Branches("SF");
            var configuration = new NativeParallelConfigurationV1(
                NativeParallelPolicyV1.Threshold, 1, 1, (NativeParallelTieBreakV1)tieBreak);
            Assert.That(NativeParallelPolicyEvaluatorV1.TryEvaluate(configuration, branches.AsReadOnly(), 0, 2, out var actual), Is.True);
            Assert.That(actual.IsTerminal, Is.True);
            Assert.That(actual.Status, Is.EqualTo(expected));
        }

        [Test]
        public void DecoratorTransformsAndCheckedDeadlinesMatchReferenceRules()
        {
            Assert.That(NativeDecoratorPolicyV1.Transform(NativeDecoratorKindV1.Inverter, NodeStatus.Success), Is.EqualTo(NodeStatus.Failure));
            Assert.That(NativeDecoratorPolicyV1.Transform(NativeDecoratorKindV1.Inverter, NodeStatus.Failure), Is.EqualTo(NodeStatus.Success));
            Assert.That(NativeDecoratorPolicyV1.Transform(NativeDecoratorKindV1.Succeeder, NodeStatus.Failure), Is.EqualTo(NodeStatus.Success));
            Assert.That(NativeDecoratorPolicyV1.Transform(NativeDecoratorKindV1.Failer, NodeStatus.Success), Is.EqualTo(NodeStatus.Failure));
            Assert.That(NativeDecoratorPolicyV1.Transform(NativeDecoratorKindV1.Inverter, NodeStatus.Running), Is.EqualTo(NodeStatus.Running));
            Assert.That(NativeDecoratorPolicyV1.TryDeadline(10, 5, out var deadline), Is.True);
            Assert.That(deadline, Is.EqualTo(15));
            Assert.That(NativeDecoratorPolicyV1.TryDeadline(long.MaxValue, 1, out deadline), Is.False);
            Assert.That(deadline, Is.Zero);
        }

        [Test]
        public void ConfigurationDecodersRejectPaddingAndBoundaryViolations()
        {
            var bytes = new NativeArray<byte>(16, Allocator.Persistent);
            try
            {
                bytes[0] = 2;
                WriteU32(bytes, 4, 1);
                WriteU32(bytes, 8, 1);
                bytes[12] = 1;
                Assert.That(NativeParallelPolicyEvaluatorV1.TryDecode(bytes.AsReadOnly(), 0, 16, 2, out var parallel), Is.True);
                Assert.That(parallel.Policy, Is.EqualTo(NativeParallelPolicyV1.Threshold));
                bytes[15] = 1;
                Assert.That(NativeParallelPolicyEvaluatorV1.TryDecode(bytes.AsReadOnly(), 0, 16, 2, out _), Is.False);
                bytes[15] = 0;

                for (var index = 0; index < bytes.Length; index++) bytes[index] = 0;
                WriteU64(bytes, 0, 100);
                bytes[8] = 1;
                Assert.That(NativeDecoratorPolicyV1.TryDecodeTimeout(bytes.AsReadOnly(), 0, 16, out var timeout), Is.True);
                Assert.That(timeout.DurationMicroseconds, Is.EqualTo(100));
                Assert.That(timeout.Result, Is.EqualTo(NodeStatus.Success));
                bytes[9] = 1;
                Assert.That(NativeDecoratorPolicyV1.TryDecodeTimeout(bytes.AsReadOnly(), 0, 16, out _), Is.False);
            }
            finally
            {
                bytes.Dispose();
            }
        }

        [Test]
        public void Repeater_ExitsBeforeReenterAndUsesOneChildAcceptancePerIteration()
        {
            using var nodes = new NativeArray<NativeCompiledNodeRecordV1>(new[]
            {
                Node(StableHash.Fnv1A64("aibt.core.repeater"), 0, 8, 0, 4, 4, 0, 1),
                Node(StableHash.Fnv1A64("aibt.tests.child"), 0, 0, 0, 0, 1, 0, 0),
            }, Allocator.Persistent);
            using var children = new NativeArray<uint>(new uint[] { 1 }, Allocator.Persistent);
            using var bindings = new NativeArray<NativeLifecycleNodeBindingV1>(new[]
            {
                new NativeLifecycleNodeBindingV1(0, NativeLifecycleNodeKindV1.Repeater),
                new NativeLifecycleNodeBindingV1(1, NativeLifecycleNodeKindV1.GeneratedLeaf),
            }, Allocator.Persistent);
            using var memory = new NativeArray<byte>(4, Allocator.Persistent);
            using var frames = new NativeArray<NativeFrameStateV1>(2, Allocator.Persistent);
            using var generations = new NativeArray<uint>(2, Allocator.Persistent);
            using var control = new NativeArray<NativeLifecycleControlV1>(1, Allocator.Persistent);
            using var config = new NativeArray<byte>(8, Allocator.Persistent);
            var mutableConfig = config;
            WriteU32(mutableConfig, 0, 2);
            Assert.That(NativeLifecycleMachineV1.TryCreate(
                nodes, children, bindings, memory, frames, generations, control, config,
                out var machine, out var failure), Is.True, failure.Code.ToString());
            Assert.That(machine.TryBeginUpdate(1, out failure), Is.True, failure.Code.ToString());
            AssertStep(ref machine, NativeLifecycleStepKindV1.CompositeEntered, 0);
            for (var iteration = 0; iteration < 2; iteration++)
            {
                AssertStep(ref machine, NativeLifecycleStepKindV1.ChildSelected, 0);
                CompleteLeaf(ref machine, iteration == 0 ? NodeStatus.Success : NodeStatus.Failure);
                AssertStep(ref machine, NativeLifecycleStepKindV1.ChildAccepted, 0);
            }
            AssertStep(ref machine, NativeLifecycleStepKindV1.CompositeExited, 0);
            var completed = AssertStep(ref machine, NativeLifecycleStepKindV1.Completed, 0);
            Assert.That(completed.RootStatus, Is.EqualTo(NodeStatus.Success));
            Assert.That(generations.ToArray(), Is.EqualTo(new uint[] { 1, 2 }));
            Assert.That(memory.ToArray(), Is.EqualTo(new byte[4]));
        }

        [Test]
        public void Timeout_AbortsRunningChildAtEqualityBeforeRetick()
        {
            using var nodes = new NativeArray<NativeCompiledNodeRecordV1>(new[]
            {
                Node(StableHash.Fnv1A64("aibt.core.timeout"), 0, 16, 0, 8, 8, 0, 1),
                Node(StableHash.Fnv1A64("aibt.tests.child"), 0, 0, 0, 0, 1, 0, 0),
            }, Allocator.Persistent);
            using var children = new NativeArray<uint>(new uint[] { 1 }, Allocator.Persistent);
            using var bindings = new NativeArray<NativeLifecycleNodeBindingV1>(new[]
            {
                new NativeLifecycleNodeBindingV1(0, NativeLifecycleNodeKindV1.Timeout),
                new NativeLifecycleNodeBindingV1(1, NativeLifecycleNodeKindV1.GeneratedLeaf),
            }, Allocator.Persistent);
            using var memory = new NativeArray<byte>(8, Allocator.Persistent);
            using var frames = new NativeArray<NativeFrameStateV1>(2, Allocator.Persistent);
            using var generations = new NativeArray<uint>(2, Allocator.Persistent);
            using var control = new NativeArray<NativeLifecycleControlV1>(1, Allocator.Persistent);
            using var config = new NativeArray<byte>(16, Allocator.Persistent);
            WriteU64(config, 0, 100);
            Assert.That(NativeLifecycleMachineV1.TryCreate(
                nodes, children, bindings, memory, frames, generations, control, config,
                out var machine, out var failure), Is.True, failure.Code.ToString());
            Assert.That(machine.TryBeginUpdate(1, 10, out failure), Is.True, failure.Code.ToString());
            AssertStep(ref machine, NativeLifecycleStepKindV1.CompositeEntered, 0);
            AssertStep(ref machine, NativeLifecycleStepKindV1.ChildSelected, 0);
            var enter = AssertStep(ref machine, NativeLifecycleStepKindV1.DispatchRequired, 1, AIBT.Burst.BurstCallbackPhase.Enter);
            Assert.That(machine.TryCompleteDispatch(enter.DispatchToken, AIBT.Burst.BurstContextResult.Success, NodeStatus.Running, out failure), Is.True, failure.Code.ToString());
            var tick = AssertStep(ref machine, NativeLifecycleStepKindV1.DispatchRequired, 1, AIBT.Burst.BurstCallbackPhase.Tick);
            Assert.That(machine.TryCompleteDispatch(tick.DispatchToken, AIBT.Burst.BurstContextResult.Success, NodeStatus.Running, out failure), Is.True, failure.Code.ToString());
            AssertStep(ref machine, NativeLifecycleStepKindV1.Waiting, 1);

            Assert.That(machine.TryBeginUpdate(2, 110, out failure), Is.True, failure.Code.ToString());
            var abort = AssertStep(ref machine, NativeLifecycleStepKindV1.DispatchRequired, 1, AIBT.Burst.BurstCallbackPhase.Abort);
            Assert.That(machine.TryCompleteDispatch(abort.DispatchToken, AIBT.Burst.BurstContextResult.Success, NodeStatus.Running, out failure), Is.True, failure.Code.ToString());
            var exit = AssertStep(ref machine, NativeLifecycleStepKindV1.DispatchRequired, 1, AIBT.Burst.BurstCallbackPhase.Exit);
            Assert.That(machine.TryCompleteDispatch(exit.DispatchToken, AIBT.Burst.BurstContextResult.Success, NodeStatus.Running, out failure), Is.True, failure.Code.ToString());
            AssertStep(ref machine, NativeLifecycleStepKindV1.ChildAccepted, 0);
            AssertStep(ref machine, NativeLifecycleStepKindV1.CompositeExited, 0);
            Assert.That(AssertStep(ref machine, NativeLifecycleStepKindV1.Completed, 0).RootStatus, Is.EqualTo(NodeStatus.Failure));
        }

        [Test]
        public void Cooldown_OnEnterPersistsAcrossRepeaterReactivationWithoutChildReenter()
        {
            using var nodes = new NativeArray<NativeCompiledNodeRecordV1>(new[]
            {
                Node(StableHash.Fnv1A64("aibt.core.repeater"), 0, 8, 0, 4, 4, 0, 1),
                Node(StableHash.Fnv1A64("aibt.core.cooldown"), 8, 16, 8, 8, 8, 1, 1, NodeMemoryLifetime.Instance),
                Node(StableHash.Fnv1A64("aibt.tests.child"), 0, 0, 0, 0, 1, 0, 0),
            }, Allocator.Persistent);
            using var children = new NativeArray<uint>(new uint[] { 1, 2 }, Allocator.Persistent);
            using var bindings = new NativeArray<NativeLifecycleNodeBindingV1>(new[]
            {
                new NativeLifecycleNodeBindingV1(0, NativeLifecycleNodeKindV1.Repeater),
                new NativeLifecycleNodeBindingV1(1, NativeLifecycleNodeKindV1.Cooldown),
                new NativeLifecycleNodeBindingV1(2, NativeLifecycleNodeKindV1.GeneratedLeaf),
            }, Allocator.Persistent);
            using var memory = new NativeArray<byte>(16, Allocator.Persistent);
            using var frames = new NativeArray<NativeFrameStateV1>(3, Allocator.Persistent);
            using var generations = new NativeArray<uint>(3, Allocator.Persistent);
            using var control = new NativeArray<NativeLifecycleControlV1>(1, Allocator.Persistent);
            using var initialized = new NativeArray<byte>(3, Allocator.Persistent);
            using var config = new NativeArray<byte>(24, Allocator.Persistent);
            var mutableConfig = config;
            WriteU32(mutableConfig, 0, 2); mutableConfig[4] = 1;
            WriteU64(mutableConfig, 8, 100); mutableConfig[16] = 0; mutableConfig[17] = 0;
            Assert.That(NativeLifecycleMachineV1.TryCreate(
                nodes, children, bindings, memory, frames, generations, control, config, initialized,
                out var machine, out var failure), Is.True, failure.Code.ToString());
            Assert.That(machine.TryBeginUpdate(1, 10, out failure), Is.True, failure.Code.ToString());
            AssertStep(ref machine, NativeLifecycleStepKindV1.CompositeEntered, 0);
            AssertStep(ref machine, NativeLifecycleStepKindV1.ChildSelected, 0);
            AssertStep(ref machine, NativeLifecycleStepKindV1.CompositeEntered, 1);
            AssertStep(ref machine, NativeLifecycleStepKindV1.ChildSelected, 1);
            CompleteLeaf(ref machine, NodeStatus.Success, 2);
            AssertStep(ref machine, NativeLifecycleStepKindV1.ChildAccepted, 1);
            AssertStep(ref machine, NativeLifecycleStepKindV1.CompositeExited, 1);
            AssertStep(ref machine, NativeLifecycleStepKindV1.ChildAccepted, 0);
            AssertStep(ref machine, NativeLifecycleStepKindV1.ChildSelected, 0);
            AssertStep(ref machine, NativeLifecycleStepKindV1.CompositeEntered, 1);
            AssertStep(ref machine, NativeLifecycleStepKindV1.ChildAccepted, 1);
            AssertStep(ref machine, NativeLifecycleStepKindV1.CompositeExited, 1);
            AssertStep(ref machine, NativeLifecycleStepKindV1.ChildAccepted, 0);
            AssertStep(ref machine, NativeLifecycleStepKindV1.CompositeExited, 0);
            Assert.That(AssertStep(ref machine, NativeLifecycleStepKindV1.Completed, 0).RootStatus, Is.EqualTo(NodeStatus.Failure));
            Assert.That(generations.ToArray(), Is.EqualTo(new uint[] { 1, 2, 1 }));
        }

        [Test]
        public void Parallel_RetainsRunningBranchAndResumesItInSemanticOrderNextUpdate()
        {
            using var nodes = new NativeArray<NativeCompiledNodeRecordV1>(new[]
            {
                Node(StableHash.Fnv1A64("aibt.core.parallel"), 0, 16, 0, 8, 4, 0, 2),
                Node(StableHash.Fnv1A64("aibt.tests.running"), 0, 0, 0, 0, 1, 0, 0),
                Node(StableHash.Fnv1A64("aibt.tests.success"), 0, 0, 0, 0, 1, 0, 0),
            }, Allocator.Persistent);
            using var children = new NativeArray<uint>(new uint[] { 1, 2 }, Allocator.Persistent);
            using var bindings = new NativeArray<NativeLifecycleNodeBindingV1>(new[]
            {
                new NativeLifecycleNodeBindingV1(0, NativeLifecycleNodeKindV1.Parallel),
                new NativeLifecycleNodeBindingV1(1, NativeLifecycleNodeKindV1.GeneratedLeaf),
                new NativeLifecycleNodeBindingV1(2, NativeLifecycleNodeKindV1.GeneratedLeaf),
            }, Allocator.Persistent);
            using var memory = new NativeArray<byte>(8, Allocator.Persistent);
            using var frames = new NativeArray<NativeFrameStateV1>(3, Allocator.Persistent);
            using var generations = new NativeArray<uint>(3, Allocator.Persistent);
            using var control = new NativeArray<NativeLifecycleControlV1>(1, Allocator.Persistent);
            using var config = new NativeArray<byte>(16, Allocator.Persistent);
            using var branches = new NativeArray<NativeParallelBranchStateV1>(2, Allocator.Persistent);
            Assert.That(NativeLifecycleMachineV1.TryCreate(
                nodes, children, bindings, memory, frames, generations, control, config,
                default, branches, out var machine, out var failure), Is.True, failure.Code.ToString());
            Assert.That(machine.TryBeginUpdate(1, out failure), Is.True, failure.Code.ToString());
            AssertStep(ref machine, NativeLifecycleStepKindV1.CompositeEntered, 0);
            AssertStep(ref machine, NativeLifecycleStepKindV1.ChildSelected, 0);
            var enter = AssertStep(ref machine, NativeLifecycleStepKindV1.DispatchRequired, 1, AIBT.Burst.BurstCallbackPhase.Enter);
            Assert.That(machine.TryCompleteDispatch(enter.DispatchToken, AIBT.Burst.BurstContextResult.Success, NodeStatus.Running, out failure), Is.True);
            var tick = AssertStep(ref machine, NativeLifecycleStepKindV1.DispatchRequired, 1, AIBT.Burst.BurstCallbackPhase.Tick);
            Assert.That(machine.TryCompleteDispatch(tick.DispatchToken, AIBT.Burst.BurstContextResult.Success, NodeStatus.Running, out failure), Is.True);
            AssertStep(ref machine, NativeLifecycleStepKindV1.ParallelBranchSuspended, 0);
            AssertStep(ref machine, NativeLifecycleStepKindV1.ChildSelected, 0);
            CompleteLeaf(ref machine, NodeStatus.Success, 2);
            AssertStep(ref machine, NativeLifecycleStepKindV1.ChildAccepted, 0);
            AssertStep(ref machine, NativeLifecycleStepKindV1.Waiting, 0);

            Assert.That(machine.TryBeginUpdate(2, out failure), Is.True, failure.Code.ToString());
            AssertStep(ref machine, NativeLifecycleStepKindV1.ChildSelected, 0);
            var resumedTick = AssertStep(ref machine, NativeLifecycleStepKindV1.DispatchRequired, 1, AIBT.Burst.BurstCallbackPhase.Tick);
            Assert.That(machine.TryCompleteDispatch(resumedTick.DispatchToken, AIBT.Burst.BurstContextResult.Success, NodeStatus.Success, out failure), Is.True);
            var resumedExit = AssertStep(ref machine, NativeLifecycleStepKindV1.DispatchRequired, 1, AIBT.Burst.BurstCallbackPhase.Exit);
            Assert.That(machine.TryCompleteDispatch(resumedExit.DispatchToken, AIBT.Burst.BurstContextResult.Success, NodeStatus.Success, out failure), Is.True);
            AssertStep(ref machine, NativeLifecycleStepKindV1.ChildAccepted, 0);
            AssertStep(ref machine, NativeLifecycleStepKindV1.ChildSelected, 0);
            AssertStep(ref machine, NativeLifecycleStepKindV1.ChildAccepted, 0);
            AssertStep(ref machine, NativeLifecycleStepKindV1.CompositeExited, 0);
            Assert.That(AssertStep(ref machine, NativeLifecycleStepKindV1.Completed, 0).RootStatus, Is.EqualTo(NodeStatus.Success));
            Assert.That(generations.ToArray(), Is.EqualTo(new uint[] { 1, 1, 1 }));
        }

        [Test]
        public void Parallel_TerminalDecisionAbortsRetainedBranchesInReverseSemanticOrder()
        {
            using var nodes = new NativeArray<NativeCompiledNodeRecordV1>(new[]
            {
                Node(StableHash.Fnv1A64("aibt.core.parallel"), 0, 16, 0, 8, 4, 0, 3),
                Node(StableHash.Fnv1A64("aibt.tests.running-a"), 0, 0, 0, 0, 1, 0, 0),
                Node(StableHash.Fnv1A64("aibt.tests.running-b"), 0, 0, 0, 0, 1, 0, 0),
                Node(StableHash.Fnv1A64("aibt.tests.success"), 0, 0, 0, 0, 1, 0, 0),
            }, Allocator.Persistent);
            using var children = new NativeArray<uint>(new uint[] { 1, 2, 3 }, Allocator.Persistent);
            using var bindings = new NativeArray<NativeLifecycleNodeBindingV1>(new[]
            {
                new NativeLifecycleNodeBindingV1(0, NativeLifecycleNodeKindV1.Parallel),
                new NativeLifecycleNodeBindingV1(1, NativeLifecycleNodeKindV1.GeneratedLeaf),
                new NativeLifecycleNodeBindingV1(2, NativeLifecycleNodeKindV1.GeneratedLeaf),
                new NativeLifecycleNodeBindingV1(3, NativeLifecycleNodeKindV1.GeneratedLeaf),
            }, Allocator.Persistent);
            using var memory = new NativeArray<byte>(8, Allocator.Persistent);
            using var frames = new NativeArray<NativeFrameStateV1>(4, Allocator.Persistent);
            using var generations = new NativeArray<uint>(4, Allocator.Persistent);
            using var control = new NativeArray<NativeLifecycleControlV1>(1, Allocator.Persistent);
            using var config = new NativeArray<byte>(16, Allocator.Persistent);
            using var branches = new NativeArray<NativeParallelBranchStateV1>(3, Allocator.Persistent);
            var mutableConfig = config;
            mutableConfig[0] = (byte)NativeParallelPolicyV1.RequireAnySuccess;
            Assert.That(NativeLifecycleMachineV1.TryCreate(
                nodes, children, bindings, memory, frames, generations, control, config,
                default, branches, out var machine, out var failure), Is.True, failure.Code.ToString());
            Assert.That(machine.TryBeginUpdate(1, out failure), Is.True, failure.Code.ToString());
            AssertStep(ref machine, NativeLifecycleStepKindV1.CompositeEntered, 0);
            for (uint node = 1; node <= 2; node++)
            {
                AssertStep(ref machine, NativeLifecycleStepKindV1.ChildSelected, 0);
                var enter = AssertStep(ref machine, NativeLifecycleStepKindV1.DispatchRequired, node, AIBT.Burst.BurstCallbackPhase.Enter);
                Assert.That(machine.TryCompleteDispatch(enter.DispatchToken, AIBT.Burst.BurstContextResult.Success, NodeStatus.Running, out failure), Is.True);
                var tick = AssertStep(ref machine, NativeLifecycleStepKindV1.DispatchRequired, node, AIBT.Burst.BurstCallbackPhase.Tick);
                Assert.That(machine.TryCompleteDispatch(tick.DispatchToken, AIBT.Burst.BurstContextResult.Success, NodeStatus.Running, out failure), Is.True);
                AssertStep(ref machine, NativeLifecycleStepKindV1.ParallelBranchSuspended, 0);
            }
            AssertStep(ref machine, NativeLifecycleStepKindV1.ChildSelected, 0);
            CompleteLeaf(ref machine, NodeStatus.Success, 3);
            AssertStep(ref machine, NativeLifecycleStepKindV1.ChildAccepted, 0);
            AssertStep(ref machine, NativeLifecycleStepKindV1.ChildAccepted, 0);
            for (var node = 2; node >= 1; node--)
            {
                var abort = AssertStep(ref machine, NativeLifecycleStepKindV1.DispatchRequired, (uint)node, AIBT.Burst.BurstCallbackPhase.Abort);
                Assert.That(machine.TryCompleteDispatch(abort.DispatchToken, AIBT.Burst.BurstContextResult.Success, NodeStatus.Running, out failure), Is.True);
                var exit = AssertStep(ref machine, NativeLifecycleStepKindV1.DispatchRequired, (uint)node, AIBT.Burst.BurstCallbackPhase.Exit);
                Assert.That(machine.TryCompleteDispatch(exit.DispatchToken, AIBT.Burst.BurstContextResult.Success, NodeStatus.Running, out failure), Is.True);
                AssertStep(ref machine, node == 2 ? NativeLifecycleStepKindV1.ChildSelected : NativeLifecycleStepKindV1.ChildAccepted, 0);
            }
            AssertStep(ref machine, NativeLifecycleStepKindV1.CompositeExited, 0);
            Assert.That(AssertStep(ref machine, NativeLifecycleStepKindV1.Completed, 0).RootStatus, Is.EqualTo(NodeStatus.Success));
        }

        [Test]
        public void Parallel_ExternalAbortUnwindsEveryRetainedBranchInReverseSemanticOrder()
        {
            using var nodes = new NativeArray<NativeCompiledNodeRecordV1>(new[]
            {
                Node(StableHash.Fnv1A64("aibt.core.parallel"), 0, 16, 0, 8, 4, 0, 2),
                Node(StableHash.Fnv1A64("aibt.tests.running-a"), 0, 0, 0, 0, 1, 0, 0),
                Node(StableHash.Fnv1A64("aibt.tests.running-b"), 0, 0, 0, 0, 1, 0, 0),
            }, Allocator.Persistent);
            using var children = new NativeArray<uint>(new uint[] { 1, 2 }, Allocator.Persistent);
            using var bindings = new NativeArray<NativeLifecycleNodeBindingV1>(new[]
            {
                new NativeLifecycleNodeBindingV1(0, NativeLifecycleNodeKindV1.Parallel),
                new NativeLifecycleNodeBindingV1(1, NativeLifecycleNodeKindV1.GeneratedLeaf),
                new NativeLifecycleNodeBindingV1(2, NativeLifecycleNodeKindV1.GeneratedLeaf),
            }, Allocator.Persistent);
            using var memory = new NativeArray<byte>(8, Allocator.Persistent);
            using var frames = new NativeArray<NativeFrameStateV1>(3, Allocator.Persistent);
            using var generations = new NativeArray<uint>(3, Allocator.Persistent);
            using var control = new NativeArray<NativeLifecycleControlV1>(1, Allocator.Persistent);
            using var config = new NativeArray<byte>(16, Allocator.Persistent);
            using var branches = new NativeArray<NativeParallelBranchStateV1>(2, Allocator.Persistent);
            Assert.That(NativeLifecycleMachineV1.TryCreate(nodes, children, bindings, memory, frames, generations,
                control, config, default, branches, out var machine, out var failure), Is.True, failure.Code.ToString());
            Assert.That(machine.TryBeginUpdate(1, out failure), Is.True, failure.Code.ToString());
            AssertStep(ref machine, NativeLifecycleStepKindV1.CompositeEntered, 0);
            for (uint node = 1; node <= 2; node++)
            {
                AssertStep(ref machine, NativeLifecycleStepKindV1.ChildSelected, 0);
                var enter = AssertStep(ref machine, NativeLifecycleStepKindV1.DispatchRequired, node, AIBT.Burst.BurstCallbackPhase.Enter);
                Assert.That(machine.TryCompleteDispatch(enter.DispatchToken, AIBT.Burst.BurstContextResult.Success, NodeStatus.Running, out failure), Is.True);
                var tick = AssertStep(ref machine, NativeLifecycleStepKindV1.DispatchRequired, node, AIBT.Burst.BurstCallbackPhase.Tick);
                Assert.That(machine.TryCompleteDispatch(tick.DispatchToken, AIBT.Burst.BurstContextResult.Success, NodeStatus.Running, out failure), Is.True);
                AssertStep(ref machine, NativeLifecycleStepKindV1.ParallelBranchSuspended, 0);
            }
            AssertStep(ref machine, NativeLifecycleStepKindV1.Waiting, 0);

            Assert.That(machine.TryBeginUpdate(2, out failure), Is.True, failure.Code.ToString());
            Assert.That(machine.TryRequestAbort(AIBT.Burst.BurstNodeAbortReason.TreeStopped, out failure), Is.True, failure.Code.ToString());
            AssertStep(ref machine, NativeLifecycleStepKindV1.ChildSelected, 0);
            for (var node = 2; node >= 1; node--)
            {
                var abort = AssertStep(ref machine, NativeLifecycleStepKindV1.DispatchRequired, (uint)node, AIBT.Burst.BurstCallbackPhase.Abort);
                Assert.That(machine.TryCompleteDispatch(abort.DispatchToken, AIBT.Burst.BurstContextResult.Success, NodeStatus.Running, out failure), Is.True);
                var exit = AssertStep(ref machine, NativeLifecycleStepKindV1.DispatchRequired, (uint)node, AIBT.Burst.BurstCallbackPhase.Exit);
                Assert.That(machine.TryCompleteDispatch(exit.DispatchToken, AIBT.Burst.BurstContextResult.Success, NodeStatus.Running, out failure), Is.True);
                AssertStep(ref machine, node == 2 ? NativeLifecycleStepKindV1.ChildSelected : NativeLifecycleStepKindV1.CompositeAborted, 0);
            }
            AssertStep(ref machine, NativeLifecycleStepKindV1.CompositeExited, 0);
            var completed = AssertStep(ref machine, NativeLifecycleStepKindV1.Completed, 0);
            Assert.That(completed.HasRootStatus, Is.False);
            Assert.That(control[0].AbortReason, Is.EqualTo(AIBT.Burst.BurstNodeAbortReason.TreeStopped));
        }

        [Test]
        public void Parallel_RestoredBranchHonorsExpiredTimeoutBeforeRetick()
        {
            using var nodes = new NativeArray<NativeCompiledNodeRecordV1>(new[]
            {
                Node(StableHash.Fnv1A64("aibt.core.parallel"), 0, 16, 0, 8, 4, 0, 2),
                Node(StableHash.Fnv1A64("aibt.core.timeout"), 16, 16, 8, 8, 8, 2, 1),
                Node(StableHash.Fnv1A64("aibt.tests.success"), 0, 0, 0, 0, 1, 0, 0),
                Node(StableHash.Fnv1A64("aibt.tests.running"), 0, 0, 0, 0, 1, 0, 0),
            }, Allocator.Persistent);
            using var children = new NativeArray<uint>(new uint[] { 1, 2, 3 }, Allocator.Persistent);
            using var bindings = new NativeArray<NativeLifecycleNodeBindingV1>(new[]
            {
                new NativeLifecycleNodeBindingV1(0, NativeLifecycleNodeKindV1.Parallel),
                new NativeLifecycleNodeBindingV1(1, NativeLifecycleNodeKindV1.Timeout),
                new NativeLifecycleNodeBindingV1(2, NativeLifecycleNodeKindV1.GeneratedLeaf),
                new NativeLifecycleNodeBindingV1(3, NativeLifecycleNodeKindV1.GeneratedLeaf),
            }, Allocator.Persistent);
            using var memory = new NativeArray<byte>(16, Allocator.Persistent);
            using var frames = new NativeArray<NativeFrameStateV1>(4, Allocator.Persistent);
            using var generations = new NativeArray<uint>(4, Allocator.Persistent);
            using var control = new NativeArray<NativeLifecycleControlV1>(1, Allocator.Persistent);
            using var config = new NativeArray<byte>(32, Allocator.Persistent);
            using var branches = new NativeArray<NativeParallelBranchStateV1>(2, Allocator.Persistent);
            WriteU64(config, 16, 100);
            Assert.That(NativeLifecycleMachineV1.TryCreate(nodes, children, bindings, memory, frames, generations,
                control, config, default, branches, out var machine, out var failure), Is.True, failure.Code.ToString());
            Assert.That(machine.TryBeginUpdate(1, 10, out failure), Is.True, failure.Code.ToString());
            AssertStep(ref machine, NativeLifecycleStepKindV1.CompositeEntered, 0);
            AssertStep(ref machine, NativeLifecycleStepKindV1.ChildSelected, 0);
            AssertStep(ref machine, NativeLifecycleStepKindV1.CompositeEntered, 1);
            AssertStep(ref machine, NativeLifecycleStepKindV1.ChildSelected, 1);
            var enter = AssertStep(ref machine, NativeLifecycleStepKindV1.DispatchRequired, 3, AIBT.Burst.BurstCallbackPhase.Enter);
            Assert.That(machine.TryCompleteDispatch(enter.DispatchToken, AIBT.Burst.BurstContextResult.Success, NodeStatus.Running, out failure), Is.True);
            var tick = AssertStep(ref machine, NativeLifecycleStepKindV1.DispatchRequired, 3, AIBT.Burst.BurstCallbackPhase.Tick);
            Assert.That(machine.TryCompleteDispatch(tick.DispatchToken, AIBT.Burst.BurstContextResult.Success, NodeStatus.Running, out failure), Is.True);
            AssertStep(ref machine, NativeLifecycleStepKindV1.ParallelBranchSuspended, 0);
            AssertStep(ref machine, NativeLifecycleStepKindV1.ChildSelected, 0);
            CompleteLeaf(ref machine, NodeStatus.Success, 2);
            AssertStep(ref machine, NativeLifecycleStepKindV1.ChildAccepted, 0);
            AssertStep(ref machine, NativeLifecycleStepKindV1.Waiting, 0);

            Assert.That(machine.TryBeginUpdate(2, 110, out failure), Is.True, failure.Code.ToString());
            AssertStep(ref machine, NativeLifecycleStepKindV1.ChildSelected, 0);
            var abort = AssertStep(ref machine, NativeLifecycleStepKindV1.DispatchRequired, 3, AIBT.Burst.BurstCallbackPhase.Abort);
            Assert.That(machine.TryCompleteDispatch(abort.DispatchToken, AIBT.Burst.BurstContextResult.Success, NodeStatus.Running, out failure), Is.True);
        }

        [Test]
        public void Parallel_ObserverTransitionFindsReactiveOwnerInsideRetainedBranch()
        {
            using var nodes = new NativeArray<NativeCompiledNodeRecordV1>(new[]
            {
                Node(StableHash.Fnv1A64("aibt.core.parallel"), 0, 16, 0, 8, 4, 0, 1),
                Node(StableHash.Fnv1A64("aibt.core.reactive-sequence"), 0, 0, 8, 4, 4, 1, 2),
                Node(StableHash.Fnv1A64("aibt.tests.guard"), 0, 0, 0, 0, 1, 0, 0),
                Node(StableHash.Fnv1A64("aibt.tests.action"), 0, 0, 0, 0, 1, 0, 0),
            }, Allocator.Persistent);
            using var children = new NativeArray<uint>(new uint[] { 1, 2, 3 }, Allocator.Persistent);
            using var bindings = new NativeArray<NativeLifecycleNodeBindingV1>(new[]
            {
                new NativeLifecycleNodeBindingV1(0, NativeLifecycleNodeKindV1.Parallel),
                new NativeLifecycleNodeBindingV1(1, NativeLifecycleNodeKindV1.ReactiveSequence),
                new NativeLifecycleNodeBindingV1(2, NativeLifecycleNodeKindV1.GeneratedLeaf),
                new NativeLifecycleNodeBindingV1(3, NativeLifecycleNodeKindV1.GeneratedLeaf),
            }, Allocator.Persistent);
            using var memory = new NativeArray<byte>(12, Allocator.Persistent);
            using var frames = new NativeArray<NativeFrameStateV1>(4, Allocator.Persistent);
            using var generations = new NativeArray<uint>(4, Allocator.Persistent);
            using var control = new NativeArray<NativeLifecycleControlV1>(1, Allocator.Persistent);
            using var config = new NativeArray<byte>(16, Allocator.Persistent);
            using var branches = new NativeArray<NativeParallelBranchStateV1>(1, Allocator.Persistent);
            Assert.That(NativeLifecycleMachineV1.TryCreate(nodes, children, bindings, memory, frames, generations,
                control, config, default, branches, out var machine, out var failure), Is.True, failure.Code.ToString());
            Assert.That(machine.TryBeginUpdate(1, out failure), Is.True, failure.Code.ToString());
            AssertStep(ref machine, NativeLifecycleStepKindV1.CompositeEntered, 0);
            AssertStep(ref machine, NativeLifecycleStepKindV1.ChildSelected, 0);
            AssertStep(ref machine, NativeLifecycleStepKindV1.CompositeEntered, 1);
            AssertStep(ref machine, NativeLifecycleStepKindV1.ChildSelected, 1);
            CompleteLeaf(ref machine, NodeStatus.Success, 2);
            AssertStep(ref machine, NativeLifecycleStepKindV1.ChildAccepted, 1);
            AssertStep(ref machine, NativeLifecycleStepKindV1.ChildSelected, 1);
            var enter = AssertStep(ref machine, NativeLifecycleStepKindV1.DispatchRequired, 3, AIBT.Burst.BurstCallbackPhase.Enter);
            Assert.That(machine.TryCompleteDispatch(enter.DispatchToken, AIBT.Burst.BurstContextResult.Success, NodeStatus.Running, out failure), Is.True);
            var tick = AssertStep(ref machine, NativeLifecycleStepKindV1.DispatchRequired, 3, AIBT.Burst.BurstCallbackPhase.Tick);
            Assert.That(machine.TryCompleteDispatch(tick.DispatchToken, AIBT.Burst.BurstContextResult.Success, NodeStatus.Running, out failure), Is.True);
            AssertStep(ref machine, NativeLifecycleStepKindV1.ParallelBranchSuspended, 0);
            AssertStep(ref machine, NativeLifecycleStepKindV1.Waiting, 0);

            Assert.That(machine.TryBeginUpdate(2, out failure), Is.True, failure.Code.ToString());
            Assert.That(machine.TryApplyObserverTransition(new NativeObserverTransitionV1(
                3, 1, CompiledObserverMode.Self, AIBT.Burst.BurstNodeAbortReason.ObserverSelf), out failure),
                Is.True, failure.Code.ToString());
            var abort = AssertStep(ref machine, NativeLifecycleStepKindV1.DispatchRequired, 3, AIBT.Burst.BurstCallbackPhase.Abort);
            Assert.That(machine.TryCompleteDispatch(abort.DispatchToken, AIBT.Burst.BurstContextResult.Success, NodeStatus.Running, out failure), Is.True);
            var exit = AssertStep(ref machine, NativeLifecycleStepKindV1.DispatchRequired, 3, AIBT.Burst.BurstCallbackPhase.Exit);
            Assert.That(machine.TryCompleteDispatch(exit.DispatchToken, AIBT.Burst.BurstContextResult.Success, NodeStatus.Running, out failure), Is.True);
            AssertStep(ref machine, NativeLifecycleStepKindV1.ReactiveReset, 1);
        }

        [Test]
        public void NestedParallel_RetainsAndRestoresEveryNestedFrameWithoutReenter()
        {
            using var nodes = new NativeArray<NativeCompiledNodeRecordV1>(new[]
            {
                Node(StableHash.Fnv1A64("aibt.core.parallel"), 0, 16, 0, 8, 4, 0, 2),
                Node(StableHash.Fnv1A64("aibt.core.parallel"), 16, 16, 8, 8, 4, 2, 1),
                Node(StableHash.Fnv1A64("aibt.tests.running"), 0, 0, 0, 0, 1, 0, 0),
                Node(StableHash.Fnv1A64("aibt.tests.success"), 0, 0, 0, 0, 1, 0, 0),
            }, Allocator.Persistent);
            using var children = new NativeArray<uint>(new uint[] { 1, 3, 2 }, Allocator.Persistent);
            using var bindings = new NativeArray<NativeLifecycleNodeBindingV1>(new[]
            {
                new NativeLifecycleNodeBindingV1(0, NativeLifecycleNodeKindV1.Parallel),
                new NativeLifecycleNodeBindingV1(1, NativeLifecycleNodeKindV1.Parallel),
                new NativeLifecycleNodeBindingV1(2, NativeLifecycleNodeKindV1.GeneratedLeaf),
                new NativeLifecycleNodeBindingV1(3, NativeLifecycleNodeKindV1.GeneratedLeaf),
            }, Allocator.Persistent);
            using var memory = new NativeArray<byte>(16, Allocator.Persistent);
            using var frames = new NativeArray<NativeFrameStateV1>(4, Allocator.Persistent);
            using var generations = new NativeArray<uint>(4, Allocator.Persistent);
            using var control = new NativeArray<NativeLifecycleControlV1>(1, Allocator.Persistent);
            using var config = new NativeArray<byte>(32, Allocator.Persistent);
            using var branches = new NativeArray<NativeParallelBranchStateV1>(3, Allocator.Persistent);
            Assert.That(NativeLifecycleMachineV1.TryCreate(nodes, children, bindings, memory, frames, generations, control,
                config, default, branches, out var machine, out var failure), Is.True, failure.Code.ToString());
            Assert.That(machine.TryBeginUpdate(1, out failure), Is.True);
            AssertStep(ref machine, NativeLifecycleStepKindV1.CompositeEntered, 0);
            AssertStep(ref machine, NativeLifecycleStepKindV1.ChildSelected, 0);
            AssertStep(ref machine, NativeLifecycleStepKindV1.CompositeEntered, 1);
            AssertStep(ref machine, NativeLifecycleStepKindV1.ChildSelected, 1);
            var enter = AssertStep(ref machine, NativeLifecycleStepKindV1.DispatchRequired, 2, AIBT.Burst.BurstCallbackPhase.Enter);
            Assert.That(machine.TryCompleteDispatch(enter.DispatchToken, AIBT.Burst.BurstContextResult.Success, NodeStatus.Running, out failure), Is.True);
            var tick = AssertStep(ref machine, NativeLifecycleStepKindV1.DispatchRequired, 2, AIBT.Burst.BurstCallbackPhase.Tick);
            Assert.That(machine.TryCompleteDispatch(tick.DispatchToken, AIBT.Burst.BurstContextResult.Success, NodeStatus.Running, out failure), Is.True);
            AssertStep(ref machine, NativeLifecycleStepKindV1.ParallelBranchSuspended, 1);
            AssertStep(ref machine, NativeLifecycleStepKindV1.ParallelBranchSuspended, 0);
            AssertStep(ref machine, NativeLifecycleStepKindV1.ChildSelected, 0);
            CompleteLeaf(ref machine, NodeStatus.Success, 3);
            AssertStep(ref machine, NativeLifecycleStepKindV1.ChildAccepted, 0);
            AssertStep(ref machine, NativeLifecycleStepKindV1.Waiting, 0);

            Assert.That(machine.TryBeginUpdate(2, out failure), Is.True);
            AssertStep(ref machine, NativeLifecycleStepKindV1.ChildSelected, 0);
            AssertStep(ref machine, NativeLifecycleStepKindV1.ChildSelected, 1);
            var resumedTick = AssertStep(ref machine, NativeLifecycleStepKindV1.DispatchRequired, 2, AIBT.Burst.BurstCallbackPhase.Tick);
            Assert.That(machine.TryCompleteDispatch(resumedTick.DispatchToken, AIBT.Burst.BurstContextResult.Success, NodeStatus.Success, out failure), Is.True);
            var exit = AssertStep(ref machine, NativeLifecycleStepKindV1.DispatchRequired, 2, AIBT.Burst.BurstCallbackPhase.Exit);
            Assert.That(machine.TryCompleteDispatch(exit.DispatchToken, AIBT.Burst.BurstContextResult.Success, NodeStatus.Success, out failure), Is.True);
            AssertStep(ref machine, NativeLifecycleStepKindV1.ChildAccepted, 1);
            AssertStep(ref machine, NativeLifecycleStepKindV1.ChildAccepted, 1);
            AssertStep(ref machine, NativeLifecycleStepKindV1.CompositeExited, 1);
            AssertStep(ref machine, NativeLifecycleStepKindV1.ChildAccepted, 0);
            AssertStep(ref machine, NativeLifecycleStepKindV1.ChildSelected, 0);
            AssertStep(ref machine, NativeLifecycleStepKindV1.ChildAccepted, 0);
            AssertStep(ref machine, NativeLifecycleStepKindV1.CompositeExited, 0);
            Assert.That(AssertStep(ref machine, NativeLifecycleStepKindV1.Completed, 0).RootStatus, Is.EqualTo(NodeStatus.Success));
            Assert.That(generations.ToArray(), Is.EqualTo(new uint[] { 1, 1, 1, 1 }));
        }

        private static NativeArray<NativeParallelBranchStateV1> Branches(string states)
        {
            var result = new NativeArray<NativeParallelBranchStateV1>(states.Length, Allocator.Persistent);
            for (var index = 0; index < states.Length; index++)
            {
                result[index] = new NativeParallelBranchStateV1
                {
                    CapacityOrdinal = (uint)index,
                    NodeIndex = (uint)index,
                    State = (byte)(states[index] == 'S' ? NativeParallelChildStateV1.Success
                        : states[index] == 'F' ? NativeParallelChildStateV1.Failure
                        : NativeParallelChildStateV1.Running),
                };
            }
            return result;
        }

        private static void WriteU32(NativeArray<byte> bytes, int offset, uint value)
        { for (var index = 0; index < 4; index++) bytes[offset + index] = (byte)(value >> (index * 8)); }
        private static void WriteU64(NativeArray<byte> bytes, int offset, ulong value)
        { for (var index = 0; index < 8; index++) bytes[offset + index] = (byte)(value >> (index * 8)); }

        private static NativeCompiledNodeRecordV1 Node(
            ulong typeId, uint configOffset, uint configSize, uint memoryOffset, uint memorySize, uint memoryAlignment,
            uint childOffset, uint childCount, NodeMemoryLifetime memoryLifetime = NodeMemoryLifetime.Activation)
            => new NativeCompiledNodeRecordV1(new CompiledNodeRecord(
                typeId, 1, configOffset, configSize, configSize == 0 ? 1u : 4u,
                memoryOffset, memorySize, memoryAlignment,
                memoryLifetime,
                new CompiledRange(childOffset, childCount), CompiledNodeFlags.BurstDomain, 0,
                new CompiledRange(0, 0), new CompiledRange(0, 0)));

        private static NativeLifecycleStepResultV1 AssertStep(
            ref NativeLifecycleMachineV1 machine, NativeLifecycleStepKindV1 expected, uint node,
            AIBT.Burst.BurstCallbackPhase phase = default)
        {
            Assert.That(machine.TryAdvance(out var step, out var failure), Is.True, failure.Code.ToString());
            Assert.That(step.Kind, Is.EqualTo(expected));
            Assert.That(step.NodeIndex, Is.EqualTo(node));
            if (expected == NativeLifecycleStepKindV1.DispatchRequired) Assert.That(step.Phase, Is.EqualTo(phase));
            return step;
        }

        private static void CompleteLeaf(ref NativeLifecycleMachineV1 machine, NodeStatus status, uint node = 1)
        {
            var enter = AssertStep(ref machine, NativeLifecycleStepKindV1.DispatchRequired, node, AIBT.Burst.BurstCallbackPhase.Enter);
            Assert.That(machine.TryCompleteDispatch(enter.DispatchToken, AIBT.Burst.BurstContextResult.Success, NodeStatus.Running, out var failure), Is.True, failure.Code.ToString());
            var tick = AssertStep(ref machine, NativeLifecycleStepKindV1.DispatchRequired, node, AIBT.Burst.BurstCallbackPhase.Tick);
            Assert.That(machine.TryCompleteDispatch(tick.DispatchToken, AIBT.Burst.BurstContextResult.Success, status, out failure), Is.True, failure.Code.ToString());
            var exit = AssertStep(ref machine, NativeLifecycleStepKindV1.DispatchRequired, node, AIBT.Burst.BurstCallbackPhase.Exit);
            Assert.That(machine.TryCompleteDispatch(exit.DispatchToken, AIBT.Burst.BurstContextResult.Success, status, out failure), Is.True, failure.Code.ToString());
        }
    }
}
