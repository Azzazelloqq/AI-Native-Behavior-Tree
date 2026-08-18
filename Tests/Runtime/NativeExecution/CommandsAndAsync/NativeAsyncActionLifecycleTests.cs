using AIBT.Burst;
using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;

namespace AIBT.Tests.Runtime.NativeExecution.CommandsAndAsync
{
    public sealed class NativeAsyncActionLifecycleTests
    {
        [Test]
        public void StartAndAbortAreEmittedAtMostOncePerActivation()
        {
            var capacity = new NativeCommandAsyncCapacityV1(2, 0, 2, 2, 0, 2, 2, 2, 2, 0);
            Assert.That(NativeCommandAsyncOwnerV1.TryCreate(
                new TreeInstanceId(41), capacity, Allocator.Persistent, out var owner), Is.EqualTo(BurstContextResult.Success));
            try
            {
                Assert.That(owner.TryAcquireExecution(out var lease), Is.EqualTo(BurstContextResult.Success));
                var view = lease.View;
                var state = default(NativeAsyncActionStateV1);
                Assert.That(NativeAsyncActionLifecycleV1.TryBeginActivation(ref state, 7), Is.EqualTo(BurstContextResult.Success));
                var start = new CommandType(0x100, 1);
                var cancel = new CommandType(0x101, 1);
                Assert.That(NativeAsyncActionLifecycleV1.TryStartOnce(
                    ref state, ref view, new RuntimeNodeIndex(3), start, cancel,
                    NativePayloadSliceV1.Empty, NativePayloadSliceV1.Empty, out var first), Is.EqualTo(BurstContextResult.Success));
                Assert.That(NativeAsyncActionLifecycleV1.TryStartOnce(
                    ref state, ref view, new RuntimeNodeIndex(3), start, cancel,
                    NativePayloadSliceV1.Empty, NativePayloadSliceV1.Empty, out var second), Is.EqualTo(BurstContextResult.Success));
                Assert.That(second, Is.EqualTo(first));
                Assert.That(NativeAsyncActionLifecycleV1.TryAbortOnce(
                    ref state, ref view, cancel, NativePayloadSliceV1.Empty, out var emitted), Is.EqualTo(BurstContextResult.Success));
                Assert.That(emitted, Is.True);
                Assert.That(NativeAsyncActionLifecycleV1.TryAbortOnce(
                    ref state, ref view, cancel, NativePayloadSliceV1.Empty, out emitted), Is.EqualTo(BurstContextResult.Success));
                Assert.That(emitted, Is.False);
                Assert.That(owner.TryGetOperationState(first, out var operationState), Is.EqualTo(BurstContextResult.Success));
                Assert.That(operationState, Is.EqualTo(NativeOperationStateV1.Cancelled));
                Assert.That(owner.TryRegisterDependency(lease, default(JobHandle)), Is.EqualTo(BurstContextResult.Success));
                Assert.That(owner.TryGetCommandStream(lease, out var stream), Is.EqualTo(BurstContextResult.Success));
                Assert.That(stream.ExecuteCount, Is.EqualTo(1));
                Assert.That(stream.CancelCount, Is.EqualTo(1));
                Assert.That(owner.TryRelease(lease), Is.EqualTo(BurstContextResult.Success));
            }
            finally
            {
                Assert.That(owner.TryDispose(), Is.EqualTo(BurstContextResult.Success));
            }
        }

        [TestCase(CompletionOutcome.Succeeded, NodeStatus.Success)]
        [TestCase(CompletionOutcome.Failed, NodeStatus.Failure)]
        [TestCase(CompletionOutcome.Cancelled, NodeStatus.Failure)]
        public void FirstValidTerminalCompletionWinsAbortAndMapsOutcome(
            CompletionOutcome outcome,
            NodeStatus expected)
        {
            var capacity = new NativeCommandAsyncCapacityV1(2, 0, 2, 2, 0, 2, 2, 2, 2, 0);
            Assert.That(NativeCommandAsyncOwnerV1.TryCreate(
                new TreeInstanceId(42), capacity, Allocator.Persistent, out var owner), Is.EqualTo(BurstContextResult.Success));
            try
            {
                Assert.That(owner.TryAcquireExecution(out var lease), Is.EqualTo(BurstContextResult.Success));
                var view = lease.View;
                var state = default(NativeAsyncActionStateV1);
                Assert.That(NativeAsyncActionLifecycleV1.TryBeginActivation(ref state, 3), Is.EqualTo(BurstContextResult.Success));
                var start = new CommandType(0x200, 1);
                var cancel = new CommandType(0x201, 1);
                Assert.That(NativeAsyncActionLifecycleV1.TryStartOnce(
                    ref state, ref view, new RuntimeNodeIndex(1), start, cancel,
                    NativePayloadSliceV1.Empty, NativePayloadSliceV1.Empty, out var operation), Is.EqualTo(BurstContextResult.Success));
                using var input = new NativeArray<NativeCompletionInputRecordV1>(new[]
                {
                    new NativeCompletionInputRecordV1(operation, outcome, default, 0, 0, 7, 1, default)
                }, Allocator.Persistent);
                using var generations = new NativeArray<uint>(new uint[] { 0, 3 }, Allocator.Persistent);
                Assert.That(view.TryNormalizeCompletions(input.AsReadOnly(), default, generations.AsReadOnly()),
                    Is.EqualTo(BurstContextResult.Success));
                Assert.That(NativeAsyncActionLifecycleV1.TryPoll(
                    ref state, ref view, new RuntimeNodeIndex(1), NativeCompletionExpectationV1.NoPayload,
                    out var terminal, out var status, out var completion), Is.EqualTo(BurstContextResult.Success));
                Assert.That(terminal, Is.True);
                Assert.That(status, Is.EqualTo(expected));
                Assert.That(completion.Outcome, Is.EqualTo(outcome));
                Assert.That(NativeAsyncActionLifecycleV1.TryAbortOnce(
                    ref state, ref view, cancel, NativePayloadSliceV1.Empty, out var emitted), Is.EqualTo(BurstContextResult.Success));
                Assert.That(emitted, Is.False, "A terminal completion wins over a later abort.");
                Assert.That(owner.TryRegisterDependency(lease, default(JobHandle)), Is.EqualTo(BurstContextResult.Success));
                Assert.That(owner.TryRelease(lease), Is.EqualTo(BurstContextResult.Success));
            }
            finally
            {
                Assert.That(owner.TryDispose(), Is.EqualTo(BurstContextResult.Success));
            }
        }
    }
}
