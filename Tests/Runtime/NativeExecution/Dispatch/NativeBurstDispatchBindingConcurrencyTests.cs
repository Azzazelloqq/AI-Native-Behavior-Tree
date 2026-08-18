using System;
using AIBT.Burst;
using AIBT.Execution.Burst.Dispatch;
using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;

namespace AIBT.Tests.Runtime.NativeExecution.Dispatch
{
    public sealed class NativeBurstDispatchBindingConcurrencyTests
    {
        private const ulong NodeTypeNumericId = 0x82d8_6b70_ea8f_4771UL;
        private const uint NodeTypeVersion = 3u;
        private const ulong PayloadTypeNumericId = 0x71e9_6c2a_f041_593bUL;
        private const uint PayloadTypeVersion = 5u;
        private const uint InstanceOrdinal = 4u;
        private const uint RuntimeNodeIndex = 9u;
        private const uint TargetOrdinal = 17u;
        private const int PayloadValue = 73;

        [Test]
        public void CompletedFrameRejectsLateCopiedBindingCarriersWithoutPublishingAgain()
        {
            using (var scenario = Scenario.Create(NativeBurstDispatchBindingKindV2.EffectCommand))
            {
                var batch = scenario.Batch;
                var frame = AcquireFrame(ref batch);
                SealEmptyMemory(in frame);
                var context = CreateEnterContext(in frame);
                var handle = DecodeEffectHandle(in frame);

                Assert.That(context.TryBeginEffect(handle, out var writer),
                    Is.EqualTo(BurstContextResult.Success));
                Assert.That(BurstGeneratedRuntimeBridge.TryWriteValue(
                    ref writer, 0u, 0u, PayloadValue), Is.EqualTo(BurstContextResult.Success));
                var lateContext = context;
                var lateWriter = writer;
                Assert.That(BurstGeneratedRuntimeBridge.TryCommitEffect(ref writer),
                    Is.EqualTo(BurstContextResult.Success));
                Assert.That(BurstGeneratedRuntimeBridge.TryCompleteEnter(
                    ref batch, in frame, ref context), Is.EqualTo(BurstContextResult.Success));

                var lateBegin = lateContext.TryBeginEffect(handle, out _);
                var lateCommit = BurstGeneratedRuntimeBridge.TryCommitEffect(ref lateWriter);
                Assert.That(scenario.Owner.TryGetTransactionSnapshot(out var snapshot), Is.True);

                Assert.That(lateBegin, Is.EqualTo(BurstContextResult.InvalidHandle));
                Assert.That(lateCommit, Is.EqualTo(BurstContextResult.InvalidHandle));
                Assert.That(snapshot.CommandCount, Is.EqualTo(1u));
                Assert.That(scenario.Owner.TryGetPublishedCommand(0u, out var command), Is.True);
                Assert.That(command.Kind, Is.EqualTo(NativeBurstDispatchCommandKindV2.Effect));
                Assert.That(command.TargetOrdinal, Is.EqualTo(TargetOrdinal));
                Assert.That(scenario.Owner.TryGetPublishedCommand(1u, out _), Is.False);
            }
        }

        [Test]
        public void CopiedWriterHasOneCommitWinnerAndARepeatedCommitRollsBackTheFrame()
        {
            using (var scenario = Scenario.Create(NativeBurstDispatchBindingKindV2.EffectCommand))
            {
                var batch = scenario.Batch;
                var frame = AcquireFrame(ref batch);
                SealEmptyMemory(in frame);
                var context = CreateEnterContext(in frame);
                var handle = DecodeEffectHandle(in frame);
                Assert.That(context.TryBeginEffect(handle, out var writer),
                    Is.EqualTo(BurstContextResult.Success));
                Assert.That(BurstGeneratedRuntimeBridge.TryWriteValue(
                    ref writer, 0u, 0u, PayloadValue), Is.EqualTo(BurstContextResult.Success));

                var copiedWriter = writer;
                var firstCommit = BurstGeneratedRuntimeBridge.TryCommitEffect(ref writer);
                var repeatedCommit = BurstGeneratedRuntimeBridge.TryCommitEffect(ref copiedWriter);
                var completion = BurstGeneratedRuntimeBridge.TryCompleteEnter(
                    ref batch, in frame, ref context);
                var failure = completion == BurstContextResult.Success
                    ? BurstContextResult.Success
                    : BurstGeneratedRuntimeBridge.TryFailDispatch(
                        ref batch, in frame, completion);
                Assert.That(scenario.Owner.TryGetTransactionSnapshot(out var snapshot), Is.True);

                Assert.That(firstCommit, Is.EqualTo(BurstContextResult.Success));
                Assert.That(repeatedCommit, Is.EqualTo(BurstContextResult.AlreadyCommitted));
                Assert.That(completion, Is.EqualTo(BurstContextResult.AlreadyCommitted));
                Assert.That(failure, Is.EqualTo(BurstContextResult.AlreadyCommitted));
                Assert.That(snapshot.CommandCount, Is.Zero);
                Assert.That(snapshot.SessionCount, Is.Zero);
                Assert.That(scenario.Owner.TryGetPublishedCommand(0u, out _), Is.False);
            }
        }

        [Test]
        public void CopiedCompletionReaderHasOneConsumeWinnerAndFaultLeavesCompletionAvailable()
        {
            using (var scenario = Scenario.Create(NativeBurstDispatchBindingKindV2.Completion))
            {
                var batch = scenario.Batch;
                var frame = AcquireFrame(ref batch);
                SealEmptyMemory(in frame);
                var context = CreateEnterContext(in frame);
                var handle = DecodeCompletionHandle(in frame);
                Assert.That(context.TryBeginConsume(
                    handle,
                    Scenario.CompletionOperationId,
                    out var outcome,
                    out var reader), Is.EqualTo(BurstContextResult.Success));
                Assert.That(outcome, Is.EqualTo(BurstCompletionOutcome.Succeeded));
                Assert.That(BurstGeneratedRuntimeBridge.TryReadValue(
                    ref reader, 0u, 0u, out int payload), Is.EqualTo(BurstContextResult.Success));
                Assert.That(payload, Is.EqualTo(PayloadValue));
                Assert.That(BurstGeneratedRuntimeBridge.TryCompleteValueRead(ref reader),
                    Is.EqualTo(BurstContextResult.Success));

                var copiedReader = reader;
                var firstCommit = BurstGeneratedRuntimeBridge.TryCommitConsume(ref reader);
                var repeatedCommit = BurstGeneratedRuntimeBridge.TryCommitConsume(ref copiedReader);
                var completion = BurstGeneratedRuntimeBridge.TryCompleteEnter(
                    ref batch, in frame, ref context);
                var failure = completion == BurstContextResult.Success
                    ? BurstContextResult.Success
                    : BurstGeneratedRuntimeBridge.TryFailDispatch(
                        ref batch, in frame, completion);
                Assert.That(scenario.Owner.TryGetCompletion(0u, out var retained), Is.True);

                Assert.That(firstCommit, Is.EqualTo(BurstContextResult.Success));
                Assert.That(repeatedCommit, Is.EqualTo(BurstContextResult.AlreadyCommitted));
                Assert.That(completion, Is.EqualTo(BurstContextResult.AlreadyCommitted));
                Assert.That(failure, Is.EqualTo(BurstContextResult.AlreadyCommitted));
                Assert.That(retained.State, Is.EqualTo(NativeBurstDispatchCompletionStateV2.Available));
            }
        }

        [Test]
        public void FirstBindingFailureIsStableAndCannotBeIgnoredToPublish()
        {
            using (var scenario = Scenario.Create(NativeBurstDispatchBindingKindV2.EffectCommand))
            {
                var batch = scenario.Batch;
                var frame = AcquireFrame(ref batch);
                SealEmptyMemory(in frame);
                var context = CreateEnterContext(in frame);
                var handle = DecodeEffectHandle(in frame);
                Assert.That(context.TryBeginEffect(handle, out var writer),
                    Is.EqualTo(BurstContextResult.Success));

                var firstFailure = BurstGeneratedRuntimeBridge.TryWriteValue(
                    ref writer, 99u, 0u, PayloadValue);
                var laterFailure = context.TryNextUInt32(0u, out var randomValue);
                var ignoredCommit = BurstGeneratedRuntimeBridge.TryCommitEffect(ref writer);
                var completion = BurstGeneratedRuntimeBridge.TryCompleteEnter(
                    ref batch, in frame, ref context);
                var failure = BurstGeneratedRuntimeBridge.TryFailDispatch(
                    ref batch, in frame, completion);
                Assert.That(scenario.Owner.TryGetTransactionSnapshot(out var snapshot), Is.True);

                Assert.That(firstFailure, Is.EqualTo(BurstContextResult.TypeMismatch));
                Assert.That(laterFailure, Is.EqualTo(firstFailure));
                Assert.That(randomValue, Is.Zero);
                Assert.That(ignoredCommit, Is.EqualTo(firstFailure));
                Assert.That(completion, Is.EqualTo(firstFailure));
                Assert.That(failure, Is.EqualTo(firstFailure));
                Assert.That(snapshot.CommandCount, Is.Zero);
                Assert.That(snapshot.SessionCount, Is.Zero);
            }
        }

        [Test]
        public void MismatchedContextCompletionLatchesAndAValidRetryCannotPublish()
        {
            using (var first = Scenario.Create(NativeBurstDispatchBindingKindV2.EffectCommand))
            using (var second = Scenario.Create(NativeBurstDispatchBindingKindV2.EffectCommand))
            {
                var firstBatch = first.Batch;
                var secondBatch = second.Batch;
                var firstFrame = AcquireFrame(ref firstBatch);
                var secondFrame = AcquireFrame(ref secondBatch);
                SealEmptyMemory(in firstFrame);
                SealEmptyMemory(in secondFrame);
                var firstContext = CreateEnterContext(in firstFrame);
                var foreignContext = CreateEnterContext(in secondFrame);

                var mismatch = BurstGeneratedRuntimeBridge.TryCompleteEnter(
                    ref firstBatch, in firstFrame, ref foreignContext);
                var retry = BurstGeneratedRuntimeBridge.TryCompleteEnter(
                    ref firstBatch, in firstFrame, ref firstContext);
                var closeFirst = retry == BurstContextResult.Success
                    ? BurstContextResult.Success
                    : BurstGeneratedRuntimeBridge.TryFailDispatch(
                        ref firstBatch, in firstFrame, mismatch);
                var closeSecond = BurstGeneratedRuntimeBridge.TryFailDispatch(
                    ref secondBatch, in secondFrame, BurstContextResult.InvalidHandle);
                Assert.That(first.Owner.TryGetTransactionSnapshot(out var firstSnapshot), Is.True);

                Assert.That(mismatch, Is.EqualTo(BurstContextResult.InvalidHandle));
                Assert.That(retry, Is.EqualTo(mismatch));
                Assert.That(closeFirst, Is.EqualTo(mismatch));
                Assert.That(closeSecond, Is.EqualTo(BurstContextResult.InvalidHandle));
                Assert.That(firstSnapshot.CommandCount, Is.Zero);
            }
        }

        [TestCase(DecoderMismatch.TypeIdentity)]
        [TestCase(DecoderMismatch.TypeVersion)]
        public void HandleDecoderRejectsWrongTypeOrVersionBeforeBindingAccess(
            DecoderMismatch mismatch)
        {
            using (var scenario = Scenario.Create(NativeBurstDispatchBindingKindV2.EffectCommand))
            {
                var batch = scenario.Batch;
                var frame = AcquireFrame(ref batch);
                SealEmptyMemory(in frame);
                var context = CreateEnterContext(in frame);
                Assert.That(BurstGeneratedRuntimeBridge.TryCreateConfigurationReader(
                    in frame, out var configuration), Is.EqualTo(BurstContextResult.Success));

                var requestedType = mismatch == DecoderMismatch.TypeIdentity
                    ? PayloadTypeNumericId + 1UL
                    : PayloadTypeNumericId;
                var requestedVersion = mismatch == DecoderMismatch.TypeVersion
                    ? PayloadTypeVersion + 1u
                    : PayloadTypeVersion;
                var decode = BurstGeneratedRuntimeBridge.TryReadCommandHandle<int>(
                    ref configuration,
                    0u,
                    requestedType,
                    requestedVersion,
                    out var rejectedHandle);
                var begin = context.TryBeginEffect(rejectedHandle, out _);
                Assert.That(scenario.Owner.TryGetTransactionSnapshot(out var activeSnapshot), Is.True);
                var completion = BurstGeneratedRuntimeBridge.TryCompleteEnter(
                    ref batch, in frame, ref context);
                var failure = BurstGeneratedRuntimeBridge.TryFailDispatch(
                    ref batch, in frame, completion);

                Assert.That(decode, Is.EqualTo(BurstContextResult.TypeMismatch));
                Assert.That(begin, Is.EqualTo(decode));
                Assert.That(activeSnapshot.SessionCount, Is.Zero);
                Assert.That(completion, Is.EqualTo(decode));
                Assert.That(failure, Is.EqualTo(decode));
            }
        }

        [Test]
        public void DisposeAfterSchedulePreparationIsRejectedUntilTheRegisteredLeaseCompletes()
        {
            using (var scenario = Scenario.Create(NativeBurstDispatchBindingKindV2.EffectCommand))
            {
                var scheduledHost = scenario.Batch;
                Assert.That(BurstGeneratedRuntimeBridge.TryPrepareSchedule(
                    ref scheduledHost, out var jobView), Is.EqualTo(BurstContextResult.Success));
                scenario.Batch = scheduledHost;

                var disposedBeforeRegistration = scenario.Owner.TryDispose(out var earlyFailure);
                var hostRead = disposedBeforeRegistration
                    ? BurstContextResult.InvalidHandle
                    : BurstGeneratedRuntimeBridge.TryGetExecutionRequest(
                        in scheduledHost, out _, out _, out _, out _, out _);
                var hasWork = false;
                var jobRead = disposedBeforeRegistration
                    ? BurstContextResult.InvalidHandle
                    : BurstGeneratedRuntimeBridge.TryGetExecutionRequest(
                        in jobView, out _, out _, out _, out _, out hasWork);

                var registered = false;
                var completed = false;
                var disposedAfterCompletion = disposedBeforeRegistration;
                var finalFailure = earlyFailure;
                if (!disposedBeforeRegistration)
                {
                    registered = scenario.Owner.TryRegisterDependency(
                        in scheduledHost, default(JobHandle));
                    completed = scenario.Owner.TryAcquireCompletedBatch(out _);
                    disposedAfterCompletion = scenario.Owner.TryDispose(out finalFailure);
                }

                if (disposedAfterCompletion)
                {
                    scenario.MarkDisposed();
                }

                Assert.That(disposedBeforeRegistration, Is.False);
                Assert.That(earlyFailure, Is.EqualTo(BurstContextResult.PhaseViolation));
                Assert.That(hostRead, Is.EqualTo(BurstContextResult.PhaseViolation));
                Assert.That(jobRead, Is.EqualTo(BurstContextResult.Success));
                Assert.That(hasWork, Is.True);
                Assert.That(registered, Is.True);
                Assert.That(completed, Is.True);
                Assert.That(disposedAfterCompletion, Is.True, finalFailure.ToString());
                Assert.That(finalFailure, Is.EqualTo(BurstContextResult.Success));
            }
        }

        private static BurstDispatchFrame AcquireFrame(ref BurstExecutionBatch batch)
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
            return frame;
        }

        private static void SealEmptyMemory(in BurstDispatchFrame frame)
        {
            Assert.That(BurstGeneratedRuntimeBridge.TryCreateMemoryAccessor(
                in frame, out var memory), Is.EqualTo(BurstContextResult.Success));
            Assert.That(BurstGeneratedRuntimeBridge.TryCommitMemory(ref memory),
                Is.EqualTo(BurstContextResult.Success));
        }

        private static BurstEnterContext CreateEnterContext(in BurstDispatchFrame frame)
        {
            Assert.That(BurstGeneratedRuntimeBridge.TryCreateEnterContext(
                in frame, out var context), Is.EqualTo(BurstContextResult.Success));
            return context;
        }

        private static CommandHandle<int> DecodeEffectHandle(in BurstDispatchFrame frame)
        {
            Assert.That(BurstGeneratedRuntimeBridge.TryCreateConfigurationReader(
                in frame, out var configuration), Is.EqualTo(BurstContextResult.Success));
            Assert.That(BurstGeneratedRuntimeBridge.TryReadCommandHandle<int>(
                ref configuration,
                0u,
                PayloadTypeNumericId,
                PayloadTypeVersion,
                out var handle), Is.EqualTo(BurstContextResult.Success));
            return handle;
        }

        private static CompletionHandle<int> DecodeCompletionHandle(in BurstDispatchFrame frame)
        {
            Assert.That(BurstGeneratedRuntimeBridge.TryCreateConfigurationReader(
                in frame, out var configuration), Is.EqualTo(BurstContextResult.Success));
            Assert.That(BurstGeneratedRuntimeBridge.TryReadCompletionHandle<int>(
                ref configuration,
                0u,
                PayloadTypeNumericId,
                PayloadTypeVersion,
                out var handle), Is.EqualTo(BurstContextResult.Success));
            return handle;
        }

        public enum DecoderMismatch : byte
        {
            TypeIdentity = 0,
            TypeVersion = 1,
        }

        private sealed class Scenario : IDisposable
        {
            internal static readonly OperationId CompletionOperationId = new OperationId(
                new TreeInstanceId(0x23UL),
                new RuntimeNodeIndex(NativeBurstDispatchBindingConcurrencyTests.RuntimeNodeIndex),
                7u,
                11UL);

            private bool _disposed;

            private Scenario(
                NativeBurstDispatchBatchOwnerV2 owner,
                BurstExecutionBatch batch)
            {
                Owner = owner;
                Batch = batch;
            }

            internal NativeBurstDispatchBatchOwnerV2 Owner { get; }
            internal BurstExecutionBatch Batch;

            internal static Scenario Create(NativeBurstDispatchBindingKindV2 kind)
            {
                using (var input = new InputBuffers(kind))
                {
                    var value = input.Value;
                    Assert.That(NativeBurstDispatchBatchOwnerV2.TryCreate(
                        in value,
                        Allocator.Persistent,
                        out var owner,
                        out var failure), Is.True, failure.ToString());
                    Assert.That(owner.TryAcquireImmediateBatch(out var batch), Is.True);
                    return new Scenario(owner, batch);
                }
            }

            internal void MarkDisposed() => _disposed = true;

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

        private sealed class InputBuffers : IDisposable
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
            private readonly NativeArray<NativeBurstDispatchResolvedBindingV2> _resolvedBindings;
            private readonly NativeArray<NativeBurstDispatchFieldV2> _valueFields;
            private readonly NativeArray<byte> _liveValueBytes;
            private readonly NativeArray<NativeBurstDispatchCompletionV2> _completions;
            private readonly NativeArray<byte> _completionPayloadBytes;
            private readonly NativeArray<NativeBurstDispatchCanonicalRangeV2> _caseCanonicalRanges;
            private readonly NativeArray<NativeBurstDispatchCanonicalRangeV2> _bindingCanonicalRanges;
            private readonly NativeArray<NativeBurstDispatchCanonicalRuleV2> _canonicalRules;

            internal InputBuffers(NativeBurstDispatchBindingKindV2 kind)
            {
                var hasCompletion = kind == NativeBurstDispatchBindingKindV2.Completion;
                _cases = new NativeArray<NativeBurstDispatchCaseV2>(
                    1, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                _requests = new NativeArray<NativeBurstDispatchRequestV2>(
                    1, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                _configurationFields = new NativeArray<NativeBurstDispatchFieldV2>(
                    1, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                _memoryFields = new NativeArray<NativeBurstDispatchFieldV2>(
                    0, Allocator.Temp, NativeArrayOptions.ClearMemory);
                _configurationBytes = new NativeArray<byte>(
                    4, Allocator.Temp, NativeArrayOptions.ClearMemory);
                _memoryBytes = new NativeArray<byte>(
                    0, Allocator.Temp, NativeArrayOptions.ClearMemory);
                _randomStates = new NativeArray<ulong>(
                    0, Allocator.Temp, NativeArrayOptions.ClearMemory);
                _randomIncrements = new NativeArray<ulong>(
                    0, Allocator.Temp, NativeArrayOptions.ClearMemory);
                _bindings = new NativeArray<NativeBurstDispatchBindingV2>(
                    1, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                _resolvedBindings = new NativeArray<NativeBurstDispatchResolvedBindingV2>(
                    1, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                _valueFields = new NativeArray<NativeBurstDispatchFieldV2>(
                    1, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                _liveValueBytes = new NativeArray<byte>(
                    0, Allocator.Temp, NativeArrayOptions.ClearMemory);
                _completions = new NativeArray<NativeBurstDispatchCompletionV2>(
                    hasCompletion ? 1 : 0,
                    Allocator.Temp,
                    NativeArrayOptions.UninitializedMemory);
                _completionPayloadBytes = new NativeArray<byte>(
                    hasCompletion ? 4 : 0,
                    Allocator.Temp,
                    NativeArrayOptions.ClearMemory);
                _caseCanonicalRanges = new NativeArray<NativeBurstDispatchCanonicalRangeV2>(
                    2, Allocator.Temp, NativeArrayOptions.ClearMemory);
                _bindingCanonicalRanges = new NativeArray<NativeBurstDispatchCanonicalRangeV2>(
                    2, Allocator.Temp, NativeArrayOptions.ClearMemory);
                _canonicalRules = new NativeArray<NativeBurstDispatchCanonicalRuleV2>(
                    0, Allocator.Temp, NativeArrayOptions.ClearMemory);

                _cases[0] = new NativeBurstDispatchCaseV2(
                    NodeTypeNumericId,
                    NodeTypeVersion,
                    0u,
                    0u,
                    1u,
                    4u,
                    0u,
                    0u,
                    0u,
                    NativeBurstDispatchPhaseMaskV2.Enter,
                    BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure | BurstNodeStatusMask.Running,
                    false,
                    0u,
                    1u);
                _requests[0] = new NativeBurstDispatchRequestV2(
                    InstanceOrdinal,
                    RuntimeNodeIndex,
                    NodeTypeNumericId,
                    NodeTypeVersion,
                    0u,
                    BurstCallbackPhase.Enter,
                    0u,
                    0u,
                    0u,
                    1_234_567L,
                    Scenario.CompletionOperationId.TreeInstanceId,
                    Scenario.CompletionOperationId.ActivationGeneration,
                    0u,
                    1u);
                _configurationFields[0] = new NativeBurstDispatchFieldV2(
                    0u,
                    0u,
                    1u,
                    4u,
                    NativeBurstDispatchFieldEncodingV2.GeneratedHandle);
                _bindings[0] = new NativeBurstDispatchBindingV2(
                    0u,
                    0u,
                    kind,
                    NativeBurstDispatchBindingV2.NoScope,
                    kind == NativeBurstDispatchBindingKindV2.Completion
                        ? NativeBurstDispatchBindingPhaseMaskV2.Completion
                        : NativeBurstDispatchBindingPhaseMaskV2.Execute,
                    PayloadTypeNumericId,
                    PayloadTypeVersion,
                    0u,
                    1u,
                    4u,
                    0UL,
                    0u,
                    0u,
                    0u,
                    0u);
                _resolvedBindings[0] = new NativeBurstDispatchResolvedBindingV2(
                    0u,
                    TargetOrdinal,
                    NativeBurstDispatchBindingV2.NoOffset);
                _valueFields[0] = new NativeBurstDispatchFieldV2(
                    0u,
                    0u,
                    1u,
                    4u,
                    NativeBurstDispatchFieldEncodingV2.Int32);
                if (hasCompletion)
                {
                    _completions[0] = new NativeBurstDispatchCompletionV2(
                        TargetOrdinal,
                        Scenario.CompletionOperationId,
                        BurstCompletionOutcome.Succeeded,
                        0u);
                    WriteInt32(_completionPayloadBytes, 0, PayloadValue);
                }

                var canonical = new NativeBurstDispatchCanonicalInputV2(
                    _caseCanonicalRanges.AsReadOnly(),
                    _bindingCanonicalRanges.AsReadOnly(),
                    _canonicalRules.AsReadOnly());
                var bindings = new NativeBurstDispatchBindingInputV2(
                    _bindings.AsReadOnly(),
                    _resolvedBindings.AsReadOnly(),
                    _valueFields.AsReadOnly(),
                    _liveValueBytes.AsReadOnly(),
                    _completions.AsReadOnly(),
                    _completionPayloadBytes.AsReadOnly(),
                    new NativeBurstDispatchBindingCapacityV2(
                        8u,
                        32u,
                        4u,
                        16u,
                        0u,
                        1UL),
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
                    bindings,
                    canonical);
            }

            internal NativeBurstDispatchCreateInputV2 Value { get; }

            public void Dispose()
            {
                if (_canonicalRules.IsCreated) _canonicalRules.Dispose();
                if (_bindingCanonicalRanges.IsCreated) _bindingCanonicalRanges.Dispose();
                if (_caseCanonicalRanges.IsCreated) _caseCanonicalRanges.Dispose();
                if (_completionPayloadBytes.IsCreated) _completionPayloadBytes.Dispose();
                if (_completions.IsCreated) _completions.Dispose();
                if (_liveValueBytes.IsCreated) _liveValueBytes.Dispose();
                if (_valueFields.IsCreated) _valueFields.Dispose();
                if (_resolvedBindings.IsCreated) _resolvedBindings.Dispose();
                if (_bindings.IsCreated) _bindings.Dispose();
                if (_randomIncrements.IsCreated) _randomIncrements.Dispose();
                if (_randomStates.IsCreated) _randomStates.Dispose();
                if (_memoryBytes.IsCreated) _memoryBytes.Dispose();
                if (_configurationBytes.IsCreated) _configurationBytes.Dispose();
                if (_memoryFields.IsCreated) _memoryFields.Dispose();
                if (_configurationFields.IsCreated) _configurationFields.Dispose();
                if (_requests.IsCreated) _requests.Dispose();
                if (_cases.IsCreated) _cases.Dispose();
            }
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

        private static void WriteInt32(NativeArray<byte> destination, int offset, int value)
        {
            var bits = unchecked((uint)value);
            destination[offset] = (byte)bits;
            destination[offset + 1] = (byte)(bits >> 8);
            destination[offset + 2] = (byte)(bits >> 16);
            destination[offset + 3] = (byte)(bits >> 24);
        }
    }
}
