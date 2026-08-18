using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine.TestTools.Constraints;
using Unity.Jobs;
using GcAllocIs = UnityEngine.TestTools.Constraints.Is;
using Is = NUnit.Framework.Is;

namespace AIBT.Tests.Runtime
{
    public sealed class NativeDiagnosticsAndTraceTests
    {
        [Test]
        public void Diagnostic_ProjectsUnknownCodeAndExactDebugIdentityWithoutThrowing()
        {
            var record = DiagnosticRecord(7777, 3, 2);
            record.PrimaryLocation = new NativeDiagnosticLocationV1(
                NativeDiagnosticLocationFlagsV1.TreeInstance
                    | NativeDiagnosticLocationFlagsV1.RuntimeNode
                    | NativeDiagnosticLocationFlagsV1.DebugIdentity,
                2, 3, 0);
            record.FieldCount = 1;
            record.Field0 = Field(NativeDiagnosticFieldIdV1.Custom0, 42);
            var debug = new[] { new CompiledDebugMapEntry(3, new NodeId("node-a"), "/tree/a", "A") };

            Assert.That(NativeDiagnosticProjectorV1.TryProject(record, debug, Array.Empty<NativeDiagnosticLocationV1>(), out var projected), Is.True);
            Assert.That(projected.Code.Value, Is.EqualTo("AIBT7777"));
            Assert.That(projected.Message, Is.EqualTo("AIBT7777 native runtime diagnostic."));
            Assert.That(projected.Location.DocumentId, Is.EqualTo("/tree/a"));
            Assert.That(projected.Location.NodeId, Is.EqualTo(new NodeId("node-a")));
            Assert.That(projected.Location.TreeInstanceId, Is.EqualTo(new TreeInstanceId(2)));

            record.PrimaryLocation = new NativeDiagnosticLocationV1(
                NativeDiagnosticLocationFlagsV1.RuntimeNode | NativeDiagnosticLocationFlagsV1.DebugIdentity,
                runtimeNodeIndex: 3,
                debugIdentityIndex: 99);
            Assert.DoesNotThrow(() => Assert.That(
                NativeDiagnosticProjectorV1.TryProject(record, debug, Array.Empty<NativeDiagnosticLocationV1>(), out _), Is.False));
        }

        [TestCase(4301, 2)]
        [TestCase(4302, 4)]
        [TestCase(4303, 4)]
        [TestCase(4304, 3)]
        [TestCase(4305, 3)]
        [TestCase(4306, 3)]
        [TestCase(4307, 3)]
        [TestCase(4308, 3)]
        [TestCase(4309, 3)]
        [TestCase(4310, 4)]
        [TestCase(4311, 6)]
        [TestCase(4312, 6)]
        public void Diagnostic_KnownCodesRequireExactStableFields(int codeValue, int expectedCount)
        {
            var code = (ushort)codeValue;
            var record = DiagnosticRecord(code, 1, 1);
            SetKnownFields(ref record, code);
            Assert.That(record.FieldCount, Is.EqualTo(expectedCount));

            Assert.That(NativeDiagnosticChannelOwnerV1.TryCreate(
                new NativeDiagnosticChannelCapacityV1(1, 0), new TreeInstanceId(1), 0,
                Allocator.Persistent, out var owner, out var failure), Is.True, failure.Code.ToString());
            try
            {
                Assert.That(owner.TryAcquireWriter(out var lease, out failure), Is.True);
                Assert.That(lease.Writer.TryAppend(record), Is.EqualTo(NativeDiagnosticAppendResultV1.Written));
                Assert.That(owner.TryReleaseWriter(lease, out failure), Is.True);

                record.FieldCount--;
                Assert.That(owner.TryReset(out failure), Is.True);
                Assert.That(owner.TryAcquireWriter(out lease, out failure), Is.True);
                Assert.That(lease.Writer.TryAppend(record), Is.EqualTo(NativeDiagnosticAppendResultV1.InvalidRecord));
                Assert.That(owner.TryReleaseWriter(lease, out failure), Is.True);
            }
            finally
            {
                Assert.That(owner.TryDispose(out failure), Is.True, failure.Code.ToString());
            }
        }

        [Test]
        public void Diagnostic_OverflowIsAtomicAndProducesExactOutOfBand4309()
        {
            Assert.That(NativeDiagnosticChannelOwnerV1.TryCreate(
                new NativeDiagnosticChannelCapacityV1(2, 1), new TreeInstanceId(1), 7,
                Allocator.Persistent, out var owner, out var failure), Is.True, failure.Code.ToString());
            var locations = new NativeArray<NativeDiagnosticLocationV1>(1, Allocator.TempJob);
            try
            {
                locations[0] = new NativeDiagnosticLocationV1(NativeDiagnosticLocationFlagsV1.RuntimeNode, runtimeNodeIndex: 9);
                Assert.That(owner.TryAcquireWriter(out var lease, out failure), Is.True);
                var first = DiagnosticRecord(7777, 1, 1, 7);
                Assert.That(lease.Writer.TryAppend(first, locations.AsReadOnly(), 1), Is.EqualTo(NativeDiagnosticAppendResultV1.Written));
                var second = DiagnosticRecord(7778, 2, 1, 7);
                Assert.That(lease.Writer.TryAppend(second, locations.AsReadOnly(), 1), Is.EqualTo(NativeDiagnosticAppendResultV1.ChannelFaulted));
                Assert.That(owner.TryReleaseWriter(lease, out failure), Is.True);
                Assert.That(owner.TryGetSnapshot(out var snapshot, out failure), Is.True);
                Assert.That(snapshot.RecordCount, Is.EqualTo(1));
                Assert.That(snapshot.RelatedLocationCount, Is.EqualTo(1));
                Assert.That(snapshot.IsFaulted, Is.True);
                Assert.That(snapshot.Rejection.CodeNumber, Is.EqualTo(4309));
                Assert.That(snapshot.Rejection.Field0.Value, Is.EqualTo((ulong)NativeDiagnosticResourceKindV1.DiagnosticLocations));
                Assert.That(snapshot.Rejection.Field1.Value, Is.EqualTo(2));
                Assert.That(snapshot.Rejection.Field2.Value, Is.EqualTo(1));
            }
            finally
            {
                locations.Dispose();
                Assert.That(owner.TryDispose(out failure), Is.True, failure.Code.ToString());
            }
        }

        [Test]
        public void Diagnostic_MergeAndRejectionWinnerArePermutationIndependent()
        {
            var source = new NativeArray<NativeDiagnosticRecordV1>(3, Allocator.TempJob);
            var destination = new NativeArray<NativeDiagnosticRecordV1>(3, Allocator.TempJob);
            try
            {
                source[0] = DiagnosticRecord(7778, 8, 2, 2, DiagnosticSeverity.Warning);
                source[1] = DiagnosticRecord(7777, 4, 1, 1, DiagnosticSeverity.Error);
                source[2] = DiagnosticRecord(7777, 4, 1, 3, DiagnosticSeverity.Error);
                Assert.That(NativeDiagnosticMergeV1.TryMergeRecords(source.AsReadOnly(), 3, destination, out var count), Is.True);
                Assert.That(count, Is.EqualTo(2), "normalized duplicate must appear once");
                Assert.That(destination[0].CodeNumber, Is.EqualTo(7777));
                Assert.That(destination[1].CodeNumber, Is.EqualTo(7778));

                for (var index = 0; index < 3; index++)
                {
                    var value = source[index];
                    value.CodeNumber = 4309;
                    value.FieldCount = 3;
                    value.Field0 = Field(NativeDiagnosticFieldIdV1.ResourceKind, (ulong)NativeDiagnosticResourceKindV1.DiagnosticRecords);
                    value.Field1 = Field(NativeDiagnosticFieldIdV1.Requested, 3);
                    value.Field2 = Field(NativeDiagnosticFieldIdV1.Capacity, 2);
                    source[index] = value;
                }
                var first = source[0];
                first.PrimaryLocation = new NativeDiagnosticLocationV1(
                    NativeDiagnosticLocationFlagsV1.TreeInstance | NativeDiagnosticLocationFlagsV1.RuntimeNode, 2, 8);
                source[0] = first;
                var third = source[2];
                third.PrimaryLocation = new NativeDiagnosticLocationV1(
                    NativeDiagnosticLocationFlagsV1.TreeInstance | NativeDiagnosticLocationFlagsV1.RuntimeNode, 1, 9);
                source[2] = third;
                Assert.That(NativeDiagnosticMergeV1.TrySelectRejection(source.AsReadOnly(), 3, out var winner), Is.True);
                Assert.That(winner.PrimaryLocation.TreeInstanceId, Is.EqualTo(1));
                Assert.That(winner.PrimaryLocation.RuntimeNodeIndex, Is.EqualTo(4));
                Assert.That(winner.WorkerOrdinal, Is.EqualTo(1));
            }
            finally
            {
                destination.Dispose();
                source.Dispose();
            }
        }

        [Test]
        public void Diagnostic_ChannelTopKRelatedLocationsAndRejectionArePermutationIndependent()
        {
            var first = RunDiagnosticPermutation(new uint[] { 4, 1, 3, 2 });
            var second = RunDiagnosticPermutation(new uint[] { 2, 3, 1, 4 });
            Assert.That(first, Is.EqualTo(second));
            Assert.That(first, Is.EqualTo("1:11|2:12|reject:3"));
        }

        [Test]
        public void Diagnostic_RejectionOrderUsesUpdateRevisionAndSequenceTieBreaks()
        {
            var source = new NativeArray<NativeDiagnosticRecordV1>(3, Allocator.TempJob);
            try
            {
                for (var index = 0; index < 3; index++)
                {
                    var record = DiagnosticRecord(4309, 1, 1);
                    record.UpdateId = (ulong)(3 - index);
                    record.SnapshotRevision = (ulong)(2 + index);
                    record.Sequence = (ulong)(9 - index);
                    record.FieldCount = 3;
                    record.Field0 = Field(NativeDiagnosticFieldIdV1.ResourceKind, (ulong)NativeDiagnosticResourceKindV1.DiagnosticRecords);
                    record.Field1 = Field(NativeDiagnosticFieldIdV1.Requested, 3);
                    record.Field2 = Field(NativeDiagnosticFieldIdV1.Capacity, 2);
                    source[index] = record;
                }
                Assert.That(NativeDiagnosticMergeV1.TrySelectRejection(source.AsReadOnly(), 3, out var winner), Is.True);
                Assert.That(winner.UpdateId, Is.EqualTo(1));
                Assert.That(winner.SnapshotRevision, Is.EqualTo(4));
                Assert.That(winner.Sequence, Is.EqualTo(7));
            }
            finally { source.Dispose(); }
        }

        [Test]
        public void Diagnostic_ConcurrentAppendRetainsCanonicalTopK()
        {
            Assert.That(NativeDiagnosticChannelOwnerV1.TryCreate(
                new NativeDiagnosticChannelCapacityV1(4, 0), new TreeInstanceId(1), 0,
                Allocator.Persistent, out var owner, out var failure), Is.True);
            var records = new NativeArray<NativeDiagnosticRecordV1>(8, Allocator.TempJob);
            try
            {
                var nodes = new uint[] { 8, 3, 6, 1, 7, 2, 5, 4 };
                for (var index = 0; index < nodes.Length; index++) records[index] = DiagnosticRecord(7777, nodes[index], 1);
                Assert.That(owner.TryAcquireWriter(out var lease, out failure), Is.True);
                var job = new ParallelDiagnosticAppendJob { Writer = lease.Writer, Records = records }.Schedule(records.Length, 1);
                Assert.That(owner.TryRegisterDependency(lease, job, out failure), Is.True);
                job.Complete();
                Assert.That(owner.TryReleaseWriter(lease, out failure), Is.True);
                Assert.That(owner.TryGetSnapshot(out var snapshot, out failure), Is.True);
                Assert.That(snapshot.IsFaulted, Is.True);
                Assert.That(snapshot.RecordCount, Is.EqualTo(4));
                for (var index = 0; index < 4; index++)
                    Assert.That(snapshot.Records[index].PrimaryLocation.RuntimeNodeIndex, Is.EqualTo((uint)index + 1));
                Assert.That(snapshot.Rejection.PrimaryLocation.RuntimeNodeIndex, Is.EqualTo(5));
            }
            finally
            {
                records.Dispose();
                Assert.That(owner.TryDispose(out failure), Is.True);
            }
        }

        [Test]
        public void Trace_EventNumbersAndReferenceProjectionMatchTraceV1NotP1Ordinals()
        {
            var expected = new[]
            {
                NativeTraceEventKindV1.UpdateStarted, NativeTraceEventKindV1.UpdateCompleted,
                NativeTraceEventKindV1.NodeEntered, NativeTraceEventKindV1.NodeTicked,
                NativeTraceEventKindV1.NodeAbortStarted, NativeTraceEventKindV1.NodeExited,
                NativeTraceEventKindV1.BlackboardChanged, NativeTraceEventKindV1.ObserverQueued,
                NativeTraceEventKindV1.ObserverEvaluated, NativeTraceEventKindV1.CommandEmitted,
                NativeTraceEventKindV1.CompletionConsumed, NativeTraceEventKindV1.CompletionDiscarded,
                NativeTraceEventKindV1.BudgetYielded, NativeTraceEventKindV1.ExecutionResumed,
                NativeTraceEventKindV1.DiagnosticRaised, NativeTraceEventKindV1.SchedulerDecision,
                NativeTraceEventKindV1.TraceDroppedSummary,
            };
            for (var index = 0; index < expected.Length; index++)
                Assert.That((byte)expected[index], Is.EqualTo(index));

            var referenceKinds = (ReferenceTraceEventKind[])Enum.GetValues(typeof(ReferenceTraceEventKind));
            foreach (var kind in referenceKinds)
            {
                var source = new ReferenceTraceRecord(
                    1, new Revision(1), Hash(), new TreeInstanceId(1), (ulong)kind + 1, kind);
                Assert.That(NativeReferenceTraceProjectionV1.TryProject(
                    source, NativeUpdatePhaseV1.Execute, 3, CompiledIndex.Invalid, out var projected), Is.True, kind.ToString());
                Assert.That(projected.Kind.ToString(), Is.EqualTo(kind.ToString()));
                if (kind == ReferenceTraceEventKind.CommandEmitted)
                    Assert.That((byte)projected.Kind, Is.EqualTo(9).And.Not.EqualTo((byte)kind));
            }
        }

        [Test]
        public void Trace_OffReturnsBeforeValidationAndCounters()
        {
            Assert.That(CreateTrace(NativeTraceLevelV1.Off, new NativeTraceChannelCapacityV1(0, 0, 0, 0), out var owner, out var failure), Is.True);
            try
            {
                Assert.That(owner.TryAcquireWriter(out var lease, out failure), Is.True);
                Assert.That(lease.Writer.TryAppend(default), Is.EqualTo(NativeTraceAppendResultV1.Filtered));
                Assert.That(owner.TryReleaseWriter(lease, out failure), Is.True);
                Assert.That(owner.TryGetSnapshot(out var snapshot, out failure), Is.True);
                Assert.That(snapshot.RecordCount, Is.Zero);
                Assert.That(snapshot.DroppedCount, Is.Zero);
                Assert.That(snapshot.IsFaulted, Is.False);
            }
            finally { Assert.That(owner.TryDispose(out failure), Is.True); }
        }

        [Test]
        public void Trace_LevelsUseExactTraceV1FiltersAndFilteredBranchReleasesLock()
        {
            Assert.That(NativeTraceWriterV1.Includes(NativeTraceLevelV1.Errors, NativeTraceEventKindV1.DiagnosticRaised), Is.True);
            Assert.That(NativeTraceWriterV1.Includes(NativeTraceLevelV1.Errors, NativeTraceEventKindV1.NodeEntered), Is.False);
            Assert.That(NativeTraceWriterV1.Includes(NativeTraceLevelV1.Lifecycle, NativeTraceEventKindV1.CompletionDiscarded), Is.True);
            Assert.That(NativeTraceWriterV1.Includes(NativeTraceLevelV1.Lifecycle, NativeTraceEventKindV1.BlackboardChanged), Is.False);
            Assert.That(NativeTraceWriterV1.Includes(NativeTraceLevelV1.Detailed, NativeTraceEventKindV1.SchedulerDecision), Is.True);
            Assert.That(NativeTraceWriterV1.Includes(NativeTraceLevelV1.Detailed, NativeTraceEventKindV1.TraceDroppedSummary), Is.False);

            Assert.That(CreateTrace(NativeTraceLevelV1.Lifecycle, new NativeTraceChannelCapacityV1(2, 0, 0, 2), out var owner, out var failure), Is.True);
            try
            {
                Assert.That(owner.TryAcquireWriter(out var lease, out failure), Is.True);
                Assert.That(lease.Writer.TryAppend(TraceRecord(1, kind: NativeTraceEventKindV1.BlackboardChanged)), Is.EqualTo(NativeTraceAppendResultV1.Filtered));
                Assert.That(lease.Writer.TryAppend(TraceRecord(2, kind: NativeTraceEventKindV1.NodeEntered)), Is.EqualTo(NativeTraceAppendResultV1.Written));
                Assert.That(owner.TryReleaseWriter(lease, out failure), Is.True);
                Assert.That(owner.TryGetSnapshot(out var snapshot, out failure), Is.True);
                Assert.That(snapshot.RecordCount, Is.EqualTo(1));
                Assert.That(snapshot.Records[0].Kind, Is.EqualTo(NativeTraceEventKindV1.NodeEntered));
            }
            finally { Assert.That(owner.TryDispose(out failure), Is.True); }
        }

        [Test]
        public void Trace_OverflowRetainsLowestSequencesAndWritesOnePayloadFreeSummary()
        {
            Assert.That(CreateTrace(NativeTraceLevelV1.Detailed, new NativeTraceChannelCapacityV1(3, 8, 4, 8), out var owner, out var failure), Is.True);
            try
            {
                Assert.That(owner.TryAcquireWriter(out var lease, out failure), Is.True);
                foreach (var sequence in new ulong[] { 5, 2, 4, 1 })
                    Assert.That(lease.Writer.TryAppend(TraceRecord(sequence)), Is.Not.EqualTo(NativeTraceAppendResultV1.InvalidRecord));
                Assert.That(owner.TryReleaseWriter(lease, out failure), Is.True);
                Assert.That(owner.TryGetSnapshot(out var snapshot, out failure), Is.True);
                Assert.That(snapshot.RecordCount, Is.EqualTo(3));
                Assert.That(snapshot.Records[0].Sequence, Is.EqualTo(1));
                Assert.That(snapshot.Records[1].Sequence, Is.EqualTo(2));
                Assert.That(snapshot.Records[2].Kind, Is.EqualTo(NativeTraceEventKindV1.TraceDroppedSummary));
                Assert.That(snapshot.Records[2].DroppedCount, Is.EqualTo(2));
                Assert.That(snapshot.Records[2].PayloadLength, Is.Zero);
                Assert.That(snapshot.DroppedCount, Is.EqualTo(2));
            }
            finally { Assert.That(owner.TryDispose(out failure), Is.True); }
        }

        [Test]
        public void Trace_EarlyOversizeDropPublishesContiguousSummaryAndReleasesLock()
        {
            Assert.That(CreateTrace(NativeTraceLevelV1.Detailed, new NativeTraceChannelCapacityV1(3, 4, 2, 4), out var owner, out var failure), Is.True);
            var payload = new NativeArray<byte>(3, Allocator.TempJob);
            try
            {
                Assert.That(owner.TryAcquireWriter(out var lease, out failure), Is.True);
                Assert.That(lease.Writer.TryAppend(TraceRecord(1), payload.AsReadOnly(), 3), Is.EqualTo(NativeTraceAppendResultV1.Dropped));
                Assert.That(lease.Writer.TryAppend(default), Is.EqualTo(NativeTraceAppendResultV1.InvalidRecord));
                Assert.That(lease.Writer.TryAppend(TraceRecord(2)), Is.EqualTo(NativeTraceAppendResultV1.Written));
                Assert.That(owner.TryReleaseWriter(lease, out failure), Is.True);
                Assert.That(owner.TryGetSnapshot(out var snapshot, out failure), Is.True);
                Assert.That(snapshot.RecordCount, Is.EqualTo(2));
                Assert.That(snapshot.Records[0].Sequence, Is.EqualTo(2));
                Assert.That(snapshot.Records[1].Kind, Is.EqualTo(NativeTraceEventKindV1.TraceDroppedSummary));
                Assert.That(snapshot.Records[1].Sequence, Is.EqualTo(1));
                Assert.That(snapshot.Records[1].DroppedCount, Is.EqualTo(1));
                Assert.That(snapshot.Records[1].PayloadLength, Is.Zero);
            }
            finally
            {
                payload.Dispose();
                Assert.That(owner.TryDispose(out failure), Is.True);
            }
        }

        [Test, Repeat(10)]
        public void Trace_ConcurrentAppendIsPermutationIndependentIncludingPayload()
        {
            Assert.That(CreateTrace(NativeTraceLevelV1.Detailed, new NativeTraceChannelCapacityV1(4, 3, 1, 8), out var owner, out var failure), Is.True);
            var records = new NativeArray<NativeTraceRecordV1>(8, Allocator.TempJob);
            var payloads = new NativeArray<byte>(8, Allocator.TempJob);
            var results = new NativeArray<NativeTraceAppendResultV1>(8, Allocator.TempJob);
            try
            {
                var input = new ulong[] { 8, 3, 6, 1, 7, 2, 5, 4 };
                for (var index = 0; index < input.Length; index++)
                {
                    records[index] = TraceRecord(input[index]);
                    payloads[index] = (byte)input[index];
                }
                Assert.That(owner.TryAcquireWriter(out var lease, out failure), Is.True);
                var job = new ParallelTraceAppendJob
                {
                    Writer = lease.Writer,
                    Records = records,
                    Payloads = payloads,
                    Results = results,
                }.Schedule(records.Length, 1);
                Assert.That(owner.TryRegisterDependency(lease, job, out failure), Is.True);
                job.Complete();
                Assert.That(owner.TryReleaseWriter(lease, out failure), Is.True);
                Assert.That(owner.TryGetSnapshot(out var snapshot, out failure), Is.True);
                Assert.That(snapshot.IsFaulted, Is.False);
                for (var index = 0; index < results.Length; index++)
                    Assert.That(results[index], Is.EqualTo(NativeTraceAppendResultV1.Written)
                        .Or.EqualTo(NativeTraceAppendResultV1.Dropped));
                Assert.That(snapshot.RecordCount, Is.EqualTo(4));
                for (var index = 0; index < 3; index++)
                {
                    Assert.That(snapshot.Records[index].Sequence, Is.EqualTo((ulong)index + 1));
                    Assert.That(snapshot.Payload[(int)snapshot.Records[index].PayloadOffset], Is.EqualTo((byte)index + 1));
                }
                Assert.That(snapshot.Records[3].Kind, Is.EqualTo(NativeTraceEventKindV1.TraceDroppedSummary));
                Assert.That(snapshot.Records[3].DroppedCount, Is.EqualTo(5));
            }
            finally
            {
                payloads.Dispose();
                results.Dispose();
                records.Dispose();
                Assert.That(owner.TryDispose(out failure), Is.True);
            }
        }

        [Test]
        public void Trace_DroppedCounterNeverWraps()
        {
            Assert.That(CreateTrace(NativeTraceLevelV1.Detailed, new NativeTraceChannelCapacityV1(1, 0, 0, 2), out var owner, out var failure), Is.True);
            try
            {
                Assert.That(owner.TryAcquireWriter(out var lease, out failure), Is.True);
                Assert.That(lease.Writer.TryAppend(TraceRecord(1)), Is.EqualTo(NativeTraceAppendResultV1.Dropped));
                Assert.That(owner.TryReleaseWriter(lease, out failure), Is.True);
                Assert.That(owner.TrySetDroppedCountForVerification(ulong.MaxValue), Is.True);
                Assert.That(owner.TryAcquireWriter(out lease, out failure), Is.True);
                Assert.That(lease.Writer.TryAppend(TraceRecord(2)), Is.EqualTo(NativeTraceAppendResultV1.ChannelFaulted));
                Assert.That(owner.TryReleaseWriter(lease, out failure), Is.True);
                Assert.That(owner.TryGetSnapshot(out var snapshot, out failure), Is.True);
                Assert.That(snapshot.DroppedCount, Is.EqualTo(ulong.MaxValue));
                Assert.That(snapshot.IsFaulted, Is.True);
            }
            finally { Assert.That(owner.TryDispose(out failure), Is.True); }
        }

        [Test]
        public void Trace_DuplicateSequenceFaultsAndMergeRejectsDuplicates()
        {
            Assert.That(CreateTrace(NativeTraceLevelV1.Detailed, new NativeTraceChannelCapacityV1(3, 0, 0, 4), out var owner, out var failure), Is.True);
            var source = new NativeArray<NativeTraceRecordV1>(2, Allocator.TempJob);
            var destination = new NativeArray<NativeTraceRecordV1>(2, Allocator.TempJob);
            try
            {
                Assert.That(owner.TryAcquireWriter(out var lease, out failure), Is.True);
                Assert.That(lease.Writer.TryAppend(TraceRecord(1)), Is.EqualTo(NativeTraceAppendResultV1.Written));
                Assert.That(lease.Writer.TryAppend(TraceRecord(1)), Is.EqualTo(NativeTraceAppendResultV1.DuplicateSequence));
                Assert.That(owner.TryReleaseWriter(lease, out failure), Is.True);
                Assert.That(owner.TryGetSnapshot(out var snapshot, out failure), Is.True);
                Assert.That(snapshot.IsFaulted, Is.True);

                source[0] = TraceRecord(7, tree: 2);
                source[1] = TraceRecord(7, tree: 2, worker: 9);
                Assert.That(NativeTraceMergeV1.TryMerge(source.AsReadOnly(), 2, destination, out _), Is.False);
            }
            finally
            {
                destination.Dispose();
                source.Dispose();
                Assert.That(owner.TryDispose(out failure), Is.True);
            }
        }

        [Test]
        public void Trace_MergeUsesExactGlobalPhaseInstanceSequenceOrder()
        {
            var source = new NativeArray<NativeTraceRecordV1>(4, Allocator.TempJob);
            var destination = new NativeArray<NativeTraceRecordV1>(4, Allocator.TempJob);
            try
            {
                source[0] = TraceRecord(3, tree: 1, phase: NativeUpdatePhaseV1.Execute);
                source[1] = TraceRecord(1, tree: 2, phase: NativeUpdatePhaseV1.NormalizeInput);
                source[2] = TraceRecord(2, tree: 1, phase: NativeUpdatePhaseV1.NormalizeInput);
                source[3] = TraceRecord(1, tree: 1, phase: NativeUpdatePhaseV1.NormalizeInput);
                Assert.That(NativeTraceMergeV1.TryMerge(source.AsReadOnly(), 4, destination, out var count), Is.True);
                Assert.That(count, Is.EqualTo(4));
                Assert.That(destination[0].TreeInstanceId, Is.EqualTo(1));
                Assert.That(destination[0].Sequence, Is.EqualTo(1));
                Assert.That(destination[1].TreeInstanceId, Is.EqualTo(1));
                Assert.That(destination[1].Sequence, Is.EqualTo(2));
                Assert.That(destination[2].TreeInstanceId, Is.EqualTo(2));
                Assert.That(destination[3].Phase, Is.EqualTo(NativeUpdatePhaseV1.Execute));
            }
            finally { destination.Dispose(); source.Dispose(); }
        }

        [Test]
        public void Trace_PayloadMergeIsByteIdenticalAcrossChannelPartitionPermutations()
        {
            var first = RunTracePayloadMerge(new[] { 0, 1, 2, 3 });
            var second = RunTracePayloadMerge(new[] { 3, 1, 0, 2 });
            Assert.That(first, Is.EqualTo(second));
            Assert.That(first, Is.EqualTo("1:1@0+1|1:2@1+1|1:3@0+0:S1|2:1@2+2|10,20,30,31"));
        }

        [Test]
        public void Trace_MergesIndependentChannelSnapshotsWithLocalPayloadOffsets()
        {
            var first = RunTraceSnapshotMerge(swapSources: false, sufficientCapacity: true);
            var second = RunTraceSnapshotMerge(swapSources: true, sufficientCapacity: true);
            Assert.That(first, Is.EqualTo(second));
            Assert.That(first, Does.StartWith("1:1@0+1|1:2@1+1|1:4@0+0:S1|2:1@2+2|10,20,30,31#"));
            Assert.That(RunTraceSnapshotMerge(swapSources: true, sufficientCapacity: false), Is.EqualTo("atomic"));
        }

        [Test]
        public void Diagnostic_RelatedLocationMergeSortsDedupesAndRejectsCapacityAtomically()
        {
            var first = RunDiagnosticRelatedMerge(new[] { 0, 1, 2, 3 }, sufficientCapacity: true);
            var second = RunDiagnosticRelatedMerge(new[] { 3, 1, 0, 2 }, sufficientCapacity: true);
            Assert.That(first, Is.EqualTo(second));
            Assert.That(first, Is.EqualTo("1@0+2|2@2+1|2@3+1|10,11,20,21"));
            Assert.That(RunDiagnosticRelatedMerge(new[] { 3, 1, 0, 2 }, sufficientCapacity: false), Is.EqualTo("atomic"));
        }

        [Test]
        public void Diagnostic_MergesIndependentSnapshotsAndDedupesAcrossLocalLocationArenas()
        {
            var first = RunDiagnosticSnapshotMerge(swapSources: false, sufficientCapacity: true);
            var second = RunDiagnosticSnapshotMerge(swapSources: true, sufficientCapacity: true);
            Assert.That(first, Is.EqualTo(second));
            Assert.That(first, Is.EqualTo("1@0+1|2@1+1|10,20"));
            Assert.That(RunDiagnosticSnapshotMerge(swapSources: true, sufficientCapacity: false), Is.EqualTo("atomic"));
        }

        [Test]
        public void Channels_BlockEarlyReleaseResetDisposeAndRejectStaleLease()
        {
            Assert.That(CreateTrace(NativeTraceLevelV1.Detailed, new NativeTraceChannelCapacityV1(2, 0, 0, 2), out var owner, out var failure), Is.True);
            Assert.That(owner.TryAcquireWriter(out var lease, out failure), Is.True);
            var job = new DelayedTraceJob { Writer = lease.Writer, Record = TraceRecord(1) }.Schedule();
            Assert.That(owner.TryRegisterDependency(lease, job, out failure), Is.True);
            Assert.That(owner.TryReleaseWriter(lease, out failure), Is.False);
            Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeLiveJobOwnershipViolation));
            Assert.That(owner.TryReset(out failure), Is.False);
            Assert.That(owner.TryDispose(out failure), Is.False);
            job.Complete();
            Assert.That(owner.TryReleaseWriter(lease, out failure), Is.True);
            Assert.That(owner.TryReset(out failure), Is.True);
            Assert.That(owner.TryReleaseWriter(lease, out failure), Is.False);
            Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeLifetimeStateInvalid));
            Assert.That(owner.TryDispose(out failure), Is.True);
            Assert.That(owner.TryDispose(out failure), Is.False);
        }

        [Test]
        public void InitializationRollbackCoversEveryNativeAllocation()
        {
            for (var failurePoint = 0; failurePoint < 7; failurePoint++)
            {
                Assert.That(NativeTraceChannelOwnerV1.TryCreate(
                    new NativeTraceChannelCapacityV1(2, 4, 4, 2), NativeTraceLevelV1.Detailed,
                    new TreeInstanceId(1), 0, Allocator.Persistent, failurePoint,
                    out var owner, out var failure), Is.False);
                Assert.That(owner, Is.Null);
                Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeTraceCapacityExceeded));
            }

            for (var failurePoint = 0; failurePoint < 5; failurePoint++)
            {
                Assert.That(NativeDiagnosticChannelOwnerV1.TryCreate(
                    new NativeDiagnosticChannelCapacityV1(2, 2), new TreeInstanceId(1), 0,
                    Allocator.Persistent, failurePoint, out var owner, out var failure), Is.False);
                Assert.That(owner, Is.Null);
                Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeDiagnosticCapacityExceeded));
            }
        }

        [Test]
        public void BurstJobCanAppendStructuredDiagnosticAndTraceRecords()
        {
            Assert.That(NativeDiagnosticChannelOwnerV1.TryCreate(
                new NativeDiagnosticChannelCapacityV1(1, 0), new TreeInstanceId(1), 0,
                Allocator.Persistent, out var diagnostics, out var diagnosticFailure), Is.True);
            Assert.That(CreateTrace(NativeTraceLevelV1.Detailed, new NativeTraceChannelCapacityV1(2, 0, 0, 2), out var trace, out var traceFailure), Is.True);
            try
            {
                Assert.That(diagnostics.TryAcquireWriter(out var diagnosticLease, out diagnosticFailure), Is.True);
                Assert.That(trace.TryAcquireWriter(out var traceLease, out traceFailure), Is.True);
                var job = new NativeChannelsBurstProbeJobV1
                {
                    DiagnosticWriter = diagnosticLease.Writer,
                    Diagnostic = DiagnosticRecord(7777, 1, 1),
                    TraceWriter = traceLease.Writer,
                    Trace = TraceRecord(1),
                }.Schedule();
                Assert.That(diagnostics.TryRegisterDependency(diagnosticLease, job, out diagnosticFailure), Is.True);
                Assert.That(trace.TryRegisterDependency(traceLease, job, out traceFailure), Is.True);
                job.Complete();
                Assert.That(diagnostics.TryReleaseWriter(diagnosticLease, out diagnosticFailure), Is.True);
                Assert.That(trace.TryReleaseWriter(traceLease, out traceFailure), Is.True);
                Assert.That(diagnostics.TryGetSnapshot(out var diagnosticSnapshot, out diagnosticFailure), Is.True);
                Assert.That(trace.TryGetSnapshot(out var traceSnapshot, out traceFailure), Is.True);
                Assert.That(diagnosticSnapshot.RecordCount, Is.EqualTo(1));
                Assert.That(traceSnapshot.RecordCount, Is.EqualTo(1));
            }
            finally
            {
                Assert.That(trace.TryDispose(out traceFailure), Is.True);
                Assert.That(diagnostics.TryDispose(out diagnosticFailure), Is.True);
            }
        }

        [Test]
        public void AppendAndMergeAllocateZeroManagedBytesAfterInitialization()
        {
            Assert.That(
                () =>
                {
                    var canary = new byte[128];
                    GC.KeepAlive(canary);
                },
                GcAllocIs.AllocatingGCMemory(),
                "allocation instrumentation canary");

            Assert.That(CreateTrace(NativeTraceLevelV1.Detailed, new NativeTraceChannelCapacityV1(66, 65, 1, 65), out var traceOwner, out var traceFailure), Is.True);
            Assert.That(NativeDiagnosticChannelOwnerV1.TryCreate(
                new NativeDiagnosticChannelCapacityV1(65, 65), new TreeInstanceId(1), 0,
                Allocator.Persistent, out var diagnosticOwner, out var diagnosticFailure), Is.True);
            var tracePayload = new NativeArray<byte>(1, Allocator.TempJob);
            var traceMergeRecords = new NativeArray<NativeTraceRecordV1>(1, Allocator.TempJob);
            var traceMergePayload = new NativeArray<byte>(1, Allocator.TempJob);
            var traceMergeOutputRecords = new NativeArray<NativeTraceRecordV1>(1, Allocator.TempJob);
            var traceMergeOutputPayload = new NativeArray<byte>(1, Allocator.TempJob);
            var diagnosticLocations = new NativeArray<NativeDiagnosticLocationV1>(1, Allocator.TempJob);
            var diagnosticMergeRecords = new NativeArray<NativeDiagnosticRecordV1>(1, Allocator.TempJob);
            var diagnosticMergeOutputRecords = new NativeArray<NativeDiagnosticRecordV1>(1, Allocator.TempJob);
            var diagnosticMergeOutputLocations = new NativeArray<NativeDiagnosticLocationV1>(1, Allocator.TempJob);
            try
            {
                tracePayload[0] = 7;
                diagnosticLocations[0] = new NativeDiagnosticLocationV1(NativeDiagnosticLocationFlagsV1.RuntimeNode, runtimeNodeIndex: 2);
                var traceRecord = TraceRecord(1);
                traceRecord.PayloadLength = 1;
                traceMergeRecords[0] = traceRecord;
                traceMergePayload[0] = 7;
                var diagnosticRecord = DiagnosticRecord(7777, 1, 1);
                diagnosticRecord.RelatedLocationCount = 1;
                diagnosticMergeRecords[0] = diagnosticRecord;
                var traceAppendRecord = TraceRecord(1);
                var diagnosticAppendRecord = DiagnosticRecord(7777, 1, 1);

                Assert.That(traceOwner.TryAcquireWriter(out var traceLease, out traceFailure), Is.True);
                Assert.That(diagnosticOwner.TryAcquireWriter(out var diagnosticLease, out diagnosticFailure), Is.True);
                try
                {
                    var tracePayloadReadOnly = tracePayload.AsReadOnly();
                    var traceMergeRecordsReadOnly = traceMergeRecords.AsReadOnly();
                    var traceMergePayloadReadOnly = traceMergePayload.AsReadOnly();
                    var diagnosticLocationsReadOnly = diagnosticLocations.AsReadOnly();
                    var diagnosticMergeRecordsReadOnly = diagnosticMergeRecords.AsReadOnly();

                    traceLease.Writer.TryAppend(traceAppendRecord, tracePayloadReadOnly, 1);
                    diagnosticLease.Writer.TryAppend(diagnosticAppendRecord, diagnosticLocationsReadOnly, 1);
                    diagnosticAppendRecord.Sequence = 2;
                    diagnosticLease.Writer.TryAppend(diagnosticAppendRecord, diagnosticLocationsReadOnly, 1);
                    NativeTraceMergeV1.TryMerge(
                        traceMergeRecordsReadOnly, 1, traceMergePayloadReadOnly, 1,
                        traceMergeOutputRecords, traceMergeOutputPayload, out _, out _);
                    NativeDiagnosticMergeV1.TryMerge(
                        diagnosticMergeRecordsReadOnly, 1, diagnosticLocationsReadOnly, 1,
                        diagnosticMergeOutputRecords, diagnosticMergeOutputLocations, out _, out _);

                    Assert.That(() =>
                    {
                        for (var index = 0; index < 8; index++)
                        {
                            traceAppendRecord.Sequence = (ulong)index + 2;
                            traceLease.Writer.TryAppend(traceAppendRecord, tracePayloadReadOnly, 1);
                        }
                    }, GcAllocIs.Not.AllocatingGCMemory(), "trace append hot path");
                    Assert.That(() =>
                    {
                        for (var index = 0; index < 8; index++)
                        {
                            diagnosticAppendRecord.Sequence = (ulong)index + 3;
                            diagnosticLease.Writer.TryAppend(diagnosticAppendRecord, diagnosticLocationsReadOnly, 1);
                        }
                    }, GcAllocIs.Not.AllocatingGCMemory(), "diagnostic append hot path");
                    Assert.That(() =>
                    {
                        for (var index = 0; index < 32; index++)
                            NativeTraceMergeV1.TryMerge(
                                traceMergeRecordsReadOnly, 1, traceMergePayloadReadOnly, 1,
                                traceMergeOutputRecords, traceMergeOutputPayload, out _, out _);
                    }, GcAllocIs.Not.AllocatingGCMemory(), "trace merge hot path");
                    Assert.That(() =>
                    {
                        for (var index = 0; index < 32; index++)
                            NativeDiagnosticMergeV1.TryMerge(
                                diagnosticMergeRecordsReadOnly, 1, diagnosticLocationsReadOnly, 1,
                                diagnosticMergeOutputRecords, diagnosticMergeOutputLocations, out _, out _);
                    }, GcAllocIs.Not.AllocatingGCMemory(), "diagnostic merge hot path");
                }
                finally
                {
                    Assert.That(traceOwner.TryReleaseWriter(traceLease, out traceFailure), Is.True);
                    Assert.That(diagnosticOwner.TryReleaseWriter(diagnosticLease, out diagnosticFailure), Is.True);
                }
            }
            finally
            {
                diagnosticMergeOutputLocations.Dispose();
                diagnosticMergeOutputRecords.Dispose();
                diagnosticMergeRecords.Dispose();
                diagnosticLocations.Dispose();
                traceMergeOutputPayload.Dispose();
                traceMergeOutputRecords.Dispose();
                traceMergePayload.Dispose();
                traceMergeRecords.Dispose();
                tracePayload.Dispose();
                Assert.That(diagnosticOwner.TryDispose(out diagnosticFailure), Is.True);
                Assert.That(traceOwner.TryDispose(out traceFailure), Is.True);
            }
        }

        private static bool CreateTrace(
            NativeTraceLevelV1 level, NativeTraceChannelCapacityV1 capacity,
            out NativeTraceChannelOwnerV1 owner, out NativeTraceChannelFailureV1 failure)
            => NativeTraceChannelOwnerV1.TryCreate(
                capacity, level, new TreeInstanceId(1), 0, Allocator.Persistent, out owner, out failure);

        private static string RunTracePayloadMerge(int[] order)
        {
            var sourceRecords = new NativeArray<NativeTraceRecordV1>(4, Allocator.TempJob);
            var sourcePayload = new NativeArray<byte>(4, Allocator.TempJob);
            var destinationRecords = new NativeArray<NativeTraceRecordV1>(4, Allocator.TempJob);
            var destinationPayload = new NativeArray<byte>(4, Allocator.TempJob);
            try
            {
                sourcePayload[0] = 10;
                sourcePayload[1] = 20;
                sourcePayload[2] = 30;
                sourcePayload[3] = 31;
                var logical = new NativeTraceRecordV1[4];
                logical[0] = TraceRecord(1, tree: 2);
                logical[0].PayloadOffset = 2;
                logical[0].PayloadLength = 2;
                logical[1] = TraceRecord(3, tree: 1, kind: NativeTraceEventKindV1.TraceDroppedSummary);
                logical[1].DroppedCount = 1;
                logical[2] = TraceRecord(2, tree: 1);
                logical[2].PayloadOffset = 1;
                logical[2].PayloadLength = 1;
                logical[3] = TraceRecord(1, tree: 1);
                logical[3].PayloadLength = 1;
                for (var index = 0; index < order.Length; index++) sourceRecords[index] = logical[order[index]];

                Assert.That(NativeTraceMergeV1.TryMerge(
                    sourceRecords.AsReadOnly(), 4, sourcePayload.AsReadOnly(), 4,
                    destinationRecords, destinationPayload, out var recordCount, out var payloadCount), Is.True);
                Assert.That(recordCount, Is.EqualTo(4));
                Assert.That(payloadCount, Is.EqualTo(4));
                var result = string.Empty;
                for (var index = 0; index < recordCount; index++)
                {
                    var record = destinationRecords[index];
                    if (index != 0) result += "|";
                    result += record.TreeInstanceId + ":" + record.Sequence + "@" + record.PayloadOffset + "+" + record.PayloadLength;
                    if (record.Kind == NativeTraceEventKindV1.TraceDroppedSummary) result += ":S" + record.DroppedCount;
                }
                result += "|";
                for (var index = 0; index < payloadCount; index++)
                {
                    if (index != 0) result += ",";
                    result += destinationPayload[index];
                }
                return result;
            }
            finally
            {
                destinationPayload.Dispose();
                destinationRecords.Dispose();
                sourcePayload.Dispose();
                sourceRecords.Dispose();
            }
        }

        private static string RunTraceSnapshotMerge(bool swapSources, bool sufficientCapacity)
        {
            Assert.That(NativeTraceChannelOwnerV1.TryCreate(
                new NativeTraceChannelCapacityV1(3, 4, 2, 3), NativeTraceLevelV1.Detailed,
                new TreeInstanceId(2), 0, Allocator.Persistent, out var firstOwner, out var firstFailure), Is.True);
            Assert.That(NativeTraceChannelOwnerV1.TryCreate(
                new NativeTraceChannelCapacityV1(3, 2, 1, 3), NativeTraceLevelV1.Detailed,
                new TreeInstanceId(1), 1, Allocator.Persistent, out var secondOwner, out var secondFailure), Is.True);
            var firstPayload = new NativeArray<byte>(2, Allocator.TempJob);
            var secondPayload = new NativeArray<byte>(1, Allocator.TempJob);
            var destinationRecords = new NativeArray<NativeTraceRecordV1>(sufficientCapacity ? 4 : 3, Allocator.TempJob);
            var destinationPayload = new NativeArray<byte>(sufficientCapacity ? 4 : 3, Allocator.TempJob);
            try
            {
                firstPayload[0] = 30;
                firstPayload[1] = 31;
                secondPayload[0] = 20;
                Assert.That(firstOwner.TryAcquireWriter(out var firstLease, out firstFailure), Is.True);
                Assert.That(firstLease.Writer.TryAppend(TraceRecord(1, tree: 2), firstPayload.AsReadOnly(), 2), Is.EqualTo(NativeTraceAppendResultV1.Written));
                Assert.That(firstOwner.TryReleaseWriter(firstLease, out firstFailure), Is.True);

                Assert.That(secondOwner.TryAcquireWriter(out var secondLease, out secondFailure), Is.True);
                var second = TraceRecord(2, tree: 1, worker: 1);
                Assert.That(secondLease.Writer.TryAppend(second, secondPayload.AsReadOnly(), 1), Is.EqualTo(NativeTraceAppendResultV1.Written));
                secondPayload[0] = 10;
                var first = TraceRecord(1, tree: 1, worker: 1);
                Assert.That(secondLease.Writer.TryAppend(first, secondPayload.AsReadOnly(), 1), Is.EqualTo(NativeTraceAppendResultV1.Written));
                var dropped = TraceRecord(4, tree: 1, worker: 1);
                Assert.That(secondLease.Writer.TryAppend(dropped), Is.EqualTo(NativeTraceAppendResultV1.Dropped));
                Assert.That(secondOwner.TryReleaseWriter(secondLease, out secondFailure), Is.True);

                Assert.That(firstOwner.TryGetSnapshot(out var firstSnapshot, out firstFailure), Is.True);
                Assert.That(secondOwner.TryGetSnapshot(out var secondSnapshot, out secondFailure), Is.True);
                if (!sufficientCapacity)
                {
                    var sentinel = TraceRecord(99, tree: 9);
                    destinationRecords[0] = sentinel;
                    destinationPayload[0] = 99;
                    var rejected = swapSources
                        ? NativeTraceMergeV1.TryMerge(secondSnapshot, firstSnapshot, destinationRecords, destinationPayload, out var rejectedRecords, out var rejectedPayload)
                        : NativeTraceMergeV1.TryMerge(firstSnapshot, secondSnapshot, destinationRecords, destinationPayload, out rejectedRecords, out rejectedPayload);
                    Assert.That(rejected, Is.False);
                    Assert.That(rejectedRecords, Is.Zero);
                    Assert.That(rejectedPayload, Is.Zero);
                    Assert.That(destinationRecords[0].TreeInstanceId, Is.EqualTo(9));
                    Assert.That(destinationPayload[0], Is.EqualTo(99));
                    return "atomic";
                }
                var merged = swapSources
                    ? NativeTraceMergeV1.TryMerge(secondSnapshot, firstSnapshot, destinationRecords, destinationPayload, out var recordCount, out var payloadCount)
                    : NativeTraceMergeV1.TryMerge(firstSnapshot, secondSnapshot, destinationRecords, destinationPayload, out recordCount, out payloadCount);
                Assert.That(merged, Is.True);
                Assert.That(recordCount, Is.EqualTo(4));
                Assert.That(payloadCount, Is.EqualTo(4));
                return DescribeTraceMerge(destinationRecords, recordCount, destinationPayload, payloadCount);
            }
            finally
            {
                destinationPayload.Dispose();
                destinationRecords.Dispose();
                secondPayload.Dispose();
                firstPayload.Dispose();
                Assert.That(secondOwner.TryDispose(out secondFailure), Is.True);
                Assert.That(firstOwner.TryDispose(out firstFailure), Is.True);
            }
        }

        private static string DescribeTraceMerge(
            NativeArray<NativeTraceRecordV1> records,
            uint recordCount,
            NativeArray<byte> payload,
            uint payloadCount)
        {
            var result = string.Empty;
            for (var index = 0; index < recordCount; index++)
            {
                var record = records[index];
                if (index != 0) result += "|";
                result += record.TreeInstanceId + ":" + record.Sequence + "@" + record.PayloadOffset + "+" + record.PayloadLength;
                if (record.Kind == NativeTraceEventKindV1.TraceDroppedSummary) result += ":S" + record.DroppedCount;
            }
            result += "|";
            for (var index = 0; index < payloadCount; index++)
            {
                if (index != 0) result += ",";
                result += payload[index];
            }
            var recordSize = UnsafeUtility.SizeOf<NativeTraceRecordV1>();
            var recordByteCount = checked((int)recordCount * recordSize);
            var raw = new byte[recordByteCount + (int)payloadCount];
            var recordBytes = records.Reinterpret<byte>(recordSize);
            for (var index = 0; index < recordByteCount; index++) raw[index] = recordBytes[index];
            for (var index = 0; index < payloadCount; index++) raw[recordByteCount + (int)index] = payload[index];
            return result + "#" + Convert.ToBase64String(raw);
        }

        private static string RunDiagnosticRelatedMerge(int[] order, bool sufficientCapacity)
        {
            var sourceRecords = new NativeArray<NativeDiagnosticRecordV1>(4, Allocator.TempJob);
            var sourceLocations = new NativeArray<NativeDiagnosticLocationV1>(5, Allocator.TempJob);
            var destinationRecords = new NativeArray<NativeDiagnosticRecordV1>(sufficientCapacity ? 3 : 2, Allocator.TempJob);
            var destinationLocations = new NativeArray<NativeDiagnosticLocationV1>(sufficientCapacity ? 4 : 3, Allocator.TempJob);
            try
            {
                var locationNodes = new uint[] { 20, 20, 21, 10, 11 };
                for (var index = 0; index < locationNodes.Length; index++)
                    sourceLocations[index] = new NativeDiagnosticLocationV1(NativeDiagnosticLocationFlagsV1.RuntimeNode, runtimeNodeIndex: locationNodes[index]);
                var logical = new NativeDiagnosticRecordV1[4];
                logical[0] = DiagnosticRecord(7777, 2, 1);
                logical[0].RelatedLocationCount = 1;
                logical[1] = DiagnosticRecord(7777, 2, 1);
                logical[1].Sequence = 9;
                logical[1].RelatedLocationOffset = 1;
                logical[1].RelatedLocationCount = 1;
                logical[2] = DiagnosticRecord(7777, 2, 1);
                logical[2].RelatedLocationOffset = 2;
                logical[2].RelatedLocationCount = 1;
                logical[3] = DiagnosticRecord(7777, 1, 1);
                logical[3].RelatedLocationOffset = 3;
                logical[3].RelatedLocationCount = 2;
                for (var index = 0; index < order.Length; index++) sourceRecords[index] = logical[order[index]];

                if (!sufficientCapacity)
                {
                    var sentinelRecord = DiagnosticRecord(9999, 99, 1);
                    destinationRecords[0] = sentinelRecord;
                    var sentinelLocation = new NativeDiagnosticLocationV1(NativeDiagnosticLocationFlagsV1.RuntimeNode, runtimeNodeIndex: 99);
                    destinationLocations[0] = sentinelLocation;
                    Assert.That(NativeDiagnosticMergeV1.TryMerge(
                        sourceRecords.AsReadOnly(), 4, sourceLocations.AsReadOnly(), 5,
                        destinationRecords, destinationLocations, out var rejectedRecords, out var rejectedLocations), Is.False);
                    Assert.That(rejectedRecords, Is.Zero);
                    Assert.That(rejectedLocations, Is.Zero);
                    Assert.That(destinationRecords[0].CodeNumber, Is.EqualTo(9999));
                    Assert.That(destinationLocations[0], Is.EqualTo(sentinelLocation));
                    return "atomic";
                }

                Assert.That(NativeDiagnosticMergeV1.TryMerge(
                    sourceRecords.AsReadOnly(), 4, sourceLocations.AsReadOnly(), 5,
                    destinationRecords, destinationLocations, out var recordCount, out var locationCount), Is.True);
                Assert.That(recordCount, Is.EqualTo(3));
                Assert.That(locationCount, Is.EqualTo(4));
                var result = string.Empty;
                for (var index = 0; index < recordCount; index++)
                {
                    var record = destinationRecords[index];
                    if (index != 0) result += "|";
                    result += record.PrimaryLocation.RuntimeNodeIndex + "@" + record.RelatedLocationOffset + "+" + record.RelatedLocationCount;
                }
                result += "|";
                for (var index = 0; index < locationCount; index++)
                {
                    if (index != 0) result += ",";
                    result += destinationLocations[index].RuntimeNodeIndex;
                }
                return result;
            }
            finally
            {
                destinationLocations.Dispose();
                destinationRecords.Dispose();
                sourceLocations.Dispose();
                sourceRecords.Dispose();
            }
        }

        private static string RunDiagnosticSnapshotMerge(bool swapSources, bool sufficientCapacity)
        {
            Assert.That(NativeDiagnosticChannelOwnerV1.TryCreate(
                new NativeDiagnosticChannelCapacityV1(1, 1), new TreeInstanceId(1), 0,
                Allocator.Persistent, out var firstOwner, out var firstFailure), Is.True);
            Assert.That(NativeDiagnosticChannelOwnerV1.TryCreate(
                new NativeDiagnosticChannelCapacityV1(2, 2), new TreeInstanceId(1), 1,
                Allocator.Persistent, out var secondOwner, out var secondFailure), Is.True);
            var location = new NativeArray<NativeDiagnosticLocationV1>(1, Allocator.TempJob);
            var destinationRecords = new NativeArray<NativeDiagnosticRecordV1>(sufficientCapacity ? 2 : 1, Allocator.TempJob);
            var destinationLocations = new NativeArray<NativeDiagnosticLocationV1>(sufficientCapacity ? 2 : 1, Allocator.TempJob);
            try
            {
                location[0] = new NativeDiagnosticLocationV1(NativeDiagnosticLocationFlagsV1.RuntimeNode, runtimeNodeIndex: 20);
                Assert.That(firstOwner.TryAcquireWriter(out var firstLease, out firstFailure), Is.True);
                Assert.That(firstLease.Writer.TryAppend(DiagnosticRecord(7777, 2, 1), location.AsReadOnly(), 1), Is.EqualTo(NativeDiagnosticAppendResultV1.Written));
                Assert.That(firstOwner.TryReleaseWriter(firstLease, out firstFailure), Is.True);

                Assert.That(secondOwner.TryAcquireWriter(out var secondLease, out secondFailure), Is.True);
                var duplicate = DiagnosticRecord(7777, 2, 1, worker: 1);
                Assert.That(secondLease.Writer.TryAppend(duplicate, location.AsReadOnly(), 1), Is.EqualTo(NativeDiagnosticAppendResultV1.Written));
                location[0] = new NativeDiagnosticLocationV1(NativeDiagnosticLocationFlagsV1.RuntimeNode, runtimeNodeIndex: 10);
                var earlier = DiagnosticRecord(7777, 1, 1, worker: 1);
                earlier.Sequence = 2;
                Assert.That(secondLease.Writer.TryAppend(earlier, location.AsReadOnly(), 1), Is.EqualTo(NativeDiagnosticAppendResultV1.Written));
                Assert.That(secondOwner.TryReleaseWriter(secondLease, out secondFailure), Is.True);

                Assert.That(firstOwner.TryGetSnapshot(out var firstSnapshot, out firstFailure), Is.True);
                Assert.That(secondOwner.TryGetSnapshot(out var secondSnapshot, out secondFailure), Is.True);
                if (!sufficientCapacity)
                {
                    var sentinel = DiagnosticRecord(9999, 99, 1);
                    destinationRecords[0] = sentinel;
                    var sentinelLocation = new NativeDiagnosticLocationV1(NativeDiagnosticLocationFlagsV1.RuntimeNode, runtimeNodeIndex: 99);
                    destinationLocations[0] = sentinelLocation;
                    var rejected = swapSources
                        ? NativeDiagnosticMergeV1.TryMerge(secondSnapshot, firstSnapshot, destinationRecords, destinationLocations, out var rejectedRecords, out var rejectedLocations)
                        : NativeDiagnosticMergeV1.TryMerge(firstSnapshot, secondSnapshot, destinationRecords, destinationLocations, out rejectedRecords, out rejectedLocations);
                    Assert.That(rejected, Is.False);
                    Assert.That(rejectedRecords, Is.Zero);
                    Assert.That(rejectedLocations, Is.Zero);
                    Assert.That(destinationRecords[0].CodeNumber, Is.EqualTo(9999));
                    Assert.That(destinationLocations[0], Is.EqualTo(sentinelLocation));
                    return "atomic";
                }

                var merged = swapSources
                    ? NativeDiagnosticMergeV1.TryMerge(secondSnapshot, firstSnapshot, destinationRecords, destinationLocations, out var recordCount, out var locationCount)
                    : NativeDiagnosticMergeV1.TryMerge(firstSnapshot, secondSnapshot, destinationRecords, destinationLocations, out recordCount, out locationCount);
                Assert.That(merged, Is.True);
                Assert.That(recordCount, Is.EqualTo(2));
                Assert.That(locationCount, Is.EqualTo(2));
                return destinationRecords[0].PrimaryLocation.RuntimeNodeIndex + "@" + destinationRecords[0].RelatedLocationOffset + "+1|"
                    + destinationRecords[1].PrimaryLocation.RuntimeNodeIndex + "@" + destinationRecords[1].RelatedLocationOffset + "+1|"
                    + destinationLocations[0].RuntimeNodeIndex + "," + destinationLocations[1].RuntimeNodeIndex;
            }
            finally
            {
                destinationLocations.Dispose();
                destinationRecords.Dispose();
                location.Dispose();
                Assert.That(secondOwner.TryDispose(out secondFailure), Is.True);
                Assert.That(firstOwner.TryDispose(out firstFailure), Is.True);
            }
        }

        private static string RunDiagnosticPermutation(uint[] nodes)
        {
            Assert.That(NativeDiagnosticChannelOwnerV1.TryCreate(
                new NativeDiagnosticChannelCapacityV1(2, 2), new TreeInstanceId(1), 0,
                Allocator.Persistent, out var owner, out var failure), Is.True);
            var location = new NativeArray<NativeDiagnosticLocationV1>(1, Allocator.TempJob);
            try
            {
                Assert.That(owner.TryAcquireWriter(out var lease, out failure), Is.True);
                foreach (var node in nodes)
                {
                    location[0] = new NativeDiagnosticLocationV1(NativeDiagnosticLocationFlagsV1.RuntimeNode, runtimeNodeIndex: node + 10);
                    lease.Writer.TryAppend(DiagnosticRecord(7777, node, 1), location.AsReadOnly(), 1);
                }
                Assert.That(owner.TryReleaseWriter(lease, out failure), Is.True);
                Assert.That(owner.TryGetSnapshot(out var snapshot, out failure), Is.True);
                Assert.That(snapshot.IsFaulted, Is.True);
                return snapshot.Records[0].PrimaryLocation.RuntimeNodeIndex + ":"
                    + snapshot.RelatedLocations[(int)snapshot.Records[0].RelatedLocationOffset].RuntimeNodeIndex + "|"
                    + snapshot.Records[1].PrimaryLocation.RuntimeNodeIndex + ":"
                    + snapshot.RelatedLocations[(int)snapshot.Records[1].RelatedLocationOffset].RuntimeNodeIndex + "|reject:"
                    + snapshot.Rejection.PrimaryLocation.RuntimeNodeIndex;
            }
            finally
            {
                location.Dispose();
                Assert.That(owner.TryDispose(out failure), Is.True);
            }
        }

        private static NativeDiagnosticRecordV1 DiagnosticRecord(
            ushort code, uint node, ulong tree, uint worker = 0, DiagnosticSeverity severity = DiagnosticSeverity.Error)
            => new NativeDiagnosticRecordV1
            {
                CodeNumber = code,
                Severity = severity,
                Phase = NativeUpdatePhaseV1.Execute,
                UpdateId = 1,
                SnapshotRevision = 1,
                Sequence = 1,
                WorkerOrdinal = worker,
                PrimaryLocation = new NativeDiagnosticLocationV1(
                    NativeDiagnosticLocationFlagsV1.TreeInstance | NativeDiagnosticLocationFlagsV1.RuntimeNode,
                    tree, node),
            };

        private static NativeTraceRecordV1 TraceRecord(
            ulong sequence, ulong tree = 1, uint worker = 0,
            NativeUpdatePhaseV1 phase = NativeUpdatePhaseV1.Execute,
            NativeTraceEventKindV1 kind = NativeTraceEventKindV1.NodeTicked)
            => new NativeTraceRecordV1
            {
                TraceFormatVersion = NativeTraceRecordV1.FormatVersion,
                Phase = phase,
                UpdateId = 1,
                SnapshotRevision = 1,
                TreeSemanticHash = new NativeHash256V1(Hash()),
                TreeInstanceId = tree,
                Sequence = sequence,
                WorkerOrdinal = worker,
                Kind = kind,
                RuntimeNodeIndex = CompiledIndex.Invalid,
                DebugIdentityIndex = CompiledIndex.Invalid,
                SourceNodeIndex = CompiledIndex.Invalid,
            };

        private static CompiledHash Hash() => new CompiledHash(new string('a', CompiledHash.HexLength));

        private static NativeDiagnosticFieldPairV1 Field(NativeDiagnosticFieldIdV1 id, ulong value)
            => new NativeDiagnosticFieldPairV1(id, NativeDiagnosticValueKindV1.Unsigned, value);

        private static void SetKnownFields(ref NativeDiagnosticRecordV1 record, ushort code)
        {
            if (code == 4301)
            {
                record.FieldCount = 2;
                record.Field0 = Field(NativeDiagnosticFieldIdV1.OwnerKind, 1);
                record.Field1 = Field(NativeDiagnosticFieldIdV1.Allocator, 1);
                return;
            }
            if (code == 4302)
            {
                record.FieldCount = 4;
                record.Field0 = Field(NativeDiagnosticFieldIdV1.ResourceKind, 1);
                record.Field1 = Field(NativeDiagnosticFieldIdV1.Requested, 2);
                record.Field2 = Field(NativeDiagnosticFieldIdV1.Capacity, 1);
                record.Field3 = Field(NativeDiagnosticFieldIdV1.Alignment, 4);
                return;
            }
            if (code == 4303)
            {
                record.FieldCount = 4;
                record.Field0 = Field(NativeDiagnosticFieldIdV1.ResourceKind, 1);
                record.Field1 = Field(NativeDiagnosticFieldIdV1.Operation, 1);
                record.Field2 = Field(NativeDiagnosticFieldIdV1.Left, 2);
                record.Field3 = Field(NativeDiagnosticFieldIdV1.Right, 3);
                return;
            }
            if (code >= 4304 && code <= 4309)
            {
                record.FieldCount = 3;
                record.Field0 = Field(NativeDiagnosticFieldIdV1.ResourceKind, 1);
                record.Field1 = Field(NativeDiagnosticFieldIdV1.Requested, 2);
                record.Field2 = Field(NativeDiagnosticFieldIdV1.Capacity, 1);
                return;
            }
            if (code == 4310)
            {
                record.FieldCount = 4;
                record.Field0 = Field(NativeDiagnosticFieldIdV1.ResourceKind, 1);
                record.Field1 = Field(NativeDiagnosticFieldIdV1.Requested, 2);
                record.Field2 = Field(NativeDiagnosticFieldIdV1.Capacity, 1);
                record.Field3 = Field(NativeDiagnosticFieldIdV1.DroppedCount, 1);
                return;
            }
            record.FieldCount = 6;
            record.Field0 = Field(NativeDiagnosticFieldIdV1.OwnerKind, 1);
            record.Field1 = Field(NativeDiagnosticFieldIdV1.Operation, 1);
            record.Field2 = Field(NativeDiagnosticFieldIdV1.OwnerId, 1);
            record.Field3 = Field(NativeDiagnosticFieldIdV1.Generation, 1);
            record.Field4 = Field(NativeDiagnosticFieldIdV1.LeaseId, 1);
            record.Field5 = Field(NativeDiagnosticFieldIdV1.OwnerState, 1);
        }

        private struct DelayedTraceJob : IJob
        {
            public NativeTraceWriterV1 Writer;
            public NativeTraceRecordV1 Record;
            public void Execute()
            {
                Thread.Sleep(100);
                Writer.TryAppend(Record);
            }
        }

        private struct ParallelTraceAppendJob : IJobParallelFor
        {
            public NativeTraceWriterV1 Writer;
            [ReadOnly] public NativeArray<NativeTraceRecordV1> Records;
            [ReadOnly] public NativeArray<byte> Payloads;
            public NativeArray<NativeTraceAppendResultV1> Results;

            public void Execute(int index)
            {
                var payload = Payloads.GetSubArray(index, 1);
                var record = Records[index];
                Results[index] = Writer.TryAppend(record, payload.AsReadOnly(), 1);
            }
        }

        private struct ParallelDiagnosticAppendJob : IJobParallelFor
        {
            public NativeDiagnosticWriterV1 Writer;
            [ReadOnly] public NativeArray<NativeDiagnosticRecordV1> Records;
            public void Execute(int index)
            {
                var record = Records[index];
                Writer.TryAppend(record);
            }
        }

    }
}
