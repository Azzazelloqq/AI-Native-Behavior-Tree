using System;
using AIBT.Burst;
using AIBT.Execution.Burst.Dispatch;
using NUnit.Framework;
using Unity.Collections;

namespace AIBT.Tests.Runtime.NativeExecution.Dispatch
{
    [TestFixture]
    public sealed class NativeBurstDispatchBindingTransactionTests
    {
        [Test]
        public void SameCaseRequests_ResolveDifferentTargets_AndPublishTheirOwnValues()
        {
            using (var scenario = ValueScenario.Create(2, NativeBurstDispatchFieldEncodingV2.Int32))
            {
                var firstHandle = ExecuteIntWriteRead(scenario, 0, 101);
                AssertBindingInt32(scenario.Owner, 0, 101);
                AssertBindingInt32(scenario.Owner, 4, 2);

                AcquireEnter(scenario, 1, out var frame, out var configuration, out var context);
                Assert.That(BurstGeneratedRuntimeBridge.TryReadBlackboardReadWriteHandle<int>(
                    ref configuration,
                    0,
                    NativeBuiltInBlackboardTypeIdsV1.Int32,
                    1,
                    out var secondHandle), Is.EqualTo(BurstContextResult.Success));
                Assert.That(firstHandle.Ordinal, Is.EqualTo(100u));
                Assert.That(secondHandle.Ordinal, Is.EqualTo(101u));
                Assert.That(context.TryBeginBlackboardRead(firstHandle, out _),
                    Is.EqualTo(BurstContextResult.InvalidHandle),
                    "A handle decoded for the prior request/frame must not resolve in this request.");

                Assert.That(BurstGeneratedRuntimeBridge.TryFailDispatch(
                    ref scenario.Batch,
                    in frame,
                    BurstContextResult.InvalidHandle), Is.EqualTo(BurstContextResult.InvalidHandle));
            }
        }

        [Test]
        public void SealedOverlay_IsVisibleBeforeLifecycle_AndLateFailureRollsItBack()
        {
            using (var scenario = ValueScenario.Create(1, NativeBurstDispatchFieldEncodingV2.Int32))
            {
                AcquireEnter(scenario, 0, out var frame, out var configuration, out var context);
                Assert.That(BurstGeneratedRuntimeBridge.TryReadBlackboardReadWriteHandle<int>(
                    ref configuration,
                    0,
                    NativeBuiltInBlackboardTypeIdsV1.Int32,
                    1,
                    out var handle), Is.EqualTo(BurstContextResult.Success));
                Assert.That(context.TryBeginBlackboardWrite(handle, out var writer),
                    Is.EqualTo(BurstContextResult.Success));
                Assert.That(BurstGeneratedRuntimeBridge.TryWriteValue(ref writer, 0, 0, 99),
                    Is.EqualTo(BurstContextResult.Success));
                Assert.That(BurstGeneratedRuntimeBridge.TryCommitBlackboardWrite(ref writer),
                    Is.EqualTo(BurstContextResult.Success));

                Assert.That(context.TryBeginBlackboardRead(handle, out var overlayReader),
                    Is.EqualTo(BurstContextResult.Success));
                Assert.That(BurstGeneratedRuntimeBridge.TryReadValue(
                    ref overlayReader, 0, 0, out int overlay), Is.EqualTo(BurstContextResult.Success));
                Assert.That(overlay, Is.EqualTo(99));
                Assert.That(BurstGeneratedRuntimeBridge.TryCompleteValueRead(ref overlayReader),
                    Is.EqualTo(BurstContextResult.Success));

                Assert.That(BurstGeneratedRuntimeBridge.TryReadBlackboardReadWriteHandle<int>(
                    ref configuration,
                    0,
                    NativeBuiltInBlackboardTypeIdsV1.Int32,
                    2,
                    out _), Is.EqualTo(BurstContextResult.TypeMismatch));
                Assert.That(context.TryBeginBlackboardRead(handle, out _),
                    Is.EqualTo(BurstContextResult.TypeMismatch),
                    "The first frame failure must stay latched.");
                Assert.That(BurstGeneratedRuntimeBridge.TryCompleteEnter(
                    ref scenario.Batch,
                    in frame,
                    ref context), Is.EqualTo(BurstContextResult.TypeMismatch));
                Assert.That(BurstGeneratedRuntimeBridge.TryFailDispatch(
                    ref scenario.Batch,
                    in frame,
                    BurstContextResult.TypeMismatch), Is.EqualTo(BurstContextResult.TypeMismatch));

                AssertBindingInt32(scenario.Owner, 0, 1);
                Assert.That(scenario.Owner.TryGetTransactionSnapshot(out var snapshot), Is.True);
                Assert.That(snapshot.CommandCount, Is.Zero);
                Assert.That(snapshot.OperationCount, Is.Zero);
                Assert.That(snapshot.SessionCount, Is.Zero);
            }
        }

        [Test]
        public void FloatWriters_CanonicalizeNegativeZeroBeforePublish()
        {
            using (var scenario = ValueScenario.Create(1, NativeBurstDispatchFieldEncodingV2.Float32))
            {
                AcquireEnter(scenario, 0, out var frame, out var configuration, out var context);
                Assert.That(BurstGeneratedRuntimeBridge.TryReadBlackboardReadWriteHandle<float>(
                    ref configuration,
                    0,
                    NativeBuiltInBlackboardTypeIdsV1.Float32,
                    1,
                    out var handle), Is.EqualTo(BurstContextResult.Success));
                Assert.That(context.TryBeginBlackboardWrite(handle, out var writer),
                    Is.EqualTo(BurstContextResult.Success));
                Assert.That(BurstGeneratedRuntimeBridge.TryWriteValue(ref writer, 0, 0, -0f),
                    Is.EqualTo(BurstContextResult.Success));
                Assert.That(BurstGeneratedRuntimeBridge.TryCommitBlackboardWrite(ref writer),
                    Is.EqualTo(BurstContextResult.Success));

                Assert.That(BurstGeneratedRuntimeBridge.TryCreateMemoryAccessor(
                    in frame, out var memory), Is.EqualTo(BurstContextResult.Success));
                Assert.That(BurstGeneratedRuntimeBridge.TryWriteMemoryFloat32(
                    ref memory, 0, 0, -0f), Is.EqualTo(BurstContextResult.Success));
                Assert.That(BurstGeneratedRuntimeBridge.TryCommitMemory(ref memory),
                    Is.EqualTo(BurstContextResult.Success));
                Assert.That(BurstGeneratedRuntimeBridge.TryCompleteEnter(
                    ref scenario.Batch, in frame, ref context), Is.EqualTo(BurstContextResult.Success));

                for (uint byteIndex = 0; byteIndex < 4; byteIndex++)
                {
                    Assert.That(scenario.Owner.TryReadBindingValueByte(byteIndex, out var value), Is.True);
                    Assert.That(value, Is.Zero);
                    Assert.That(scenario.Owner.TryReadCommittedMemoryByte(0, byteIndex, out value), Is.True);
                    Assert.That(value, Is.Zero);
                }
            }
        }

        [TestCase(false, false, TestName = "NestedAsset_MalformedAbsentLocal_IsRejected")]
        [TestCase(true, true, TestName = "NestedAsset_OmittedRuleWithAnchor_IsRejected")]
        public void NestedRegisteredAsset_CanonicalAuthorityFailsClosed(
            bool omitRule,
            bool validBytes)
        {
            using (var input = new NestedAssetInput(omitRule, validBytes))
            {
                var value = input.Value;
                Assert.That(NativeBurstDispatchBatchOwnerV2.TryCreate(
                    in value,
                    Allocator.Persistent,
                    out var owner,
                    out var failure), Is.False);
                Assert.That(owner, Is.Null);
                Assert.That(failure, Is.EqualTo(BurstContextResult.InvalidEncoding));
            }
        }

        [TestCase(FixedStringFailure.Length)]
        [TestCase(FixedStringFailure.Utf8)]
        [TestCase(FixedStringFailure.TrailingByte)]
        public void FixedStringConfiguration_MalformedCanonicalBytesAreRejected(
            FixedStringFailure failureKind)
        {
            using (var input = new FixedStringInput(failureKind))
            {
                var value = input.Value;
                Assert.That(NativeBurstDispatchBatchOwnerV2.TryCreate(
                    in value,
                    Allocator.Persistent,
                    out var owner,
                    out var failure), Is.False);
                Assert.That(owner, Is.Null);
                Assert.That(failure, Is.EqualTo(BurstContextResult.InvalidEncoding));
            }
        }

        [TestCase(LiveAliasForgery.PartialDifferentTarget, false)]
        [TestCase(LiveAliasForgery.FullDifferentTarget, false)]
        [TestCase(LiveAliasForgery.ExactSameTarget, true)]
        public void LiveValueRanges_OnlyExactSemanticAliasesAreAccepted(
            LiveAliasForgery forgery,
            bool expectedSuccess)
        {
            using (var input = new ValueInput(
                       2,
                       NativeBurstDispatchFieldEncodingV2.Int32,
                       false,
                       forgery))
            {
                var value = input.Value;
                var created = NativeBurstDispatchBatchOwnerV2.TryCreate(
                    in value,
                    Allocator.Persistent,
                    out var owner,
                    out var failure);
                Assert.That(created, Is.EqualTo(expectedSuccess));
                Assert.That(failure, Is.EqualTo(
                    expectedSuccess
                        ? BurstContextResult.Success
                        : BurstContextResult.InvalidEncoding));
                if (created)
                {
                    Assert.That(owner.TryDispose(out failure), Is.True, failure.ToString());
                }
                else
                {
                    Assert.That(owner, Is.Null);
                }
            }
        }

        [Test]
        public void Start_PublishesAtomically_AndCancelTombstoneSurvivesFailedAbortFrame()
        {
            using (var scenario = AsyncScenario.Create())
            {
                var operationId = PublishStart(scenario);
                Assert.That(scenario.Owner.TryGetTransactionSnapshot(out var published), Is.True);
                Assert.That(published.CommandCount, Is.EqualTo(1u));
                Assert.That(published.CommandPayloadByteCount, Is.EqualTo(8u));
                Assert.That(published.OperationCount, Is.EqualTo(1u));
                Assert.That(scenario.Owner.TryGetPublishedOperation(0, out var operation), Is.True);
                Assert.That(operation.State, Is.EqualTo(NativeBurstDispatchOperationStateV2.Active));

                AcquireAbort(scenario, out var frame, out var context, out var handle);
                Assert.That(context.TryBeginCancel(handle, operationId, out var writer),
                    Is.EqualTo(BurstContextResult.Success));
                Assert.That(BurstGeneratedRuntimeBridge.TryWriteValue(
                    ref writer, 0, 0, unchecked((int)0x01020304)), Is.EqualTo(BurstContextResult.Success));
                Assert.That(BurstGeneratedRuntimeBridge.TryCommitCancel(ref writer),
                    Is.EqualTo(BurstContextResult.Success));

                Assert.That(scenario.Owner.TryGetTransactionSnapshot(out var cancelled), Is.True);
                Assert.That(cancelled.CommandCount, Is.EqualTo(2u));
                Assert.That(cancelled.CommandPayloadByteCount, Is.EqualTo(12u));
                Assert.That(scenario.Owner.TryGetPublishedOperation(0, out operation), Is.True);
                Assert.That(operation.State, Is.EqualTo(NativeBurstDispatchOperationStateV2.Tombstoned));
                AssertPayload(scenario.Owner, 8, 0x04, 0x03, 0x02, 0x01);

                var failure = BurstGeneratedRuntimeBridge.TryCompleteAbort(
                    ref scenario.Batch,
                    in frame);
                Assert.That(failure, Is.EqualTo(BurstContextResult.IncompleteValue));
                Assert.That(BurstGeneratedRuntimeBridge.TryFailDispatch(
                    ref scenario.Batch,
                    in frame,
                    failure), Is.EqualTo(BurstContextResult.IncompleteValue));

                Assert.That(scenario.Owner.TryGetTransactionSnapshot(out var afterFailure), Is.True);
                Assert.That(afterFailure.CommandCount, Is.EqualTo(2u));
                Assert.That(afterFailure.OperationCount, Is.EqualTo(1u));
                Assert.That(scenario.Owner.TryGetPublishedOperation(0, out operation), Is.True);
                Assert.That(operation.State, Is.EqualTo(NativeBurstDispatchOperationStateV2.Tombstoned));
            }
        }

        [Test]
        public void PartialCancel_ReturnsIncompleteValue_BeforeTombstoning()
        {
            using (var scenario = AsyncScenario.Create())
            {
                var operationId = PublishStart(scenario);
                AcquireAbort(scenario, out var frame, out var context, out var handle);
                Assert.That(context.TryBeginCancel(handle, operationId, out var writer),
                    Is.EqualTo(BurstContextResult.Success));
                var failure = BurstGeneratedRuntimeBridge.TryCommitCancel(ref writer);
                Assert.That(failure, Is.EqualTo(BurstContextResult.IncompleteValue));
                Assert.That(scenario.Owner.TryGetPublishedOperation(0, out var operation), Is.True);
                Assert.That(operation.State, Is.EqualTo(NativeBurstDispatchOperationStateV2.Active));
                Assert.That(scenario.Owner.TryGetTransactionSnapshot(out var snapshot), Is.True);
                Assert.That(snapshot.CommandCount, Is.EqualTo(1u));
                Assert.That(BurstGeneratedRuntimeBridge.TryFailDispatch(
                    ref scenario.Batch,
                    in frame,
                    failure), Is.EqualTo(BurstContextResult.IncompleteValue));
            }
        }

        [Test]
        public void CancelCapacityFailure_LeavesTheIrreversibleTombstoneWithoutAppendingACommand()
        {
            using (var scenario = AsyncScenario.Create(1))
            {
                var operationId = PublishStart(scenario);
                AcquireAbort(scenario, out var frame, out var context, out var handle);
                Assert.That(context.TryBeginCancel(handle, operationId, out var writer),
                    Is.EqualTo(BurstContextResult.Success));
                Assert.That(BurstGeneratedRuntimeBridge.TryWriteValue(ref writer, 0, 0, 22),
                    Is.EqualTo(BurstContextResult.Success));

                var failure = BurstGeneratedRuntimeBridge.TryCommitCancel(ref writer);
                Assert.That(failure, Is.EqualTo(BurstContextResult.CapacityExceeded));
                Assert.That(scenario.Owner.TryGetPublishedOperation(0, out var operation), Is.True);
                Assert.That(operation.State, Is.EqualTo(NativeBurstDispatchOperationStateV2.Tombstoned));
                Assert.That(scenario.Owner.TryGetTransactionSnapshot(out var beforeRollback), Is.True);
                Assert.That(beforeRollback.CommandCount, Is.EqualTo(1u));
                Assert.That(BurstGeneratedRuntimeBridge.TryFailDispatch(
                    ref scenario.Batch,
                    in frame,
                    failure), Is.EqualTo(BurstContextResult.CapacityExceeded));

                Assert.That(scenario.Owner.TryGetPublishedOperation(0, out operation), Is.True);
                Assert.That(operation.State, Is.EqualTo(NativeBurstDispatchOperationStateV2.Tombstoned));
                Assert.That(scenario.Owner.TryGetTransactionSnapshot(out var afterRollback), Is.True);
                Assert.That(afterRollback.CommandCount, Is.EqualTo(1u));
            }
        }

        [Test]
        public void FailedStartFrame_DoesNotPublish_ButDoesNotReuseSequence()
        {
            using (var scenario = AsyncScenario.Create())
            {
                AcquireEnter(scenario, 0, out var frame, out var configuration, out var context);
                Assert.That(BurstGeneratedRuntimeBridge.TryReadAsyncOperationHandle<int, int>(
                    ref configuration,
                    0,
                    NativeBuiltInBlackboardTypeIdsV1.Int32,
                    1,
                    NativeBuiltInBlackboardTypeIdsV1.Int32,
                    1,
                    out var handle), Is.EqualTo(BurstContextResult.Success));
                Assert.That(context.TryBeginStart(handle, out var start, out var faultCancel),
                    Is.EqualTo(BurstContextResult.Success));
                Assert.That(BurstGeneratedRuntimeBridge.TryWriteValue(ref start, 0, 0, 11),
                    Is.EqualTo(BurstContextResult.Success));
                Assert.That(BurstGeneratedRuntimeBridge.TryWriteValue(ref faultCancel, 0, 0, 22),
                    Is.EqualTo(BurstContextResult.Success));
                Assert.That(BurstGeneratedRuntimeBridge.TryCommitStart(
                    ref start, ref faultCancel, out var operationId), Is.EqualTo(BurstContextResult.Success));
                Assert.That(operationId.Sequence, Is.EqualTo(5ul));

                Assert.That(BurstGeneratedRuntimeBridge.TryReadAsyncOperationHandle<int, int>(
                    ref configuration,
                    0,
                    NativeBuiltInBlackboardTypeIdsV1.Int32,
                    2,
                    NativeBuiltInBlackboardTypeIdsV1.Int32,
                    1,
                    out _), Is.EqualTo(BurstContextResult.TypeMismatch));
                Assert.That(BurstGeneratedRuntimeBridge.TryFailDispatch(
                    ref scenario.Batch,
                    in frame,
                    BurstContextResult.TypeMismatch), Is.EqualTo(BurstContextResult.TypeMismatch));

                Assert.That(scenario.Owner.TryGetTransactionSnapshot(out var snapshot), Is.True);
                Assert.That(snapshot.CommandCount, Is.Zero);
                Assert.That(snapshot.OperationCount, Is.Zero);
                Assert.That(snapshot.NextOperationSequence, Is.EqualTo(6ul));
            }
        }

        [Test]
        public void CompletionConsume_PublishesConsumedStateOnlyWithLifecycleSuccess()
        {
            using (var scenario = CompletionScenario.Create())
            {
                AcquireEnter(scenario, 0, out var frame, out var configuration, out var context);
                Assert.That(BurstGeneratedRuntimeBridge.TryReadCompletionHandle<int>(
                    ref configuration,
                    0,
                    NativeBuiltInBlackboardTypeIdsV1.Int32,
                    1,
                    out var handle), Is.EqualTo(BurstContextResult.Success));
                Assert.That(context.TryBeginConsume(
                    handle,
                    CompletionOperation(0),
                    out var outcome,
                    out var reader), Is.EqualTo(BurstContextResult.Success));
                Assert.That(outcome, Is.EqualTo(BurstCompletionOutcome.Succeeded));
                Assert.That(BurstGeneratedRuntimeBridge.TryReadValue(
                    ref reader, 0, 0, out int payload), Is.EqualTo(BurstContextResult.Success));
                Assert.That(payload, Is.EqualTo(41));
                Assert.That(BurstGeneratedRuntimeBridge.TryCompleteValueRead(ref reader),
                    Is.EqualTo(BurstContextResult.Success));
                Assert.That(BurstGeneratedRuntimeBridge.TryCommitConsume(ref reader),
                    Is.EqualTo(BurstContextResult.Success));
                Assert.That(scenario.Owner.TryGetCompletion(0, out var staged), Is.True);
                Assert.That(staged.State, Is.EqualTo(NativeBurstDispatchCompletionStateV2.Available));

                Assert.That(BurstGeneratedRuntimeBridge.TryCreateMemoryAccessor(
                    in frame, out var memory), Is.EqualTo(BurstContextResult.Success));
                Assert.That(BurstGeneratedRuntimeBridge.TryWriteMemoryInt32(
                    ref memory, 0, 0, 1), Is.EqualTo(BurstContextResult.Success));
                Assert.That(BurstGeneratedRuntimeBridge.TryCommitMemory(ref memory),
                    Is.EqualTo(BurstContextResult.Success));
                Assert.That(BurstGeneratedRuntimeBridge.TryCompleteEnter(
                    ref scenario.Batch, in frame, ref context), Is.EqualTo(BurstContextResult.Success));

                Assert.That(scenario.Owner.TryGetCompletion(0, out var published), Is.True);
                Assert.That(published.State, Is.EqualTo(NativeBurstDispatchCompletionStateV2.Consumed));
            }
        }

        [Test]
        public void SnapshotRead_UsesResolvedLiveValueWithoutPublishingAMutation()
        {
            using (var scenario = SnapshotScenario.Create())
            {
                AcquireEnter(scenario, 0, out var frame, out var configuration, out var context);
                Assert.That(BurstGeneratedRuntimeBridge.TryReadSnapshotHandle<int>(
                    ref configuration,
                    0,
                    NativeBuiltInBlackboardTypeIdsV1.Int32,
                    1,
                    out var handle), Is.EqualTo(BurstContextResult.Success));
                Assert.That(context.TryBeginSnapshotRead(handle, out var reader),
                    Is.EqualTo(BurstContextResult.Success));
                Assert.That(BurstGeneratedRuntimeBridge.TryReadValue(
                    ref reader, 0, 0, out int observed), Is.EqualTo(BurstContextResult.Success));
                Assert.That(observed, Is.EqualTo(1));
                Assert.That(BurstGeneratedRuntimeBridge.TryCompleteValueRead(ref reader),
                    Is.EqualTo(BurstContextResult.Success));

                Assert.That(BurstGeneratedRuntimeBridge.TryCreateMemoryAccessor(
                    in frame, out var memory), Is.EqualTo(BurstContextResult.Success));
                Assert.That(BurstGeneratedRuntimeBridge.TryWriteMemoryInt32(
                    ref memory, 0, 0, 1), Is.EqualTo(BurstContextResult.Success));
                Assert.That(BurstGeneratedRuntimeBridge.TryCommitMemory(ref memory),
                    Is.EqualTo(BurstContextResult.Success));
                Assert.That(BurstGeneratedRuntimeBridge.TryCompleteEnter(
                    ref scenario.Batch, in frame, ref context), Is.EqualTo(BurstContextResult.Success));
                AssertBindingInt32(scenario.Owner, 0, 1);
            }
        }

        private static BlackboardReadWriteHandle<int> ExecuteIntWriteRead(
            ValueScenario scenario,
            int requestIndex,
            int value)
        {
            AcquireEnter(scenario, requestIndex, out var frame, out var configuration, out var context);
            Assert.That(BurstGeneratedRuntimeBridge.TryReadBlackboardReadWriteHandle<int>(
                ref configuration,
                0,
                NativeBuiltInBlackboardTypeIdsV1.Int32,
                1,
                out var handle), Is.EqualTo(BurstContextResult.Success));
            Assert.That(context.TryBeginBlackboardWrite(handle, out var writer),
                Is.EqualTo(BurstContextResult.Success));
            Assert.That(BurstGeneratedRuntimeBridge.TryWriteValue(ref writer, 0, 0, value),
                Is.EqualTo(BurstContextResult.Success));
            Assert.That(BurstGeneratedRuntimeBridge.TryCommitBlackboardWrite(ref writer),
                Is.EqualTo(BurstContextResult.Success));
            Assert.That(context.TryBeginBlackboardRead(handle, out var reader),
                Is.EqualTo(BurstContextResult.Success));
            Assert.That(BurstGeneratedRuntimeBridge.TryReadValue(ref reader, 0, 0, out int observed),
                Is.EqualTo(BurstContextResult.Success));
            Assert.That(observed, Is.EqualTo(value));
            Assert.That(BurstGeneratedRuntimeBridge.TryCompleteValueRead(ref reader),
                Is.EqualTo(BurstContextResult.Success));
            Assert.That(BurstGeneratedRuntimeBridge.TryCreateMemoryAccessor(
                in frame, out var memory), Is.EqualTo(BurstContextResult.Success));
            Assert.That(BurstGeneratedRuntimeBridge.TryWriteMemoryInt32(
                ref memory, 0, 0, requestIndex + 10), Is.EqualTo(BurstContextResult.Success));
            Assert.That(BurstGeneratedRuntimeBridge.TryCommitMemory(ref memory),
                Is.EqualTo(BurstContextResult.Success));
            Assert.That(BurstGeneratedRuntimeBridge.TryCompleteEnter(
                ref scenario.Batch, in frame, ref context), Is.EqualTo(BurstContextResult.Success));
            return handle;
        }

        private static OperationId PublishStart(AsyncScenario scenario)
        {
            AcquireEnter(scenario, 0, out var frame, out var configuration, out var context);
            Assert.That(BurstGeneratedRuntimeBridge.TryReadAsyncOperationHandle<int, int>(
                ref configuration,
                0,
                NativeBuiltInBlackboardTypeIdsV1.Int32,
                1,
                NativeBuiltInBlackboardTypeIdsV1.Int32,
                1,
                out var handle), Is.EqualTo(BurstContextResult.Success));
            Assert.That(context.TryBeginStart(handle, out var start, out var faultCancel),
                Is.EqualTo(BurstContextResult.Success));
            Assert.That(BurstGeneratedRuntimeBridge.TryWriteValue(ref start, 0, 0, 11),
                Is.EqualTo(BurstContextResult.Success));
            Assert.That(BurstGeneratedRuntimeBridge.TryWriteValue(ref faultCancel, 0, 0, 22),
                Is.EqualTo(BurstContextResult.Success));
            Assert.That(BurstGeneratedRuntimeBridge.TryCommitStart(
                ref start, ref faultCancel, out var operationId), Is.EqualTo(BurstContextResult.Success));

            Assert.That(scenario.Owner.TryGetTransactionSnapshot(out var staged), Is.True);
            Assert.That(staged.CommandCount, Is.Zero);
            Assert.That(staged.OperationCount, Is.Zero);
            Assert.That(staged.NextOperationSequence, Is.EqualTo(6ul));

            Assert.That(BurstGeneratedRuntimeBridge.TryCreateMemoryAccessor(
                in frame, out var memory), Is.EqualTo(BurstContextResult.Success));
            Assert.That(BurstGeneratedRuntimeBridge.TryWriteMemoryInt32(ref memory, 0, 0, 1),
                Is.EqualTo(BurstContextResult.Success));
            Assert.That(BurstGeneratedRuntimeBridge.TryCommitMemory(ref memory),
                Is.EqualTo(BurstContextResult.Success));
            Assert.That(BurstGeneratedRuntimeBridge.TryCompleteEnter(
                ref scenario.Batch, in frame, ref context), Is.EqualTo(BurstContextResult.Success));
            return operationId;
        }

        private static void AcquireAbort(
            AsyncScenario scenario,
            out BurstDispatchFrame frame,
            out BurstAbortContext context,
            out AsyncOperationHandle<int, int> handle)
        {
            AssertNextRequest(scenario, 1, 10, BurstCallbackPhase.Abort);
            Assert.That(BurstGeneratedRuntimeBridge.TryAcquireDispatchFrame(
                ref scenario.Batch, 1, 10, 0, BurstCallbackPhase.Abort, out frame),
                Is.EqualTo(BurstContextResult.Success));
            Assert.That(BurstGeneratedRuntimeBridge.TryCreateConfigurationReader(
                in frame, out var configuration), Is.EqualTo(BurstContextResult.Success));
            Assert.That(BurstGeneratedRuntimeBridge.TryReadAsyncOperationHandle<int, int>(
                ref configuration,
                0,
                NativeBuiltInBlackboardTypeIdsV1.Int32,
                1,
                NativeBuiltInBlackboardTypeIdsV1.Int32,
                1,
                out handle), Is.EqualTo(BurstContextResult.Success));
            Assert.That(BurstGeneratedRuntimeBridge.TryCreateAbortContext(in frame, out context),
                Is.EqualTo(BurstContextResult.Success));
        }

        private static void AcquireEnter(
            ScenarioBase scenario,
            int requestIndex,
            out BurstDispatchFrame frame,
            out BurstConfigurationReader configuration,
            out BurstEnterContext context)
        {
            AssertNextRequest(
                scenario,
                (uint)requestIndex,
                (uint)(10 + requestIndex),
                BurstCallbackPhase.Enter);
            Assert.That(BurstGeneratedRuntimeBridge.TryAcquireDispatchFrame(
                ref scenario.Batch,
                (uint)requestIndex,
                (uint)(10 + requestIndex),
                0,
                BurstCallbackPhase.Enter,
                out frame), Is.EqualTo(BurstContextResult.Success));
            Assert.That(BurstGeneratedRuntimeBridge.TryCreateConfigurationReader(
                in frame, out configuration), Is.EqualTo(BurstContextResult.Success));
            Assert.That(BurstGeneratedRuntimeBridge.TryCreateEnterContext(in frame, out context),
                Is.EqualTo(BurstContextResult.Success));
        }

        private static void AcquireEnter(
            AsyncScenario scenario,
            int requestIndex,
            out BurstDispatchFrame frame,
            out BurstConfigurationReader configuration,
            out BurstEnterContext context)
        {
            AssertNextRequest(scenario, (uint)requestIndex, 10, BurstCallbackPhase.Enter);
            Assert.That(BurstGeneratedRuntimeBridge.TryAcquireDispatchFrame(
                ref scenario.Batch,
                (uint)requestIndex,
                10,
                0,
                BurstCallbackPhase.Enter,
                out frame), Is.EqualTo(BurstContextResult.Success));
            Assert.That(BurstGeneratedRuntimeBridge.TryCreateConfigurationReader(
                in frame, out configuration), Is.EqualTo(BurstContextResult.Success));
            Assert.That(BurstGeneratedRuntimeBridge.TryCreateEnterContext(in frame, out context),
                Is.EqualTo(BurstContextResult.Success));
        }

        private static void AssertNextRequest(
            ScenarioBase scenario,
            uint instanceOrdinal,
            uint runtimeNodeIndex,
            BurstCallbackPhase phase)
        {
            Assert.That(BurstGeneratedRuntimeBridge.TryGetExecutionRequest(
                in scenario.Batch,
                out var actualInstanceOrdinal,
                out var actualRuntimeNodeIndex,
                out var catalogCaseIndex,
                out var actualPhase,
                out var hasWork), Is.EqualTo(BurstContextResult.Success));
            Assert.That(hasWork, Is.True);
            Assert.That(actualInstanceOrdinal, Is.EqualTo(instanceOrdinal));
            Assert.That(actualRuntimeNodeIndex, Is.EqualTo(runtimeNodeIndex));
            Assert.That(catalogCaseIndex, Is.Zero);
            Assert.That(actualPhase, Is.EqualTo(phase));
        }

        private static void AssertBindingInt32(
            NativeBurstDispatchBatchOwnerV2 owner,
            uint offset,
            int expected)
        {
            uint bits = 0;
            for (uint byteIndex = 0; byteIndex < 4; byteIndex++)
            {
                Assert.That(owner.TryReadBindingValueByte(offset + byteIndex, out var value), Is.True);
                bits |= (uint)value << (int)(byteIndex * 8);
            }

            Assert.That(unchecked((int)bits), Is.EqualTo(expected));
        }

        private static void AssertPayload(
            NativeBurstDispatchBatchOwnerV2 owner,
            uint offset,
            params byte[] expected)
        {
            for (uint index = 0; index < expected.Length; index++)
            {
                Assert.That(owner.TryReadPublishedCommandPayloadByte(offset + index, out var value), Is.True);
                Assert.That(value, Is.EqualTo(expected[index]));
            }
        }

        private static BurstCatalogHandshake Handshake()
            => new BurstCatalogHandshake(
                2,
                new BurstCatalogFingerprint(Hash(0x10)),
                Hash(0x20),
                1,
                1,
                Hash(0x30),
                Hash(0x40),
                Hash(0x50));

        private static BurstHash256 Hash(uint first)
            => new BurstHash256(
                first,
                first + 1,
                first + 2,
                first + 3,
                first + 4,
                first + 5,
                first + 6,
                first + 7);

        private static OperationId CompletionOperation(int requestIndex)
            => new OperationId(
                new TreeInstanceId((ulong)(100 + requestIndex)),
                new RuntimeNodeIndex((uint)(10 + requestIndex)),
                1,
                (ulong)(requestIndex + 1));

        private static void WriteUInt32(NativeArray<byte> bytes, int offset, uint value)
        {
            bytes[offset] = (byte)value;
            bytes[offset + 1] = (byte)(value >> 8);
            bytes[offset + 2] = (byte)(value >> 16);
            bytes[offset + 3] = (byte)(value >> 24);
        }

        public enum FixedStringFailure : byte
        {
            Length = 0,
            Utf8 = 1,
            TrailingByte = 2
        }

        public enum LiveAliasForgery : byte
        {
            None = 0,
            PartialDifferentTarget = 1,
            FullDifferentTarget = 2,
            ExactSameTarget = 3
        }

        private abstract class ScenarioBase : IDisposable
        {
            private bool _disposed;

            protected ScenarioBase(NativeBurstDispatchBatchOwnerV2 owner, BurstExecutionBatch batch)
            {
                Owner = owner;
                Batch = batch;
            }

            internal NativeBurstDispatchBatchOwnerV2 Owner { get; }
            internal BurstExecutionBatch Batch;

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                Assert.That(Owner.TryDispose(out var failure), Is.True, failure.ToString());
                _disposed = true;
            }
        }

        private sealed class ValueScenario : ScenarioBase
        {
            private ValueScenario(NativeBurstDispatchBatchOwnerV2 owner, BurstExecutionBatch batch)
                : base(owner, batch)
            {
            }

            internal static ValueScenario Create(
                int requestCount,
                NativeBurstDispatchFieldEncodingV2 encoding)
            {
                using (var input = new ValueInput(requestCount, encoding))
                {
                    var value = input.Value;
                    Assert.That(NativeBurstDispatchBatchOwnerV2.TryCreate(
                        in value,
                        Allocator.Persistent,
                        out var owner,
                        out var failure), Is.True, failure.ToString());
                    Assert.That(owner.TryAcquireImmediateBatch(out var batch), Is.True);
                    return new ValueScenario(owner, batch);
                }
            }
        }

        private sealed class AsyncScenario : ScenarioBase
        {
            private AsyncScenario(NativeBurstDispatchBatchOwnerV2 owner, BurstExecutionBatch batch)
                : base(owner, batch)
            {
            }

            internal static AsyncScenario Create(uint maxCommands = 8)
            {
                using (var input = new AsyncInput(maxCommands))
                {
                    var value = input.Value;
                    Assert.That(NativeBurstDispatchBatchOwnerV2.TryCreate(
                        in value,
                        Allocator.Persistent,
                        out var owner,
                        out var failure), Is.True, failure.ToString());
                    Assert.That(owner.TryAcquireImmediateBatch(out var batch), Is.True);
                    return new AsyncScenario(owner, batch);
                }
            }
        }

        private sealed class CompletionScenario : ScenarioBase
        {
            private CompletionScenario(NativeBurstDispatchBatchOwnerV2 owner, BurstExecutionBatch batch)
                : base(owner, batch)
            {
            }

            internal static CompletionScenario Create()
            {
                using (var input = new ValueInput(1, NativeBurstDispatchFieldEncodingV2.Int32, true))
                {
                    var value = input.Value;
                    Assert.That(NativeBurstDispatchBatchOwnerV2.TryCreate(
                        in value,
                        Allocator.Persistent,
                        out var owner,
                        out var failure), Is.True, failure.ToString());
                    Assert.That(owner.TryAcquireImmediateBatch(out var batch), Is.True);
                    return new CompletionScenario(owner, batch);
                }
            }
        }

        private sealed class SnapshotScenario : ScenarioBase
        {
            private SnapshotScenario(NativeBurstDispatchBatchOwnerV2 owner, BurstExecutionBatch batch)
                : base(owner, batch)
            {
            }

            internal static SnapshotScenario Create()
            {
                using (var input = new ValueInput(
                           1,
                           NativeBurstDispatchFieldEncodingV2.Int32,
                           NativeBurstDispatchBindingKindV2.SnapshotRead))
                {
                    var value = input.Value;
                    Assert.That(NativeBurstDispatchBatchOwnerV2.TryCreate(
                        in value,
                        Allocator.Persistent,
                        out var owner,
                        out var failure), Is.True, failure.ToString());
                    Assert.That(owner.TryAcquireImmediateBatch(out var batch), Is.True);
                    return new SnapshotScenario(owner, batch);
                }
            }
        }

        private sealed class ValueInput : IDisposable
        {
            private readonly NativeArray<NativeBurstDispatchCaseV2> _cases;
            private readonly NativeArray<NativeBurstDispatchRequestV2> _requests;
            private readonly NativeArray<NativeBurstDispatchFieldV2> _configurationFields;
            private readonly NativeArray<NativeBurstDispatchFieldV2> _memoryFields;
            private readonly NativeArray<byte> _configurationBytes;
            private readonly NativeArray<byte> _memoryBytes;
            private readonly NativeArray<ulong> _randomStates;
            private readonly NativeArray<ulong> _randomIncrements;
            private readonly NativeArray<NativeBurstDispatchBindingV2> _bindings;
            private readonly NativeArray<NativeBurstDispatchResolvedBindingV2> _resolved;
            private readonly NativeArray<NativeBurstDispatchFieldV2> _valueFields;
            private readonly NativeArray<byte> _liveBytes;
            private readonly NativeArray<NativeBurstDispatchCompletionV2> _completions;
            private readonly NativeArray<byte> _completionBytes;
            private readonly NativeArray<NativeBurstDispatchCanonicalRangeV2> _caseRanges;
            private readonly NativeArray<NativeBurstDispatchCanonicalRangeV2> _bindingRanges;
            private readonly NativeArray<NativeBurstDispatchCanonicalRuleV2> _rules;

            internal ValueInput(int requestCount, NativeBurstDispatchFieldEncodingV2 encoding)
                : this(requestCount, encoding, false, LiveAliasForgery.None)
            {
            }

            internal ValueInput(
                int requestCount,
                NativeBurstDispatchFieldEncodingV2 encoding,
                bool completion)
                : this(requestCount, encoding, completion, LiveAliasForgery.None)
            {
            }

            internal ValueInput(
                int requestCount,
                NativeBurstDispatchFieldEncodingV2 encoding,
                NativeBurstDispatchBindingKindV2 bindingKind)
                : this(requestCount, encoding, bindingKind, LiveAliasForgery.None)
            {
            }

            internal ValueInput(
                int requestCount,
                NativeBurstDispatchFieldEncodingV2 encoding,
                bool completion,
                LiveAliasForgery liveAliasForgery)
                : this(
                    requestCount,
                    encoding,
                    completion
                        ? NativeBurstDispatchBindingKindV2.Completion
                        : NativeBurstDispatchBindingKindV2.BlackboardReadWrite,
                    liveAliasForgery)
            {
            }

            private ValueInput(
                int requestCount,
                NativeBurstDispatchFieldEncodingV2 encoding,
                NativeBurstDispatchBindingKindV2 bindingKind,
                LiveAliasForgery liveAliasForgery)
            {
                var completion = bindingKind == NativeBurstDispatchBindingKindV2.Completion;
                if (completion && liveAliasForgery != LiveAliasForgery.None)
                {
                    throw new ArgumentException("Completion inputs cannot forge live-value aliases.");
                }

                if (bindingKind != NativeBurstDispatchBindingKindV2.BlackboardReadWrite
                    && bindingKind != NativeBurstDispatchBindingKindV2.SnapshotRead
                    && !completion)
                {
                    throw new ArgumentException("This fixture only supports value, snapshot, or completion bindings.");
                }

                var typeId = encoding == NativeBurstDispatchFieldEncodingV2.Float32
                    ? NativeBuiltInBlackboardTypeIdsV1.Float32
                    : NativeBuiltInBlackboardTypeIdsV1.Int32;
                _cases = new NativeArray<NativeBurstDispatchCaseV2>(1, Allocator.Temp);
                _requests = new NativeArray<NativeBurstDispatchRequestV2>(requestCount, Allocator.Temp);
                _configurationFields = new NativeArray<NativeBurstDispatchFieldV2>(1, Allocator.Temp);
                _memoryFields = new NativeArray<NativeBurstDispatchFieldV2>(1, Allocator.Temp);
                _configurationBytes = new NativeArray<byte>(requestCount * 4, Allocator.Temp);
                _memoryBytes = new NativeArray<byte>(requestCount * 4, Allocator.Temp);
                _randomStates = new NativeArray<ulong>(0, Allocator.Temp);
                _randomIncrements = new NativeArray<ulong>(0, Allocator.Temp);
                _bindings = new NativeArray<NativeBurstDispatchBindingV2>(1, Allocator.Temp);
                _resolved = new NativeArray<NativeBurstDispatchResolvedBindingV2>(requestCount, Allocator.Temp);
                _valueFields = new NativeArray<NativeBurstDispatchFieldV2>(1, Allocator.Temp);
                var liveByteCount = completion
                    ? 0
                    : liveAliasForgery == LiveAliasForgery.PartialDifferentTarget
                        ? 6
                        : liveAliasForgery == LiveAliasForgery.FullDifferentTarget
                            || liveAliasForgery == LiveAliasForgery.ExactSameTarget
                            ? 4
                            : requestCount * 4;
                _liveBytes = new NativeArray<byte>(liveByteCount, Allocator.Temp);
                _completions = new NativeArray<NativeBurstDispatchCompletionV2>(
                    completion ? requestCount : 0,
                    Allocator.Temp);
                _completionBytes = new NativeArray<byte>(
                    completion ? requestCount * 4 : 0,
                    Allocator.Temp);
                _caseRanges = new NativeArray<NativeBurstDispatchCanonicalRangeV2>(2, Allocator.Temp);
                _bindingRanges = new NativeArray<NativeBurstDispatchCanonicalRangeV2>(2, Allocator.Temp);
                _rules = new NativeArray<NativeBurstDispatchCanonicalRuleV2>(0, Allocator.Temp);

                _cases[0] = new NativeBurstDispatchCaseV2(
                    9001,
                    1,
                    0,
                    0,
                    1,
                    4,
                    0,
                    1,
                    4,
                    NativeBurstDispatchPhaseMaskV2.Enter,
                    BurstNodeStatusMask.Success,
                    false,
                    0,
                    1);
                _configurationFields[0] = new NativeBurstDispatchFieldV2(
                    0, 0, 1, 4, NativeBurstDispatchFieldEncodingV2.GeneratedHandle);
                _memoryFields[0] = new NativeBurstDispatchFieldV2(0, 0, 1, 4, encoding);
                _valueFields[0] = new NativeBurstDispatchFieldV2(0, 0, 1, 4, encoding);
                _bindings[0] = new NativeBurstDispatchBindingV2(
                    0,
                    0,
                    bindingKind,
                    bindingKind == NativeBurstDispatchBindingKindV2.BlackboardReadWrite
                        ? (byte)BlackboardScope.Tree
                        : NativeBurstDispatchBindingV2.NoScope,
                    completion
                        ? NativeBurstDispatchBindingPhaseMaskV2.Completion
                        : NativeBurstDispatchBindingPhaseMaskV2.None,
                    typeId,
                    1,
                    0,
                    1,
                    4,
                    0,
                    0,
                    0,
                    0,
                    0);
                for (var index = 0; index < requestCount; index++)
                {
                    _requests[index] = new NativeBurstDispatchRequestV2(
                        (uint)index,
                        (uint)(10 + index),
                        9001,
                        1,
                        0,
                        BurstCallbackPhase.Enter,
                        (uint)(index * 4),
                        (uint)(index * 4),
                        0,
                        123,
                        new TreeInstanceId((ulong)(100 + index)),
                        1,
                        (uint)index,
                        1);
                    var liveOffset = liveAliasForgery == LiveAliasForgery.PartialDifferentTarget
                        && index == 1
                            ? 2u
                            : liveAliasForgery == LiveAliasForgery.FullDifferentTarget
                                || liveAliasForgery == LiveAliasForgery.ExactSameTarget
                                ? 0u
                                : (uint)(index * 4);
                    var targetOrdinal = liveAliasForgery == LiveAliasForgery.ExactSameTarget
                        ? 100u
                        : (uint)(100 + index);
                    _resolved[index] = new NativeBurstDispatchResolvedBindingV2(
                        0,
                        targetOrdinal,
                        completion
                            ? NativeBurstDispatchBindingV2.NoOffset
                            : liveOffset);
                    WriteUInt32(_configurationBytes, index * 4, 0);
                    if (completion)
                    {
                        _completions[index] = new NativeBurstDispatchCompletionV2(
                            (uint)(100 + index),
                            CompletionOperation(index),
                            BurstCompletionOutcome.Succeeded,
                            (uint)(index * 4));
                        WriteUInt32(_completionBytes, index * 4, (uint)(41 + index));
                    }
                    else
                    {
                        WriteUInt32(_liveBytes, (int)liveOffset, (uint)(index + 1));
                    }
                }

                var canonical = new NativeBurstDispatchCanonicalInputV2(
                    _caseRanges.AsReadOnly(),
                    _bindingRanges.AsReadOnly(),
                    _rules.AsReadOnly());
                var bindingInput = new NativeBurstDispatchBindingInputV2(
                    _bindings.AsReadOnly(),
                    _resolved.AsReadOnly(),
                    _valueFields.AsReadOnly(),
                    _liveBytes.AsReadOnly(),
                    _completions.AsReadOnly(),
                    _completionBytes.AsReadOnly(),
                    new NativeBurstDispatchBindingCapacityV2(16, 128, 8, 128, 8),
                    canonical);
                Value = new NativeBurstDispatchCreateInputV2(
                    Handshake(),
                    _cases.AsReadOnly(),
                    _requests.AsReadOnly(),
                    _configurationFields.AsReadOnly(),
                    _memoryFields.AsReadOnly(),
                    _configurationBytes.AsReadOnly(),
                    _memoryBytes.AsReadOnly(),
                    _randomStates.AsReadOnly(),
                    _randomIncrements.AsReadOnly(),
                    bindingInput);
            }

            internal NativeBurstDispatchCreateInputV2 Value { get; }

            public void Dispose()
            {
                _rules.Dispose();
                _bindingRanges.Dispose();
                _caseRanges.Dispose();
                _completionBytes.Dispose();
                _completions.Dispose();
                _liveBytes.Dispose();
                _valueFields.Dispose();
                _resolved.Dispose();
                _bindings.Dispose();
                _randomIncrements.Dispose();
                _randomStates.Dispose();
                _memoryBytes.Dispose();
                _configurationBytes.Dispose();
                _memoryFields.Dispose();
                _configurationFields.Dispose();
                _requests.Dispose();
                _cases.Dispose();
            }
        }

        private sealed class AsyncInput : IDisposable
        {
            private readonly NativeArray<NativeBurstDispatchCaseV2> _cases;
            private readonly NativeArray<NativeBurstDispatchRequestV2> _requests;
            private readonly NativeArray<NativeBurstDispatchFieldV2> _configurationFields;
            private readonly NativeArray<NativeBurstDispatchFieldV2> _memoryFields;
            private readonly NativeArray<byte> _configurationBytes;
            private readonly NativeArray<byte> _memoryBytes;
            private readonly NativeArray<ulong> _randomStates;
            private readonly NativeArray<ulong> _randomIncrements;
            private readonly NativeArray<NativeBurstDispatchBindingV2> _bindings;
            private readonly NativeArray<NativeBurstDispatchResolvedBindingV2> _resolved;
            private readonly NativeArray<NativeBurstDispatchFieldV2> _valueFields;
            private readonly NativeArray<byte> _liveBytes;
            private readonly NativeArray<NativeBurstDispatchCompletionV2> _completions;
            private readonly NativeArray<byte> _completionBytes;
            private readonly NativeArray<NativeBurstDispatchCanonicalRangeV2> _caseRanges;
            private readonly NativeArray<NativeBurstDispatchCanonicalRangeV2> _bindingRanges;
            private readonly NativeArray<NativeBurstDispatchCanonicalRuleV2> _rules;

            internal AsyncInput(uint maxCommands)
            {
                _cases = new NativeArray<NativeBurstDispatchCaseV2>(1, Allocator.Temp);
                _requests = new NativeArray<NativeBurstDispatchRequestV2>(2, Allocator.Temp);
                _configurationFields = new NativeArray<NativeBurstDispatchFieldV2>(1, Allocator.Temp);
                _memoryFields = new NativeArray<NativeBurstDispatchFieldV2>(1, Allocator.Temp);
                _configurationBytes = new NativeArray<byte>(8, Allocator.Temp);
                _memoryBytes = new NativeArray<byte>(8, Allocator.Temp);
                _randomStates = new NativeArray<ulong>(0, Allocator.Temp);
                _randomIncrements = new NativeArray<ulong>(0, Allocator.Temp);
                _bindings = new NativeArray<NativeBurstDispatchBindingV2>(1, Allocator.Temp);
                _resolved = new NativeArray<NativeBurstDispatchResolvedBindingV2>(2, Allocator.Temp);
                _valueFields = new NativeArray<NativeBurstDispatchFieldV2>(2, Allocator.Temp);
                _liveBytes = new NativeArray<byte>(0, Allocator.Temp);
                _completions = new NativeArray<NativeBurstDispatchCompletionV2>(0, Allocator.Temp);
                _completionBytes = new NativeArray<byte>(0, Allocator.Temp);
                _caseRanges = new NativeArray<NativeBurstDispatchCanonicalRangeV2>(2, Allocator.Temp);
                _bindingRanges = new NativeArray<NativeBurstDispatchCanonicalRangeV2>(2, Allocator.Temp);
                _rules = new NativeArray<NativeBurstDispatchCanonicalRuleV2>(0, Allocator.Temp);

                _cases[0] = new NativeBurstDispatchCaseV2(
                    9002,
                    1,
                    0,
                    0,
                    1,
                    4,
                    0,
                    1,
                    4,
                    NativeBurstDispatchPhaseMaskV2.Enter | NativeBurstDispatchPhaseMaskV2.Abort,
                    BurstNodeStatusMask.Success,
                    false,
                    0,
                    1);
                _configurationFields[0] = new NativeBurstDispatchFieldV2(
                    0, 0, 1, 4, NativeBurstDispatchFieldEncodingV2.GeneratedHandle);
                _memoryFields[0] = new NativeBurstDispatchFieldV2(
                    0, 0, 1, 4, NativeBurstDispatchFieldEncodingV2.Int32);
                _valueFields[0] = new NativeBurstDispatchFieldV2(
                    0, 0, 1, 4, NativeBurstDispatchFieldEncodingV2.Int32);
                _valueFields[1] = new NativeBurstDispatchFieldV2(
                    0, 0, 1, 4, NativeBurstDispatchFieldEncodingV2.Int32);
                _bindings[0] = new NativeBurstDispatchBindingV2(
                    0,
                    0,
                    NativeBurstDispatchBindingKindV2.AsyncOperation,
                    NativeBurstDispatchBindingV2.NoScope,
                    NativeBurstDispatchBindingPhaseMaskV2.Execute
                        | NativeBurstDispatchBindingPhaseMaskV2.Cancel,
                    NativeBuiltInBlackboardTypeIdsV1.Int32,
                    1,
                    0,
                    1,
                    4,
                    NativeBuiltInBlackboardTypeIdsV1.Int32,
                    1,
                    1,
                    1,
                    4);
                for (var index = 0; index < 2; index++)
                {
                    _requests[index] = new NativeBurstDispatchRequestV2(
                        (uint)index,
                        10,
                        9002,
                        1,
                        0,
                        index == 0 ? BurstCallbackPhase.Enter : BurstCallbackPhase.Abort,
                        (uint)(index * 4),
                        (uint)(index * 4),
                        0,
                        123,
                        new TreeInstanceId(777),
                        9,
                        (uint)index,
                        1);
                    _resolved[index] = new NativeBurstDispatchResolvedBindingV2(
                        0,
                        500,
                        NativeBurstDispatchBindingV2.NoOffset);
                }

                var canonical = new NativeBurstDispatchCanonicalInputV2(
                    _caseRanges.AsReadOnly(),
                    _bindingRanges.AsReadOnly(),
                    _rules.AsReadOnly());
                var bindingInput = new NativeBurstDispatchBindingInputV2(
                    _bindings.AsReadOnly(),
                    _resolved.AsReadOnly(),
                    _valueFields.AsReadOnly(),
                    _liveBytes.AsReadOnly(),
                    _completions.AsReadOnly(),
                    _completionBytes.AsReadOnly(),
                    new NativeBurstDispatchBindingCapacityV2(
                        16,
                        128,
                        maxCommands,
                        128,
                        8,
                        5),
                    canonical);
                Value = new NativeBurstDispatchCreateInputV2(
                    Handshake(),
                    _cases.AsReadOnly(),
                    _requests.AsReadOnly(),
                    _configurationFields.AsReadOnly(),
                    _memoryFields.AsReadOnly(),
                    _configurationBytes.AsReadOnly(),
                    _memoryBytes.AsReadOnly(),
                    _randomStates.AsReadOnly(),
                    _randomIncrements.AsReadOnly(),
                    bindingInput);
            }

            internal NativeBurstDispatchCreateInputV2 Value { get; }

            public void Dispose()
            {
                _rules.Dispose();
                _bindingRanges.Dispose();
                _caseRanges.Dispose();
                _completionBytes.Dispose();
                _completions.Dispose();
                _liveBytes.Dispose();
                _valueFields.Dispose();
                _resolved.Dispose();
                _bindings.Dispose();
                _randomIncrements.Dispose();
                _randomStates.Dispose();
                _memoryBytes.Dispose();
                _configurationBytes.Dispose();
                _memoryFields.Dispose();
                _configurationFields.Dispose();
                _requests.Dispose();
                _cases.Dispose();
            }
        }

        private sealed class NestedAssetInput : IDisposable
        {
            private readonly NativeArray<NativeBurstDispatchCaseV2> _cases;
            private readonly NativeArray<NativeBurstDispatchRequestV2> _requests;
            private readonly NativeArray<NativeBurstDispatchFieldV2> _configurationFields;
            private readonly NativeArray<NativeBurstDispatchFieldV2> _memoryFields;
            private readonly NativeArray<byte> _configurationBytes;
            private readonly NativeArray<byte> _memoryBytes;
            private readonly NativeArray<ulong> _randomStates;
            private readonly NativeArray<ulong> _randomIncrements;
            private readonly NativeArray<NativeBurstDispatchBindingV2> _bindings;
            private readonly NativeArray<NativeBurstDispatchResolvedBindingV2> _resolved;
            private readonly NativeArray<NativeBurstDispatchFieldV2> _valueFields;
            private readonly NativeArray<byte> _liveBytes;
            private readonly NativeArray<NativeBurstDispatchCompletionV2> _completions;
            private readonly NativeArray<byte> _completionBytes;
            private readonly NativeArray<NativeBurstDispatchCanonicalRangeV2> _caseRanges;
            private readonly NativeArray<NativeBurstDispatchCanonicalRangeV2> _bindingRanges;
            private readonly NativeArray<NativeBurstDispatchCanonicalRuleV2> _rules;

            internal NestedAssetInput(bool omitRule, bool validBytes)
            {
                _cases = new NativeArray<NativeBurstDispatchCaseV2>(1, Allocator.Temp);
                _requests = new NativeArray<NativeBurstDispatchRequestV2>(1, Allocator.Temp);
                _configurationFields = new NativeArray<NativeBurstDispatchFieldV2>(1, Allocator.Temp);
                _memoryFields = new NativeArray<NativeBurstDispatchFieldV2>(0, Allocator.Temp);
                _configurationBytes = new NativeArray<byte>(4, Allocator.Temp);
                _memoryBytes = new NativeArray<byte>(0, Allocator.Temp);
                _randomStates = new NativeArray<ulong>(0, Allocator.Temp);
                _randomIncrements = new NativeArray<ulong>(0, Allocator.Temp);
                _bindings = new NativeArray<NativeBurstDispatchBindingV2>(1, Allocator.Temp);
                _resolved = new NativeArray<NativeBurstDispatchResolvedBindingV2>(1, Allocator.Temp);
                _valueFields = new NativeArray<NativeBurstDispatchFieldV2>(4, Allocator.Temp);
                _liveBytes = new NativeArray<byte>(40, Allocator.Temp);
                _completions = new NativeArray<NativeBurstDispatchCompletionV2>(0, Allocator.Temp);
                _completionBytes = new NativeArray<byte>(0, Allocator.Temp);
                _caseRanges = new NativeArray<NativeBurstDispatchCanonicalRangeV2>(2, Allocator.Temp);
                _bindingRanges = new NativeArray<NativeBurstDispatchCanonicalRangeV2>(2, Allocator.Temp);
                _rules = new NativeArray<NativeBurstDispatchCanonicalRuleV2>(omitRule ? 0 : 1, Allocator.Temp);

                _cases[0] = new NativeBurstDispatchCaseV2(
                    9003,
                    1,
                    0,
                    0,
                    1,
                    4,
                    0,
                    0,
                    0,
                    NativeBurstDispatchPhaseMaskV2.Enter,
                    BurstNodeStatusMask.Success,
                    false,
                    0,
                    1);
                _requests[0] = new NativeBurstDispatchRequestV2(
                    0,
                    10,
                    9003,
                    1,
                    0,
                    BurstCallbackPhase.Enter,
                    0,
                    0,
                    0,
                    123,
                    new TreeInstanceId(900),
                    1,
                    0,
                    1);
                _configurationFields[0] = new NativeBurstDispatchFieldV2(
                    0, 0, 1, 4, NativeBurstDispatchFieldEncodingV2.GeneratedHandle);
                _valueFields[0] = new NativeBurstDispatchFieldV2(
                    0,
                    0,
                    8,
                    1,
                    8,
                    NativeBurstDispatchFieldEncodingV2.UInt64,
                    NativeBurstDispatchCanonicalRuleKindV2.AssetId);
                _valueFields[1] = new NativeBurstDispatchFieldV2(
                    0, 1, 16, 1, 8, NativeBurstDispatchFieldEncodingV2.UInt64);
                _valueFields[2] = new NativeBurstDispatchFieldV2(
                    0, 2, 24, 1, 8, NativeBurstDispatchFieldEncodingV2.Int64);
                _valueFields[3] = new NativeBurstDispatchFieldV2(
                    0, 3, 32, 1, 1, NativeBurstDispatchFieldEncodingV2.Boolean);
                _bindings[0] = new NativeBurstDispatchBindingV2(
                    0,
                    0,
                    NativeBurstDispatchBindingKindV2.BlackboardRead,
                    (byte)BlackboardScope.Tree,
                    NativeBurstDispatchBindingPhaseMaskV2.None,
                    0xf001,
                    1,
                    0,
                    4,
                    40,
                    0,
                    0,
                    0,
                    0,
                    0);
                _resolved[0] = new NativeBurstDispatchResolvedBindingV2(0, 1, 0);
                _bindingRanges[0] = new NativeBurstDispatchCanonicalRangeV2(
                    0,
                    omitRule ? 0u : 1u);
                _bindingRanges[1] = new NativeBurstDispatchCanonicalRangeV2(
                    omitRule ? 0u : 1u,
                    0);
                if (!omitRule)
                {
                    _rules[0] = new NativeBurstDispatchCanonicalRuleV2(
                        NativeBurstDispatchCanonicalRuleKindV2.AssetId,
                        8);
                }

                WriteUInt32(_configurationBytes, 0, 0);
                WriteUInt32(_liveBytes, 8, 1);
                if (!validBytes)
                {
                    WriteUInt32(_liveBytes, 24, 7);
                }

                var canonical = new NativeBurstDispatchCanonicalInputV2(
                    _caseRanges.AsReadOnly(),
                    _bindingRanges.AsReadOnly(),
                    _rules.AsReadOnly());
                var bindingInput = new NativeBurstDispatchBindingInputV2(
                    _bindings.AsReadOnly(),
                    _resolved.AsReadOnly(),
                    _valueFields.AsReadOnly(),
                    _liveBytes.AsReadOnly(),
                    _completions.AsReadOnly(),
                    _completionBytes.AsReadOnly(),
                    new NativeBurstDispatchBindingCapacityV2(4, 64, 0, 0, 0),
                    canonical);
                Value = new NativeBurstDispatchCreateInputV2(
                    Handshake(),
                    _cases.AsReadOnly(),
                    _requests.AsReadOnly(),
                    _configurationFields.AsReadOnly(),
                    _memoryFields.AsReadOnly(),
                    _configurationBytes.AsReadOnly(),
                    _memoryBytes.AsReadOnly(),
                    _randomStates.AsReadOnly(),
                    _randomIncrements.AsReadOnly(),
                    bindingInput);
            }

            internal NativeBurstDispatchCreateInputV2 Value { get; }

            public void Dispose()
            {
                _rules.Dispose();
                _bindingRanges.Dispose();
                _caseRanges.Dispose();
                _completionBytes.Dispose();
                _completions.Dispose();
                _liveBytes.Dispose();
                _valueFields.Dispose();
                _resolved.Dispose();
                _bindings.Dispose();
                _randomIncrements.Dispose();
                _randomStates.Dispose();
                _memoryBytes.Dispose();
                _configurationBytes.Dispose();
                _memoryFields.Dispose();
                _configurationFields.Dispose();
                _requests.Dispose();
                _cases.Dispose();
            }
        }

        private sealed class FixedStringInput : IDisposable
        {
            private readonly NativeArray<NativeBurstDispatchCaseV2> _cases;
            private readonly NativeArray<NativeBurstDispatchRequestV2> _requests;
            private readonly NativeArray<NativeBurstDispatchFieldV2> _configurationFields;
            private readonly NativeArray<NativeBurstDispatchFieldV2> _memoryFields;
            private readonly NativeArray<byte> _configurationBytes;
            private readonly NativeArray<byte> _memoryBytes;
            private readonly NativeArray<ulong> _randomStates;
            private readonly NativeArray<ulong> _randomIncrements;
            private readonly NativeArray<NativeBurstDispatchCanonicalRangeV2> _caseRanges;
            private readonly NativeArray<NativeBurstDispatchCanonicalRangeV2> _bindingRanges;
            private readonly NativeArray<NativeBurstDispatchCanonicalRuleV2> _rules;

            internal FixedStringInput(FixedStringFailure failureKind)
            {
                _cases = new NativeArray<NativeBurstDispatchCaseV2>(1, Allocator.Temp);
                _requests = new NativeArray<NativeBurstDispatchRequestV2>(1, Allocator.Temp);
                _configurationFields = new NativeArray<NativeBurstDispatchFieldV2>(2, Allocator.Temp);
                _memoryFields = new NativeArray<NativeBurstDispatchFieldV2>(0, Allocator.Temp);
                _configurationBytes = new NativeArray<byte>(32, Allocator.Temp);
                _memoryBytes = new NativeArray<byte>(0, Allocator.Temp);
                _randomStates = new NativeArray<ulong>(0, Allocator.Temp);
                _randomIncrements = new NativeArray<ulong>(0, Allocator.Temp);
                _caseRanges = new NativeArray<NativeBurstDispatchCanonicalRangeV2>(2, Allocator.Temp);
                _bindingRanges = new NativeArray<NativeBurstDispatchCanonicalRangeV2>(0, Allocator.Temp);
                _rules = new NativeArray<NativeBurstDispatchCanonicalRuleV2>(1, Allocator.Temp);

                _cases[0] = new NativeBurstDispatchCaseV2(
                    9004,
                    1,
                    0,
                    0,
                    2,
                    32,
                    0,
                    0,
                    0,
                    NativeBurstDispatchPhaseMaskV2.Enter,
                    BurstNodeStatusMask.Success,
                    false);
                _requests[0] = new NativeBurstDispatchRequestV2(
                    0,
                    10,
                    9004,
                    1,
                    0,
                    BurstCallbackPhase.Enter,
                    0,
                    0,
                    0,
                    123);
                _configurationFields[0] = new NativeBurstDispatchFieldV2(
                    0,
                    0,
                    0,
                    1,
                    2,
                    NativeBurstDispatchFieldEncodingV2.UInt16,
                    NativeBurstDispatchCanonicalRuleKindV2.FixedString32);
                _configurationFields[1] = new NativeBurstDispatchFieldV2(
                    0,
                    1,
                    2,
                    30,
                    1,
                    NativeBurstDispatchFieldEncodingV2.UInt8);
                _caseRanges[0] = new NativeBurstDispatchCanonicalRangeV2(0, 1);
                _caseRanges[1] = new NativeBurstDispatchCanonicalRangeV2(1, 0);
                _rules[0] = new NativeBurstDispatchCanonicalRuleV2(
                    NativeBurstDispatchCanonicalRuleKindV2.FixedString32,
                    0);

                if (failureKind == FixedStringFailure.Length)
                {
                    _configurationBytes[0] = 31;
                }
                else if (failureKind == FixedStringFailure.Utf8)
                {
                    _configurationBytes[0] = 1;
                    _configurationBytes[2] = 0xc0;
                }
                else
                {
                    _configurationBytes[0] = 1;
                    _configurationBytes[2] = (byte)'a';
                    _configurationBytes[3] = 1;
                }

                var canonical = new NativeBurstDispatchCanonicalInputV2(
                    _caseRanges.AsReadOnly(),
                    _bindingRanges.AsReadOnly(),
                    _rules.AsReadOnly());
                Value = new NativeBurstDispatchCreateInputV2(
                    Handshake(),
                    _cases.AsReadOnly(),
                    _requests.AsReadOnly(),
                    _configurationFields.AsReadOnly(),
                    _memoryFields.AsReadOnly(),
                    _configurationBytes.AsReadOnly(),
                    _memoryBytes.AsReadOnly(),
                    _randomStates.AsReadOnly(),
                    _randomIncrements.AsReadOnly(),
                    default,
                    canonical);
            }

            internal NativeBurstDispatchCreateInputV2 Value { get; }

            public void Dispose()
            {
                _rules.Dispose();
                _bindingRanges.Dispose();
                _caseRanges.Dispose();
                _randomIncrements.Dispose();
                _randomStates.Dispose();
                _memoryBytes.Dispose();
                _configurationBytes.Dispose();
                _memoryFields.Dispose();
                _configurationFields.Dispose();
                _requests.Dispose();
                _cases.Dispose();
            }
        }
    }
}
