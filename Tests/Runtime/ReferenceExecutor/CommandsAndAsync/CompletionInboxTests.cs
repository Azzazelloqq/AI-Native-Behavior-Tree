using System;
using NUnit.Framework;

namespace AIBT.Tests.Runtime
{
    public sealed class CompletionInboxTests
    {
        [Test]
        public void Normalize_PersistsUntilConsumedAndUsesDeterministicSourceOrdering()
        {
            var fixture = new Fixture();
            var operation = fixture.Allocate();
            var laterKey = Record(operation, CompletionOutcome.Failed, sourceId: 2, sourceSequence: 1);
            var earlierKey = Record(operation, CompletionOutcome.Succeeded, sourceId: 1, sourceSequence: 9);

            var normalized = fixture.Inbox.Normalize(
                new CompletionBatch(new[] { laterKey, earlierKey }, Array.Empty<byte>()),
                fixture.Generations);
            Assert.That(normalized, Is.Empty);
            Assert.That(fixture.Inbox.PendingCount, Is.EqualTo(2));

            Assert.That(fixture.Inbox.Normalize(CompletionBatch.Empty, fixture.Generations), Is.Empty);
            Assert.That(fixture.Inbox.TryConsume(operation, out var consumed, out var consumeDiagnostics), Is.True);
            Assert.That(consumed.Record.Outcome, Is.EqualTo(CompletionOutcome.Succeeded));
            Assert.That(consumeDiagnostics.Count, Is.EqualTo(1));
            Assert.That(consumeDiagnostics[0].Code, Is.EqualTo(CommandAsyncDiagnosticCodes.AlreadyConsumedOperation));
            Assert.That(fixture.Inbox.PendingCount, Is.Zero);
        }

        [Test]
        public void Normalize_DuplicateOrderingKeyDiscardsWholeGroupWithoutAdvancingHighWater()
        {
            var fixture = new Fixture();
            var operation = fixture.Allocate();
            var first = Record(operation, CompletionOutcome.Succeeded, 4, 5);
            var second = Record(operation, CompletionOutcome.Failed, 4, 5);

            var duplicate = fixture.Inbox.Normalize(
                new CompletionBatch(new[] { first, second }, Array.Empty<byte>()),
                fixture.Generations);
            Assert.That(duplicate.Count, Is.EqualTo(1));
            Assert.That(duplicate[0].Code, Is.EqualTo(CommandAsyncDiagnosticCodes.DuplicateCompletionOrderingKey));
            Assert.That(fixture.Inbox.PendingCount, Is.Zero);

            Assert.That(fixture.Inbox.Normalize(
                new CompletionBatch(new[] { first }, Array.Empty<byte>()),
                fixture.Generations), Is.Empty);
            Assert.That(fixture.Inbox.PendingCount, Is.EqualTo(1));
        }

        [Test]
        public void Normalize_TracksPerSourceHighWaterAcrossUpdatesAndAllowsGaps()
        {
            var fixture = new Fixture();
            var firstOperation = fixture.Allocate();
            var secondOperation = fixture.Allocate();
            var thirdOperation = fixture.Allocate();

            Assert.That(fixture.Inbox.Normalize(
                Batch(Record(firstOperation, CompletionOutcome.Succeeded, 1, 10)),
                fixture.Generations), Is.Empty);
            var backwards = fixture.Inbox.Normalize(
                Batch(Record(secondOperation, CompletionOutcome.Succeeded, 1, 9)),
                fixture.Generations);
            Assert.That(backwards.Count, Is.EqualTo(1));
            Assert.That(backwards[0].Code, Is.EqualTo(CommandAsyncDiagnosticCodes.NonIncreasingSourceSequence));

            Assert.That(fixture.Inbox.Normalize(
                Batch(Record(thirdOperation, CompletionOutcome.Succeeded, 1, 100)),
                fixture.Generations), Is.Empty);
            Assert.That(fixture.Inbox.PendingCount, Is.EqualTo(2));
        }

        [Test]
        public void Normalize_ClassifiesUnknownCancelledConsumedAndStaleWithoutReactivation()
        {
            var fixture = new Fixture();
            var cancelled = fixture.Allocate();
            var consumed = fixture.Allocate();
            var stale = fixture.Allocate();
            fixture.Ledger.MarkCancelled(cancelled);
            fixture.Ledger.MarkConsumed(consumed);
            fixture.Generations[0] = 2;
            var unknown = new OperationId(fixture.Tree, new RuntimeNodeIndex(0), 2, 999);

            var diagnostics = fixture.Inbox.Normalize(
                new CompletionBatch(new[]
                {
                    Record(unknown, CompletionOutcome.Succeeded, 1, 1),
                    Record(cancelled, CompletionOutcome.Succeeded, 2, 1),
                    Record(consumed, CompletionOutcome.Succeeded, 3, 1),
                    Record(stale, CompletionOutcome.Succeeded, 4, 1),
                }, Array.Empty<byte>()),
                fixture.Generations);

            Assert.That(Contains(diagnostics, CommandAsyncDiagnosticCodes.UnknownOperation), Is.True);
            Assert.That(Contains(diagnostics, CommandAsyncDiagnosticCodes.CancelledOperation), Is.True);
            Assert.That(Contains(diagnostics, CommandAsyncDiagnosticCodes.AlreadyConsumedOperation), Is.True);
            Assert.That(Contains(diagnostics, CommandAsyncDiagnosticCodes.StaleOperationGeneration), Is.True);
            Assert.That(fixture.Inbox.PendingCount, Is.Zero);
            Assert.That(fixture.Ledger.TryGetState(stale, out var staleState), Is.True);
            Assert.That(staleState, Is.EqualTo(ReferenceOperationState.Cancelled));
        }

        [Test]
        public void Consume_SkipsInvalidTypedPayloadAndConsumesFirstValidRecordExactlyOnce()
        {
            var fixture = new Fixture();
            var operation = fixture.Allocate();
            var wrongType = new CompletionPayloadType(100, 1);
            var expectedType = new CompletionPayloadType(200, 2);
            var records = new[]
            {
                Record(operation, CompletionOutcome.Failed, 1, 1, wrongType, 0, 4),
                Record(operation, CompletionOutcome.Succeeded, 2, 1, expectedType, 4, 4),
            };
            fixture.Inbox.Normalize(new CompletionBatch(records, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }), fixture.Generations);

            Assert.That(fixture.Inbox.TryConsume(
                operation,
                ReferenceCompletionExpectation.Typed(expectedType, 4),
                out var completion,
                out var diagnostics), Is.True);
            Assert.That(completion.Record.Outcome, Is.EqualTo(CompletionOutcome.Succeeded));
            CollectionAssert.AreEqual(new byte[] { 5, 6, 7, 8 }, completion.Payload.ToArray());
            Assert.That(diagnostics.Count, Is.EqualTo(1));
            Assert.That(diagnostics[0].Code, Is.EqualTo(CommandAsyncDiagnosticCodes.CompletionPayloadMismatch));

            Assert.That(fixture.Inbox.TryConsume(operation, out _, out var duplicate), Is.False);
            Assert.That(duplicate[0].Code, Is.EqualTo(CommandAsyncDiagnosticCodes.AlreadyConsumedOperation));
        }

        [TestCase(CompletionOutcome.Succeeded)]
        [TestCase(CompletionOutcome.Failed)]
        [TestCase(CompletionOutcome.Cancelled)]
        public void Consume_PreservesTerminalOutcomeAndSnapshotProvenance(CompletionOutcome outcome)
        {
            var fixture = new Fixture();
            var operation = fixture.Allocate();
            var record = Record(operation, outcome, 1, 1, snapshotRevision: default);
            fixture.Inbox.Normalize(Batch(record), fixture.Generations);

            Assert.That(fixture.Inbox.TryConsume(
                operation,
                ReferenceCompletionExpectation.NoPayload,
                out var completion,
                out var diagnostics), Is.True);
            Assert.That(diagnostics, Is.Empty);
            Assert.That(completion.Record.Outcome, Is.EqualTo(outcome));
            Assert.That(completion.Record.SnapshotRevision.IsValid, Is.False);
        }

        [Test]
        public void Cancellation_DiscardsPersistentPendingCompletionAndIsIdempotent()
        {
            var fixture = new Fixture();
            var operation = fixture.Allocate();
            fixture.Inbox.Normalize(Batch(Record(operation, CompletionOutcome.Succeeded, 1, 1)), fixture.Generations);

            Assert.That(fixture.Ledger.MarkCancelled(operation), Is.EqualTo(ReferenceOperationTransition.Applied));
            var discarded = fixture.Inbox.DiscardCancelled(operation);
            Assert.That(discarded.Count, Is.EqualTo(1));
            Assert.That(discarded[0].Code, Is.EqualTo(CommandAsyncDiagnosticCodes.CancelledOperation));
            Assert.That(fixture.Ledger.MarkCancelled(operation), Is.EqualTo(ReferenceOperationTransition.AlreadyApplied));
            Assert.That(fixture.Inbox.DiscardCancelled(operation), Is.Empty);
        }

        [Test]
        public void CompletionBatch_CopiesPayloadAndRejectsInvalidRanges()
        {
            var fixture = new Fixture();
            var operation = fixture.Allocate();
            var type = new CompletionPayloadType(1, 1);
            var record = Record(operation, CompletionOutcome.Succeeded, 1, 1, type, 0, 2);
            var payload = new byte[] { 7, 8 };
            var batch = new CompletionBatch(new[] { record }, payload);
            payload[0] = 99;

            Assert.That(batch.GetPayloadByte(0), Is.EqualTo(7));
            Assert.Throws<ArgumentException>(() => new CompletionBatch(
                new[] { Record(operation, CompletionOutcome.Succeeded, 1, 1, type, 1, 2) },
                new byte[2]));
        }

        private static CompletionBatch Batch(CompletionRecord record)
        {
            return new CompletionBatch(new[] { record }, Array.Empty<byte>());
        }

        private static CompletionRecord Record(
            OperationId operation,
            CompletionOutcome outcome,
            ulong sourceId,
            ulong sourceSequence,
            CompletionPayloadType payloadType = default,
            uint payloadOffset = 0,
            uint payloadSize = 0,
            Revision snapshotRevision = default)
        {
            return new CompletionRecord(
                operation,
                outcome,
                payloadType,
                payloadOffset,
                payloadSize,
                sourceId,
                sourceSequence,
                snapshotRevision);
        }

        private static bool Contains(DiagnosticCollection diagnostics, DiagnosticCode code)
        {
            for (var index = 0; index < diagnostics.Count; index++)
            {
                if (diagnostics[index].Code == code) return true;
            }

            return false;
        }

        private sealed class Fixture
        {
            internal Fixture()
            {
                Ledger = new ReferenceOperationLedger(Tree);
                Inbox = new ReferenceCompletionInbox(Ledger);
            }

            internal TreeInstanceId Tree { get; } = new TreeInstanceId(7);
            internal uint[] Generations { get; } = { 1 };
            internal ReferenceOperationLedger Ledger { get; }
            internal ReferenceCompletionInbox Inbox { get; }

            internal OperationId Allocate()
            {
                Assert.That(Ledger.TryAllocate(new RuntimeNodeIndex(0), Generations[0], out var operation, out _), Is.True);
                return operation;
            }
        }
    }
}
