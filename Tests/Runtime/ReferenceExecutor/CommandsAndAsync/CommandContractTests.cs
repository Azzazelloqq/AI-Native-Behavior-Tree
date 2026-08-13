using System;
using NUnit.Framework;

namespace AIBT.Tests.Runtime
{
    public sealed class CommandContractTests
    {
        [Test]
        public void CommandBatch_CopiesInputsAndValidatesPayloadRanges()
        {
            var type = new CommandType(10, 1);
            var records = new[] { new CommandRecord(type, default, 0, 2, CommandPhase.Execute, new TreeInstanceId(1), 1) };
            var payload = new byte[] { 4, 5 };
            var batch = new CommandBatch(records, payload);

            payload[0] = 99;
            records[0] = default;

            Assert.That(batch.Records[0].CommandType, Is.EqualTo(type));
            Assert.That(batch.GetPayloadByte(0), Is.EqualTo(4));
            Assert.Throws<ArgumentException>(() => new CommandBatch(
                new[] { new CommandRecord(type, default, 1, 2, CommandPhase.Execute, new TreeInstanceId(1), 1) },
                new byte[2]));
        }

        [Test]
        public void Merger_OrdersByPhaseThenTreeThenSequenceAndRewritesPayloadOffsets()
        {
            var type = new CommandType(10, 1);
            var tree1 = new TreeInstanceId(1);
            var tree2 = new TreeInstanceId(2);
            var cancel = new CommandBatch(
                new[] { new CommandRecord(type, default, 0, 1, CommandPhase.Cancel, tree1, 2) },
                new byte[] { 30 });
            var execute2 = new CommandBatch(
                new[] { new CommandRecord(type, default, 0, 1, CommandPhase.Execute, tree2, 1) },
                new byte[] { 20 });
            var execute1 = new CommandBatch(
                new[] { new CommandRecord(type, default, 0, 1, CommandPhase.Execute, tree1, 1) },
                new byte[] { 10 });

            var merged = CommandBatchMerger.Merge(new[] { cancel, execute2, execute1 });

            Assert.That(merged.Records.Count, Is.EqualTo(3));
            Assert.That(merged.Records[0].TreeInstanceId, Is.EqualTo(tree1));
            Assert.That(merged.Records[1].TreeInstanceId, Is.EqualTo(tree2));
            Assert.That(merged.Records[2].Phase, Is.EqualTo(CommandPhase.Cancel));
            CollectionAssert.AreEqual(new byte[] { 10 }, merged.GetPayload(merged.Records[0]).ToArray());
            CollectionAssert.AreEqual(new byte[] { 20 }, merged.GetPayload(merged.Records[1]).ToArray());
            CollectionAssert.AreEqual(new byte[] { 30 }, merged.GetPayload(merged.Records[2]).ToArray());
        }

        [Test]
        public void Merger_RejectsPerInstanceSequenceCollisionAcrossBatches()
        {
            var type = new CommandType(10, 1);
            var tree = new TreeInstanceId(1);
            var otherTree = new TreeInstanceId(2);
            var first = new CommandBatch(
                new[] { new CommandRecord(type, default, 0, 0, CommandPhase.Execute, tree, 1) },
                Array.Empty<byte>());
            var intervening = new CommandBatch(
                new[] { new CommandRecord(type, default, 0, 0, CommandPhase.Execute, otherTree, 1) },
                Array.Empty<byte>());
            var second = new CommandBatch(
                new[] { new CommandRecord(type, default, 0, 0, CommandPhase.Cancel, tree, 1) },
                Array.Empty<byte>());

            Assert.Throws<ArgumentException>(() => CommandBatchMerger.Merge(new[] { first, intervening, second }));
        }

        [Test]
        public void ReferenceBuffer_IssuesMaxSequenceOnceAndNeverWrapsOrResetsOnTake()
        {
            var tree = new TreeInstanceId(7);
            var buffer = new ReferenceCommandBuffer(tree, ulong.MaxValue);

            Assert.That(buffer.TryAppend(
                new CommandType(10, 1),
                default,
                CommandPhase.Execute,
                new byte[] { 1 },
                out var issued,
                out var firstDiagnostic), Is.True);
            Assert.That(firstDiagnostic, Is.Null);
            Assert.That(issued.Sequence, Is.EqualTo(ulong.MaxValue));
            Assert.That(buffer.TakeBatch().Records.Count, Is.EqualTo(1));

            Assert.That(buffer.TryAppend(
                new CommandType(10, 1),
                default,
                CommandPhase.Execute,
                ReadOnlySpan<byte>.Empty,
                out _,
                out var overflow), Is.False);
            Assert.That(overflow.Code, Is.EqualTo(CommandAsyncDiagnosticCodes.CommandSequenceOverflow));
        }

        [Test]
        public void ReferenceBuffer_RejectsOperationFromAnotherTreeWithoutAppending()
        {
            var buffer = new ReferenceCommandBuffer(new TreeInstanceId(7));
            var foreignOperation = new OperationId(new TreeInstanceId(8), new RuntimeNodeIndex(0), 1, 1);

            Assert.That(buffer.TryAppend(
                new CommandType(10, 1),
                foreignOperation,
                CommandPhase.Cancel,
                ReadOnlySpan<byte>.Empty,
                out _,
                out var diagnostic), Is.False);
            Assert.That(diagnostic.Code, Is.EqualTo(CommandAsyncDiagnosticCodes.InvalidCommand));
            Assert.That(buffer.TakeBatch().Records, Is.Empty);
        }
    }
}
