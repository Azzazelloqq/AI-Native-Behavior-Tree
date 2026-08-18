using System;
using System.Collections.Generic;
using System.Threading;
using AIBT.Burst;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using UnityEngine.TestTools.Constraints;
using Unity.Jobs;
using GcAllocIs = UnityEngine.TestTools.Constraints.Is;
using Is = NUnit.Framework.Is;

namespace AIBT.Tests.Runtime.NativeExecution.CommandsAndAsync
{
    public sealed class NativeCommandsAndAsyncTests
    {
        private static readonly CommandType StartType = new CommandType(101, 1);
        private static readonly CommandType CancelType = new CommandType(102, 1);
        private static readonly CommandType EffectType = new CommandType(103, 1);
        private readonly List<NativeCommandAsyncOwnerV1> _owners = new List<NativeCommandAsyncOwnerV1>();
        private readonly List<NativeCommandMergeOwnerV1> _mergers = new List<NativeCommandMergeOwnerV1>();

        [BurstCompile(CompileSynchronously = true)]
        private struct StartJob : IJob
        {
            public NativeCommandAsyncViewV1 View;
            public NativeArray<OperationId> Output;
            public NativeArray<BurstContextResult> Result;

            public void Execute()
            {
                var result = View.TryStart(
                    new RuntimeNodeIndex(0),
                    1,
                    StartType,
                    CancelType,
                    NativePayloadSliceV1.Empty,
                    NativePayloadSliceV1.Empty,
                    out var operation);
                Output[0] = result == BurstContextResult.Success ? operation : default;
                Result[0] = result;
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct EmitJob : IJob
        {
            public NativeCommandAsyncViewV1 View;
            public uint CommandCount;
            public uint Work;
            public NativeArray<uint> SuccessCount;

            public void Execute()
            {
                var accumulator = 0u;
                for (var index = 0u; index < Work; index++) accumulator = unchecked(accumulator * 1664525u + 1013904223u);
                for (var index = 0u; index < CommandCount; index++)
                {
                    if (View.TryEmitEffect(
                        new CommandType(103, 1),
                        CommandPhase.Execute,
                        NativePayloadSliceV1.Empty,
                        out _) == BurstContextResult.Success)
                    {
                        SuccessCount[0]++;
                    }
                }

                if (accumulator == uint.MaxValue)
                {
                    View.TryEmitEffect(new CommandType(103, 1), CommandPhase.Execute, NativePayloadSliceV1.Empty, out _);
                }
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct AsyncMutationJob : IJob
        {
            public NativeCommandAsyncViewV1 View;
            [ReadOnly] public NativeArray<NativeCompletionInputRecordV1> Input;
            [ReadOnly] public NativeArray<byte> InputPayload;
            [ReadOnly] public NativeArray<uint> Generations;
            public OperationId ConsumeOperation;
            public OperationId CancelOperation;
            public CompletionPayloadType ExpectedPayloadType;
            public NativeArray<BurstContextResult> Results;
            public NativeArray<byte> PayloadResult;
            public NativeArray<uint> EmittedResult;

            public void Execute()
            {
                Results[0] = View.TryNormalizeCompletions(
                    Input.AsReadOnly(), InputPayload.AsReadOnly(), Generations.AsReadOnly());
                Results[1] = View.TryConsume(
                    ConsumeOperation.NodeIndex,
                    ConsumeOperation.ActivationGeneration,
                    ConsumeOperation,
                    NativeCompletionExpectationV1.Typed(ExpectedPayloadType, 1),
                    out var completion);
                Results[2] = completion.TryGetPayloadByte(0, out var value);
                PayloadResult[0] = value;
                Results[3] = View.TryCancel(
                    CancelOperation,
                    CancelType,
                    NativePayloadSliceV1.Empty,
                    out var emitted);
                EmittedResult[0] = emitted ? 1u : 0u;
                Results[4] = View.TryRestart(out var faultEmitted);
                EmittedResult[1] = faultEmitted;
            }
        }

        [TearDown]
        public void TearDown()
        {
            for (var index = _mergers.Count - 1; index >= 0; index--) _mergers[index]?.TryDispose();
            for (var index = _owners.Count - 1; index >= 0; index--) _owners[index]?.TryDispose();
            _mergers.Clear();
            _owners.Clear();
        }

        [Test]
        public void StartCancelAndRestart_PreserveFullIdentityAndIrreversibleTombstones()
        {
            var owner = CreateOwner(new TreeInstanceId(7));
            var lease = Acquire(owner);
            var view = lease.View;

            Assert.That(view.TryStart(
                new RuntimeNodeIndex(3), 9, StartType, CancelType,
                NativePayloadSliceV1.Empty, NativePayloadSliceV1.Empty, out var operation),
                Is.EqualTo(BurstContextResult.Success));
            Assert.That(operation, Is.EqualTo(new OperationId(new TreeInstanceId(7), new RuntimeNodeIndex(3), 9, 1)));
            var foreign = new OperationId(new TreeInstanceId(70), operation.NodeIndex, operation.ActivationGeneration, operation.Sequence);
            Assert.That(view.TryCancel(foreign, CancelType, NativePayloadSliceV1.Empty, out _),
                Is.EqualTo(BurstContextResult.InvalidHandle));
            Assert.That(view.TryCancel(operation, new CommandType(999, 1), NativePayloadSliceV1.Empty, out _),
                Is.EqualTo(BurstContextResult.TypeMismatch));
            Assert.That(owner.TryGetOperationState(operation, out var activeState), Is.EqualTo(BurstContextResult.Success));
            Assert.That(activeState, Is.EqualTo(NativeOperationStateV1.Active));
            Assert.That(view.TryCancel(operation, CancelType, NativePayloadSliceV1.Empty, out var emitted),
                Is.EqualTo(BurstContextResult.Success));
            Assert.That(emitted, Is.True);
            Assert.That(view.TryCancel(operation, CancelType, NativePayloadSliceV1.Empty, out emitted),
                Is.EqualTo(BurstContextResult.Success));
            Assert.That(emitted, Is.False);
            Assert.That(view.TryConsume(
                new RuntimeNodeIndex(3), 9, operation, NativeCompletionExpectationV1.Any, out _),
                Is.EqualTo(BurstContextResult.StaleCompletion));

            Assert.That(view.TryStart(
                new RuntimeNodeIndex(3), 10, StartType, CancelType,
                NativePayloadSliceV1.Empty, NativePayloadSliceV1.Empty, out var restarted),
                Is.EqualTo(BurstContextResult.Success));
            Assert.That(restarted.Sequence, Is.EqualTo(2));
            Assert.That(view.TryRestart(out var restartCancels), Is.EqualTo(BurstContextResult.Success));
            Assert.That(restartCancels, Is.EqualTo(1));
            Assert.That(owner.TryGetOperationState(operation, out var firstState), Is.EqualTo(BurstContextResult.Success));
            Assert.That(owner.TryGetOperationState(restarted, out var secondState), Is.EqualTo(BurstContextResult.Success));
            Assert.That(firstState, Is.EqualTo(NativeOperationStateV1.Cancelled));
            Assert.That(secondState, Is.EqualTo(NativeOperationStateV1.Cancelled));

            RegisterAndComplete(owner, lease);
            Assert.That(owner.TryGetCommandStream(lease, out var stream), Is.EqualTo(BurstContextResult.Success));
            Assert.That(stream.ExecuteCount, Is.EqualTo(2));
            Assert.That(stream.CancelCount, Is.EqualTo(2));
            stream.TryGetRecord(0, out var firstStart);
            stream.TryGetRecord(stream.ExecuteCount, out var firstCancel);
            Assert.That(firstStart.OperationId, Is.EqualTo(operation));
            Assert.That(firstCancel.OperationId, Is.EqualTo(operation));
            Assert.That(firstStart.Sequence, Is.EqualTo(1));
            Assert.That(firstCancel.Sequence, Is.EqualTo(2));
            Assert.That(owner.TryRelease(lease), Is.EqualTo(BurstContextResult.Success));
        }

        [Test]
        public void FaultCleanup_SortsOperationIdentityAndPreservesPerOperationPartialFailure()
        {
            var capacity = Capacity(execute: 2, cancel: 1, commandPayload: 8);
            var owner = CreateOwner(new TreeInstanceId(8), capacity);
            var lease = Acquire(owner);
            var view = lease.View;
            using var faultPayloads = Native((byte)5, (byte)6);
            view.TryStart(new RuntimeNodeIndex(5), 1, StartType, CancelType,
                NativePayloadSliceV1.Empty, new NativePayloadSliceV1(faultPayloads.AsReadOnly(), 0, 1), out var first);
            view.TryStart(new RuntimeNodeIndex(1), 1, StartType, CancelType,
                NativePayloadSliceV1.Empty, new NativePayloadSliceV1(faultPayloads.AsReadOnly(), 1, 1), out var second);

            Assert.That(view.TryFaultCancelAll(out var emitted), Is.EqualTo(BurstContextResult.CapacityExceeded));
            Assert.That(emitted, Is.EqualTo(1));
            Assert.That(owner.TryGetOperationState(first, out var firstState), Is.EqualTo(BurstContextResult.Success));
            Assert.That(owner.TryGetOperationState(second, out var secondState), Is.EqualTo(BurstContextResult.Success));
            Assert.That(firstState, Is.EqualTo(NativeOperationStateV1.Cancelled));
            Assert.That(secondState, Is.EqualTo(NativeOperationStateV1.Cancelled));
            Assert.That(view.TryFaultCancelAll(out emitted), Is.EqualTo(BurstContextResult.Success));
            Assert.That(emitted, Is.Zero);
            RegisterAndComplete(owner, lease);
            owner.TryGetCommandStream(lease, out var stream);
            Assert.That(stream.CancelCount, Is.EqualTo(1));
            stream.TryGetRecord(stream.ExecuteCount, out var cancel);
            Assert.That(cancel.OperationId, Is.EqualTo(second), "P1 fault cleanup sorts full OperationId, not start sequence");
            Assert.That(cancel.Sequence, Is.EqualTo(3));
            Assert.That(stream.TryGetPayloadByte(cancel.PayloadOffset, out var faultByte), Is.EqualTo(BurstContextResult.Success));
            Assert.That(faultByte, Is.EqualTo(6));
            owner.TryRelease(lease);
            Assert.That(owner.TryResetPublishedBuffers(), Is.EqualTo(BurstContextResult.Success));
            var sentinelLease = Acquire(owner);
            Assert.That(sentinelLease.View.TryEmitEffect(EffectType, CommandPhase.Execute,
                NativePayloadSliceV1.Empty, out var nextSequence), Is.EqualTo(BurstContextResult.Success));
            Assert.That(nextSequence, Is.EqualTo(4), "failed Cancel append must not advance command sequence");
            RegisterAndComplete(owner, sentinelLease);
            owner.TryRelease(sentinelLease);
        }

        [Test]
        public void Normalize_MatchesP1OrderingDuplicateHighWaterAndTerminalClassifications()
        {
            var owner = CreateOwner(new TreeInstanceId(9));
            var lease = Acquire(owner);
            var view = lease.View;
            var operations = new OperationId[5];
            for (var index = 0; index < operations.Length; index++)
            {
                Assert.That(view.TryStart(new RuntimeNodeIndex((uint)index), 1, StartType, CancelType,
                    NativePayloadSliceV1.Empty, NativePayloadSliceV1.Empty, out operations[index]),
                    Is.EqualTo(BurstContextResult.Success));
            }

            Assert.That(view.TryCancel(operations[1], CancelType, NativePayloadSliceV1.Empty, out _),
                Is.EqualTo(BurstContextResult.Success));
            using var generations = Native(1u, 1u, 1u, 1u, 1u);
            using var first = Native(
                Record(operations[0], CompletionOutcome.Failed, 1, 5),
                Record(operations[0], CompletionOutcome.Succeeded, 2, 1),
                Record(operations[0], CompletionOutcome.Succeeded, 1, 5));

            Assert.That(view.TryNormalizeCompletions(first.AsReadOnly(), default, generations.AsReadOnly()),
                Is.EqualTo(BurstContextResult.Success));
            AssertDiagnostic(lease, 0, NativeCommandAsyncDiagnosticCodeV1.DuplicateCompletionOrderingKey);
            Assert.That(view.TryNormalizeCompletions(default, default, generations.AsReadOnly()),
                Is.EqualTo(BurstContextResult.Success));
            Assert.That(view.TryConsume(new RuntimeNodeIndex(0), 1, operations[0],
                NativeCompletionExpectationV1.NoPayload, out var consumed), Is.EqualTo(BurstContextResult.Success));
            Assert.That(consumed.Outcome, Is.EqualTo(CompletionOutcome.Succeeded));

            using var staleGenerations = Native(1u, 1u, 1u, 2u, 1u);
            var unknown = new OperationId(new TreeInstanceId(9), new RuntimeNodeIndex(0), 1, 999);
            using var terminal = Native(
                Record(unknown, CompletionOutcome.Succeeded, 3, 1),
                Record(operations[1], CompletionOutcome.Succeeded, 4, 1),
                Record(operations[0], CompletionOutcome.Succeeded, 5, 1),
                Record(operations[3], CompletionOutcome.Succeeded, 6, 1));
            Assert.That(view.TryNormalizeCompletions(terminal.AsReadOnly(), default, staleGenerations.AsReadOnly()),
                Is.EqualTo(BurstContextResult.Success));
            AssertDiagnostic(lease, 0, NativeCommandAsyncDiagnosticCodeV1.DuplicateCompletionOrderingKey);
            AssertDiagnostic(lease, 1, NativeCommandAsyncDiagnosticCodeV1.UnknownOperation);
            AssertDiagnostic(lease, 2, NativeCommandAsyncDiagnosticCodeV1.CancelledOperation);
            AssertDiagnostic(lease, 3, NativeCommandAsyncDiagnosticCodeV1.AlreadyConsumedOperation);
            AssertDiagnostic(lease, 4, NativeCommandAsyncDiagnosticCodeV1.StaleOperationGeneration);

            var acceptedRecord = Record(operations[4], CompletionOutcome.Succeeded, 7, 10);
            using var duplicate = Native(
                acceptedRecord,
                Record(operations[4], CompletionOutcome.Failed, 7, 10));
            Assert.That(view.TryNormalizeCompletions(duplicate.AsReadOnly(), default, staleGenerations.AsReadOnly()),
                Is.EqualTo(BurstContextResult.Success));
            using var acceptedAfterDuplicate = Native(acceptedRecord);
            Assert.That(view.TryNormalizeCompletions(acceptedAfterDuplicate.AsReadOnly(), default, staleGenerations.AsReadOnly()),
                Is.EqualTo(BurstContextResult.Success));
            using var backwards = Native(Record(operations[4], CompletionOutcome.Succeeded, 7, 9));
            Assert.That(view.TryNormalizeCompletions(backwards.AsReadOnly(), default, staleGenerations.AsReadOnly()),
                Is.EqualTo(BurstContextResult.Success));
            AssertDiagnostic(lease, 5, NativeCommandAsyncDiagnosticCodeV1.DuplicateCompletionOrderingKey);
            AssertDiagnostic(lease, 6, NativeCommandAsyncDiagnosticCodeV1.NonIncreasingSourceSequence);

            RegisterAndComplete(owner, lease);
            owner.TryRelease(lease);
        }

        [Test]
        public void TypedCompletion_SkipsMismatchAndConsumesFirstValidPayloadExactlyOnce()
        {
            var owner = CreateOwner(new TreeInstanceId(10));
            var lease = Acquire(owner);
            var view = lease.View;
            view.TryStart(new RuntimeNodeIndex(0), 1, StartType, CancelType,
                NativePayloadSliceV1.Empty, NativePayloadSliceV1.Empty, out var operation);
            using var generations = Native(1u);
            using var payload = Native(
                (byte)1, (byte)2, (byte)3, (byte)4,
                (byte)5, (byte)6, (byte)7, (byte)8,
                (byte)9, (byte)10, (byte)11, (byte)12,
                (byte)13, (byte)14, (byte)15, (byte)16);
            var wrong = new CompletionPayloadType(200, 1);
            var expected = new CompletionPayloadType(201, 2);
            using var records = Native(
                Record(operation, CompletionOutcome.Failed, 1, 1, wrong, 0, 4),
                Record(operation, CompletionOutcome.Failed, 2, 1, new CompletionPayloadType(201, 3), 4, 4),
                Record(operation, CompletionOutcome.Failed, 3, 1, expected, 8, 3),
                Record(operation, CompletionOutcome.Succeeded, 4, 1, expected, 12, 4));
            Assert.That(view.TryNormalizeCompletions(records.AsReadOnly(), payload.AsReadOnly(), generations.AsReadOnly()),
                Is.EqualTo(BurstContextResult.Success));
            Assert.That(view.TryConsume(new RuntimeNodeIndex(0), 1, operation,
                NativeCompletionExpectationV1.Typed(expected, 4), out var completion),
                Is.EqualTo(BurstContextResult.Success));
            Assert.That(completion.Outcome, Is.EqualTo(CompletionOutcome.Succeeded));
            Assert.That(completion.TryGetPayloadByte(0, out var firstByte), Is.EqualTo(BurstContextResult.Success));
            Assert.That(firstByte, Is.EqualTo(13));
            AssertDiagnostic(lease, 0, NativeCommandAsyncDiagnosticCodeV1.CompletionPayloadMismatch);
            AssertDiagnostic(lease, 1, NativeCommandAsyncDiagnosticCodeV1.CompletionPayloadMismatch);
            AssertDiagnostic(lease, 2, NativeCommandAsyncDiagnosticCodeV1.CompletionPayloadMismatch);
            Assert.That(view.TryConsume(new RuntimeNodeIndex(0), 1, operation,
                NativeCompletionExpectationV1.Any, out _), Is.EqualTo(BurstContextResult.StaleCompletion));
            using var empty = new NativeArray<NativeCompletionInputRecordV1>(0, Allocator.Temp);
            Assert.That(view.TryNormalizeCompletions(empty.AsReadOnly(), default, generations.AsReadOnly()),
                Is.EqualTo(BurstContextResult.Success));
            Assert.That(completion.TryGetPayloadByte(0, out _), Is.EqualTo(BurstContextResult.InvalidHandle));
            RegisterAndComplete(owner, lease);
            owner.TryRelease(lease);
        }

        [Test]
        public void DuplicateTieAndDiscardDiagnostics_MatchCanonicalAndReverseP1Order()
        {
            var duplicateOwner = CreateOwner(new TreeInstanceId(18));
            var duplicateLease = Acquire(duplicateOwner);
            var duplicateView = duplicateLease.View;
            duplicateView.TryStart(new RuntimeNodeIndex(5), 1, StartType, CancelType,
                NativePayloadSliceV1.Empty, NativePayloadSliceV1.Empty, out var largerOperation);
            duplicateView.TryStart(new RuntimeNodeIndex(1), 1, StartType, CancelType,
                NativePayloadSliceV1.Empty, NativePayloadSliceV1.Empty, out var canonicalOperation);
            using var duplicateGenerations = Native(1u, 1u, 1u, 1u, 1u, 1u);
            using var forward = Native(
                Record(largerOperation, CompletionOutcome.Failed, 40, 7),
                Record(canonicalOperation, CompletionOutcome.Succeeded, 40, 7));
            using var reverse = Native(
                Record(canonicalOperation, CompletionOutcome.Succeeded, 40, 7),
                Record(largerOperation, CompletionOutcome.Failed, 40, 7));
            Assert.That(duplicateView.TryNormalizeCompletions(
                forward.AsReadOnly(), default, duplicateGenerations.AsReadOnly()), Is.EqualTo(BurstContextResult.Success));
            AssertDiagnosticMetadata(
                duplicateLease, 0, NativeCommandAsyncDiagnosticCodeV1.DuplicateCompletionOrderingKey,
                canonicalOperation, 40, 7);
            Assert.That(duplicateView.TryNormalizeCompletions(
                reverse.AsReadOnly(), default, duplicateGenerations.AsReadOnly()), Is.EqualTo(BurstContextResult.Success));
            AssertDiagnosticMetadata(
                duplicateLease, 1, NativeCommandAsyncDiagnosticCodeV1.DuplicateCompletionOrderingKey,
                canonicalOperation, 40, 7);

            var cancelOwner = CreateOwner(new TreeInstanceId(19));
            var cancelLease = Acquire(cancelOwner);
            var cancelView = cancelLease.View;
            cancelView.TryStart(new RuntimeNodeIndex(0), 1, StartType, CancelType,
                NativePayloadSliceV1.Empty, NativePayloadSliceV1.Empty, out var cancelOperation);
            using var oneGeneration = Native(1u);
            using var cancelRecords = Native(
                Record(cancelOperation, CompletionOutcome.Succeeded, 1, 1),
                Record(cancelOperation, CompletionOutcome.Succeeded, 2, 1),
                Record(cancelOperation, CompletionOutcome.Succeeded, 3, 1));
            Assert.That(cancelView.TryNormalizeCompletions(
                cancelRecords.AsReadOnly(), default, oneGeneration.AsReadOnly()), Is.EqualTo(BurstContextResult.Success));
            Assert.That(cancelView.TryCancel(
                cancelOperation, CancelType, NativePayloadSliceV1.Empty, out _), Is.EqualTo(BurstContextResult.Success));
            AssertDiagnosticMetadata(cancelLease, 0, NativeCommandAsyncDiagnosticCodeV1.CancelledOperation, cancelOperation, 3, 1);
            AssertDiagnosticMetadata(cancelLease, 1, NativeCommandAsyncDiagnosticCodeV1.CancelledOperation, cancelOperation, 2, 1);
            AssertDiagnosticMetadata(cancelLease, 2, NativeCommandAsyncDiagnosticCodeV1.CancelledOperation, cancelOperation, 1, 1);

            var consumeOwner = CreateOwner(new TreeInstanceId(20));
            var consumeLease = Acquire(consumeOwner);
            var consumeView = consumeLease.View;
            consumeView.TryStart(new RuntimeNodeIndex(0), 1, StartType, CancelType,
                NativePayloadSliceV1.Empty, NativePayloadSliceV1.Empty, out var consumeOperation);
            var expected = new CompletionPayloadType(801, 1);
            var wrong = new CompletionPayloadType(802, 1);
            var p1Ledger = new ReferenceOperationLedger(new TreeInstanceId(20));
            p1Ledger.TryAllocate(new RuntimeNodeIndex(0), 1, out var p1Operation, out _);
            var p1Inbox = new ReferenceCompletionInbox(p1Ledger);
            var p1NormalizeDiagnostics = p1Inbox.Normalize(
                new CompletionBatch(
                    new[]
                    {
                        new CompletionRecord(p1Operation, CompletionOutcome.Failed, wrong, 0, 1, 1, 1, default),
                        new CompletionRecord(p1Operation, CompletionOutcome.Succeeded, expected, 1, 1, 2, 1, default),
                        new CompletionRecord(p1Operation, CompletionOutcome.Failed, expected, 2, 1, 3, 1, default),
                        new CompletionRecord(p1Operation, CompletionOutcome.Failed, expected, 3, 1, 4, 1, default),
                    },
                    new byte[] { 1, 2, 3, 4 }),
                new uint[] { 1 });
            Assert.That(p1NormalizeDiagnostics, Is.Empty);
            Assert.That(p1Inbox.PendingCount, Is.EqualTo(4));
            Assert.That(p1Inbox.TryConsume(
                p1Operation,
                ReferenceCompletionExpectation.Typed(expected, 1),
                out _,
                out var p1Diagnostics), Is.True);
            Assert.That(p1Diagnostics.Count, Is.EqualTo(2),
                "P1 DiagnosticCollection deduplicates the two source-less 4106 records");
            Assert.That(p1Diagnostics[0].Code, Is.EqualTo(CommandAsyncDiagnosticCodes.CompletionPayloadMismatch));
            Assert.That(p1Diagnostics[1].Code, Is.EqualTo(CommandAsyncDiagnosticCodes.AlreadyConsumedOperation));
            Assert.That(p1Inbox.PendingCount, Is.Zero, "both later P1 records were discarded before diagnostic deduplication");
            using var consumePayload = Native((byte)1, (byte)2, (byte)3, (byte)4);
            using var consumeRecords = Native(
                Record(consumeOperation, CompletionOutcome.Failed, 1, 1, wrong, 0, 1),
                Record(consumeOperation, CompletionOutcome.Succeeded, 2, 1, expected, 1, 1),
                Record(consumeOperation, CompletionOutcome.Failed, 3, 1, expected, 2, 1),
                Record(consumeOperation, CompletionOutcome.Failed, 4, 1, expected, 3, 1));
            Assert.That(consumeView.TryNormalizeCompletions(
                consumeRecords.AsReadOnly(), consumePayload.AsReadOnly(), oneGeneration.AsReadOnly()),
                Is.EqualTo(BurstContextResult.Success));
            Assert.That(consumeView.TryConsume(
                new RuntimeNodeIndex(0), 1, consumeOperation,
                NativeCompletionExpectationV1.Typed(expected, 1), out _), Is.EqualTo(BurstContextResult.Success));
            AssertDiagnosticMetadata(
                consumeLease, 0, NativeCommandAsyncDiagnosticCodeV1.CompletionPayloadMismatch, consumeOperation, 1, 1);
            AssertDiagnosticMetadata(
                consumeLease, 1, NativeCommandAsyncDiagnosticCodeV1.AlreadyConsumedOperation, consumeOperation, 4, 1);
            AssertDiagnosticMetadata(
                consumeLease, 2, NativeCommandAsyncDiagnosticCodeV1.AlreadyConsumedOperation, consumeOperation, 3, 1);

            RegisterAndComplete(duplicateOwner, duplicateLease);
            duplicateOwner.TryRelease(duplicateLease);
            RegisterAndComplete(cancelOwner, cancelLease);
            cancelOwner.TryRelease(cancelLease);
            RegisterAndComplete(consumeOwner, consumeLease);
            consumeOwner.TryRelease(consumeLease);
        }

        [Test]
        public void DiagnosticCapacity_PreflightsWholeCancelAndConsumeBatches()
        {
            var cancelOwner = CreateOwner(new TreeInstanceId(23), Capacity(diagnostics: 2));
            var cancelLease = Acquire(cancelOwner);
            var cancelView = cancelLease.View;
            cancelView.TryStart(new RuntimeNodeIndex(0), 1, StartType, CancelType,
                NativePayloadSliceV1.Empty, NativePayloadSliceV1.Empty, out var cancelOperation);
            using var generations = Native(1u);
            using var cancelRecords = Native(
                Record(cancelOperation, CompletionOutcome.Succeeded, 1, 1),
                Record(cancelOperation, CompletionOutcome.Succeeded, 2, 1),
                Record(cancelOperation, CompletionOutcome.Succeeded, 3, 1));
            cancelView.TryNormalizeCompletions(cancelRecords.AsReadOnly(), default, generations.AsReadOnly());
            Assert.That(cancelView.TryCancel(
                cancelOperation, CancelType, NativePayloadSliceV1.Empty, out var emitted),
                Is.EqualTo(BurstContextResult.CapacityExceeded));
            Assert.That(emitted, Is.True, "cancel append remains independent of diagnostic publication capacity");
            Assert.That(cancelOwner.TryGetOperationState(cancelOperation, out var state), Is.EqualTo(BurstContextResult.Success));
            Assert.That(state, Is.EqualTo(NativeOperationStateV1.Cancelled));
            Assert.That(cancelView.TryGetDiagnostic(0, out _), Is.EqualTo(BurstContextResult.InvalidHandle),
                "diagnostic batch must not partially publish");
            Assert.That(cancelView.TryConsume(
                new RuntimeNodeIndex(0), 1, cancelOperation, NativeCompletionExpectationV1.Any, out _),
                Is.EqualTo(BurstContextResult.StaleCompletion), "pending records were mandatorily discarded");
            RegisterAndComplete(cancelOwner, cancelLease);
            Assert.That(cancelOwner.TryGetCommandStream(cancelLease, out var cancelStream), Is.EqualTo(BurstContextResult.Success));
            Assert.That(cancelStream.CancelCount, Is.EqualTo(1));

            var consumeOwner = CreateOwner(new TreeInstanceId(24), Capacity(diagnostics: 2));
            var consumeLease = Acquire(consumeOwner);
            var consumeView = consumeLease.View;
            consumeView.TryStart(new RuntimeNodeIndex(0), 1, StartType, CancelType,
                NativePayloadSliceV1.Empty, NativePayloadSliceV1.Empty, out var consumeOperation);
            using var consumeRecords = Native(
                Record(consumeOperation, CompletionOutcome.Succeeded, 1, 1),
                Record(consumeOperation, CompletionOutcome.Succeeded, 2, 1),
                Record(consumeOperation, CompletionOutcome.Succeeded, 3, 1),
                Record(consumeOperation, CompletionOutcome.Succeeded, 4, 1));
            consumeView.TryNormalizeCompletions(consumeRecords.AsReadOnly(), default, generations.AsReadOnly());
            Assert.That(consumeView.TryConsume(
                new RuntimeNodeIndex(0), 1, consumeOperation, NativeCompletionExpectationV1.Any, out _),
                Is.EqualTo(BurstContextResult.CapacityExceeded));
            Assert.That(consumeOwner.TryGetOperationState(consumeOperation, out state), Is.EqualTo(BurstContextResult.Success));
            Assert.That(state, Is.EqualTo(NativeOperationStateV1.Active));
            Assert.That(consumeView.TryGetDiagnostic(0, out _), Is.EqualTo(BurstContextResult.InvalidHandle));

            cancelOwner.TryRelease(cancelLease);
            RegisterAndComplete(consumeOwner, consumeLease);
            consumeOwner.TryRelease(consumeLease);
        }

        [Test]
        public void Normalize_AppendsDiagnosticsAndCapacityFailurePreservesPriorState()
        {
            var owner = CreateOwner(new TreeInstanceId(27), Capacity(diagnostics: 1));
            var lease = Acquire(owner);
            var view = lease.View;
            view.TryStart(new RuntimeNodeIndex(0), 1, StartType, CancelType,
                NativePayloadSliceV1.Empty, NativePayloadSliceV1.Empty, out var operation);
            using var generations = Native(1u);
            using var duplicate = Native(
                Record(operation, CompletionOutcome.Succeeded, 1, 1),
                Record(operation, CompletionOutcome.Failed, 1, 1));
            Assert.That(view.TryNormalizeCompletions(
                duplicate.AsReadOnly(), default, generations.AsReadOnly()), Is.EqualTo(BurstContextResult.Success));
            AssertDiagnosticMetadata(
                lease, 0, NativeCommandAsyncDiagnosticCodeV1.DuplicateCompletionOrderingKey,
                operation, 1, 1);

            var unknown = new OperationId(new TreeInstanceId(27), new RuntimeNodeIndex(0), 1, 999);
            using var staleGenerations = Native(2u);
            using var overflowingDiagnostics = Native(
                Record(unknown, CompletionOutcome.Succeeded, 10, 1),
                Record(operation, CompletionOutcome.Succeeded, 11, 1));
            Assert.That(view.TryNormalizeCompletions(
                overflowingDiagnostics.AsReadOnly(), default, staleGenerations.AsReadOnly()),
                Is.EqualTo(BurstContextResult.CapacityExceeded));
            AssertDiagnosticMetadata(
                lease, 0, NativeCommandAsyncDiagnosticCodeV1.DuplicateCompletionOrderingKey,
                operation, 1, 1);
            Assert.That(view.TryGetDiagnostic(1, out _), Is.EqualTo(BurstContextResult.InvalidHandle));
            Assert.That(owner.TryGetOperationState(operation, out var state), Is.EqualTo(BurstContextResult.Success));
            Assert.That(state, Is.EqualTo(NativeOperationStateV1.Active),
                "failed second normalization must not commit staged stale cancellation");

            using var retry = Native(Record(operation, CompletionOutcome.Succeeded, 10, 1));
            Assert.That(view.TryNormalizeCompletions(
                retry.AsReadOnly(), default, generations.AsReadOnly()), Is.EqualTo(BurstContextResult.Success),
                "failed second normalization must not advance source high-water");
            Assert.That(view.TryConsume(
                new RuntimeNodeIndex(0), 1, operation, NativeCompletionExpectationV1.NoPayload, out _),
                Is.EqualTo(BurstContextResult.Success), "failed second normalization must not mutate inbox");
            AssertDiagnosticMetadata(
                lease, 0, NativeCommandAsyncDiagnosticCodeV1.DuplicateCompletionOrderingKey,
                operation, 1, 1);

            RegisterAndComplete(owner, lease);
            owner.TryRelease(lease);
        }

        [Test]
        public void CompletionOutcomesAndSnapshotProvenance_RoundTripWithoutCurrentRevisionCoupling()
        {
            var owner = CreateOwner(new TreeInstanceId(17));
            var lease = Acquire(owner);
            var view = lease.View;
            var outcomes = new[] { CompletionOutcome.Succeeded, CompletionOutcome.Failed, CompletionOutcome.Cancelled };
            var operations = new OperationId[outcomes.Length];
            for (var index = 0; index < operations.Length; index++)
            {
                view.TryStart(new RuntimeNodeIndex((uint)index), 1, StartType, CancelType,
                    NativePayloadSliceV1.Empty, NativePayloadSliceV1.Empty, out operations[index]);
            }

            using var generations = Native(1u, 1u, 1u);
            using var input = Native(
                Record(operations[0], outcomes[0], 1, 1, snapshotRevision: new Revision(90)),
                Record(operations[1], outcomes[1], 2, 1, snapshotRevision: new Revision(91)),
                Record(operations[2], outcomes[2], 3, 1, snapshotRevision: new Revision(92)));
            Assert.That(view.TryNormalizeCompletions(input.AsReadOnly(), default, generations.AsReadOnly()),
                Is.EqualTo(BurstContextResult.Success));
            for (var index = 0; index < operations.Length; index++)
            {
                Assert.That(view.TryConsume(new RuntimeNodeIndex((uint)index), 1, operations[index],
                    NativeCompletionExpectationV1.NoPayload, out var completion), Is.EqualTo(BurstContextResult.Success));
                Assert.That(completion.Outcome, Is.EqualTo(outcomes[index]));
                Assert.That(completion.SnapshotRevision, Is.EqualTo(new Revision((ulong)(90 + index))));
            }

            RegisterAndComplete(owner, lease);
            owner.TryRelease(lease);
        }

        [Test]
        public void CapacityAndSequenceOverflow_RejectAtomicallyWithoutAdvancingIdsOrCommands()
        {
            var tiny = CreateOwner(new TreeInstanceId(11), Capacity(operationPayload: 0, commandPayload: 0));
            var tinyLease = Acquire(tiny);
            using var oneByte = Native((byte)7);
            Assert.That(tinyLease.View.TryEmitEffect(default, CommandPhase.Execute,
                NativePayloadSliceV1.Empty, out _), Is.EqualTo(BurstContextResult.InvalidEncoding),
                "native AIBT4108 boundary is InvalidEncoding with no publication");
            Assert.That(tinyLease.View.TryStart(new RuntimeNodeIndex(0), 1, StartType, CancelType,
                new NativePayloadSliceV1(oneByte.AsReadOnly(), 0, 1), NativePayloadSliceV1.Empty, out _),
                Is.EqualTo(BurstContextResult.CapacityExceeded));
            Assert.That(tinyLease.View.TryStart(new RuntimeNodeIndex(0), 1, StartType, CancelType,
                NativePayloadSliceV1.Empty, NativePayloadSliceV1.Empty, out var first),
                Is.EqualTo(BurstContextResult.Success));
            Assert.That(first.Sequence, Is.EqualTo(1));
            RegisterAndComplete(tiny, tinyLease);
            tiny.TryGetCommandStream(tinyLease, out var tinyStream);
            Assert.That(tinyStream.ExecuteCount, Is.EqualTo(1));
            tiny.TryRelease(tinyLease);

            Assert.That(NativeCommandAsyncOwnerV1.TryCreate(
                new TreeInstanceId(12), Capacity(), Allocator.Persistent,
                ulong.MaxValue, ulong.MaxValue, out var maxOwner), Is.EqualTo(BurstContextResult.Success));
            _owners.Add(maxOwner);
            var maxLease = Acquire(maxOwner);
            Assert.That(maxLease.View.TryStart(new RuntimeNodeIndex(0), 1, StartType, CancelType,
                NativePayloadSliceV1.Empty, NativePayloadSliceV1.Empty, out var maxOperation),
                Is.EqualTo(BurstContextResult.Success));
            Assert.That(maxOperation.Sequence, Is.EqualTo(ulong.MaxValue));
            Assert.That(maxLease.View.TryStart(new RuntimeNodeIndex(0), 2, StartType, CancelType,
                NativePayloadSliceV1.Empty, NativePayloadSliceV1.Empty, out _),
                Is.EqualTo(BurstContextResult.Overflow), "native AIBT4107 boundary is Overflow");
            Assert.That(maxLease.View.TryCancel(maxOperation, CancelType, NativePayloadSliceV1.Empty, out var emitted),
                Is.EqualTo(BurstContextResult.Overflow), "native AIBT4109 boundary is Overflow after tombstone");
            Assert.That(emitted, Is.False);
            Assert.That(maxOwner.TryGetOperationState(maxOperation, out var state), Is.EqualTo(BurstContextResult.Success));
            Assert.That(state, Is.EqualTo(NativeOperationStateV1.Cancelled));
            RegisterAndComplete(maxOwner, maxLease);
            maxOwner.TryGetCommandStream(maxLease, out var stream);
            Assert.That(stream.ExecuteCount, Is.EqualTo(1));
            Assert.That(stream.CancelCount, Is.Zero);
            maxOwner.TryRelease(maxLease);
        }

        [Test]
        public void CompletionCapacityAndInvalidRanges_FailWholePublishWithoutHighWaterOrInboxMutation()
        {
            var owner = CreateOwner(new TreeInstanceId(16), Capacity(input: 4, pending: 1, completionPayload: 1));
            var lease = Acquire(owner);
            var view = lease.View;
            view.TryStart(new RuntimeNodeIndex(0), 1, StartType, CancelType,
                NativePayloadSliceV1.Empty, NativePayloadSliceV1.Empty, out var first);
            view.TryStart(new RuntimeNodeIndex(1), 1, StartType, CancelType,
                NativePayloadSliceV1.Empty, NativePayloadSliceV1.Empty, out var second);
            using var generations = Native(1u, 1u);
            using var overflowing = Native(
                Record(first, CompletionOutcome.Succeeded, 1, 1),
                Record(second, CompletionOutcome.Succeeded, 2, 1));
            Assert.That(view.TryNormalizeCompletions(overflowing.AsReadOnly(), default, generations.AsReadOnly()),
                Is.EqualTo(BurstContextResult.CapacityExceeded));

            using var accepted = Native(Record(first, CompletionOutcome.Succeeded, 1, 1));
            Assert.That(view.TryNormalizeCompletions(accepted.AsReadOnly(), default, generations.AsReadOnly()),
                Is.EqualTo(BurstContextResult.Success), "failed capacity preflight must not advance source high-water");
            Assert.That(view.TryConsume(new RuntimeNodeIndex(0), 1, first,
                NativeCompletionExpectationV1.NoPayload, out _), Is.EqualTo(BurstContextResult.Success));

            var typed = new CompletionPayloadType(300, 1);
            using var oneByte = Native((byte)9);
            using var invalidRange = Native(Record(second, CompletionOutcome.Succeeded, 3, 1, typed, 1, 1));
            Assert.That(view.TryNormalizeCompletions(invalidRange.AsReadOnly(), oneByte.AsReadOnly(), generations.AsReadOnly()),
                Is.EqualTo(BurstContextResult.InvalidEncoding));
            using var fixedRange = Native(Record(second, CompletionOutcome.Succeeded, 3, 1, typed, 0, 1));
            Assert.That(view.TryNormalizeCompletions(fixedRange.AsReadOnly(), oneByte.AsReadOnly(), generations.AsReadOnly()),
                Is.EqualTo(BurstContextResult.Success), "invalid range must not advance source high-water");
            Assert.That(view.TryConsume(new RuntimeNodeIndex(1), 1, second,
                NativeCompletionExpectationV1.Typed(typed, 1), out _), Is.EqualTo(BurstContextResult.Success));
            Assert.That(view.TryConsume(new RuntimeNodeIndex(1), 1, second,
                NativeCompletionExpectationV1.Typed(default, 0), out _), Is.EqualTo(BurstContextResult.InvalidEncoding));
            RegisterAndComplete(owner, lease);
            owner.TryRelease(lease);
        }

        [Test]
        public void Merge_IsIndependentOfWorkerCompletionOrderAndRewritesPayloadInSortedOrder()
        {
            var firstOwner = CreateOwner(new TreeInstanceId(1));
            var secondOwner = CreateOwner(new TreeInstanceId(2));
            var firstLease = Acquire(firstOwner);
            var secondLease = Acquire(secondOwner);
            using var payload = Native((byte)10, (byte)20, (byte)30);
            Assert.That(firstLease.View.TryEmitEffect(EffectType, CommandPhase.Execute,
                new NativePayloadSliceV1(payload.AsReadOnly(), 0, 1), out _), Is.EqualTo(BurstContextResult.Success));
            Assert.That(firstLease.View.TryEmitEffect(EffectType, CommandPhase.Cancel,
                new NativePayloadSliceV1(payload.AsReadOnly(), 2, 1), out _), Is.EqualTo(BurstContextResult.Success));
            Assert.That(secondLease.View.TryEmitEffect(EffectType, CommandPhase.Execute,
                new NativePayloadSliceV1(payload.AsReadOnly(), 1, 1), out _), Is.EqualTo(BurstContextResult.Success));
            RegisterAndComplete(firstOwner, firstLease);
            RegisterAndComplete(secondOwner, secondLease);
            firstOwner.TryGetCommandStream(firstLease, out var firstStream);
            secondOwner.TryGetCommandStream(secondLease, out var secondStream);

            var merger = CreateMerger(8, 8);
            Assert.That(merger.TryAddStream(secondStream), Is.EqualTo(BurstContextResult.Success));
            Assert.That(merger.TryAddStream(firstStream), Is.EqualTo(BurstContextResult.Success));
            Assert.That(merger.TryFinalize(out var merged), Is.EqualTo(BurstContextResult.Success));
            Assert.That(merged.Count, Is.EqualTo(3));
            merged.TryGetRecord(0, out var record0);
            merged.TryGetRecord(1, out var record1);
            merged.TryGetRecord(2, out var record2);
            Assert.That(record0.TreeInstanceId, Is.EqualTo(new TreeInstanceId(1)));
            Assert.That(record1.TreeInstanceId, Is.EqualTo(new TreeInstanceId(2)));
            Assert.That(record2.Phase, Is.EqualTo(CommandPhase.Cancel));
            merged.TryGetPayloadByte(0, out var byte0);
            merged.TryGetPayloadByte(1, out var byte1);
            merged.TryGetPayloadByte(2, out var byte2);
            CollectionAssert.AreEqual(new byte[] { 10, 20, 30 }, new[] { byte0, byte1, byte2 });

            Assert.That(merger.TryReset(), Is.EqualTo(BurstContextResult.Success));
            Assert.That(merged.TryGetRecord(0, out _), Is.EqualTo(BurstContextResult.InvalidHandle));
            Assert.That(merger.TryAddStream(firstStream), Is.EqualTo(BurstContextResult.Success));
            Assert.That(merger.TryAddStream(firstStream), Is.EqualTo(BurstContextResult.Success));
            Assert.That(merger.TryFinalize(out var rejected), Is.EqualTo(BurstContextResult.InvalidStatus));
            Assert.That(rejected.Count, Is.Zero);
            Assert.That(merger.TryGetOutput(out _), Is.EqualTo(BurstContextResult.PhaseViolation));
            Assert.That(merger.TryReset(), Is.EqualTo(BurstContextResult.Success));
            Assert.That(merger.TryAddStream(secondStream), Is.EqualTo(BurstContextResult.Success));
            Assert.That(merger.TryFinalize(out var duplicateSentinel), Is.EqualTo(BurstContextResult.Success));
            Assert.That(duplicateSentinel.Count, Is.EqualTo(1), "duplicate failure must not partially publish output");

            var tiny = CreateMerger(1, 1);
            Assert.That(tiny.TryAddStream(firstStream), Is.EqualTo(BurstContextResult.CapacityExceeded));
            Assert.That(tiny.TryAddStream(secondStream), Is.EqualTo(BurstContextResult.Success));
            Assert.That(tiny.TryFinalize(out var capacitySentinel), Is.EqualTo(BurstContextResult.Success));
            Assert.That(capacitySentinel.Count, Is.EqualTo(1), "capacity failure must not partially stage a stream");
            capacitySentinel.TryGetPayloadByte(0, out var sentinelByte);
            Assert.That(sentinelByte, Is.EqualTo(20));
            firstOwner.TryRelease(firstLease);
            secondOwner.TryRelease(secondLease);

            var emptyMerger = CreateMerger(2, 0);
            Assert.That(emptyMerger.TryAddStream(default), Is.EqualTo(BurstContextResult.InvalidHandle));
            var emptyOwner = CreateOwner(new TreeInstanceId(25));
            var emptyLease = Acquire(emptyOwner);
            RegisterAndComplete(emptyOwner, emptyLease);
            Assert.That(emptyOwner.TryGetCommandStream(emptyLease, out var emptyStream), Is.EqualTo(BurstContextResult.Success));
            Assert.That(emptyStream.Count, Is.Zero);
            Assert.That(emptyMerger.TryAddStream(emptyStream), Is.EqualTo(BurstContextResult.Success));
            Assert.That(emptyOwner.TryRelease(emptyLease), Is.EqualTo(BurstContextResult.Success));
            Assert.That(emptyMerger.TryAddStream(emptyStream), Is.EqualTo(BurstContextResult.InvalidHandle),
                "released empty streams must not bypass validation");
        }

        [Test]
        public void PermutedBurstJobCompletion_MergesByKeysNotCompletionOrSubmissionOrder()
        {
            var tree1 = CreateOwner(new TreeInstanceId(21));
            var tree2 = CreateOwner(new TreeInstanceId(22));
            var lease1 = Acquire(tree1);
            var lease2 = Acquire(tree2);
            using var slowCount = new NativeArray<uint>(1, Allocator.TempJob);
            using var fastCount = new NativeArray<uint>(1, Allocator.TempJob);
            var slow = new EmitJob { View = lease1.View, CommandCount = 2, Work = 100_000, SuccessCount = slowCount }.Schedule();
            var fast = new EmitJob { View = lease2.View, CommandCount = 1, Work = 1, SuccessCount = fastCount }.Schedule();
            Assert.That(tree1.TryRegisterDependency(lease1, slow), Is.EqualTo(BurstContextResult.Success));
            Assert.That(tree2.TryRegisterDependency(lease2, fast), Is.EqualTo(BurstContextResult.Success));
            JobHandle.ScheduleBatchedJobs();
            while (!fast.IsCompleted) Thread.Yield();
            tree2.TryGetCommandStream(lease2, out var completedFirst);
            while (!slow.IsCompleted) Thread.Yield();
            tree1.TryGetCommandStream(lease1, out var completedLast);
            Assert.That(slowCount[0], Is.EqualTo(2));
            Assert.That(fastCount[0], Is.EqualTo(1));

            var merger = CreateMerger(4, 0);
            Assert.That(merger.TryAddStream(completedFirst), Is.EqualTo(BurstContextResult.Success));
            Assert.That(merger.TryAddStream(completedLast), Is.EqualTo(BurstContextResult.Success));
            Assert.That(merger.TryFinalize(out var merged), Is.EqualTo(BurstContextResult.Success));
            Assert.That(merged.Count, Is.EqualTo(3));
            merged.TryGetRecord(0, out var first);
            merged.TryGetRecord(1, out var second);
            merged.TryGetRecord(2, out var third);
            Assert.That(first.TreeInstanceId, Is.EqualTo(new TreeInstanceId(21)));
            Assert.That(second.TreeInstanceId, Is.EqualTo(new TreeInstanceId(21)));
            Assert.That(third.TreeInstanceId, Is.EqualTo(new TreeInstanceId(22)));
            Assert.That(first.Sequence, Is.EqualTo(1));
            Assert.That(second.Sequence, Is.EqualTo(2));
            tree1.TryRelease(lease1);
            tree2.TryRelease(lease2);
        }

        [Test]
        public void BurstJob_ExecutesNormalizeConsumePayloadCancelAndFaultRestartPaths()
        {
            var owner = CreateOwner(new TreeInstanceId(26));
            var lease = Acquire(owner);
            var view = lease.View;
            view.TryStart(new RuntimeNodeIndex(0), 1, StartType, CancelType,
                NativePayloadSliceV1.Empty, NativePayloadSliceV1.Empty, out var consumeOperation);
            view.TryStart(new RuntimeNodeIndex(1), 1, StartType, CancelType,
                NativePayloadSliceV1.Empty, NativePayloadSliceV1.Empty, out var cancelOperation);
            view.TryStart(new RuntimeNodeIndex(2), 1, StartType, CancelType,
                NativePayloadSliceV1.Empty, NativePayloadSliceV1.Empty, out var faultOperation);
            var payloadType = new CompletionPayloadType(900, 1);
            using var inputPayload = Native((byte)77);
            using var input = Native(Record(
                consumeOperation, CompletionOutcome.Succeeded, 60, 1, payloadType, 0, 1, new Revision(123)));
            using var generations = Native(1u, 1u, 1u);
            using var results = new NativeArray<BurstContextResult>(5, Allocator.TempJob);
            using var payloadResult = new NativeArray<byte>(1, Allocator.TempJob);
            using var emittedResult = new NativeArray<uint>(2, Allocator.TempJob);
            var dependency = new AsyncMutationJob
            {
                View = view,
                Input = input,
                InputPayload = inputPayload,
                Generations = generations,
                ConsumeOperation = consumeOperation,
                CancelOperation = cancelOperation,
                ExpectedPayloadType = payloadType,
                Results = results,
                PayloadResult = payloadResult,
                EmittedResult = emittedResult,
            }.Schedule();
            Assert.That(owner.TryRegisterDependency(lease, dependency), Is.EqualTo(BurstContextResult.Success));
            JobHandle.ScheduleBatchedJobs();
            while (!dependency.IsCompleted) Thread.Yield();
            Assert.That(owner.TryGetCommandStream(lease, out var stream), Is.EqualTo(BurstContextResult.Success));

            for (var index = 0; index < results.Length; index++)
                Assert.That(results[index], Is.EqualTo(BurstContextResult.Success), $"Burst mutation result {index}");
            Assert.That(payloadResult[0], Is.EqualTo(77));
            Assert.That(emittedResult[0], Is.EqualTo(1));
            Assert.That(emittedResult[1], Is.EqualTo(1));
            Assert.That(stream.ExecuteCount, Is.EqualTo(3));
            Assert.That(stream.CancelCount, Is.EqualTo(2));
            Assert.That(owner.TryGetOperationState(consumeOperation, out var consumeState), Is.EqualTo(BurstContextResult.Success));
            Assert.That(owner.TryGetOperationState(cancelOperation, out var cancelState), Is.EqualTo(BurstContextResult.Success));
            Assert.That(owner.TryGetOperationState(faultOperation, out var faultState), Is.EqualTo(BurstContextResult.Success));
            Assert.That(consumeState, Is.EqualTo(NativeOperationStateV1.Consumed));
            Assert.That(cancelState, Is.EqualTo(NativeOperationStateV1.Cancelled));
            Assert.That(faultState, Is.EqualTo(NativeOperationStateV1.Cancelled));
            Assert.That(owner.TryRelease(lease), Is.EqualTo(BurstContextResult.Success));
        }

        [Test]
        public void ScheduledBurstJob_CompletesSafetyStateBeforeHostReadAndRejectsForeignStaleLeases()
        {
            var owner = CreateOwner(new TreeInstanceId(13));
            var foreignOwner = CreateOwner(new TreeInstanceId(14));
            var lease = Acquire(owner);
            using var output = new NativeArray<OperationId>(1, Allocator.TempJob);
            using var result = new NativeArray<BurstContextResult>(1, Allocator.TempJob);
            var handle = new StartJob { View = lease.View, Output = output, Result = result }.Schedule();
            Assert.That(owner.TryRegisterDependency(lease, handle), Is.EqualTo(BurstContextResult.Success));
            JobHandle.ScheduleBatchedJobs();
            while (!handle.IsCompleted) Thread.Yield();
            Assert.That(owner.TryGetCommandStream(lease, out var stream), Is.EqualTo(BurstContextResult.Success));
            Assert.That(result[0], Is.EqualTo(BurstContextResult.Success));
            Assert.That(stream.ExecuteCount, Is.EqualTo(1));
            Assert.That(lease.View.TryEmitEffect(EffectType, CommandPhase.Execute, NativePayloadSliceV1.Empty, out _),
                Is.EqualTo(BurstContextResult.InvalidHandle), "host read seals the execution view");
            Assert.That(stream.ExecuteCount, Is.EqualTo(1), "rejected post-seal mutation must not change publication");
            Assert.That(owner.TryGetCommandStream(lease, out var reread), Is.EqualTo(BurstContextResult.Success));
            Assert.That(reread.ExecuteCount, Is.EqualTo(1));
            Assert.That(output[0].IsValid, Is.True);
            Assert.That(foreignOwner.TryRegisterDependency(lease, default), Is.EqualTo(BurstContextResult.InvalidHandle));
            Assert.That(owner.TryDispose(), Is.EqualTo(BurstContextResult.PhaseViolation));
            Assert.That(owner.TryRelease(lease), Is.EqualTo(BurstContextResult.Success));
            Assert.That(stream.TryGetRecord(0, out _), Is.EqualTo(BurstContextResult.InvalidHandle));
            Assert.That(owner.TryResetPublishedBuffers(), Is.EqualTo(BurstContextResult.Success));
            Assert.That(stream.TryGetRecord(0, out _), Is.EqualTo(BurstContextResult.InvalidHandle));
            Assert.That(lease.View.TryEmitEffect(EffectType, CommandPhase.Execute, NativePayloadSliceV1.Empty, out _),
                Is.EqualTo(BurstContextResult.InvalidHandle));
            Assert.That(owner.TryRelease(lease), Is.EqualTo(BurstContextResult.InvalidHandle));
        }

        [Test]
        public void InitializationFailureInjection_RollsBackEveryPartialNativeAllocation()
        {
            try
            {
                for (var ordinal = 1; ordinal <= 16; ordinal++)
                {
                    NativeCommandAsyncTestHooksV1.FailAllocationAt(ordinal);
                    var result = NativeCommandAsyncOwnerV1.TryCreate(
                        new TreeInstanceId((ulong)(100 + ordinal)),
                        Capacity(),
                        Allocator.Persistent,
                        out var failedOwner);
                    failedOwner?.TryDispose();
                    Assert.That(result, Is.EqualTo(BurstContextResult.CapacityExceeded), $"owner allocation ordinal {ordinal}");
                    Assert.That(failedOwner, Is.Null);
                }

                for (var ordinal = 1; ordinal <= 5; ordinal++)
                {
                    NativeCommandAsyncTestHooksV1.FailAllocationAt(ordinal);
                    var result = NativeCommandMergeOwnerV1.TryCreate(
                        new NativeCommandMergeCapacityV1(4, 4),
                        Allocator.Persistent,
                        out var failedMerge);
                    failedMerge?.TryDispose();
                    Assert.That(result, Is.EqualTo(BurstContextResult.CapacityExceeded), $"merge allocation ordinal {ordinal}");
                    Assert.That(failedMerge, Is.Null);
                }
            }
            finally
            {
                NativeCommandAsyncTestHooksV1.ResetAllocationFailure();
            }

            Assert.That(NativeCommandAsyncOwnerV1.TryCreate(
                new TreeInstanceId(999), Capacity(), Allocator.Persistent, out var owner),
                Is.EqualTo(BurstContextResult.Success));
            Assert.That(owner.TryDispose(), Is.EqualTo(BurstContextResult.Success));
            Assert.That(NativeCommandMergeOwnerV1.TryCreate(
                new NativeCommandMergeCapacityV1(4, 4), Allocator.Persistent, out var merger),
                Is.EqualTo(BurstContextResult.Success));
            Assert.That(merger.TryDispose(), Is.EqualTo(BurstContextResult.Success));
        }

        [Test]
        public void InitializedHotPaths_AllocateNoManagedMemoryWithControlledProbe()
        {
            WarmNormalizeConsumeAndMerge();
            var owner = CreateOwner(new TreeInstanceId(15), Capacity(execute: 2048));
            var lease = Acquire(owner);
            var view = lease.View;
            Assert.That(
                () =>
                {
                    var canary = new byte[128];
                    GC.KeepAlive(canary);
                },
                GcAllocIs.AllocatingGCMemory(), "allocation instrumentation canary");

            var successes = 0;
            Assert.That(
                () =>
                {
                    for (var index = 0; index < 1000; index++)
                    {
                        if (view.TryEmitEffect(EffectType, CommandPhase.Execute,
                                NativePayloadSliceV1.Empty, out _) == BurstContextResult.Success)
                        {
                            successes++;
                        }
                    }
                },
                GcAllocIs.Not.AllocatingGCMemory());
            Assert.That(successes, Is.EqualTo(1000));

            Assert.That(view.TryStart(new RuntimeNodeIndex(0), 1, StartType, CancelType,
                NativePayloadSliceV1.Empty, NativePayloadSliceV1.Empty, out var operation),
                Is.EqualTo(BurstContextResult.Success));
            using var generations = Native(1u);
            using var completionInput = Native(Record(operation, CompletionOutcome.Succeeded, 50, 1));

            var mergeSource = CreateOwner(new TreeInstanceId(150));
            var mergeLease = Acquire(mergeSource);
            using var mergePayload = Native((byte)42);
            Assert.That(mergeLease.View.TryEmitEffect(EffectType, CommandPhase.Execute,
                new NativePayloadSliceV1(mergePayload.AsReadOnly(), 0, 1), out _),
                Is.EqualTo(BurstContextResult.Success));
            RegisterAndComplete(mergeSource, mergeLease);
            mergeSource.TryGetCommandStream(mergeLease, out var nonemptyStream);
            var merger = CreateMerger(4, 4);
            var normalizeResult = BurstContextResult.InvalidStatus;
            var consumeResult = BurstContextResult.InvalidStatus;
            var addResult = BurstContextResult.InvalidStatus;
            var mergeResult = BurstContextResult.InvalidStatus;
            Assert.That(
                () =>
                {
                    normalizeResult = view.TryNormalizeCompletions(
                        completionInput.AsReadOnly(), default, generations.AsReadOnly());
                    consumeResult = view.TryConsume(new RuntimeNodeIndex(0), 1, operation,
                        NativeCompletionExpectationV1.NoPayload, out _);
                    addResult = merger.TryAddStream(nonemptyStream);
                    mergeResult = merger.TryFinalize(out _);
                },
                GcAllocIs.Not.AllocatingGCMemory());
            Assert.That(normalizeResult, Is.EqualTo(BurstContextResult.Success));
            Assert.That(consumeResult, Is.EqualTo(BurstContextResult.Success));
            Assert.That(addResult, Is.EqualTo(BurstContextResult.Success));
            Assert.That(mergeResult, Is.EqualTo(BurstContextResult.Success));
            mergeSource.TryRelease(mergeLease);
            RegisterAndComplete(owner, lease);
            owner.TryRelease(lease);
        }

        private void WarmNormalizeConsumeAndMerge()
        {
            var owner = CreateOwner(new TreeInstanceId(151));
            var lease = Acquire(owner);
            var view = lease.View;
            Assert.That(view.TryStart(new RuntimeNodeIndex(0), 1, StartType, CancelType,
                NativePayloadSliceV1.Empty, NativePayloadSliceV1.Empty, out var operation),
                Is.EqualTo(BurstContextResult.Success));
            using var generations = Native(1u);
            using var completionInput = Native(Record(operation, CompletionOutcome.Succeeded, 1, 1));
            Assert.That(view.TryNormalizeCompletions(completionInput.AsReadOnly(), default, generations.AsReadOnly()),
                Is.EqualTo(BurstContextResult.Success));
            Assert.That(view.TryConsume(new RuntimeNodeIndex(0), 1, operation,
                NativeCompletionExpectationV1.NoPayload, out _), Is.EqualTo(BurstContextResult.Success));

            var source = CreateOwner(new TreeInstanceId(152));
            var sourceLease = Acquire(source);
            Assert.That(sourceLease.View.TryEmitEffect(EffectType, CommandPhase.Execute,
                NativePayloadSliceV1.Empty, out _), Is.EqualTo(BurstContextResult.Success));
            RegisterAndComplete(source, sourceLease);
            Assert.That(source.TryGetCommandStream(sourceLease, out var stream), Is.EqualTo(BurstContextResult.Success));
            var merger = CreateMerger(1, 0);
            Assert.That(merger.TryAddStream(stream), Is.EqualTo(BurstContextResult.Success));
            Assert.That(merger.TryFinalize(out _), Is.EqualTo(BurstContextResult.Success));
            Assert.That(merger.TryDispose(), Is.EqualTo(BurstContextResult.Success));
            Assert.That(source.TryRelease(sourceLease), Is.EqualTo(BurstContextResult.Success));
            Assert.That(source.TryDispose(), Is.EqualTo(BurstContextResult.Success));
            RegisterAndComplete(owner, lease);
            Assert.That(owner.TryRelease(lease), Is.EqualTo(BurstContextResult.Success));
            Assert.That(owner.TryDispose(), Is.EqualTo(BurstContextResult.Success));
        }

        private NativeCommandAsyncOwnerV1 CreateOwner(
            TreeInstanceId treeInstanceId,
            NativeCommandAsyncCapacityV1? capacity = null)
        {
            Assert.That(NativeCommandAsyncOwnerV1.TryCreate(
                treeInstanceId,
                capacity ?? Capacity(),
                Allocator.Persistent,
                out var owner), Is.EqualTo(BurstContextResult.Success));
            _owners.Add(owner);
            return owner;
        }

        private NativeCommandMergeOwnerV1 CreateMerger(uint records, uint payload)
        {
            Assert.That(NativeCommandMergeOwnerV1.TryCreate(
                new NativeCommandMergeCapacityV1(records, payload),
                Allocator.Persistent,
                out var owner), Is.EqualTo(BurstContextResult.Success));
            _mergers.Add(owner);
            return owner;
        }

        private static NativeCommandAsyncLeaseV1 Acquire(NativeCommandAsyncOwnerV1 owner)
        {
            Assert.That(owner.TryAcquireExecution(out var lease), Is.EqualTo(BurstContextResult.Success));
            return lease;
        }

        private static void RegisterAndComplete(NativeCommandAsyncOwnerV1 owner, NativeCommandAsyncLeaseV1 lease)
        {
            Assert.That(owner.TryRegisterDependency(lease, default), Is.EqualTo(BurstContextResult.Success));
        }

        private static void AssertDiagnostic(
            NativeCommandAsyncLeaseV1 lease,
            uint index,
            NativeCommandAsyncDiagnosticCodeV1 code)
        {
            Assert.That(lease.View.TryGetDiagnostic(index, out var diagnostic), Is.EqualTo(BurstContextResult.Success));
            Assert.That(diagnostic.Code, Is.EqualTo(code));
        }

        private static void AssertDiagnosticMetadata(
            NativeCommandAsyncLeaseV1 lease,
            uint index,
            NativeCommandAsyncDiagnosticCodeV1 code,
            OperationId operationId,
            ulong sourceId,
            ulong sourceSequence)
        {
            Assert.That(lease.View.TryGetDiagnostic(index, out var diagnostic), Is.EqualTo(BurstContextResult.Success));
            Assert.That(diagnostic.Code, Is.EqualTo(code));
            Assert.That(diagnostic.OperationId, Is.EqualTo(operationId));
            Assert.That(diagnostic.SourceId, Is.EqualTo(sourceId));
            Assert.That(diagnostic.SourceSequence, Is.EqualTo(sourceSequence));
        }

        private static NativeCommandAsyncCapacityV1 Capacity(
            uint operations = 32,
            uint operationPayload = 64,
            uint input = 32,
            uint pending = 32,
            uint completionPayload = 128,
            uint sources = 32,
            uint diagnostics = 64,
            uint execute = 32,
            uint cancel = 32,
            uint commandPayload = 128)
        {
            return new NativeCommandAsyncCapacityV1(
                operations, operationPayload, input, pending, completionPayload,
                sources, diagnostics, execute, cancel, commandPayload);
        }

        private static NativeCompletionInputRecordV1 Record(
            OperationId operation,
            CompletionOutcome outcome,
            ulong sourceId,
            ulong sourceSequence,
            CompletionPayloadType payloadType = default,
            uint payloadOffset = 0,
            uint payloadSize = 0,
            Revision snapshotRevision = default)
        {
            return new NativeCompletionInputRecordV1(
                operation,
                outcome,
                payloadType,
                payloadOffset,
                payloadSize,
                sourceId,
                sourceSequence,
                snapshotRevision);
        }

        private static NativeArray<T> Native<T>(params T[] values) where T : struct
        {
            var result = new NativeArray<T>(values.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            for (var index = 0; index < values.Length; index++) result[index] = values[index];
            return result;
        }
    }
}
