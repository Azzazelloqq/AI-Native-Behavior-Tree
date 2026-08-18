using NUnit.Framework;
using AIBT.Burst;
using Unity.Collections;

namespace AIBT.Tests.Runtime.NativeExecution.Observers
{
    public sealed class NativeObserverQueueTests
    {
        [Test]
        public void ChangedSlotsDeduplicateAndDrainByObserverNodeIndex()
        {
            using var observers = new NativeArray<NativeCompiledObserverRecordV1>(new[]
            {
                Observer(2, 0, CompiledObserverMode.Self, 0, 2),
                Observer(7, 0, CompiledObserverMode.Both, 2, 1),
            }, Allocator.Persistent);
            using var watched = new NativeArray<uint>(new uint[] { 0, 1, 0 }, Allocator.Persistent);
            Assert.That(NativeObserverQueueV1.TryCreate(observers, watched, 2, Allocator.Persistent, out var queue), Is.True);
            try
            {
                Assert.That(queue.TryEnqueueChangedSlot(0), Is.True);
                Assert.That(queue.TryEnqueueChangedSlot(0), Is.True);
                Assert.That(queue.TryEnqueueChangedSlot(1), Is.True);
                Assert.That(queue.Count, Is.EqualTo(2));
                Assert.That(queue.TryDequeue(out var first), Is.True);
                Assert.That(first, Is.Zero);
                Assert.That(queue.TryDequeue(out var second), Is.True);
                Assert.That(second, Is.EqualTo(1));
                Assert.That(queue.TryDequeue(out _), Is.False);
            }
            finally { queue.Dispose(); }
        }

        [TestCase(CompiledObserverMode.LowerPriority, BurstNodeAbortReason.ObserverLowerPriority)]
        [TestCase(CompiledObserverMode.Both, BurstNodeAbortReason.ObserverLowerPriority)]
        public void EvaluationSeedsThenOnlyChangedResultsPublishTransition(
            CompiledObserverMode mode,
            BurstNodeAbortReason expectedReason)
        {
            using var observers = new NativeArray<NativeCompiledObserverRecordV1>(new[] { Observer(3, 1, mode, 0, 1) }, Allocator.Persistent);
            using var watched = new NativeArray<uint>(new uint[] { 0 }, Allocator.Persistent);
            Assert.That(NativeObserverQueueV1.TryCreate(observers, watched, 1, Allocator.Persistent, out var queue), Is.True);
            try
            {
                Assert.That(queue.TryAcceptEvaluation(0, ConditionResult.Failure, out var changed, out _), Is.True);
                Assert.That(changed, Is.False);
                Assert.That(queue.TryAcceptEvaluation(0, ConditionResult.Failure, out changed, out _), Is.True);
                Assert.That(changed, Is.False);
                Assert.That(queue.TryAcceptEvaluation(0, ConditionResult.Success, out changed, out var transition), Is.True);
                Assert.That(changed, Is.True);
                Assert.That(transition.ObserverNodeIndex, Is.EqualTo(3));
                Assert.That(transition.OwnerNodeIndex, Is.EqualTo(1));
                Assert.That(transition.Mode, Is.EqualTo(mode));
                Assert.That(transition.Reason, Is.EqualTo(expectedReason));
            }
            finally { queue.Dispose(); }
        }

        [TestCase(CompiledObserverMode.Self, true)]
        [TestCase(CompiledObserverMode.LowerPriority, false)]
        [TestCase(CompiledObserverMode.Both, true)]
        public void SuccessToFailureTriggersOnlySelfDirections(CompiledObserverMode mode, bool expected)
        {
            using var observers = new NativeArray<NativeCompiledObserverRecordV1>(new[] { Observer(3, 1, mode, 0, 1) }, Allocator.Persistent);
            using var watched = new NativeArray<uint>(new uint[] { 0 }, Allocator.Persistent);
            Assert.That(NativeObserverQueueV1.TryCreate(observers, watched, 1, Allocator.Persistent, out var queue), Is.True);
            try
            {
                Assert.That(queue.TryAcceptEvaluation(0, ConditionResult.Success, out _, out _), Is.True);
                Assert.That(queue.TryAcceptEvaluation(0, ConditionResult.Failure, out var changed, out var transition), Is.True);
                Assert.That(changed, Is.EqualTo(expected));
                if (expected) Assert.That(transition.Reason, Is.EqualTo(BurstNodeAbortReason.ObserverSelf));
            }
            finally { queue.Dispose(); }
        }

        private static NativeCompiledObserverRecordV1 Observer(
            uint observer,
            uint owner,
            CompiledObserverMode mode,
            uint offset,
            uint count)
            => new NativeCompiledObserverRecordV1(new CompiledObserverRecord(
                observer, owner, mode, new CompiledRange(offset, count)));
    }
}
