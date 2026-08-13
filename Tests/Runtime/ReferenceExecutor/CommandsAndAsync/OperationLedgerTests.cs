using System;
using NUnit.Framework;

namespace AIBT.Tests.Runtime
{
    public sealed class OperationLedgerTests
    {
        [Test]
        public void Allocation_BindsIdentityAndRetainsTerminalTombstones()
        {
            var ledger = new ReferenceOperationLedger(new TreeInstanceId(9));

            Assert.That(ledger.TryAllocate(new RuntimeNodeIndex(3), 4, out var first, out var diagnostic), Is.True);
            Assert.That(diagnostic, Is.Null);
            Assert.That(first, Is.EqualTo(new OperationId(new TreeInstanceId(9), new RuntimeNodeIndex(3), 4, 1)));
            Assert.That(ledger.ActiveCount, Is.EqualTo(1));
            Assert.That(ledger.MarkConsumed(first), Is.EqualTo(ReferenceOperationTransition.Applied));
            Assert.That(ledger.ActiveCount, Is.Zero);
            Assert.That(ledger.MarkConsumed(first), Is.EqualTo(ReferenceOperationTransition.AlreadyApplied));
            Assert.That(ledger.MarkCancelled(first), Is.EqualTo(ReferenceOperationTransition.InvalidState));
            Assert.That(ledger.TryGetState(first, out var state), Is.True);
            Assert.That(state, Is.EqualTo(ReferenceOperationState.Consumed));

            Assert.That(ledger.TryAllocate(new RuntimeNodeIndex(3), 5, out var second, out _), Is.True);
            Assert.That(second.Sequence, Is.EqualTo(2));
            Assert.That(ledger.ActiveCount, Is.EqualTo(1));
        }

        [Test]
        public void Cancellation_IsIdempotentAndCancelAllLeavesTombstones()
        {
            var ledger = new ReferenceOperationLedger(new TreeInstanceId(9));
            ledger.TryAllocate(new RuntimeNodeIndex(0), 1, out var first, out _);
            ledger.TryAllocate(new RuntimeNodeIndex(1), 1, out var second, out _);
            Assert.That(ledger.ActiveCount, Is.EqualTo(2));

            Assert.That(ledger.MarkCancelled(first), Is.EqualTo(ReferenceOperationTransition.Applied));
            Assert.That(ledger.ActiveCount, Is.EqualTo(1));
            Assert.That(ledger.MarkCancelled(first), Is.EqualTo(ReferenceOperationTransition.AlreadyApplied));
            ledger.CancelAllActive();

            Assert.That(ledger.TryGetState(first, out var firstState), Is.True);
            Assert.That(ledger.TryGetState(second, out var secondState), Is.True);
            Assert.That(firstState, Is.EqualTo(ReferenceOperationState.Cancelled));
            Assert.That(secondState, Is.EqualTo(ReferenceOperationState.Cancelled));
            Assert.That(ledger.Count, Is.EqualTo(2));
            Assert.That(ledger.ActiveCount, Is.Zero);
        }

        [Test]
        public void Allocation_IssuesMaxSequenceOnceThenReportsStructuredOverflow()
        {
            var ledger = new ReferenceOperationLedger(new TreeInstanceId(9), ulong.MaxValue);

            Assert.That(ledger.TryAllocate(new RuntimeNodeIndex(0), 1, out var issued, out _), Is.True);
            Assert.That(issued.Sequence, Is.EqualTo(ulong.MaxValue));
            Assert.That(ledger.TryAllocate(new RuntimeNodeIndex(0), 2, out _, out var overflow), Is.False);
            Assert.That(overflow.Code, Is.EqualTo(CommandAsyncDiagnosticCodes.OperationSequenceOverflow));
            Assert.That(ledger.Count, Is.EqualTo(1));
        }

        [Test]
        public void Coordinator_EmitsStartThenCancelOnceWithTheSameOperation()
        {
            var tree = new TreeInstanceId(9);
            var ledger = new ReferenceOperationLedger(tree);
            var commands = new ReferenceCommandBuffer(tree);
            var coordinator = new ReferenceAsyncCommandCoordinator(ledger, commands);
            var contract = new ReferenceAsyncCommandContract(new CommandType(10, 1), new CommandType(11, 1));

            Assert.That(coordinator.TryStart(
                new RuntimeNodeIndex(2),
                3,
                contract,
                new byte[] { 1 },
                out var operation,
                out _), Is.True);
            Assert.That(coordinator.TryCancel(
                operation,
                contract,
                new byte[] { 2 },
                out var emitted,
                out _), Is.True);
            Assert.That(emitted, Is.True);
            Assert.That(coordinator.TryCancel(
                operation,
                contract,
                ReadOnlySpan<byte>.Empty,
                out var emittedAgain,
                out _), Is.True);
            Assert.That(emittedAgain, Is.False);

            var batch = commands.TakeBatch();
            Assert.That(batch.Records.Count, Is.EqualTo(2));
            Assert.That(batch.Records[0].Phase, Is.EqualTo(CommandPhase.Execute));
            Assert.That(batch.Records[1].Phase, Is.EqualTo(CommandPhase.Cancel));
            Assert.That(batch.Records[0].OperationId, Is.EqualTo(operation));
            Assert.That(batch.Records[1].OperationId, Is.EqualTo(operation));
            Assert.That(batch.Records[0].Sequence, Is.LessThan(batch.Records[1].Sequence));
        }

        [Test]
        public void Coordinator_CommitsCancelledStateBeforeCancellationAppendFailure()
        {
            var tree = new TreeInstanceId(9);
            var ledger = new ReferenceOperationLedger(tree);
            var commands = new ReferenceCommandBuffer(tree, ulong.MaxValue);
            var coordinator = new ReferenceAsyncCommandCoordinator(ledger, commands);
            var contract = new ReferenceAsyncCommandContract(new CommandType(10, 1), new CommandType(11, 1));
            Assert.That(coordinator.TryStart(
                new RuntimeNodeIndex(2),
                3,
                contract,
                ReadOnlySpan<byte>.Empty,
                out var operation,
                out _), Is.True);

            Assert.That(coordinator.TryCancel(
                operation,
                contract,
                ReadOnlySpan<byte>.Empty,
                out var emitted,
                out var diagnostic), Is.False);
            Assert.That(emitted, Is.False);
            Assert.That(diagnostic.Code, Is.EqualTo(CommandAsyncDiagnosticCodes.CommandSequenceOverflow));
            Assert.That(ledger.TryGetState(operation, out var state), Is.True);
            Assert.That(state, Is.EqualTo(ReferenceOperationState.Cancelled));
        }
    }
}
