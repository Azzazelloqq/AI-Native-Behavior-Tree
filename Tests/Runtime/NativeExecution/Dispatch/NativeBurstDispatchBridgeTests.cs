using System;
using AIBT.Burst;
using AIBT.Execution.Burst.Dispatch;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace AIBT.Tests.Runtime.NativeExecution.Dispatch
{
    public sealed class NativeBurstDispatchBridgeTests
    {
        private const ulong TypeNumericId = 0x82d8_6b70_ea8f_4771ul;
        private const uint TypeVersion = 3u;
        private const uint CatalogCaseIndex = 0u;
        private const uint InstanceOrdinal = 4u;
        private const uint RuntimeNodeIndex = 9u;
        private const uint ConfigurationValue = 0xdead_beefu;
        private const int InitialMemory0 = 17;
        private const int InitialMemory1 = -4;
        private const long TimeMicroseconds = 7_654_321L;
        private const ulong InitialRandomState = 0xcd06_63b1_aab3_8607ul;
        private const ulong RandomIncrement = 0x3641_42da_8f45_ed0bul;
        private const uint FirstRandomValue = 0x650f_0350u;
        private const ulong PcgMultiplier = 6_364_136_223_846_793_005ul;

        [Test]
        public void Abi2HandshakeAndRequestExposeTheExactFrozenInputs()
        {
            using (var scenario = Scenario.Create())
            {
                Assert.That(BurstGeneratedRuntimeBridge.TryGetCatalogHandshake(
                    in scenario.Batch, out var handshake), Is.EqualTo(BurstContextResult.Success));
                AssertHandshake(handshake, 2u);

                Assert.That(GetRequest(in scenario.Batch, out var request), Is.EqualTo(BurstContextResult.Success));
                Assert.That(request.HasWork, Is.True);
                Assert.That(request.InstanceOrdinal, Is.EqualTo(InstanceOrdinal));
                Assert.That(request.RuntimeNodeIndex, Is.EqualTo(RuntimeNodeIndex));
                Assert.That(request.CatalogCaseIndex, Is.EqualTo(CatalogCaseIndex));
                Assert.That(request.Phase, Is.EqualTo(BurstCallbackPhase.Enter));

                var batch = scenario.Batch;
                Assert.That(BurstGeneratedRuntimeBridge.TryAcquireDispatchFrame(
                    ref batch,
                    request.InstanceOrdinal,
                    request.RuntimeNodeIndex,
                    request.CatalogCaseIndex,
                    request.Phase,
                    out var frame), Is.EqualTo(BurstContextResult.Success));
                SealUnchangedMemory(in frame);
                Assert.That(BurstGeneratedRuntimeBridge.TryCreateEnterContext(
                    in frame, out var context), Is.EqualTo(BurstContextResult.Success));
                Assert.That(BurstGeneratedRuntimeBridge.TryCompleteEnter(
                    ref batch, in frame, ref context), Is.EqualTo(BurstContextResult.Success));

                AssertTerminalSuccess(in scenario.Batch);
            }
        }

        [Test]
        public void ConfigurationAndMemoryAreFieldwiseAndPublishOnlyWithLifecycleCompletion()
        {
            using (var scenario = Scenario.Create())
            {
                var batch = scenario.Batch;
                var frame = AcquireExactFrame(ref batch);

                Assert.That(BurstGeneratedRuntimeBridge.TryCreateConfigurationReader(
                    in frame, out var configuration), Is.EqualTo(BurstContextResult.Success));
                Assert.That(BurstGeneratedRuntimeBridge.TryReadUInt32(
                    ref configuration, 0u, 0u, out var configurationValue), Is.EqualTo(BurstContextResult.Success));
                Assert.That(configurationValue, Is.EqualTo(ConfigurationValue));

                Assert.That(BurstGeneratedRuntimeBridge.TryCreateMemoryAccessor(
                    in frame, out var memory), Is.EqualTo(BurstContextResult.Success));
                Assert.That(BurstGeneratedRuntimeBridge.TryReadMemoryInt32(
                    ref memory, 0u, 0u, out var first), Is.EqualTo(BurstContextResult.Success));
                Assert.That(BurstGeneratedRuntimeBridge.TryReadMemoryInt32(
                    ref memory, 1u, 0u, out var second), Is.EqualTo(BurstContextResult.Success));
                Assert.That(first, Is.EqualTo(InitialMemory0));
                Assert.That(second, Is.EqualTo(InitialMemory1));

                Assert.That(BurstGeneratedRuntimeBridge.TryWriteMemoryInt32(
                    ref memory, 0u, 0u, 41), Is.EqualTo(BurstContextResult.Success));
                Assert.That(BurstGeneratedRuntimeBridge.TryWriteMemoryInt32(
                    ref memory, 1u, 0u, 99), Is.EqualTo(BurstContextResult.Success));
                Assert.That(BurstGeneratedRuntimeBridge.TryCommitMemory(
                    ref memory), Is.EqualTo(BurstContextResult.Success));

                AssertMemory(scenario.Owner, InitialMemory0, InitialMemory1);

                Assert.That(BurstGeneratedRuntimeBridge.TryCreateEnterContext(
                    in frame, out var context), Is.EqualTo(BurstContextResult.Success));
                Assert.That(BurstGeneratedRuntimeBridge.TryCompleteEnter(
                    ref batch, in frame, ref context), Is.EqualTo(BurstContextResult.Success));

                AssertMemory(scenario.Owner, 41, 99);
                AssertTerminalSuccess(in scenario.Batch);
            }
        }

        [TestCase(4u, true)]
        [TestCase(8u, false)]
        public void GeneratedHandleConfigurationDescriptorsRequireExactlyFourBytes(
            uint elementSize,
            bool expectedToCreate)
        {
            using (var input = new GeneratedHandleInputBuffers(elementSize))
            {
                var createInput = input.Value;
                var created = NativeBurstDispatchBatchOwnerV2.TryCreate(
                    in createInput,
                    Allocator.Persistent,
                    out var owner,
                    out var failure);
                Assert.That(created, Is.EqualTo(expectedToCreate));
                if (!expectedToCreate)
                {
                    Assert.That(owner, Is.Null);
                    Assert.That(failure, Is.EqualTo(BurstContextResult.InvalidEncoding));
                    return;
                }

                Assert.That(failure, Is.EqualTo(BurstContextResult.Success));
                Assert.That(owner.TryDispose(out failure), Is.True, failure.ToString());
            }
        }

        [Test]
        public void EnterTimeAndPcgPublishExactlyOnceWithTheMatchingCompletion()
        {
            using (var scenario = Scenario.Create())
            {
                var batch = scenario.Batch;
                var frame = AcquireExactFrame(ref batch);
                SealUnchangedMemory(in frame);
                Assert.That(BurstGeneratedRuntimeBridge.TryCreateEnterContext(
                    in frame, out var context), Is.EqualTo(BurstContextResult.Success));
                var copiedContext = context;
                var copiedFrame = frame;

                Assert.That(context.TryGetTimeMicroseconds(out var time), Is.EqualTo(BurstContextResult.Success));
                Assert.That(time, Is.EqualTo(TimeMicroseconds));
                Assert.That(context.TryNextUInt32(out var random), Is.EqualTo(BurstContextResult.Success));
                Assert.That(random, Is.EqualTo(FirstRandomValue));
                AssertRandomState(scenario.Owner, InitialRandomState);

                Assert.That(BurstGeneratedRuntimeBridge.TryCompleteEnter(
                    ref batch, in frame, ref context), Is.EqualTo(BurstContextResult.Success));
                var advancedState = unchecked(InitialRandomState * PcgMultiplier + RandomIncrement);
                AssertRandomState(scenario.Owner, advancedState);

                Assert.That(BurstGeneratedRuntimeBridge.TryCompleteEnter(
                    ref batch, in copiedFrame, ref copiedContext), Is.EqualTo(BurstContextResult.StaleCompletion));
                Assert.That(copiedContext.TryGetTimeMicroseconds(out time), Is.EqualTo(BurstContextResult.InvalidHandle));
                Assert.That(time, Is.Zero);
                Assert.That(BurstGeneratedRuntimeBridge.TryCreateConfigurationReader(
                    in copiedFrame, out _), Is.EqualTo(BurstContextResult.InvalidHandle));
                AssertRandomState(scenario.Owner, advancedState);
                AssertTerminalSuccess(in scenario.Batch);
            }
        }

        [Test]
        public void NonRandomEnterCompletesWithItsCanonicalInertContextWithoutPublishingRandomState()
        {
            using (var scenario = Scenario.Create(hasRandomStream: false))
            {
                var batch = scenario.Batch;
                var frame = AcquireExactFrame(ref batch);
                SealUnchangedMemory(in frame);
                Assert.That(BurstGeneratedRuntimeBridge.TryCreateEnterContext(
                    in frame, out var context), Is.EqualTo(BurstContextResult.Success));
                Assert.That(BurstGeneratedRuntimeBridge.TryCompleteEnter(
                    ref batch, in frame, ref context), Is.EqualTo(BurstContextResult.Success));

                AssertRandomState(scenario.Owner, InitialRandomState);
                AssertTerminalSuccess(in scenario.Batch);
            }
        }

        [Test]
        public void OldCopiedFrameRemainsStaleAfterTheFollowingRequestCompletes()
        {
            using (var scenario = Scenario.Create(requestCount: 2))
            {
                var batch = scenario.Batch;
                var firstFrame = AcquireExactFrame(ref batch);
                SealUnchangedMemory(in firstFrame);
                Assert.That(BurstGeneratedRuntimeBridge.TryCreateEnterContext(
                    in firstFrame, out var firstContext), Is.EqualTo(BurstContextResult.Success));
                var copiedFirstFrame = firstFrame;
                var copiedFirstContext = firstContext;
                Assert.That(BurstGeneratedRuntimeBridge.TryCompleteEnter(
                    ref batch, in firstFrame, ref firstContext), Is.EqualTo(BurstContextResult.Success));

                var secondFrame = AcquireExactFrame(ref batch);
                SealUnchangedMemory(in secondFrame);
                Assert.That(BurstGeneratedRuntimeBridge.TryCreateEnterContext(
                    in secondFrame, out var secondContext), Is.EqualTo(BurstContextResult.Success));
                Assert.That(BurstGeneratedRuntimeBridge.TryCompleteEnter(
                    ref batch, in secondFrame, ref secondContext), Is.EqualTo(BurstContextResult.Success));

                Assert.That(BurstGeneratedRuntimeBridge.TryCompleteEnter(
                    ref batch, in copiedFirstFrame, ref copiedFirstContext),
                    Is.EqualTo(BurstContextResult.StaleCompletion));
                AssertMemory(scenario.Owner, InitialMemory0, InitialMemory1);
                AssertRandomState(scenario.Owner, InitialRandomState);
                AssertTerminalSuccess(in scenario.Batch, 2u, 2ul);
            }
        }

        [Test]
        public void IgnoredFirstBridgeFailureBlocksPublicationUntilTheFrameIsFaulted()
        {
            using (var scenario = Scenario.Create())
            {
                var batch = scenario.Batch;
                var frame = AcquireExactFrame(ref batch);

                Assert.That(BurstGeneratedRuntimeBridge.TryCreateMemoryAccessor(
                    in frame, out var memory), Is.EqualTo(BurstContextResult.Success));
                Assert.That(BurstGeneratedRuntimeBridge.TryWriteMemoryInt32(
                    ref memory, 0u, 0u, 41), Is.EqualTo(BurstContextResult.Success));
                Assert.That(BurstGeneratedRuntimeBridge.TryWriteMemoryInt32(
                    ref memory, 1u, 0u, 99), Is.EqualTo(BurstContextResult.Success));
                Assert.That(BurstGeneratedRuntimeBridge.TryCommitMemory(
                    ref memory), Is.EqualTo(BurstContextResult.Success));

                Assert.That(BurstGeneratedRuntimeBridge.TryCreateEnterContext(
                    in frame, out var context), Is.EqualTo(BurstContextResult.Success));
                Assert.That(context.TryNextUInt32(out var random), Is.EqualTo(BurstContextResult.Success));
                Assert.That(random, Is.EqualTo(FirstRandomValue));

                Assert.That(BurstGeneratedRuntimeBridge.TryCreateConfigurationReader(
                    in frame, out var configuration), Is.EqualTo(BurstContextResult.Success));
                var firstFailure = BurstGeneratedRuntimeBridge.TryReadUInt32(
                    ref configuration, 1u, 0u, out var missingValue);
                Assert.That(firstFailure, Is.EqualTo(BurstContextResult.TypeMismatch));
                Assert.That(missingValue, Is.Zero);

                Assert.That(context.TryNextUInt32(0u, out var rejectedRandom),
                    Is.EqualTo(firstFailure));
                Assert.That(rejectedRandom, Is.Zero);

                var completionFailure = BurstGeneratedRuntimeBridge.TryCompleteEnter(
                    ref batch, in frame, ref context);
                Assert.That(completionFailure, Is.EqualTo(firstFailure));
                AssertMemory(scenario.Owner, InitialMemory0, InitialMemory1);
                AssertRandomState(scenario.Owner, InitialRandomState);

                Assert.That(BurstGeneratedRuntimeBridge.TryFailDispatch(
                    ref batch, in frame, completionFailure), Is.EqualTo(firstFailure));
                AssertMemory(scenario.Owner, InitialMemory0, InitialMemory1);
                AssertRandomState(scenario.Owner, InitialRandomState);
                AssertTerminalFault(in scenario.Batch);
            }
        }

        [Test]
        public void IncompleteMemoryIsRolledBackWhenTheFrameFaults()
        {
            using (var scenario = Scenario.Create())
            {
                var batch = scenario.Batch;
                var frame = AcquireExactFrame(ref batch);
                Assert.That(BurstGeneratedRuntimeBridge.TryCreateMemoryAccessor(
                    in frame, out var memory), Is.EqualTo(BurstContextResult.Success));
                Assert.That(BurstGeneratedRuntimeBridge.TryWriteMemoryInt32(
                    ref memory, 0u, 0u, 1_234), Is.EqualTo(BurstContextResult.Success));
                Assert.That(BurstGeneratedRuntimeBridge.TryCommitMemory(
                    ref memory), Is.EqualTo(BurstContextResult.IncompleteValue));

                Assert.That(BurstGeneratedRuntimeBridge.TryFailDispatch(
                    ref batch, in frame, BurstContextResult.IncompleteValue), Is.EqualTo(BurstContextResult.IncompleteValue));
                AssertMemory(scenario.Owner, InitialMemory0, InitialMemory1);
                AssertRandomState(scenario.Owner, InitialRandomState);
                AssertTerminalFault(in scenario.Batch);
            }
        }

        [Test]
        public void UndefinedFailureCodeCannotCloseOrPublishTheLiveFrame()
        {
            using (var scenario = Scenario.Create())
            {
                var batch = scenario.Batch;
                var frame = AcquireExactFrame(ref batch);
                Assert.That(BurstGeneratedRuntimeBridge.TryCreateMemoryAccessor(
                    in frame, out var memory), Is.EqualTo(BurstContextResult.Success));
                Assert.That(BurstGeneratedRuntimeBridge.TryWriteMemoryInt32(
                    ref memory, 0u, 0u, 41), Is.EqualTo(BurstContextResult.Success));
                Assert.That(BurstGeneratedRuntimeBridge.TryWriteMemoryInt32(
                    ref memory, 1u, 0u, 99), Is.EqualTo(BurstContextResult.Success));
                Assert.That(BurstGeneratedRuntimeBridge.TryCommitMemory(
                    ref memory), Is.EqualTo(BurstContextResult.Success));

                Assert.That(BurstGeneratedRuntimeBridge.TryFailDispatch(
                    ref batch, in frame, (BurstContextResult)byte.MaxValue),
                    Is.EqualTo(BurstContextResult.InvalidStatus));
                AssertMemory(scenario.Owner, InitialMemory0, InitialMemory1);
                AssertRandomState(scenario.Owner, InitialRandomState);

                Assert.That(BurstGeneratedRuntimeBridge.TryFailDispatch(
                    ref batch, in frame, BurstContextResult.InvalidStatus),
                    Is.EqualTo(BurstContextResult.InvalidStatus));
                AssertMemory(scenario.Owner, InitialMemory0, InitialMemory1);
                AssertRandomState(scenario.Owner, InitialRandomState);
                AssertTerminalFault(in scenario.Batch);
            }
        }

        [Test]
        public void ConfigurationBoundsFailureFaultsWithoutPublishingMemoryOrRandomState()
        {
            using (var scenario = Scenario.Create())
            {
                var batch = scenario.Batch;
                var frame = AcquireExactFrame(ref batch);
                Assert.That(BurstGeneratedRuntimeBridge.TryCreateConfigurationReader(
                    in frame, out var configuration), Is.EqualTo(BurstContextResult.Success));
                var boundsFailure = BurstGeneratedRuntimeBridge.TryReadUInt32(
                    ref configuration, 1u, 0u, out var value);
                Assert.That(boundsFailure, Is.Not.EqualTo(BurstContextResult.Success));
                Assert.That(value, Is.Zero);
                Assert.That(BurstGeneratedRuntimeBridge.TryFailDispatch(
                    ref batch, in frame, boundsFailure), Is.EqualTo(boundsFailure));

                AssertMemory(scenario.Owner, InitialMemory0, InitialMemory1);
                AssertRandomState(scenario.Owner, InitialRandomState);
                AssertTerminalFault(in scenario.Batch);
            }
        }

        [TestCase(RequestMismatch.TypeIdentity)]
        [TestCase(RequestMismatch.TypeVersion)]
        public void RequestCaseMismatchFaultsBeforeAnyTransactionMutation(RequestMismatch mismatch)
        {
            using (var scenario = Scenario.Create(mismatch: mismatch))
            {
                Assert.That(GetRequest(in scenario.Batch, out var request), Is.EqualTo(BurstContextResult.Success));
                Assert.That(request.HasWork, Is.True);
                var batch = scenario.Batch;
                Assert.That(BurstGeneratedRuntimeBridge.TryAcquireDispatchFrame(
                    ref batch,
                    request.InstanceOrdinal,
                    request.RuntimeNodeIndex,
                    request.CatalogCaseIndex,
                    request.Phase,
                    out _), Is.Not.EqualTo(BurstContextResult.Success));

                AssertMemory(scenario.Owner, InitialMemory0, InitialMemory1);
                AssertRandomState(scenario.Owner, InitialRandomState);
                AssertTerminalFault(in scenario.Batch);
            }
        }

        [TestCase(FrameMismatch.Instance)]
        [TestCase(FrameMismatch.Node)]
        [TestCase(FrameMismatch.CatalogCase)]
        [TestCase(FrameMismatch.Phase)]
        public void WrongFrameCoordinatesFaultBeforeAnyTransactionMutation(FrameMismatch mismatch)
        {
            using (var scenario = Scenario.Create())
            {
                Assert.That(GetRequest(in scenario.Batch, out var request), Is.EqualTo(BurstContextResult.Success));
                var instanceOrdinal = mismatch == FrameMismatch.Instance ? request.InstanceOrdinal + 1u : request.InstanceOrdinal;
                var runtimeNodeIndex = mismatch == FrameMismatch.Node ? request.RuntimeNodeIndex + 1u : request.RuntimeNodeIndex;
                var caseIndex = mismatch == FrameMismatch.CatalogCase ? request.CatalogCaseIndex + 1u : request.CatalogCaseIndex;
                var phase = mismatch == FrameMismatch.Phase ? BurstCallbackPhase.Tick : request.Phase;
                var batch = scenario.Batch;

                Assert.That(BurstGeneratedRuntimeBridge.TryAcquireDispatchFrame(
                    ref batch, instanceOrdinal, runtimeNodeIndex, caseIndex, phase, out _),
                    Is.Not.EqualTo(BurstContextResult.Success));
                AssertMemory(scenario.Owner, InitialMemory0, InitialMemory1);
                AssertRandomState(scenario.Owner, InitialRandomState);
                AssertTerminalFault(in scenario.Batch);
            }
        }

        [Test]
        public void WrongContextPhaseFaultsWithoutPublishingTheLiveFrame()
        {
            using (var scenario = Scenario.Create())
            {
                var batch = scenario.Batch;
                var frame = AcquireExactFrame(ref batch);
                Assert.That(BurstGeneratedRuntimeBridge.TryCreateTickContext(
                    in frame, out _), Is.EqualTo(BurstContextResult.PhaseViolation));
                Assert.That(BurstGeneratedRuntimeBridge.TryFailDispatch(
                    ref batch, in frame, BurstContextResult.PhaseViolation), Is.EqualTo(BurstContextResult.PhaseViolation));
                AssertTerminalFault(in scenario.Batch);
                AssertMemory(scenario.Owner, InitialMemory0, InitialMemory1);
                AssertRandomState(scenario.Owner, InitialRandomState);
            }
        }

        [Test]
        public void ReentryFaultsAndRollsBackTheActiveTransaction()
        {
            using (var scenario = Scenario.Create())
            {
                var batch = scenario.Batch;
                var frame = AcquireExactFrame(ref batch);
                Assert.That(BurstGeneratedRuntimeBridge.TryCreateMemoryAccessor(
                    in frame, out var memory), Is.EqualTo(BurstContextResult.Success));
                Assert.That(BurstGeneratedRuntimeBridge.TryWriteMemoryInt32(
                    ref memory, 0u, 0u, 555), Is.EqualTo(BurstContextResult.Success));
                Assert.That(BurstGeneratedRuntimeBridge.TryWriteMemoryInt32(
                    ref memory, 1u, 0u, 777), Is.EqualTo(BurstContextResult.Success));
                Assert.That(BurstGeneratedRuntimeBridge.TryCommitMemory(
                    ref memory), Is.EqualTo(BurstContextResult.Success));
                Assert.That(BurstGeneratedRuntimeBridge.TryCreateEnterContext(
                    in frame, out var context), Is.EqualTo(BurstContextResult.Success));
                Assert.That(context.TryNextUInt32(out _), Is.EqualTo(BurstContextResult.Success));

                var alias = batch;
                Assert.That(BurstGeneratedRuntimeBridge.TryAcquireDispatchFrame(
                    ref alias,
                    InstanceOrdinal,
                    RuntimeNodeIndex,
                    CatalogCaseIndex,
                    BurstCallbackPhase.Enter,
                    out _), Is.EqualTo(BurstContextResult.PhaseViolation));
                Assert.That(BurstGeneratedRuntimeBridge.TryCompleteEnter(
                    ref batch, in frame, ref context), Is.EqualTo(BurstContextResult.InvalidHandle));

                AssertTerminalFault(in scenario.Batch);
                AssertMemory(scenario.Owner, InitialMemory0, InitialMemory1);
                AssertRandomState(scenario.Owner, InitialRandomState);
            }
        }

        [Test]
        public void ConcurrentCopiedBatchesAndRepeatedAcquireAllowOnlyOneFrameClaim()
        {
            using (var scenario = Scenario.Create())
            using (var results = new NativeArray<int>(
                2, Allocator.TempJob, NativeArrayOptions.UninitializedMemory))
            {
                Assert.That(GetRequest(in scenario.Batch, out var request),
                    Is.EqualTo(BurstContextResult.Success));
                Assert.That(request.HasWork, Is.True);

                new ConcurrentFrameAcquireJob
                {
                    Batch = scenario.Batch,
                    Results = results,
                }.Schedule(results.Length, 1).Complete();

                var successes = 0;
                var phaseViolations = 0;
                for (var index = 0; index < results.Length; index++)
                {
                    var result = (BurstContextResult)results[index];
                    if (result == BurstContextResult.Success) successes++;
                    if (result == BurstContextResult.PhaseViolation) phaseViolations++;
                }

                Assert.That(successes, Is.EqualTo(1));
                Assert.That(phaseViolations, Is.EqualTo(1));

                var batch = scenario.Batch;
                Assert.That(BurstGeneratedRuntimeBridge.TryAcquireDispatchFrame(
                    ref batch,
                    request.InstanceOrdinal,
                    request.RuntimeNodeIndex,
                    request.CatalogCaseIndex,
                    request.Phase,
                    out _), Is.EqualTo(BurstContextResult.PhaseViolation));
                AssertMemory(scenario.Owner, InitialMemory0, InitialMemory1);
                AssertRandomState(scenario.Owner, InitialRandomState);
                AssertTerminalFault(in scenario.Batch);
            }
        }

        [Test]
        public void DefaultCarriersFailClosedAndResetEveryOutput()
        {
            var batch = default(BurstExecutionBatch);
            Assert.That(BurstGeneratedRuntimeBridge.TryGetCatalogHandshake(
                in batch, out var handshake), Is.EqualTo(BurstContextResult.InvalidHandle));
            Assert.That(handshake.AbiVersion, Is.Zero);

            Assert.That(BurstGeneratedRuntimeBridge.TryGetExecutionRequest(
                in batch,
                out var instanceOrdinal,
                out var runtimeNodeIndex,
                out var catalogCaseIndex,
                out var phase,
                out var hasWork), Is.EqualTo(BurstContextResult.InvalidHandle));
            Assert.That(instanceOrdinal, Is.Zero);
            Assert.That(runtimeNodeIndex, Is.Zero);
            Assert.That(catalogCaseIndex, Is.Zero);
            Assert.That(phase, Is.EqualTo(default(BurstCallbackPhase)));
            Assert.That(hasWork, Is.False);

            var context = default(BurstEnterContext);
            Assert.That(context.TryGetTimeMicroseconds(out var time), Is.EqualTo(BurstContextResult.InvalidHandle));
            Assert.That(time, Is.Zero);
            Assert.That(context.TryNextUInt32(0u, out var random), Is.EqualTo(BurstContextResult.InvalidHandle));
            Assert.That(random, Is.Zero);
        }

        [Test]
        public void EmptyBatchIsImmediatelySuccessfulAndExposesNoRequest()
        {
            using (var scenario = Scenario.Create(requestCount: 0))
            {
                Assert.That(GetRequest(in scenario.Batch, out var request),
                    Is.EqualTo(BurstContextResult.Success));
                Assert.That(request.HasWork, Is.False);
                Assert.That(request.InstanceOrdinal, Is.Zero);
                Assert.That(request.RuntimeNodeIndex, Is.Zero);
                Assert.That(request.CatalogCaseIndex, Is.Zero);
                Assert.That(request.Phase, Is.EqualTo(default(BurstCallbackPhase)));
                AssertTerminalSuccess(in scenario.Batch, 0u, 0ul);
            }
        }

        [Test]
        public void Abi1HandshakeIsRejectedWith5012BeforeWorkOrMutation()
        {
            using (var scenario = Scenario.Create(abiVersion: 1u))
            {
                Assert.That(BurstGeneratedRuntimeBridge.TryGetCatalogHandshake(
                    in scenario.Batch, out var handshake), Is.EqualTo(BurstContextResult.Success));
                AssertHandshake(handshake, 1u);

                var batch = scenario.Batch;
                var rejection = new BurstCatalogValidationResult(
                    BurstCatalogValidationCode.AbiVersionMismatch, 5012);
                Assert.That(BurstGeneratedRuntimeBridge.TryRejectBatch(
                    ref batch, in rejection), Is.EqualTo(BurstContextResult.Success));
                Assert.That(BurstGeneratedRuntimeBridge.TryGetExecutionResult(
                    in scenario.Batch, out var result), Is.EqualTo(BurstContextResult.Success));
                Assert.That(result.Code, Is.EqualTo(BurstExecutionCode.ValidationFailed));
                Assert.That(result.DiagnosticNumber, Is.EqualTo(5012));
                Assert.That(result.InstancesVisited, Is.Zero);
                Assert.That(result.SegmentSteps, Is.Zero);

                Assert.That(GetRequest(in scenario.Batch, out var request), Is.EqualTo(BurstContextResult.PhaseViolation));
                Assert.That(request.HasWork, Is.False);
                Assert.That(request.InstanceOrdinal, Is.Zero);
                Assert.That(request.RuntimeNodeIndex, Is.Zero);
                Assert.That(request.CatalogCaseIndex, Is.Zero);
                Assert.That(request.Phase, Is.EqualTo(default(BurstCallbackPhase)));
                AssertMemory(scenario.Owner, InitialMemory0, InitialMemory1);
                AssertRandomState(scenario.Owner, InitialRandomState);
            }
        }

        [Test]
        public void ScheduledJobUsesTheClaimedCopyAfterItsDependencyAndHostAccessFailsBeforeSafetyLockedStorage()
        {
            using (var scenario = Scenario.Create())
            using (var probe = new NativeArray<int>(2, Allocator.TempJob, NativeArrayOptions.ClearMemory))
            {
                var staleHostCopy = scenario.Batch;
                var hostBatch = staleHostCopy;
                Assert.That(BurstGeneratedRuntimeBridge.TryPrepareSchedule(
                    ref hostBatch, out var scheduledBatch), Is.EqualTo(BurstContextResult.Success));
                Assert.That(BurstGeneratedRuntimeBridge.TryPrepareSchedule(
                    ref staleHostCopy, out _), Is.EqualTo(BurstContextResult.PhaseViolation));
                Assert.That(scenario.Owner.TryAcquireImmediateBatch(out _), Is.False);
                Assert.That(scenario.Owner.TryRegisterDependency(
                    in staleHostCopy, default), Is.False);
                Assert.That(BurstGeneratedRuntimeBridge.TryGetExecutionRequest(
                    in staleHostCopy, out _, out _, out _, out _, out _),
                    Is.EqualTo(BurstContextResult.PhaseViolation));

                var dependency = new DependencyMarkerJob { Probe = probe }.Schedule();
                var dispatch = new ScheduledEnterBridgeJob
                {
                    Batch = scheduledBatch,
                    Probe = probe,
                }.Schedule(dependency);
                Assert.That(scenario.Owner.TryRegisterDependency(
                    in hostBatch, dispatch), Is.True);
                Assert.That(scenario.Owner.TryRegisterDependency(
                    in hostBatch, dispatch), Is.False);

                BurstContextResult hostAccess = BurstContextResult.Success;
                Assert.DoesNotThrow(() =>
                {
                    hostAccess = BurstGeneratedRuntimeBridge.TryGetExecutionRequest(
                        in hostBatch, out _, out _, out _, out _, out _);
                });
                Assert.That(hostAccess, Is.EqualTo(BurstContextResult.PhaseViolation));

                dispatch.Complete();
                Assert.That(probe[0], Is.EqualTo(1), "dependency did not run");
                Assert.That(probe[1], Is.EqualTo(1), "scheduled bridge stage failed");
                Assert.That(scenario.Owner.TryAcquireCompletedBatch(
                    out var completedBatch), Is.True);
                AssertMemory(scenario.Owner, 42, 100);
                AssertTerminalSuccess(in completedBatch);
                AssertTerminalSuccess(in scenario.Batch);
                Assert.That(scenario.Owner.TryAcquireImmediateBatch(out _), Is.False);
                Assert.That(BurstGeneratedRuntimeBridge.TryGetExecutionRequest(
                    in scheduledBatch, out _, out _, out _, out _, out _), Is.EqualTo(BurstContextResult.InvalidHandle));
            }
        }

        private static BurstDispatchFrame AcquireExactFrame(ref BurstExecutionBatch batch)
        {
            Assert.That(GetRequest(in batch, out var request), Is.EqualTo(BurstContextResult.Success));
            Assert.That(request.HasWork, Is.True);
            Assert.That(BurstGeneratedRuntimeBridge.TryAcquireDispatchFrame(
                ref batch,
                request.InstanceOrdinal,
                request.RuntimeNodeIndex,
                request.CatalogCaseIndex,
                request.Phase,
                out var frame), Is.EqualTo(BurstContextResult.Success));
            return frame;
        }

        private static void SealUnchangedMemory(in BurstDispatchFrame frame)
        {
            Assert.That(BurstGeneratedRuntimeBridge.TryCreateMemoryAccessor(
                in frame, out var memory), Is.EqualTo(BurstContextResult.Success));
            Assert.That(BurstGeneratedRuntimeBridge.TryReadMemoryInt32(
                ref memory, 0u, 0u, out var first), Is.EqualTo(BurstContextResult.Success));
            Assert.That(BurstGeneratedRuntimeBridge.TryReadMemoryInt32(
                ref memory, 1u, 0u, out var second), Is.EqualTo(BurstContextResult.Success));
            Assert.That(BurstGeneratedRuntimeBridge.TryWriteMemoryInt32(
                ref memory, 0u, 0u, first), Is.EqualTo(BurstContextResult.Success));
            Assert.That(BurstGeneratedRuntimeBridge.TryWriteMemoryInt32(
                ref memory, 1u, 0u, second), Is.EqualTo(BurstContextResult.Success));
            Assert.That(BurstGeneratedRuntimeBridge.TryCommitMemory(
                ref memory), Is.EqualTo(BurstContextResult.Success));
        }

        private static BurstContextResult GetRequest(
            in BurstExecutionBatch batch,
            out RequestObservation request)
        {
            var result = BurstGeneratedRuntimeBridge.TryGetExecutionRequest(
                in batch,
                out var instanceOrdinal,
                out var runtimeNodeIndex,
                out var catalogCaseIndex,
                out var phase,
                out var hasWork);
            request = new RequestObservation(
                instanceOrdinal, runtimeNodeIndex, catalogCaseIndex, phase, hasWork);
            return result;
        }

        private static void AssertHandshake(BurstCatalogHandshake actual, uint abiVersion)
        {
            Assert.That(actual.AbiVersion, Is.EqualTo(abiVersion));
            Assert.That(actual.Catalog.Value.Word0, Is.EqualTo(0x10u));
            Assert.That(actual.NodeRegistry.Word0, Is.EqualTo(0x20u));
            Assert.That(actual.CompiledFormatVersion, Is.EqualTo(1u));
            Assert.That(actual.ExecutionSemanticsVersion, Is.EqualTo(1u));
            Assert.That(actual.ConfigurationLayout.Word0, Is.EqualTo(0x30u));
            Assert.That(actual.MemoryLayout.Word0, Is.EqualTo(0x40u));
            Assert.That(actual.AccessLayout.Word0, Is.EqualTo(0x50u));
        }

        private static void AssertMemory(
            NativeBurstDispatchBatchOwnerV2 owner,
            int expectedFirst,
            int expectedSecond)
        {
            Assert.That(owner.TryReadMemoryInt32(
                0u, 0u, out var first), Is.True);
            Assert.That(owner.TryReadMemoryInt32(
                0u, 1u, out var second), Is.True);
            Assert.That(first, Is.EqualTo(expectedFirst));
            Assert.That(second, Is.EqualTo(expectedSecond));
        }

        private static void AssertRandomState(NativeBurstDispatchBatchOwnerV2 owner, ulong expected)
        {
            Assert.That(owner.TryReadCommittedRandomState(
                0u, out var actual), Is.True);
            Assert.That(actual, Is.EqualTo(expected));
        }

        private static void AssertTerminalSuccess(
            in BurstExecutionBatch batch,
            uint expectedInstancesVisited = 1u,
            ulong expectedSegmentSteps = 1ul)
        {
            Assert.That(BurstGeneratedRuntimeBridge.TryGetExecutionResult(
                in batch, out var result), Is.EqualTo(BurstContextResult.Success));
            Assert.That(result.Code, Is.EqualTo(BurstExecutionCode.Success));
            Assert.That(result.DiagnosticNumber, Is.Zero);
            Assert.That(result.InstancesVisited, Is.EqualTo(expectedInstancesVisited));
            Assert.That(result.SegmentSteps, Is.EqualTo(expectedSegmentSteps));
        }

        private static void AssertTerminalFault(in BurstExecutionBatch batch)
        {
            Assert.That(BurstGeneratedRuntimeBridge.TryGetExecutionResult(
                in batch, out var result), Is.EqualTo(BurstContextResult.Success));
            Assert.That(result.Code, Is.EqualTo(BurstExecutionCode.Faulted));
            Assert.That(result.InstancesVisited, Is.Zero);
            Assert.That(result.SegmentSteps, Is.Zero);
        }

        private static BurstCatalogHandshake Handshake(uint abiVersion)
        {
            return new BurstCatalogHandshake(
                abiVersion,
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

        public enum RequestMismatch : byte
        {
            None = 0,
            TypeIdentity = 1,
            TypeVersion = 2,
        }

        public enum FrameMismatch : byte
        {
            Instance = 0,
            Node = 1,
            CatalogCase = 2,
            Phase = 3,
        }

        private readonly struct RequestObservation
        {
            internal RequestObservation(
                uint instanceOrdinal,
                uint runtimeNodeIndex,
                uint catalogCaseIndex,
                BurstCallbackPhase phase,
                bool hasWork)
            {
                InstanceOrdinal = instanceOrdinal;
                RuntimeNodeIndex = runtimeNodeIndex;
                CatalogCaseIndex = catalogCaseIndex;
                Phase = phase;
                HasWork = hasWork;
            }

            internal uint InstanceOrdinal { get; }
            internal uint RuntimeNodeIndex { get; }
            internal uint CatalogCaseIndex { get; }
            internal BurstCallbackPhase Phase { get; }
            internal bool HasWork { get; }
        }

        internal sealed class Scenario : IDisposable
        {
            private bool _disposed;

            private Scenario(NativeBurstDispatchBatchOwnerV2 owner, BurstExecutionBatch batch)
            {
                Owner = owner;
                Batch = batch;
            }

            internal NativeBurstDispatchBatchOwnerV2 Owner { get; }
            internal BurstExecutionBatch Batch;

            internal static Scenario Create(
                uint abiVersion = 2u,
                RequestMismatch mismatch = RequestMismatch.None,
                int requestCount = 1,
                bool hasRandomStream = true)
            {
                using (var input = new InputBuffers(
                           abiVersion,
                           mismatch,
                           requestCount,
                           hasRandomStream))
                {
                    var createInput = input.Value;
                    Assert.That(NativeBurstDispatchBatchOwnerV2.TryCreate(
                        in createInput,
                        Allocator.Persistent,
                        out var owner,
                        out var failure), Is.True, failure.ToString());
                    Assert.That(owner.TryAcquireImmediateBatch(
                        out var batch), Is.True);
                    return new Scenario(owner, batch);
                }
            }

            public void Dispose()
            {
                if (_disposed) return;
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

            internal InputBuffers(
                uint abiVersion,
                RequestMismatch mismatch,
                int requestCount = 1,
                bool hasRandomStream = true,
                NativeBurstDispatchFieldEncodingV2 configurationEncoding =
                    NativeBurstDispatchFieldEncodingV2.UInt32,
                uint configurationElementSize = 4u)
            {
                if (requestCount < 0) throw new ArgumentOutOfRangeException(nameof(requestCount));
                if (configurationElementSize > int.MaxValue)
                    throw new ArgumentOutOfRangeException(nameof(configurationElementSize));

                _cases = new NativeArray<NativeBurstDispatchCaseV2>(
                    1, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                _requests = new NativeArray<NativeBurstDispatchRequestV2>(
                    requestCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                _configurationFields = new NativeArray<NativeBurstDispatchFieldV2>(
                    1, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                _memoryFields = new NativeArray<NativeBurstDispatchFieldV2>(
                    2, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                _configurationBytes = new NativeArray<byte>(
                    (int)configurationElementSize, Allocator.Temp, NativeArrayOptions.ClearMemory);
                _memoryBytes = new NativeArray<byte>(
                    8, Allocator.Temp, NativeArrayOptions.ClearMemory);
                _randomStates = new NativeArray<ulong>(
                    1, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                _randomIncrements = new NativeArray<ulong>(
                    1, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

                _cases[0] = new NativeBurstDispatchCaseV2(
                    TypeNumericId,
                    TypeVersion,
                    CatalogCaseIndex,
                    0u,
                    1u,
                    configurationElementSize,
                    0u,
                    2u,
                    8u,
                    NativeBurstDispatchPhaseMaskV2.Enter,
                    BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure | BurstNodeStatusMask.Running,
                    hasRandomStream);

                var requestTypeNumericId = mismatch == RequestMismatch.TypeIdentity
                    ? TypeNumericId + 1ul
                    : TypeNumericId;
                var requestTypeVersion = mismatch == RequestMismatch.TypeVersion
                    ? TypeVersion + 1u
                    : TypeVersion;
                for (var requestIndex = 0; requestIndex < requestCount; requestIndex++)
                {
                    _requests[requestIndex] = new NativeBurstDispatchRequestV2(
                        InstanceOrdinal + (uint)requestIndex,
                        RuntimeNodeIndex + (uint)requestIndex,
                        requestTypeNumericId,
                        requestTypeVersion,
                        CatalogCaseIndex,
                        BurstCallbackPhase.Enter,
                        0u,
                        0u,
                        0u,
                        TimeMicroseconds + requestIndex,
                        BurstNodeAbortReason.Timeout,
                        BurstNodeExitReason.Aborted);
                }

                _configurationFields[0] = new NativeBurstDispatchFieldV2(
                    0u,
                    0u,
                    1u,
                    configurationElementSize,
                    configurationEncoding);
                _memoryFields[0] = new NativeBurstDispatchFieldV2(
                    0u, 0u, 1u, 4u, NativeBurstDispatchFieldEncodingV2.Int32);
                _memoryFields[1] = new NativeBurstDispatchFieldV2(
                    1u, 4u, 1u, 4u, NativeBurstDispatchFieldEncodingV2.Int32);
                if (_configurationBytes.Length >= 4)
                {
                    _configurationBytes[0] = 0xef;
                    _configurationBytes[1] = 0xbe;
                    _configurationBytes[2] = 0xad;
                    _configurationBytes[3] = 0xde;
                }
                WriteInt32(_memoryBytes, 0, InitialMemory0);
                WriteInt32(_memoryBytes, 4, InitialMemory1);
                _randomStates[0] = InitialRandomState;
                _randomIncrements[0] = RandomIncrement;

                Value = new NativeBurstDispatchCreateInputV2(
                    Handshake(abiVersion),
                    _cases.AsReadOnly(),
                    _requests.AsReadOnly(),
                    _configurationFields.AsReadOnly(),
                    _memoryFields.AsReadOnly(),
                    _configurationBytes.AsReadOnly(),
                    _memoryBytes.AsReadOnly(),
                    _randomStates.AsReadOnly(),
                    _randomIncrements.AsReadOnly());
            }

            internal NativeBurstDispatchCreateInputV2 Value { get; }

            public void Dispose()
            {
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

        private sealed class GeneratedHandleInputBuffers : IDisposable
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
            private readonly NativeArray<NativeBurstDispatchCanonicalRangeV2> _caseRanges;
            private readonly NativeArray<NativeBurstDispatchCanonicalRangeV2> _bindingRanges;
            private readonly NativeArray<NativeBurstDispatchCanonicalRuleV2> _rules;

            internal GeneratedHandleInputBuffers(uint elementSize)
            {
                if (elementSize > int.MaxValue)
                    throw new ArgumentOutOfRangeException(nameof(elementSize));

                _cases = new NativeArray<NativeBurstDispatchCaseV2>(1, Allocator.Temp);
                _requests = new NativeArray<NativeBurstDispatchRequestV2>(1, Allocator.Temp);
                _configurationFields = new NativeArray<NativeBurstDispatchFieldV2>(1, Allocator.Temp);
                _memoryFields = new NativeArray<NativeBurstDispatchFieldV2>(1, Allocator.Temp);
                _configurationBytes = new NativeArray<byte>((int)elementSize, Allocator.Temp);
                _memoryBytes = new NativeArray<byte>(4, Allocator.Temp);
                _randomStates = new NativeArray<ulong>(0, Allocator.Temp);
                _randomIncrements = new NativeArray<ulong>(0, Allocator.Temp);
                _bindings = new NativeArray<NativeBurstDispatchBindingV2>(1, Allocator.Temp);
                _resolvedBindings = new NativeArray<NativeBurstDispatchResolvedBindingV2>(1, Allocator.Temp);
                _valueFields = new NativeArray<NativeBurstDispatchFieldV2>(1, Allocator.Temp);
                _liveValueBytes = new NativeArray<byte>(0, Allocator.Temp);
                _completions = new NativeArray<NativeBurstDispatchCompletionV2>(0, Allocator.Temp);
                _completionPayloadBytes = new NativeArray<byte>(0, Allocator.Temp);
                _caseRanges = new NativeArray<NativeBurstDispatchCanonicalRangeV2>(2, Allocator.Temp);
                _bindingRanges = new NativeArray<NativeBurstDispatchCanonicalRangeV2>(2, Allocator.Temp);
                _rules = new NativeArray<NativeBurstDispatchCanonicalRuleV2>(0, Allocator.Temp);

                _cases[0] = new NativeBurstDispatchCaseV2(
                    TypeNumericId,
                    TypeVersion,
                    CatalogCaseIndex,
                    0u,
                    1u,
                    elementSize,
                    0u,
                    1u,
                    4u,
                    NativeBurstDispatchPhaseMaskV2.Enter,
                    BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure | BurstNodeStatusMask.Running,
                    false,
                    0u,
                    1u);
                _requests[0] = new NativeBurstDispatchRequestV2(
                    InstanceOrdinal,
                    RuntimeNodeIndex,
                    TypeNumericId,
                    TypeVersion,
                    CatalogCaseIndex,
                    BurstCallbackPhase.Enter,
                    0u,
                    0u,
                    0u,
                    TimeMicroseconds,
                    new TreeInstanceId(1u),
                    1u,
                    0u,
                    1u);
                _configurationFields[0] = new NativeBurstDispatchFieldV2(
                    0u,
                    0u,
                    1u,
                    elementSize,
                    NativeBurstDispatchFieldEncodingV2.GeneratedHandle);
                _memoryFields[0] = new NativeBurstDispatchFieldV2(
                    0u, 0u, 1u, 4u, NativeBurstDispatchFieldEncodingV2.Int32);
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
                    0u,
                    0u,
                    0u,
                    0u,
                    0u);
                _resolvedBindings[0] = new NativeBurstDispatchResolvedBindingV2(
                    0u, 0u, NativeBurstDispatchBindingV2.NoOffset);
                _valueFields[0] = new NativeBurstDispatchFieldV2(
                    0u, 0u, 1u, 4u, NativeBurstDispatchFieldEncodingV2.Int32);

                var canonical = new NativeBurstDispatchCanonicalInputV2(
                    _caseRanges.AsReadOnly(),
                    _bindingRanges.AsReadOnly(),
                    _rules.AsReadOnly());
                var bindings = new NativeBurstDispatchBindingInputV2(
                    _bindings.AsReadOnly(),
                    _resolvedBindings.AsReadOnly(),
                    _valueFields.AsReadOnly(),
                    _liveValueBytes.AsReadOnly(),
                    _completions.AsReadOnly(),
                    _completionPayloadBytes.AsReadOnly(),
                    new NativeBurstDispatchBindingCapacityV2(1u, 4u, 1u, 4u, 0u),
                    canonical);
                Value = new NativeBurstDispatchCreateInputV2(
                    Handshake(2u),
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
                if (_rules.IsCreated) _rules.Dispose();
                if (_bindingRanges.IsCreated) _bindingRanges.Dispose();
                if (_caseRanges.IsCreated) _caseRanges.Dispose();
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

        [BurstCompile(CompileSynchronously = true)]
        private struct DependencyMarkerJob : IJob
        {
            internal NativeArray<int> Probe;

            public void Execute()
            {
                Probe[0] = 1;
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct ScheduledEnterBridgeJob : IJob
        {
            internal BurstExecutionBatch Batch;
            internal NativeArray<int> Probe;

            public void Execute()
            {
                if (Probe[0] != 1)
                {
                    Probe[1] = -1;
                    return;
                }

                var batch = Batch;
                var result = BurstGeneratedRuntimeBridge.TryGetExecutionRequest(
                    in batch,
                    out var instanceOrdinal,
                    out var runtimeNodeIndex,
                    out var catalogCaseIndex,
                    out var phase,
                    out var hasWork);
                if (result != BurstContextResult.Success || !hasWork)
                {
                    Probe[1] = -2;
                    return;
                }

                result = BurstGeneratedRuntimeBridge.TryAcquireDispatchFrame(
                    ref batch,
                    instanceOrdinal,
                    runtimeNodeIndex,
                    catalogCaseIndex,
                    phase,
                    out var frame);
                if (result != BurstContextResult.Success)
                {
                    Probe[1] = -3;
                    return;
                }

                result = BurstGeneratedRuntimeBridge.TryCreateConfigurationReader(
                    in frame, out var configuration);
                if (result != BurstContextResult.Success)
                {
                    Probe[1] = -4;
                    return;
                }

                result = BurstGeneratedRuntimeBridge.TryReadUInt32(
                    ref configuration, 0u, 0u, out var configValue);
                if (result != BurstContextResult.Success || configValue != ConfigurationValue)
                {
                    Probe[1] = -5;
                    return;
                }

                result = BurstGeneratedRuntimeBridge.TryCreateMemoryAccessor(
                    in frame, out var memory);
                if (result != BurstContextResult.Success)
                {
                    Probe[1] = -6;
                    return;
                }

                result = BurstGeneratedRuntimeBridge.TryReadMemoryInt32(
                    ref memory, 0u, 0u, out var first);
                if (result != BurstContextResult.Success)
                {
                    Probe[1] = -7;
                    return;
                }

                result = BurstGeneratedRuntimeBridge.TryReadMemoryInt32(
                    ref memory, 1u, 0u, out var second);
                if (result != BurstContextResult.Success)
                {
                    Probe[1] = -8;
                    return;
                }

                result = BurstGeneratedRuntimeBridge.TryWriteMemoryInt32(
                    ref memory, 0u, 0u, first + 25);
                if (result != BurstContextResult.Success)
                {
                    Probe[1] = -9;
                    return;
                }

                result = BurstGeneratedRuntimeBridge.TryWriteMemoryInt32(
                    ref memory, 1u, 0u, second + 104);
                if (result != BurstContextResult.Success)
                {
                    Probe[1] = -10;
                    return;
                }

                result = BurstGeneratedRuntimeBridge.TryCommitMemory(ref memory);
                if (result != BurstContextResult.Success)
                {
                    Probe[1] = -11;
                    return;
                }

                result = BurstGeneratedRuntimeBridge.TryCreateEnterContext(
                    in frame, out var context);
                if (result != BurstContextResult.Success)
                {
                    Probe[1] = -12;
                    return;
                }

                result = BurstGeneratedRuntimeBridge.TryCompleteEnter(
                    ref batch, in frame, ref context);
                if (result != BurstContextResult.Success)
                {
                    Probe[1] = -13;
                    return;
                }

                result = BurstGeneratedRuntimeBridge.TryGetExecutionRequest(
                    in batch, out _, out _, out _, out _, out hasWork);
                Probe[1] = result == BurstContextResult.Success && !hasWork ? 1 : -14;
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct ConcurrentFrameAcquireJob : IJobParallelFor
        {
            internal BurstExecutionBatch Batch;
            internal NativeArray<int> Results;

            public void Execute(int index)
            {
                var batch = Batch;
                Results[index] = (int)BurstGeneratedRuntimeBridge.TryAcquireDispatchFrame(
                    ref batch,
                    InstanceOrdinal,
                    RuntimeNodeIndex,
                    CatalogCaseIndex,
                    BurstCallbackPhase.Enter,
                    out _);
            }
        }
    }
}
