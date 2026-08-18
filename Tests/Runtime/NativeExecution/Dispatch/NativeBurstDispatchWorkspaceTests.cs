using System;
using System.Threading;
using AIBT.Burst;
using AIBT.Execution.Burst.Dispatch;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using UnityEngine.TestTools.Constraints;
using Unity.Jobs;
using GcAllocIs = UnityEngine.TestTools.Constraints.Is;
using Is = NUnit.Framework.Is;

namespace AIBT.Tests.Runtime.NativeExecution.Dispatch
{
    [TestFixture]
    public sealed class NativeBurstDispatchWorkspaceTests
    {
        private const ulong TypeNumericId = 0xa175_8871_4a36_2d19UL;
        private const uint TypeVersion = 1u;
        private const uint InstanceOrdinal = 3u;
        private const uint RuntimeNodeIndex = 7u;
        private const ulong InitialRandomState = 0xcd06_63b1_aab3_8607UL;
        private const ulong RandomIncrement = 0x3641_42da_8f45_ed0bUL;

        [Test]
        public void BorrowedViewsPublishAndResetInvalidatesEveryPriorCarrier()
        {
            using (var scenario = WorkspaceScenario.Create(false))
            using (var first = RequestBuffers.Create(false, 11, 1u))
            using (var second = RequestBuffers.Create(false, 29, 2u))
            {
                Assert.That(scenario.Owner.State, Is.EqualTo(NativeBurstDispatchWorkspaceStateV2.Idle));
                var firstViews = first.Views;
                Assert.That(scenario.Owner.TryBeginRequest(
                    in firstViews,
                    out var firstLease,
                    out var failure), Is.True, failure.ToString());
                Assert.That(failure, Is.EqualTo(BurstContextResult.Success));
                Assert.That(scenario.Owner.State, Is.EqualTo(NativeBurstDispatchWorkspaceStateV2.Prepared));
                Assert.That(scenario.Owner.TryAcquireImmediateBatch(
                    in firstLease,
                    out var firstBatch,
                    out failure), Is.True, failure.ToString());

                ExecuteEnter(ref firstBatch, 11, 12);
                Assert.That(scenario.Owner.State, Is.EqualTo(NativeBurstDispatchWorkspaceStateV2.Terminal));
                Assert.That(scenario.Owner.TryConsumeResult(
                    in firstLease,
                    out var firstResult,
                    out failure), Is.True, failure.ToString());
                Assert.That(firstResult.Execution.Code, Is.EqualTo(BurstExecutionCode.Success));
                Assert.That(firstResult.Status, Is.EqualTo(NodeStatus.Success));
                Assert.That(ReadInt32(first.MemoryBytes, 0), Is.EqualTo(12));
                Assert.That(first.RandomStates[0], Is.Not.EqualTo(InitialRandomState));
                Assert.That(scenario.Owner.State, Is.EqualTo(NativeBurstDispatchWorkspaceStateV2.Consumed));

                Assert.That(scenario.Owner.TryReset(in firstLease, out failure), Is.True, failure.ToString());
                Assert.That(scenario.Owner.State, Is.EqualTo(NativeBurstDispatchWorkspaceStateV2.Idle));
                Assert.That(BurstGeneratedRuntimeBridge.TryGetCatalogHandshake(
                    in firstBatch, out _), Is.EqualTo(BurstContextResult.InvalidHandle));
                Assert.That(scenario.Owner.TryAcquireImmediateBatch(
                    in firstLease, out _, out failure), Is.False);
                Assert.That(failure, Is.EqualTo(BurstContextResult.InvalidHandle));

                var secondViews = second.WithDurableArenasFrom(first);
                Assert.That(scenario.Owner.TryBeginRequest(
                    in secondViews,
                    out var secondLease,
                    out failure), Is.True, failure.ToString());
                Assert.That(secondLease.Generation, Is.Not.EqualTo(firstLease.Generation));
                Assert.That(scenario.Owner.TryAcquireImmediateBatch(
                    in secondLease,
                    out var secondBatch,
                    out failure), Is.True, failure.ToString());
                ExecuteEnter(ref secondBatch, 29, 31);
                Assert.That(scenario.Owner.TryConsumeResult(
                    in secondLease,
                    out var secondResult,
                    out failure), Is.True, failure.ToString());
                Assert.That(secondResult.Execution.Code, Is.EqualTo(BurstExecutionCode.Success));
                Assert.That(ReadInt32(second.MemoryBytes, 0), Is.EqualTo(31));
                Assert.That(ReadInt32(first.MemoryBytes, 0), Is.EqualTo(12),
                    "Reset/rebind must not retain the prior request's borrowed memory view.");
                Assert.That(scenario.Owner.TryReset(in secondLease, out failure), Is.True, failure.ToString());
            }
        }

        [Test]
        public void StateMachineAndBorrowedTransactionControlFailClosed()
        {
            using (var scenario = WorkspaceScenario.Create(false))
            using (var request = RequestBuffers.Create(false, 5, 1u))
            using (var shortControl = new NativeArray<NativeBurstDispatchTransactionControlV2>(
                       0, Allocator.Temp, NativeArrayOptions.ClearMemory))
            {
                var malformed = request.WithTransactionControl(shortControl);
                Assert.That(scenario.Owner.TryBeginRequest(
                    in malformed,
                    out _,
                    out var failure), Is.False);
                Assert.That(failure, Is.EqualTo(BurstContextResult.InvalidEncoding));
                Assert.That(scenario.Owner.State, Is.EqualTo(NativeBurstDispatchWorkspaceStateV2.Idle));

                var views = request.Views;
                Assert.That(scenario.Owner.TryBeginRequest(
                    in views,
                    out var lease,
                    out failure), Is.True, failure.ToString());
                Assert.That(scenario.Owner.TryBeginRequest(
                    in views,
                    out _,
                    out failure), Is.False);
                Assert.That(failure, Is.EqualTo(BurstContextResult.PhaseViolation));
                Assert.That(scenario.Owner.TryConsumeResult(
                    in lease,
                    out _,
                    out failure), Is.False);
                Assert.That(failure, Is.EqualTo(BurstContextResult.PhaseViolation));
                Assert.That(scenario.Owner.TryReset(in lease, out failure), Is.False);
                Assert.That(failure, Is.EqualTo(BurstContextResult.PhaseViolation));

                Assert.That(scenario.Owner.TryAcquireImmediateBatch(
                    in lease,
                    out var batch,
                    out failure), Is.True, failure.ToString());
                Assert.That(scenario.Owner.TryAcquireImmediateBatch(
                    in lease,
                    out _,
                    out failure), Is.False);
                Assert.That(failure, Is.EqualTo(BurstContextResult.PhaseViolation));
                ExecuteEnter(ref batch, 5, 6);
                var defaultLease = default(NativeBurstDispatchWorkspaceLeaseV2);
                Assert.That(scenario.Owner.TryConsumeResult(
                    in defaultLease,
                    out _,
                    out failure), Is.False);
                Assert.That(failure, Is.EqualTo(BurstContextResult.InvalidHandle));
                Assert.That(scenario.Owner.TryConsumeResult(
                    in lease,
                    out _,
                    out failure), Is.True, failure.ToString());
                Assert.That(scenario.Owner.TryReset(in lease, out failure), Is.True, failure.ToString());
            }
        }

        [Test]
        public void CallerOwnedPublishedPrefixAndSequenceSurviveResetAndRebind()
        {
            using (var scenario = WorkspaceScenario.Create(true))
            using (var request = RequestBuffers.Create(true, 0, 1u))
            using (var foreignLedger = new NativeArray<NativeBurstDispatchTransactionControlV2>(
                       1, Allocator.Temp, NativeArrayOptions.ClearMemory))
            using (var sequenceRegression = new NativeArray<NativeBurstDispatchTransactionControlV2>(
                       1, Allocator.Temp, NativeArrayOptions.ClearMemory))
            using (var prefixRegression = new NativeArray<NativeBurstDispatchTransactionControlV2>(
                       1, Allocator.Temp, NativeArrayOptions.ClearMemory))
            {
                PublishEffect(scenario.Owner, request, 101);
                Assert.That(request.TransactionControl[0].CommandCount, Is.EqualTo(1u));
                Assert.That(request.TransactionControl[0].CommandPayloadByteCount, Is.EqualTo(4u));
                Assert.That(request.TransactionControl[0].NextOperationSequence, Is.EqualTo(7UL));
                Assert.That(request.Commands[0].TargetOrdinal, Is.EqualTo(41u));
                Assert.That(ReadInt32(request.CommandPayloadBytes, 0), Is.EqualTo(101));

                SetTransaction(foreignLedger, new NativeBurstDispatchTransactionControlV2
                {
                    LedgerToken = 0xdeadUL,
                    TreeInstanceId = new TreeInstanceId(19UL),
                    CommandCount = 1u,
                    CommandPayloadByteCount = 4u,
                    NextOperationSequence = 7UL
                });
                var foreignViews = request.WithTransactionControl(foreignLedger);
                Assert.That(scenario.Owner.TryBeginRequest(
                    in foreignViews, out _, out var failure), Is.False);
                Assert.That(failure, Is.EqualTo(BurstContextResult.InvalidHandle));

                var wrongTree = request.TransactionControl[0];
                wrongTree.TreeInstanceId = new TreeInstanceId(20UL);
                SetTransaction(foreignLedger, wrongTree);
                foreignViews = request.WithTransactionControl(foreignLedger);
                Assert.That(scenario.Owner.TryBeginRequest(
                    in foreignViews, out _, out failure), Is.False);
                Assert.That(failure, Is.EqualTo(BurstContextResult.InvalidHandle));

                SetTransaction(sequenceRegression, new NativeBurstDispatchTransactionControlV2
                {
                    LedgerToken = 0xabc1UL,
                    TreeInstanceId = new TreeInstanceId(19UL),
                    CommandCount = 1u,
                    CommandPayloadByteCount = 4u,
                    NextOperationSequence = 6UL
                });
                var sequenceViews = request.WithTransactionControl(sequenceRegression);
                Assert.That(scenario.Owner.TryBeginRequest(
                    in sequenceViews, out _, out failure), Is.False);
                Assert.That(failure, Is.EqualTo(BurstContextResult.InvalidHandle));

                SetTransaction(prefixRegression, new NativeBurstDispatchTransactionControlV2
                {
                    LedgerToken = 0xabc1UL,
                    TreeInstanceId = new TreeInstanceId(19UL),
                    NextOperationSequence = 7UL
                });
                var prefixViews = request.WithTransactionControl(prefixRegression);
                Assert.That(scenario.Owner.TryBeginRequest(
                    in prefixViews, out _, out failure), Is.False);
                Assert.That(failure, Is.EqualTo(BurstContextResult.InvalidHandle));
                Assert.That(scenario.Owner.State, Is.EqualTo(NativeBurstDispatchWorkspaceStateV2.Idle));

                request.SetActivationGeneration(2u);
                PublishEffect(scenario.Owner, request, 202);
                Assert.That(request.TransactionControl[0].CommandCount, Is.EqualTo(2u));
                Assert.That(request.TransactionControl[0].CommandPayloadByteCount, Is.EqualTo(8u));
                Assert.That(request.TransactionControl[0].NextOperationSequence, Is.EqualTo(7UL));
                Assert.That(request.Commands[0].TargetOrdinal, Is.EqualTo(41u));
                Assert.That(request.Commands[1].TargetOrdinal, Is.EqualTo(41u));
                Assert.That(ReadInt32(request.CommandPayloadBytes, 0), Is.EqualTo(101));
                Assert.That(ReadInt32(request.CommandPayloadBytes, 4), Is.EqualTo(202));
            }
        }

        [Test]
        public void TerminalLedgerRegressionIsRejectedBeforeResultConsumption()
        {
            using (var scenario = WorkspaceScenario.Create(true))
            using (var request = RequestBuffers.Create(true, 0, 1u))
            {
                var beforeFrame = request.TransactionControl[0];
                ExecuteEffectToTerminal(
                    scenario.Owner,
                    request,
                    101,
                    out var lease);
                var terminal = request.TransactionControl[0];
                Assert.That(terminal.CommandCount, Is.EqualTo(1u));
                Assert.That(terminal.MutationVersion, Is.GreaterThan(beforeFrame.MutationVersion));

                var beyondCapacity = terminal;
                beyondCapacity.CommandCount = 3u;
                NativeBurstDispatchTransactionLedgerV2.Seal(ref beyondCapacity);
                request.TransactionControl[0] = beyondCapacity;
                Assert.That(scenario.Owner.TryConsumeResult(
                    in lease, out _, out var failure), Is.False);
                Assert.That(failure, Is.EqualTo(BurstContextResult.InvalidEncoding));

                request.TransactionControl[0] = beforeFrame;
                Assert.That(scenario.Owner.TryConsumeResult(
                    in lease, out _, out failure), Is.False);
                Assert.That(failure, Is.EqualTo(BurstContextResult.InvalidHandle));

                request.TransactionControl[0] = terminal;
                Assert.That(scenario.Owner.TryConsumeResult(
                    in lease, out var result, out failure), Is.True, failure.ToString());
                Assert.That(result.Execution.Code, Is.EqualTo(BurstExecutionCode.Success));
                Assert.That(scenario.Owner.TryReset(in lease, out failure), Is.True, failure.ToString());
            }
        }

        [Test]
        public void ScheduledJobKeepsTheWorkspaceExclusiveUntilItsRegisteredDependencyCompletes()
        {
            using (var scenario = WorkspaceScenario.Create(false))
            using (var request = RequestBuffers.Create(false, 11, 1u))
            using (var probe = new NativeArray<int>(1, Allocator.TempJob, NativeArrayOptions.ClearMemory))
            {
                var views = request.Views;
                Assert.That(scenario.Owner.TryBeginRequest(
                    in views, out var lease, out var failure), Is.True, failure.ToString());
                Assert.That(scenario.Owner.TryAcquireImmediateBatch(
                    in lease, out var hostBatch, out failure), Is.True, failure.ToString());
                Assert.That(BurstGeneratedRuntimeBridge.TryPrepareSchedule(
                    ref hostBatch, out var jobBatch), Is.EqualTo(BurstContextResult.Success));

                var blocker = new WorkspaceBlockerJob { DurationMilliseconds = 250 }.Schedule();
                var dispatch = new WorkspaceEnterJob
                {
                    Batch = jobBatch,
                    Probe = probe
                }.Schedule(blocker);
                try
                {
                    Assert.That(scenario.Owner.TryRegisterDependency(
                        in lease, in hostBatch, dispatch, out failure), Is.True, failure.ToString());
                    Assert.That(dispatch.IsCompleted, Is.False);
                    Assert.That(scenario.Owner.TryConsumeResult(
                        in lease, out _, out failure), Is.False);
                    Assert.That(failure, Is.EqualTo(BurstContextResult.PhaseViolation));
                    Assert.That(scenario.Owner.TryReset(in lease, out failure), Is.False);
                    Assert.That(failure, Is.EqualTo(BurstContextResult.PhaseViolation));
                    Assert.That(scenario.Owner.TryDispose(out failure), Is.False);
                    Assert.That(failure, Is.EqualTo(BurstContextResult.PhaseViolation));
                }
                finally
                {
                    dispatch.Complete();
                }

                Assert.That(probe[0], Is.EqualTo(1));
                Assert.That(scenario.Owner.TryAcquireCompletedBatch(
                    in lease, out var completedBatch, out failure), Is.True, failure.ToString());
                Assert.That(BurstGeneratedRuntimeBridge.TryGetExecutionResult(
                    in completedBatch, out var execution), Is.EqualTo(BurstContextResult.Success));
                Assert.That(execution.Code, Is.EqualTo(BurstExecutionCode.Success));
                Assert.That(scenario.Owner.TryConsumeResult(
                    in lease, out _, out failure), Is.True, failure.ToString());
                Assert.That(scenario.Owner.TryReset(in lease, out failure), Is.True, failure.ToString());
            }
        }

        [Test]
        public void EveryOwnedAllocationFailureRollsBackWithoutBorrowingOrLeaking()
        {
            using (var shape = new ShapeBuffers(true))
            {
                var value = shape.Value;
                var capacity = new NativeBurstDispatchWorkspaceCapacityV2(
                    4u,
                    new NativeBurstDispatchBindingCapacityV2(1u, 4u, 2u, 8u, 0u, 7UL));
                const int ownedAllocationCount = 18;
                for (var failAfter = 0; failAfter < ownedAllocationCount; failAfter++)
                {
                    Assert.That(NativeBurstDispatchWorkspaceOwnerV2.TryCreate(
                        in value,
                        in capacity,
                        Allocator.Persistent,
                        failAfter,
                        out var rejected,
                        out var failure), Is.False, "failAfter=" + failAfter);
                    Assert.That(rejected, Is.Null);
                    Assert.That(failure, Is.EqualTo(BurstContextResult.CapacityExceeded));
                }

                Assert.That(NativeBurstDispatchWorkspaceOwnerV2.TryCreate(
                    in value,
                    in capacity,
                    Allocator.Persistent,
                    ownedAllocationCount,
                    out var owner,
                    out var finalFailure), Is.True, finalFailure.ToString());
                Assert.That(owner.TryDispose(out finalFailure), Is.True, finalFailure.ToString());
            }
        }

        [Test]
        public void WarmRequestCycleAllocatesNoManagedMemoryAndAllocationCanaryIsPositive()
        {
            using (var scenario = WorkspaceScenario.Create(false))
            using (var request = RequestBuffers.Create(false, 1, 1u))
            {
                Assert.That(TryRunPlainCycle(scenario.Owner, request, 2), Is.True);
                request.ResetPlain(2, 2u);

                var succeeded = false;
                Assert.That(
                    () => { succeeded = TryRunPlainCycle(scenario.Owner, request, 3); },
                    GcAllocIs.Not.AllocatingGCMemory());
                Assert.That(succeeded, Is.True);
                Assert.That(
                    () =>
                    {
                        var canary = new string('x', 128);
                        GC.KeepAlive(canary);
                    },
                    GcAllocIs.AllocatingGCMemory(),
                    "allocation instrumentation canary");
            }
        }

        [Test]
        public void MalformedShapeAndBorrowedViewsRejectAtomicallyBeforePreparedState()
        {
            using (var malformedShape = new ShapeBuffers(false))
            {
                malformedShape.InvalidateCase();
                var value = malformedShape.Value;
                var capacity = new NativeBurstDispatchWorkspaceCapacityV2(4u, default);
                Assert.That(NativeBurstDispatchWorkspaceOwnerV2.TryCreate(
                    in value,
                    in capacity,
                    Allocator.Persistent,
                    out var rejected,
                    out var failure), Is.False);
                Assert.That(rejected, Is.Null);
                Assert.That(failure, Is.EqualTo(BurstContextResult.InvalidEncoding));
            }

            using (var shape = new ShapeBuffers(false))
            {
                var value = shape.Value;
                var tooSmall = new NativeBurstDispatchWorkspaceCapacityV2(3u, default);
                Assert.That(NativeBurstDispatchWorkspaceOwnerV2.TryCreate(
                    in value,
                    in tooSmall,
                    Allocator.Persistent,
                    out var rejected,
                    out var failure), Is.False);
                Assert.That(rejected, Is.Null);
                Assert.That(failure, Is.EqualTo(BurstContextResult.CapacityExceeded));
            }

            using (var scenario = WorkspaceScenario.Create(false))
            using (var request = RequestBuffers.Create(false, 5, 1u))
            using (var shortMemory = new NativeArray<byte>(
                       3, Allocator.Temp, NativeArrayOptions.ClearMemory))
            {
                var defaultConfiguration = request.WithConfiguration(default);
                Assert.That(scenario.Owner.TryBeginRequest(
                    in defaultConfiguration, out _, out var failure), Is.False);
                Assert.That(failure, Is.EqualTo(BurstContextResult.InvalidEncoding));
                Assert.That(scenario.Owner.State, Is.EqualTo(NativeBurstDispatchWorkspaceStateV2.Idle));

                var shortMemoryViews = request.WithMemory(shortMemory);
                Assert.That(scenario.Owner.TryBeginRequest(
                    in shortMemoryViews, out _, out failure), Is.False);
                Assert.That(failure, Is.EqualTo(BurstContextResult.InvalidEncoding));
                Assert.That(scenario.Owner.State, Is.EqualTo(NativeBurstDispatchWorkspaceStateV2.Idle));

                var wrongRequest = RequestBuffers.Request(1u, false, catalogCaseIndex: 1u);
                var wrongCase = request.WithRequest(in wrongRequest);
                Assert.That(scenario.Owner.TryBeginRequest(
                    in wrongCase, out _, out failure), Is.False);
                Assert.That(failure, Is.EqualTo(BurstContextResult.InvalidEncoding));
                Assert.That(scenario.Owner.State, Is.EqualTo(NativeBurstDispatchWorkspaceStateV2.Idle));

                var valid = request.Views;
                Assert.That(scenario.Owner.TryBeginRequest(
                    in valid, out var lease, out failure), Is.True, failure.ToString());
                Assert.That(scenario.Owner.TryAcquireImmediateBatch(
                    in lease, out var batch, out failure), Is.True, failure.ToString());
                ExecuteEnter(ref batch, 5, 6);
                Assert.That(scenario.Owner.TryConsumeResult(
                    in lease, out _, out failure), Is.True, failure.ToString());
                Assert.That(scenario.Owner.TryReset(in lease, out failure), Is.True, failure.ToString());
            }
        }

        [Test]
        public void ResetNeverDisposesBorrowedRequestArenas()
        {
            var scenario = WorkspaceScenario.Create(false);
            using (var request = RequestBuffers.Create(false, 4, 1u))
            {
                try
                {
                    Assert.That(TryRunPlainCycle(scenario.Owner, request, 5), Is.True);
                    Assert.That(scenario.Owner.TryDispose(out var failure), Is.True, failure.ToString());
                    scenario.MarkDisposed();
                    Assert.That(request.AllBorrowedArraysAreCreated, Is.True);
                    WriteInt32(request.MemoryBytes, 0, 77);
                    Assert.That(ReadInt32(request.MemoryBytes, 0), Is.EqualTo(77));
                }
                finally
                {
                    scenario.Dispose();
                }
            }
        }

        [Test]
        public void ResetOverflowLeavesTheConsumedWorkspaceSafelyDisposable()
        {
            using (var shape = new ShapeBuffers(false))
            using (var request = RequestBuffers.Create(false, 8, 1u))
            {
                var value = shape.Value;
                var capacity = new NativeBurstDispatchWorkspaceCapacityV2(4u, default);
                Assert.That(NativeBurstDispatchWorkspaceOwnerV2.TryCreate(
                    in value,
                    in capacity,
                    Allocator.Persistent,
                    -1,
                    uint.MaxValue,
                    out var owner,
                    out var failure), Is.True, failure.ToString());

                var views = request.Views;
                Assert.That(owner.TryBeginRequest(
                    in views, out var lease, out failure), Is.True, failure.ToString());
                Assert.That(owner.TryAcquireImmediateBatch(
                    in lease, out var batch, out failure), Is.True, failure.ToString());
                ExecuteEnter(ref batch, 8, 9);
                Assert.That(owner.TryConsumeResult(
                    in lease, out _, out failure), Is.True, failure.ToString());
                Assert.That(owner.TryReset(in lease, out failure), Is.False);
                Assert.That(failure, Is.EqualTo(BurstContextResult.Overflow));
                Assert.That(owner.State, Is.EqualTo(NativeBurstDispatchWorkspaceStateV2.Consumed));
                Assert.That(owner.TryDispose(out failure), Is.True, failure.ToString());
                Assert.That(request.AllBorrowedArraysAreCreated, Is.True);
            }
        }

        [Test]
        public void FaultedStartSequenceAndCancelTombstoneRemainDurableAcrossReset()
        {
            using (var scenario = AsyncWorkspaceScenario.Create())
            using (var request = new AsyncRequestBuffers())
            {
                var operationId = PublishAsyncStart(scenario.Owner, request);
                Assert.That(operationId.Sequence, Is.EqualTo(5UL));
                Assert.That(request.TransactionControl[0].CommandCount, Is.EqualTo(1u));
                Assert.That(request.TransactionControl[0].OperationCount, Is.EqualTo(1u));
                Assert.That(request.TransactionControl[0].NextOperationSequence, Is.EqualTo(6UL));

                FailAsyncStart(scenario.Owner, request);
                Assert.That(request.TransactionControl[0].CommandCount, Is.EqualTo(1u));
                Assert.That(request.TransactionControl[0].OperationCount, Is.EqualTo(1u));
                Assert.That(request.TransactionControl[0].NextOperationSequence, Is.EqualTo(7UL));

                CancelAsyncOperation(scenario.Owner, request, operationId);
                Assert.That(request.TransactionControl[0].CommandCount, Is.EqualTo(2u));
                Assert.That(request.TransactionControl[0].OperationCount, Is.EqualTo(1u));
                Assert.That(request.TransactionControl[0].NextOperationSequence, Is.EqualTo(7UL));
                Assert.That(request.Operations[0].State,
                    Is.EqualTo(NativeBurstDispatchOperationStateV2.Tombstoned));
                Assert.That(request.Commands[0].Kind, Is.EqualTo(NativeBurstDispatchCommandKindV2.Start));
                Assert.That(request.Commands[1].Kind, Is.EqualTo(NativeBurstDispatchCommandKindV2.Cancel));
                Assert.That(ReadInt32(request.CommandPayloadBytes, 0), Is.EqualTo(11));
                Assert.That(ReadInt32(request.CommandPayloadBytes, 8), Is.EqualTo(33));
            }
        }

        private static OperationId PublishAsyncStart(
            NativeBurstDispatchWorkspaceOwnerV2 owner,
            AsyncRequestBuffers request)
        {
            request.SetRequest(BurstCallbackPhase.Enter, 9u);
            BeginAsync(owner, request, out var lease, out var batch, out var frame, out var configuration);
            Assert.That(BurstGeneratedRuntimeBridge.TryReadAsyncOperationHandle<int, int>(
                ref configuration,
                0u,
                NativeBuiltInBlackboardTypeIdsV1.Int32,
                1u,
                NativeBuiltInBlackboardTypeIdsV1.Int32,
                1u,
                out var handle), Is.EqualTo(BurstContextResult.Success));
            Assert.That(BurstGeneratedRuntimeBridge.TryCreateEnterContext(
                in frame, out var context), Is.EqualTo(BurstContextResult.Success));
            Assert.That(context.TryBeginStart(handle, out var start, out var faultCancel),
                Is.EqualTo(BurstContextResult.Success));
            Assert.That(BurstGeneratedRuntimeBridge.TryWriteValue(ref start, 0u, 0u, 11),
                Is.EqualTo(BurstContextResult.Success));
            Assert.That(BurstGeneratedRuntimeBridge.TryWriteValue(ref faultCancel, 0u, 0u, 22),
                Is.EqualTo(BurstContextResult.Success));
            Assert.That(BurstGeneratedRuntimeBridge.TryCommitStart(
                ref start, ref faultCancel, out var operationId), Is.EqualTo(BurstContextResult.Success));
            SealMemory(in frame, 1);
            Assert.That(BurstGeneratedRuntimeBridge.TryCompleteEnter(
                ref batch, in frame, ref context), Is.EqualTo(BurstContextResult.Success));
            Assert.That(owner.TryConsumeResult(in lease, out var result, out var failure),
                Is.True, failure.ToString());
            Assert.That(result.Execution.Code, Is.EqualTo(BurstExecutionCode.Success));
            Assert.That(result.FrameAcquired, Is.True);
            Assert.That(owner.TryReset(in lease, out failure), Is.True, failure.ToString());
            return operationId;
        }

        private static void FailAsyncStart(
            NativeBurstDispatchWorkspaceOwnerV2 owner,
            AsyncRequestBuffers request)
        {
            request.SetRequest(BurstCallbackPhase.Enter, 10u);
            BeginAsync(owner, request, out var lease, out var batch, out var frame, out var configuration);
            Assert.That(BurstGeneratedRuntimeBridge.TryReadAsyncOperationHandle<int, int>(
                ref configuration,
                0u,
                NativeBuiltInBlackboardTypeIdsV1.Int32,
                1u,
                NativeBuiltInBlackboardTypeIdsV1.Int32,
                1u,
                out var handle), Is.EqualTo(BurstContextResult.Success));
            Assert.That(BurstGeneratedRuntimeBridge.TryCreateEnterContext(
                in frame, out var context), Is.EqualTo(BurstContextResult.Success));
            Assert.That(context.TryBeginStart(handle, out var start, out var faultCancel),
                Is.EqualTo(BurstContextResult.Success));
            Assert.That(BurstGeneratedRuntimeBridge.TryWriteValue(ref start, 0u, 0u, 44),
                Is.EqualTo(BurstContextResult.Success));
            Assert.That(BurstGeneratedRuntimeBridge.TryWriteValue(ref faultCancel, 0u, 0u, 55),
                Is.EqualTo(BurstContextResult.Success));
            Assert.That(BurstGeneratedRuntimeBridge.TryCommitStart(
                ref start, ref faultCancel, out var skippedOperation), Is.EqualTo(BurstContextResult.Success));
            Assert.That(skippedOperation.Sequence, Is.EqualTo(6UL));

            var firstFailure = BurstGeneratedRuntimeBridge.TryReadAsyncOperationHandle<int, int>(
                ref configuration,
                0u,
                NativeBuiltInBlackboardTypeIdsV1.Int32,
                2u,
                NativeBuiltInBlackboardTypeIdsV1.Int32,
                1u,
                out _);
            Assert.That(firstFailure, Is.EqualTo(BurstContextResult.TypeMismatch));
            Assert.That(BurstGeneratedRuntimeBridge.TryFailDispatch(
                ref batch, in frame, firstFailure), Is.EqualTo(firstFailure));
            Assert.That(owner.TryConsumeResult(in lease, out var result, out var failure),
                Is.True, failure.ToString());
            Assert.That(result.Execution.Code, Is.EqualTo(BurstExecutionCode.Faulted));
            Assert.That(result.CallbackFailure, Is.EqualTo(firstFailure));
            Assert.That(result.FrameAcquired, Is.True);
            Assert.That(owner.TryReset(in lease, out failure), Is.True, failure.ToString());
        }

        private static void CancelAsyncOperation(
            NativeBurstDispatchWorkspaceOwnerV2 owner,
            AsyncRequestBuffers request,
            OperationId operationId)
        {
            request.SetRequest(BurstCallbackPhase.Abort, 9u);
            BeginAsync(owner, request, out var lease, out var batch, out var frame, out var configuration);
            Assert.That(BurstGeneratedRuntimeBridge.TryReadAsyncOperationHandle<int, int>(
                ref configuration,
                0u,
                NativeBuiltInBlackboardTypeIdsV1.Int32,
                1u,
                NativeBuiltInBlackboardTypeIdsV1.Int32,
                1u,
                out var handle), Is.EqualTo(BurstContextResult.Success));
            Assert.That(BurstGeneratedRuntimeBridge.TryCreateAbortContext(
                in frame, out var context), Is.EqualTo(BurstContextResult.Success));
            Assert.That(context.TryBeginCancel(handle, operationId, out var cancel),
                Is.EqualTo(BurstContextResult.Success));
            Assert.That(BurstGeneratedRuntimeBridge.TryWriteValue(ref cancel, 0u, 0u, 33),
                Is.EqualTo(BurstContextResult.Success));
            Assert.That(BurstGeneratedRuntimeBridge.TryCommitCancel(ref cancel),
                Is.EqualTo(BurstContextResult.Success));

            var firstFailure = BurstGeneratedRuntimeBridge.TryCompleteAbort(ref batch, in frame);
            Assert.That(firstFailure, Is.EqualTo(BurstContextResult.IncompleteValue));
            Assert.That(BurstGeneratedRuntimeBridge.TryFailDispatch(
                ref batch, in frame, firstFailure), Is.EqualTo(firstFailure));
            Assert.That(owner.TryConsumeResult(in lease, out var result, out var failure),
                Is.True, failure.ToString());
            Assert.That(result.Execution.Code, Is.EqualTo(BurstExecutionCode.Faulted));
            Assert.That(result.CallbackFailure, Is.EqualTo(firstFailure));
            Assert.That(owner.TryReset(in lease, out failure), Is.True, failure.ToString());
        }

        private static void BeginAsync(
            NativeBurstDispatchWorkspaceOwnerV2 owner,
            AsyncRequestBuffers request,
            out NativeBurstDispatchWorkspaceLeaseV2 lease,
            out BurstExecutionBatch batch,
            out BurstDispatchFrame frame,
            out BurstConfigurationReader configuration)
        {
            var views = request.Views;
            Assert.That(owner.TryBeginRequest(in views, out lease, out var failure),
                Is.True, failure.ToString());
            Assert.That(owner.TryAcquireImmediateBatch(in lease, out batch, out failure),
                Is.True, failure.ToString());
            Assert.That(BurstGeneratedRuntimeBridge.TryGetExecutionRequest(
                in batch,
                out var instanceOrdinal,
                out var runtimeNodeIndex,
                out var catalogCaseIndex,
                out var phase,
                out var hasWork), Is.EqualTo(BurstContextResult.Success));
            Assert.That(hasWork, Is.True);
            Assert.That(BurstGeneratedRuntimeBridge.TryAcquireDispatchFrame(
                ref batch,
                instanceOrdinal,
                runtimeNodeIndex,
                catalogCaseIndex,
                phase,
                out frame), Is.EqualTo(BurstContextResult.Success));
            Assert.That(BurstGeneratedRuntimeBridge.TryCreateConfigurationReader(
                in frame, out configuration), Is.EqualTo(BurstContextResult.Success));
        }

        private static void PublishEffect(
            NativeBurstDispatchWorkspaceOwnerV2 owner,
            RequestBuffers request,
            int payload)
        {
            ExecuteEffectToTerminal(owner, request, payload, out var lease);
            Assert.That(owner.TryConsumeResult(in lease, out var result, out var failure),
                Is.True, failure.ToString());
            Assert.That(result.Execution.Code, Is.EqualTo(BurstExecutionCode.Success));
            Assert.That(owner.TryReset(in lease, out failure), Is.True, failure.ToString());
        }

        private static void ExecuteEffectToTerminal(
            NativeBurstDispatchWorkspaceOwnerV2 owner,
            RequestBuffers request,
            int payload,
            out NativeBurstDispatchWorkspaceLeaseV2 lease)
        {
            var views = request.Views;
            Assert.That(owner.TryBeginRequest(in views, out lease, out var failure),
                Is.True, failure.ToString());
            Assert.That(owner.TryAcquireImmediateBatch(in lease, out var batch, out failure),
                Is.True, failure.ToString());
            Assert.That(BurstGeneratedRuntimeBridge.TryGetExecutionRequest(
                in batch,
                out var instanceOrdinal,
                out var runtimeNodeIndex,
                out var catalogCaseIndex,
                out var phase,
                out var hasWork), Is.EqualTo(BurstContextResult.Success));
            Assert.That(hasWork, Is.True);
            Assert.That(BurstGeneratedRuntimeBridge.TryAcquireDispatchFrame(
                ref batch,
                instanceOrdinal,
                runtimeNodeIndex,
                catalogCaseIndex,
                phase,
                out var frame), Is.EqualTo(BurstContextResult.Success));
            Assert.That(BurstGeneratedRuntimeBridge.TryCreateConfigurationReader(
                in frame, out var configuration), Is.EqualTo(BurstContextResult.Success));
            Assert.That(BurstGeneratedRuntimeBridge.TryReadCommandHandle<int>(
                ref configuration,
                0u,
                NativeBuiltInBlackboardTypeIdsV1.Int32,
                1u,
                out var handle), Is.EqualTo(BurstContextResult.Success));
            Assert.That(BurstGeneratedRuntimeBridge.TryCreateEnterContext(
                in frame, out var context), Is.EqualTo(BurstContextResult.Success));
            Assert.That(context.TryBeginEffect(handle, out var writer),
                Is.EqualTo(BurstContextResult.Success));
            Assert.That(BurstGeneratedRuntimeBridge.TryWriteValue(ref writer, 0u, 0u, payload),
                Is.EqualTo(BurstContextResult.Success));
            Assert.That(BurstGeneratedRuntimeBridge.TryCommitEffect(ref writer),
                Is.EqualTo(BurstContextResult.Success));
            SealMemory(in frame, 0);
            Assert.That(BurstGeneratedRuntimeBridge.TryCompleteEnter(
                ref batch, in frame, ref context), Is.EqualTo(BurstContextResult.Success));
        }

        private static void ExecuteEnter(ref BurstExecutionBatch batch, int expected, int written)
        {
            Assert.That(BurstGeneratedRuntimeBridge.TryGetExecutionRequest(
                in batch,
                out var instanceOrdinal,
                out var runtimeNodeIndex,
                out var catalogCaseIndex,
                out var phase,
                out var hasWork), Is.EqualTo(BurstContextResult.Success));
            Assert.That(hasWork, Is.True);
            Assert.That(BurstGeneratedRuntimeBridge.TryAcquireDispatchFrame(
                ref batch,
                instanceOrdinal,
                runtimeNodeIndex,
                catalogCaseIndex,
                phase,
                out var frame), Is.EqualTo(BurstContextResult.Success));
            Assert.That(BurstGeneratedRuntimeBridge.TryCreateConfigurationReader(
                in frame, out var configuration), Is.EqualTo(BurstContextResult.Success));
            Assert.That(BurstGeneratedRuntimeBridge.TryReadUInt32(
                ref configuration, 0u, 0u, out var config), Is.EqualTo(BurstContextResult.Success));
            Assert.That(config, Is.EqualTo(0x1234_5678u));
            Assert.That(BurstGeneratedRuntimeBridge.TryCreateMemoryAccessor(
                in frame, out var memory), Is.EqualTo(BurstContextResult.Success));
            Assert.That(BurstGeneratedRuntimeBridge.TryReadMemoryInt32(
                ref memory, 0u, 0u, out var current), Is.EqualTo(BurstContextResult.Success));
            Assert.That(current, Is.EqualTo(expected));
            Assert.That(BurstGeneratedRuntimeBridge.TryWriteMemoryInt32(
                ref memory, 0u, 0u, written), Is.EqualTo(BurstContextResult.Success));
            Assert.That(BurstGeneratedRuntimeBridge.TryCommitMemory(ref memory),
                Is.EqualTo(BurstContextResult.Success));
            Assert.That(BurstGeneratedRuntimeBridge.TryCreateEnterContext(
                in frame, out var context), Is.EqualTo(BurstContextResult.Success));
            Assert.That(context.TryNextUInt32(out _), Is.EqualTo(BurstContextResult.Success));
            Assert.That(BurstGeneratedRuntimeBridge.TryCompleteEnter(
                ref batch, in frame, ref context), Is.EqualTo(BurstContextResult.Success));
        }

        private static bool TryRunPlainCycle(
            NativeBurstDispatchWorkspaceOwnerV2 owner,
            RequestBuffers request,
            int written)
        {
            var views = request.Views;
            if (!owner.TryBeginRequest(in views, out var lease, out _)
                || !owner.TryAcquireImmediateBatch(in lease, out var batch, out _)
                || BurstGeneratedRuntimeBridge.TryGetExecutionRequest(
                    in batch,
                    out var instanceOrdinal,
                    out var runtimeNodeIndex,
                    out var catalogCaseIndex,
                    out var phase,
                    out var hasWork) != BurstContextResult.Success
                || !hasWork
                || BurstGeneratedRuntimeBridge.TryAcquireDispatchFrame(
                    ref batch,
                    instanceOrdinal,
                    runtimeNodeIndex,
                    catalogCaseIndex,
                    phase,
                    out var frame) != BurstContextResult.Success
                || BurstGeneratedRuntimeBridge.TryCreateConfigurationReader(
                    in frame, out var configuration) != BurstContextResult.Success
                || BurstGeneratedRuntimeBridge.TryReadUInt32(
                    ref configuration, 0u, 0u, out var config) != BurstContextResult.Success
                || config != 0x1234_5678u
                || BurstGeneratedRuntimeBridge.TryCreateMemoryAccessor(
                    in frame, out var memory) != BurstContextResult.Success
                || BurstGeneratedRuntimeBridge.TryWriteMemoryInt32(
                    ref memory, 0u, 0u, written) != BurstContextResult.Success
                || BurstGeneratedRuntimeBridge.TryCommitMemory(ref memory) != BurstContextResult.Success
                || BurstGeneratedRuntimeBridge.TryCreateEnterContext(
                    in frame, out var context) != BurstContextResult.Success
                || context.TryNextUInt32(out _) != BurstContextResult.Success
                || BurstGeneratedRuntimeBridge.TryCompleteEnter(
                    ref batch, in frame, ref context) != BurstContextResult.Success
                || !owner.TryConsumeResult(in lease, out var result, out _)
                || result.Execution.Code != BurstExecutionCode.Success
                || !owner.TryReset(in lease, out _))
            {
                return false;
            }

            return true;
        }

        private static void SealMemory(in BurstDispatchFrame frame, int value)
        {
            Assert.That(BurstGeneratedRuntimeBridge.TryCreateMemoryAccessor(
                in frame, out var memory), Is.EqualTo(BurstContextResult.Success));
            Assert.That(BurstGeneratedRuntimeBridge.TryWriteMemoryInt32(
                ref memory, 0u, 0u, value), Is.EqualTo(BurstContextResult.Success));
            Assert.That(BurstGeneratedRuntimeBridge.TryCommitMemory(ref memory),
                Is.EqualTo(BurstContextResult.Success));
        }

        private static BurstCatalogHandshake Handshake()
        {
            return new BurstCatalogHandshake(
                2u,
                new BurstCatalogFingerprint(Hash(0x10u)),
                Hash(0x20u),
                1u,
                1u,
                Hash(0x30u),
                Hash(0x40u),
                Hash(0x50u));
        }

        private static BurstHash256 Hash(uint firstWord)
        {
            return new BurstHash256(
                firstWord,
                firstWord + 1u,
                firstWord + 2u,
                firstWord + 3u,
                firstWord + 4u,
                firstWord + 5u,
                firstWord + 6u,
                firstWord + 7u);
        }

        private static int ReadInt32(NativeArray<byte> source, int offset)
        {
            var bits = source[offset]
                | (uint)source[offset + 1] << 8
                | (uint)source[offset + 2] << 16
                | (uint)source[offset + 3] << 24;
            return unchecked((int)bits);
        }

        private static void WriteInt32(NativeArray<byte> destination, int offset, int value)
        {
            var bits = unchecked((uint)value);
            destination[offset] = (byte)bits;
            destination[offset + 1] = (byte)(bits >> 8);
            destination[offset + 2] = (byte)(bits >> 16);
            destination[offset + 3] = (byte)(bits >> 24);
        }

        private static void SetTransaction(
            NativeArray<NativeBurstDispatchTransactionControlV2> destination,
            NativeBurstDispatchTransactionControlV2 value)
        {
            if (value.MutationVersion == 0)
            {
                NativeBurstDispatchTransactionLedgerV2.Initialize(ref value);
            }
            else
            {
                NativeBurstDispatchTransactionLedgerV2.Seal(ref value);
            }

            destination[0] = value;
        }

        private sealed class WorkspaceScenario : IDisposable
        {
            private bool _disposed;

            private WorkspaceScenario(NativeBurstDispatchWorkspaceOwnerV2 owner)
            {
                Owner = owner;
            }

            internal NativeBurstDispatchWorkspaceOwnerV2 Owner { get; }

            internal void MarkDisposed() => _disposed = true;

            internal static WorkspaceScenario Create(bool withEffect)
            {
                using (var shape = new ShapeBuffers(withEffect))
                {
                    var value = shape.Value;
                    var capacity = withEffect
                        ? new NativeBurstDispatchWorkspaceCapacityV2(
                            4u,
                            new NativeBurstDispatchBindingCapacityV2(1u, 4u, 2u, 8u, 0u, 7UL))
                        : new NativeBurstDispatchWorkspaceCapacityV2(4u, default);
                    Assert.That(NativeBurstDispatchWorkspaceOwnerV2.TryCreate(
                        in value,
                        in capacity,
                        Allocator.Persistent,
                        out var owner,
                        out var failure), Is.True, failure.ToString());
                    return new WorkspaceScenario(owner);
                }
            }

            public void Dispose()
            {
                if (_disposed) return;
                Assert.That(Owner.TryDispose(out var failure), Is.True, failure.ToString());
                _disposed = true;
            }
        }

        private sealed class ShapeBuffers : IDisposable
        {
            private NativeArray<NativeBurstDispatchCaseV2> _cases;
            private readonly NativeArray<NativeBurstDispatchFieldV2> _configurationFields;
            private readonly NativeArray<NativeBurstDispatchFieldV2> _memoryFields;
            private readonly NativeArray<NativeBurstDispatchBindingV2> _bindings;
            private readonly NativeArray<NativeBurstDispatchFieldV2> _valueFields;
            private readonly NativeArray<NativeBurstDispatchCanonicalRangeV2> _caseRanges;
            private readonly NativeArray<NativeBurstDispatchCanonicalRangeV2> _bindingRanges;
            private readonly NativeArray<NativeBurstDispatchCanonicalRuleV2> _rules;

            internal ShapeBuffers(bool withEffect)
            {
                _cases = new NativeArray<NativeBurstDispatchCaseV2>(
                    1, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                _configurationFields = new NativeArray<NativeBurstDispatchFieldV2>(
                    1, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                _memoryFields = new NativeArray<NativeBurstDispatchFieldV2>(
                    1, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                _bindings = withEffect
                    ? new NativeArray<NativeBurstDispatchBindingV2>(
                        1, Allocator.Temp, NativeArrayOptions.UninitializedMemory)
                    : default;
                _valueFields = withEffect
                    ? new NativeArray<NativeBurstDispatchFieldV2>(
                        1, Allocator.Temp, NativeArrayOptions.UninitializedMemory)
                    : default;
                _caseRanges = withEffect
                    ? new NativeArray<NativeBurstDispatchCanonicalRangeV2>(
                        2, Allocator.Temp, NativeArrayOptions.ClearMemory)
                    : default;
                _bindingRanges = withEffect
                    ? new NativeArray<NativeBurstDispatchCanonicalRangeV2>(
                        2, Allocator.Temp, NativeArrayOptions.ClearMemory)
                    : default;
                _rules = withEffect
                    ? new NativeArray<NativeBurstDispatchCanonicalRuleV2>(
                        0, Allocator.Temp, NativeArrayOptions.ClearMemory)
                    : default;

                _cases[0] = new NativeBurstDispatchCaseV2(
                    TypeNumericId,
                    TypeVersion,
                    0u,
                    0u,
                    1u,
                    4u,
                    0u,
                    1u,
                    4u,
                    NativeBurstDispatchPhaseMaskV2.Enter,
                    BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure | BurstNodeStatusMask.Running,
                    true,
                    0u,
                    withEffect ? 1u : 0u);
                _configurationFields[0] = new NativeBurstDispatchFieldV2(
                    0u,
                    0u,
                    1u,
                    4u,
                    withEffect
                        ? NativeBurstDispatchFieldEncodingV2.GeneratedHandle
                        : NativeBurstDispatchFieldEncodingV2.UInt32);
                _memoryFields[0] = new NativeBurstDispatchFieldV2(
                    0u, 0u, 1u, 4u, NativeBurstDispatchFieldEncodingV2.Int32);

                if (withEffect)
                {
                    _bindings[0] = new NativeBurstDispatchBindingV2(
                        0u,
                        0u,
                        NativeBurstDispatchBindingKindV2.EffectCommand,
                        NativeBurstDispatchBindingV2.NoScope,
                        NativeBurstDispatchBindingPhaseMaskV2.Execute,
                        NativeBuiltInBlackboardTypeIdsV1.Int32,
                        1u,
                        0u,
                        1u,
                        4u,
                        0UL,
                        0u,
                        0u,
                        0u,
                        0u);
                    _valueFields[0] = new NativeBurstDispatchFieldV2(
                        0u, 0u, 1u, 4u, NativeBurstDispatchFieldEncodingV2.Int32);
                }

                var canonical = withEffect
                    ? new NativeBurstDispatchCanonicalInputV2(
                        _caseRanges.AsReadOnly(),
                        _bindingRanges.AsReadOnly(),
                        _rules.AsReadOnly())
                    : default;
                Value = new NativeBurstDispatchWorkspaceShapeV2(
                    Handshake(),
                    _cases.AsReadOnly(),
                    _configurationFields.AsReadOnly(),
                    _memoryFields.AsReadOnly(),
                    withEffect ? _bindings.AsReadOnly() : default,
                    withEffect ? _valueFields.AsReadOnly() : default,
                    canonical);
            }

            internal NativeBurstDispatchWorkspaceShapeV2 Value { get; }

            internal void InvalidateCase() => _cases[0] = default;

            public void Dispose()
            {
                if (_rules.IsCreated) _rules.Dispose();
                if (_bindingRanges.IsCreated) _bindingRanges.Dispose();
                if (_caseRanges.IsCreated) _caseRanges.Dispose();
                if (_valueFields.IsCreated) _valueFields.Dispose();
                if (_bindings.IsCreated) _bindings.Dispose();
                if (_memoryFields.IsCreated) _memoryFields.Dispose();
                if (_configurationFields.IsCreated) _configurationFields.Dispose();
                if (_cases.IsCreated) _cases.Dispose();
            }
        }

        private sealed class RequestBuffers : IDisposable
        {
            private NativeBurstDispatchRequestV2 _request;
            private readonly bool _withEffect;
            private readonly NativeArray<byte> _configurationBytes;
            internal readonly NativeArray<byte> MemoryBytes;
            internal NativeArray<ulong> RandomStates;
            private readonly NativeArray<ulong> _randomIncrements;
            private readonly NativeArray<NativeBurstDispatchResolvedBindingV2> _resolvedBindings;
            private readonly NativeArray<byte> _bindingValueBytes;
            private readonly NativeArray<NativeBurstDispatchCompletionV2> _completions;
            private readonly NativeArray<byte> _completionPayloadBytes;
            internal readonly NativeArray<NativeBurstDispatchCommandV2> Commands;
            internal readonly NativeArray<byte> CommandPayloadBytes;
            private readonly NativeArray<NativeBurstDispatchOperationV2> _operations;
            internal NativeArray<NativeBurstDispatchTransactionControlV2> TransactionControl;

            private RequestBuffers(bool withEffect, int memoryValue, uint activationGeneration)
            {
                _withEffect = withEffect;
                _configurationBytes = new NativeArray<byte>(
                    4, Allocator.Persistent, NativeArrayOptions.ClearMemory);
                MemoryBytes = new NativeArray<byte>(
                    4, Allocator.Persistent, NativeArrayOptions.ClearMemory);
                RandomStates = new NativeArray<ulong>(
                    1, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                _randomIncrements = new NativeArray<ulong>(
                    1, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                _resolvedBindings = new NativeArray<NativeBurstDispatchResolvedBindingV2>(
                    withEffect ? 1 : 0,
                    Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory);
                _bindingValueBytes = new NativeArray<byte>(
                    0, Allocator.Persistent, NativeArrayOptions.ClearMemory);
                _completions = new NativeArray<NativeBurstDispatchCompletionV2>(
                    0, Allocator.Persistent, NativeArrayOptions.ClearMemory);
                _completionPayloadBytes = new NativeArray<byte>(
                    0, Allocator.Persistent, NativeArrayOptions.ClearMemory);
                Commands = new NativeArray<NativeBurstDispatchCommandV2>(
                    withEffect ? 2 : 0, Allocator.Persistent, NativeArrayOptions.ClearMemory);
                CommandPayloadBytes = new NativeArray<byte>(
                    withEffect ? 8 : 0, Allocator.Persistent, NativeArrayOptions.ClearMemory);
                _operations = new NativeArray<NativeBurstDispatchOperationV2>(
                    0, Allocator.Persistent, NativeArrayOptions.ClearMemory);
                TransactionControl = new NativeArray<NativeBurstDispatchTransactionControlV2>(
                    1, Allocator.Persistent, NativeArrayOptions.ClearMemory);

                if (!withEffect)
                {
                    _configurationBytes[0] = 0x78;
                    _configurationBytes[1] = 0x56;
                    _configurationBytes[2] = 0x34;
                    _configurationBytes[3] = 0x12;
                }

                WriteInt32(MemoryBytes, 0, memoryValue);
                RandomStates[0] = InitialRandomState;
                _randomIncrements[0] = RandomIncrement;
                if (withEffect)
                {
                    _resolvedBindings[0] = new NativeBurstDispatchResolvedBindingV2(
                        0u, 41u, NativeBurstDispatchBindingV2.NoOffset);
                }

                var transaction = new NativeBurstDispatchTransactionControlV2
                {
                    LedgerToken = 0xabc1UL,
                    TreeInstanceId = new TreeInstanceId(19UL),
                    NextOperationSequence = 7UL
                };
                NativeBurstDispatchTransactionLedgerV2.Initialize(ref transaction);
                TransactionControl[0] = transaction;
                _request = Request(activationGeneration, withEffect);
            }

            internal static RequestBuffers Create(
                bool withEffect,
                int memoryValue,
                uint activationGeneration)
                => new RequestBuffers(withEffect, memoryValue, activationGeneration);

            internal NativeBurstDispatchWorkspaceRequestViewsV2 Views
                => WithTransactionControl(TransactionControl);

            internal bool AllBorrowedArraysAreCreated
                => _configurationBytes.IsCreated
                    && MemoryBytes.IsCreated
                    && RandomStates.IsCreated
                    && _randomIncrements.IsCreated
                    && _resolvedBindings.IsCreated
                    && _bindingValueBytes.IsCreated
                    && _completions.IsCreated
                    && _completionPayloadBytes.IsCreated
                    && Commands.IsCreated
                    && CommandPayloadBytes.IsCreated
                    && _operations.IsCreated
                    && TransactionControl.IsCreated;

            internal NativeBurstDispatchWorkspaceRequestViewsV2 WithTransactionControl(
                NativeArray<NativeBurstDispatchTransactionControlV2> transactionControl)
                => CreateViews(
                    _request,
                    _configurationBytes,
                    MemoryBytes,
                    transactionControl);

            internal NativeBurstDispatchWorkspaceRequestViewsV2 WithDurableArenasFrom(
                RequestBuffers durable)
                => new NativeBurstDispatchWorkspaceRequestViewsV2(
                    _request,
                    _configurationBytes,
                    MemoryBytes,
                    RandomStates,
                    _randomIncrements,
                    _resolvedBindings,
                    _bindingValueBytes,
                    _completions,
                    _completionPayloadBytes,
                    durable.Commands,
                    durable.CommandPayloadBytes,
                    durable._operations,
                    durable.TransactionControl);

            internal NativeBurstDispatchWorkspaceRequestViewsV2 WithConfiguration(
                NativeArray<byte> configurationBytes)
                => CreateViews(_request, configurationBytes, MemoryBytes, TransactionControl);

            internal NativeBurstDispatchWorkspaceRequestViewsV2 WithMemory(
                NativeArray<byte> memoryBytes)
                => CreateViews(_request, _configurationBytes, memoryBytes, TransactionControl);

            internal NativeBurstDispatchWorkspaceRequestViewsV2 WithRequest(
                in NativeBurstDispatchRequestV2 request)
                => CreateViews(request, _configurationBytes, MemoryBytes, TransactionControl);

            internal void ResetPlain(int memoryValue, uint activationGeneration)
            {
                _request = Request(activationGeneration, false);
                WriteInt32(MemoryBytes, 0, memoryValue);
                RandomStates[0] = InitialRandomState;
            }

            private NativeBurstDispatchWorkspaceRequestViewsV2 CreateViews(
                NativeBurstDispatchRequestV2 request,
                NativeArray<byte> configurationBytes,
                NativeArray<byte> memoryBytes,
                NativeArray<NativeBurstDispatchTransactionControlV2> transactionControl)
                => new NativeBurstDispatchWorkspaceRequestViewsV2(
                    request,
                    configurationBytes,
                    memoryBytes,
                    RandomStates,
                    _randomIncrements,
                    _resolvedBindings,
                    _bindingValueBytes,
                    _completions,
                    _completionPayloadBytes,
                    Commands,
                    CommandPayloadBytes,
                    _operations,
                    transactionControl);

            internal void SetActivationGeneration(uint value)
            {
                _request = Request(value, _withEffect);
                RandomStates[0] = InitialRandomState;
            }

            public void Dispose()
            {
                if (TransactionControl.IsCreated) TransactionControl.Dispose();
                if (_operations.IsCreated) _operations.Dispose();
                if (CommandPayloadBytes.IsCreated) CommandPayloadBytes.Dispose();
                if (Commands.IsCreated) Commands.Dispose();
                if (_completionPayloadBytes.IsCreated) _completionPayloadBytes.Dispose();
                if (_completions.IsCreated) _completions.Dispose();
                if (_bindingValueBytes.IsCreated) _bindingValueBytes.Dispose();
                if (_resolvedBindings.IsCreated) _resolvedBindings.Dispose();
                if (_randomIncrements.IsCreated) _randomIncrements.Dispose();
                if (RandomStates.IsCreated) RandomStates.Dispose();
                if (MemoryBytes.IsCreated) MemoryBytes.Dispose();
                if (_configurationBytes.IsCreated) _configurationBytes.Dispose();
            }

            internal static NativeBurstDispatchRequestV2 Request(
                uint activationGeneration,
                bool withEffect,
                uint catalogCaseIndex = 0u)
            {
                return new NativeBurstDispatchRequestV2(
                    InstanceOrdinal,
                    RuntimeNodeIndex,
                    TypeNumericId,
                    TypeVersion,
                    catalogCaseIndex,
                    BurstCallbackPhase.Enter,
                    0u,
                    0u,
                    0u,
                    123_456L,
                    new TreeInstanceId(19UL),
                    activationGeneration,
                    0u,
                    withEffect ? 1u : 0u);
            }
        }

        private sealed class AsyncWorkspaceScenario : IDisposable
        {
            private bool _disposed;

            private AsyncWorkspaceScenario(NativeBurstDispatchWorkspaceOwnerV2 owner)
            {
                Owner = owner;
            }

            internal NativeBurstDispatchWorkspaceOwnerV2 Owner { get; }

            internal static AsyncWorkspaceScenario Create()
            {
                using (var shape = new AsyncShapeBuffers())
                {
                    var value = shape.Value;
                    var capacity = new NativeBurstDispatchWorkspaceCapacityV2(
                        4u,
                        new NativeBurstDispatchBindingCapacityV2(
                            4u, 16u, 4u, 32u, 2u, 5UL));
                    Assert.That(NativeBurstDispatchWorkspaceOwnerV2.TryCreate(
                        in value,
                        in capacity,
                        Allocator.Persistent,
                        out var owner,
                        out var failure), Is.True, failure.ToString());
                    return new AsyncWorkspaceScenario(owner);
                }
            }

            public void Dispose()
            {
                if (_disposed) return;
                Assert.That(Owner.TryDispose(out var failure), Is.True, failure.ToString());
                _disposed = true;
            }
        }

        private sealed class AsyncShapeBuffers : IDisposable
        {
            private readonly NativeArray<NativeBurstDispatchCaseV2> _cases;
            private readonly NativeArray<NativeBurstDispatchFieldV2> _configurationFields;
            private readonly NativeArray<NativeBurstDispatchFieldV2> _memoryFields;
            private readonly NativeArray<NativeBurstDispatchBindingV2> _bindings;
            private readonly NativeArray<NativeBurstDispatchFieldV2> _valueFields;
            private readonly NativeArray<NativeBurstDispatchCanonicalRangeV2> _caseRanges;
            private readonly NativeArray<NativeBurstDispatchCanonicalRangeV2> _bindingRanges;
            private readonly NativeArray<NativeBurstDispatchCanonicalRuleV2> _rules;

            internal AsyncShapeBuffers()
            {
                _cases = new NativeArray<NativeBurstDispatchCaseV2>(1, Allocator.Temp);
                _configurationFields = new NativeArray<NativeBurstDispatchFieldV2>(1, Allocator.Temp);
                _memoryFields = new NativeArray<NativeBurstDispatchFieldV2>(1, Allocator.Temp);
                _bindings = new NativeArray<NativeBurstDispatchBindingV2>(1, Allocator.Temp);
                _valueFields = new NativeArray<NativeBurstDispatchFieldV2>(2, Allocator.Temp);
                _caseRanges = new NativeArray<NativeBurstDispatchCanonicalRangeV2>(2, Allocator.Temp);
                _bindingRanges = new NativeArray<NativeBurstDispatchCanonicalRangeV2>(2, Allocator.Temp);
                _rules = new NativeArray<NativeBurstDispatchCanonicalRuleV2>(0, Allocator.Temp);

                _cases[0] = new NativeBurstDispatchCaseV2(
                    TypeNumericId,
                    TypeVersion,
                    0u,
                    0u,
                    1u,
                    4u,
                    0u,
                    1u,
                    4u,
                    NativeBurstDispatchPhaseMaskV2.Enter | NativeBurstDispatchPhaseMaskV2.Abort,
                    BurstNodeStatusMask.Success,
                    false,
                    0u,
                    1u);
                _configurationFields[0] = new NativeBurstDispatchFieldV2(
                    0u, 0u, 1u, 4u, NativeBurstDispatchFieldEncodingV2.GeneratedHandle);
                _memoryFields[0] = new NativeBurstDispatchFieldV2(
                    0u, 0u, 1u, 4u, NativeBurstDispatchFieldEncodingV2.Int32);
                _bindings[0] = new NativeBurstDispatchBindingV2(
                    0u,
                    0u,
                    NativeBurstDispatchBindingKindV2.AsyncOperation,
                    NativeBurstDispatchBindingV2.NoScope,
                    NativeBurstDispatchBindingPhaseMaskV2.Execute
                        | NativeBurstDispatchBindingPhaseMaskV2.Cancel,
                    NativeBuiltInBlackboardTypeIdsV1.Int32,
                    1u,
                    0u,
                    1u,
                    4u,
                    NativeBuiltInBlackboardTypeIdsV1.Int32,
                    1u,
                    1u,
                    1u,
                    4u);
                _valueFields[0] = new NativeBurstDispatchFieldV2(
                    0u, 0u, 1u, 4u, NativeBurstDispatchFieldEncodingV2.Int32);
                _valueFields[1] = new NativeBurstDispatchFieldV2(
                    0u, 0u, 1u, 4u, NativeBurstDispatchFieldEncodingV2.Int32);

                var canonical = new NativeBurstDispatchCanonicalInputV2(
                    _caseRanges.AsReadOnly(),
                    _bindingRanges.AsReadOnly(),
                    _rules.AsReadOnly());
                Value = new NativeBurstDispatchWorkspaceShapeV2(
                    Handshake(),
                    _cases.AsReadOnly(),
                    _configurationFields.AsReadOnly(),
                    _memoryFields.AsReadOnly(),
                    _bindings.AsReadOnly(),
                    _valueFields.AsReadOnly(),
                    canonical);
            }

            internal NativeBurstDispatchWorkspaceShapeV2 Value { get; }

            public void Dispose()
            {
                _rules.Dispose();
                _bindingRanges.Dispose();
                _caseRanges.Dispose();
                _valueFields.Dispose();
                _bindings.Dispose();
                _memoryFields.Dispose();
                _configurationFields.Dispose();
                _cases.Dispose();
            }
        }

        private sealed class AsyncRequestBuffers : IDisposable
        {
            private NativeBurstDispatchRequestV2 _request;
            private readonly NativeArray<byte> _configurationBytes;
            private readonly NativeArray<byte> _memoryBytes;
            private readonly NativeArray<ulong> _randomStates;
            private readonly NativeArray<ulong> _randomIncrements;
            private readonly NativeArray<NativeBurstDispatchResolvedBindingV2> _resolvedBindings;
            private readonly NativeArray<byte> _bindingValueBytes;
            private readonly NativeArray<NativeBurstDispatchCompletionV2> _completions;
            private readonly NativeArray<byte> _completionPayloadBytes;
            internal readonly NativeArray<NativeBurstDispatchCommandV2> Commands;
            internal readonly NativeArray<byte> CommandPayloadBytes;
            internal readonly NativeArray<NativeBurstDispatchOperationV2> Operations;
            internal readonly NativeArray<NativeBurstDispatchTransactionControlV2> TransactionControl;

            internal AsyncRequestBuffers()
            {
                _configurationBytes = new NativeArray<byte>(4, Allocator.Persistent);
                _memoryBytes = new NativeArray<byte>(4, Allocator.Persistent);
                _randomStates = new NativeArray<ulong>(0, Allocator.Persistent);
                _randomIncrements = new NativeArray<ulong>(0, Allocator.Persistent);
                _resolvedBindings = new NativeArray<NativeBurstDispatchResolvedBindingV2>(
                    1, Allocator.Persistent);
                _bindingValueBytes = new NativeArray<byte>(0, Allocator.Persistent);
                _completions = new NativeArray<NativeBurstDispatchCompletionV2>(0, Allocator.Persistent);
                _completionPayloadBytes = new NativeArray<byte>(0, Allocator.Persistent);
                Commands = new NativeArray<NativeBurstDispatchCommandV2>(4, Allocator.Persistent);
                CommandPayloadBytes = new NativeArray<byte>(32, Allocator.Persistent);
                Operations = new NativeArray<NativeBurstDispatchOperationV2>(2, Allocator.Persistent);
                TransactionControl = new NativeArray<NativeBurstDispatchTransactionControlV2>(
                    1, Allocator.Persistent);
                _resolvedBindings[0] = new NativeBurstDispatchResolvedBindingV2(
                    0u, 500u, NativeBurstDispatchBindingV2.NoOffset);
                var transaction = new NativeBurstDispatchTransactionControlV2
                {
                    LedgerToken = 0xabc2UL,
                    TreeInstanceId = new TreeInstanceId(19UL),
                    NextOperationSequence = 5UL
                };
                NativeBurstDispatchTransactionLedgerV2.Initialize(ref transaction);
                TransactionControl[0] = transaction;
                SetRequest(BurstCallbackPhase.Enter, 9u);
            }

            internal NativeBurstDispatchWorkspaceRequestViewsV2 Views
                => new NativeBurstDispatchWorkspaceRequestViewsV2(
                    _request,
                    _configurationBytes,
                    _memoryBytes,
                    _randomStates,
                    _randomIncrements,
                    _resolvedBindings,
                    _bindingValueBytes,
                    _completions,
                    _completionPayloadBytes,
                    Commands,
                    CommandPayloadBytes,
                    Operations,
                    TransactionControl);

            internal void SetRequest(BurstCallbackPhase phase, uint activationGeneration)
            {
                _request = new NativeBurstDispatchRequestV2(
                    InstanceOrdinal,
                    RuntimeNodeIndex,
                    TypeNumericId,
                    TypeVersion,
                    0u,
                    phase,
                    0u,
                    0u,
                    0u,
                    321L,
                    new TreeInstanceId(19UL),
                    activationGeneration,
                    0u,
                    1u,
                    BurstNodeAbortReason.Explicit,
                    BurstNodeExitReason.Aborted);
                WriteInt32(_memoryBytes, 0, 0);
            }

            public void Dispose()
            {
                TransactionControl.Dispose();
                Operations.Dispose();
                CommandPayloadBytes.Dispose();
                Commands.Dispose();
                _completionPayloadBytes.Dispose();
                _completions.Dispose();
                _bindingValueBytes.Dispose();
                _resolvedBindings.Dispose();
                _randomIncrements.Dispose();
                _randomStates.Dispose();
                _memoryBytes.Dispose();
                _configurationBytes.Dispose();
            }
        }

        private struct WorkspaceBlockerJob : IJob
        {
            internal int DurationMilliseconds;

            public void Execute() => Thread.Sleep(DurationMilliseconds);
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct WorkspaceEnterJob : IJob
        {
            internal BurstExecutionBatch Batch;
            internal NativeArray<int> Probe;

            public void Execute()
            {
                var batch = Batch;
                if (BurstGeneratedRuntimeBridge.TryGetExecutionRequest(
                        in batch,
                        out var instanceOrdinal,
                        out var runtimeNodeIndex,
                        out var catalogCaseIndex,
                        out var phase,
                        out var hasWork) != BurstContextResult.Success
                    || !hasWork
                    || BurstGeneratedRuntimeBridge.TryAcquireDispatchFrame(
                        ref batch,
                        instanceOrdinal,
                        runtimeNodeIndex,
                        catalogCaseIndex,
                        phase,
                        out var frame) != BurstContextResult.Success
                    || BurstGeneratedRuntimeBridge.TryCreateMemoryAccessor(
                        in frame, out var memory) != BurstContextResult.Success
                    || BurstGeneratedRuntimeBridge.TryWriteMemoryInt32(
                        ref memory, 0u, 0u, 12) != BurstContextResult.Success
                    || BurstGeneratedRuntimeBridge.TryCommitMemory(ref memory) != BurstContextResult.Success
                    || BurstGeneratedRuntimeBridge.TryCreateEnterContext(
                        in frame, out var context) != BurstContextResult.Success
                    || context.TryNextUInt32(out _) != BurstContextResult.Success
                    || BurstGeneratedRuntimeBridge.TryCompleteEnter(
                        ref batch, in frame, ref context) != BurstContextResult.Success)
                {
                    Probe[0] = -1;
                    return;
                }

                Probe[0] = 1;
            }
        }
    }
}
